# Grocery Store SOP Assistant

An internal chatbot POC that helps grocery store employees get grounded answers
about store Standard Operating Procedures. Built as a vertical slice: document
ingestion → chunking + embedding → local JSON vector store → RAG-based chat
with tool-calling and multi-turn context.

**Stack:** .NET 10 Web API + OpenAI SDK for C# · React 19 + Vite + Tailwind v4
· `text-embedding-3-small` · `gpt-4o-mini`

> Original challenge brief archived at `CHALLENGE.md`.

---

## What it does

Employees ask questions in a chat UI. The assistant:

1. Decides whether to call a tool based on the question.
2. For procedural / policy / safety questions, calls `search_sop` — a semantic
   search over chunks of the ingested SOP.
3. For "where is X?" product-location questions, calls `lookup_product_location`
   — a deterministic aisle-map lookup (faster, more reliable than vector search
   for this class of question).
4. Returns a concise answer grounded in retrieved SOP passages with citations,
   plus a trace of which tools fired.
5. Preserves conversation context so follow-up questions like "and what time
   does that happen?" resolve against prior turns.

---

## Running it locally

### Prerequisites

- .NET 10 SDK (10.0.202 tested) — https://dotnet.microsoft.com/download/dotnet/10.0
- Node.js 20+ and npm
- An OpenAI API key

### 1. Backend

```bash
cd backend/src/Api

# Set the API key — env var is preferred; appsettings is fine for local dev.
export OPENAI_API_KEY="sk-..."

dotnet run --urls http://localhost:5181
```

The API listens on `http://localhost:5181`. Three endpoints:

- `GET  /api/health` — liveness
- `POST /api/ingest` — reads the SOP, chunks, embeds, persists to
  `backend/src/Api/Data/vector-store.json`
- `POST /api/chat` — multi-turn chat with tool calling

### 2. Frontend

```bash
cd frontend
npm install
npm run dev
```

Opens on `http://localhost:5173` (or 5174 if 5173 is busy). If Vite picks a
different port, add it to `Cors:AllowedOrigins` in
`backend/src/Api/appsettings.json`.

### 3. Use it

1. Open the frontend in a browser.
2. Click **Run ingest** in the right-hand panel. This chunks the SOP into ~25
   sections and embeds them (~3 seconds).
3. Ask the assistant questions. Try the starter suggestions below the
   transcript:
   - "What are the opening checklist steps for the manager on duty?"
   - "How much can a cashier refund without a manager?"
   - "Where is the milk?"
   - "What are the food safety temperature rules for the deli?"

---

## Architecture

```
┌─────────────────────────┐     POST /api/chat       ┌─────────────────────────────────────┐
│  React UI               │ ────────────────────────▶│ ChatController                      │
│  - Transcript           │                          │   ↓                                 │
│  - Composer             │◀────── JSON response ────│ OpenAIRetrievalChatService          │
│  - Ingest panel         │                          │   ├─ build messages + tools         │
│  - Tool-calls, Citations│                          │   ├─ loop: complete → tool-calls    │
│  - Starter suggestions  │                          │   │         → execute → repeat      │
└─────────────────────────┘                          │   └─ final answer + citations       │
                                                     └─┬───────────────────────────────────┘
                                                       │
                                                       ▼
                                      ┌───────────────────────────────────────┐
                                      │ SopToolExecutor                       │
                                      │  ┌─ search_sop                        │
                                      │  │   └─ EmbeddingService → OpenAI     │
                                      │  │       ↓                            │
                                      │  │   FileVectorStore.SearchAsync      │
                                      │  │       (cosine similarity on JSON)  │
                                      │  └─ lookup_product_location           │
                                      │      └─ static aisle map (dict)       │
                                      └───────────────────────────────────────┘

Ingest path: IngestController → MarkdownChunkingService →
             OpenAIEmbeddingService (batched) →
             FileVectorStoreService.SaveAsync (atomic write, JSON array on disk)
```

### Key files

| File | Purpose |
|---|---|
| `backend/src/Api/Services/MarkdownChunkingService.cs` | Splits the SOP on H2 headers, sub-splits oversized sections on paragraph boundaries, keeps section + line-range metadata for citations |
| `backend/src/Api/Services/OpenAIEmbeddingService.cs` | Thin wrapper over OpenAI's embedding client; batches ingest calls |
| `backend/src/Api/Services/FileVectorStoreService.cs` | Lazy-load, atomic save (temp file + rename), in-memory cache, cosine similarity search |
| `backend/src/Api/Services/SopToolRegistry.cs` | Declares the tool schemas (JSON Schema), provider-agnostic |
| `backend/src/Api/Services/SopToolExecutor.cs` | Dispatches tool calls; owns the static product catalog |
| `backend/src/Api/Services/OpenAIRetrievalChatService.cs` | Orchestrates the chat completion + tool-calling loop; forces a final answer on the last iteration by stripping tools |
| `frontend/src/pages/ChatPage.tsx` | Chat state, conversation id, starter suggestions, ingest trigger |

---

## Design decisions & tradeoffs

### Header-aware chunking over fixed-size windows
Splitting on `## ` boundaries gives each chunk one SOP section, so retrieved
passages are semantically coherent and citations point at meaningful source
locations ("Section 6. Cash Handling"). Oversized sections are sub-split on
paragraph boundaries with a 2000-char cap. For a 33 KB document this produces
~25 chunks — plenty of recall for the quiz-style questions in this domain.

Tradeoff: questions that span multiple sections can miss context. For a larger
corpus I would add an overlapping sliding window on top of the header split.

### Two tools, different classes of question
The challenge asked for tool-calling; the interesting design decision was
*which tools*. `search_sop` is the obvious one — it's the RAG primitive. I
added `lookup_product_location` specifically to demonstrate the judgment of
*when NOT to RAG*: "where is the milk?" should be a structured lookup, not a
vector search. The assistant picks correctly thanks to the system prompt +
tool descriptions.

The catalog is hardcoded in `SopToolExecutor.BuildCatalog()`. In production it
would live in the product database.

### Deterministic termination: strip tools on last iteration
A naive tool-calling loop can run forever if the model keeps calling tools
without producing a final answer. I cap at 4 iterations and remove tools from
the options on the final iteration so the model is *forced* to produce content.
This is a small code change but a meaningful safety rail — worth calling out
because I hit it during development (snake_case bug on tool args caused the
model to retry because it kept getting `{"error": "item_name is required"}`).

### JSON vector store with atomic writes
For ≤ a few thousand vectors, cosine similarity over an in-memory array is
fast enough and eliminates all operational complexity. Persistence uses
write-to-temp-then-rename so a crash mid-save can't corrupt the artifact.
`LoadAsync` is lazy and cached behind a semaphore.

### Provider-agnostic tool schema
`IToolRegistryService` returns `ToolDefinition` (name + description + JSON
Schema string), not OpenAI-specific types. The chat service converts to
`ChatTool` at the boundary. If we swap to Anthropic or a local model, the
registry doesn't change.

### Snake-case for tool I/O
The OpenAI tool schema uses `snake_case` fields (`item_name`, `top_k`). The
tool executor's deserializer must match — I configure
`JsonNamingPolicy.SnakeCaseLower` for inbound args. Outbound payloads to the
model also use snake_case so the model reads results in the same shape it
reasoned about when generating the call.

### Frontend kept intentionally small
Tailwind v4 + ~150 lines of custom primitives (Button, Card, Input, Badge)
rather than shadcn CLI. The scaffold's visual language is retained but rebuilt
with utility classes so the UI is easy to theme. Starter questions surface the
two tool paths so a reviewer can sanity-check behavior in under a minute.

### Security + abuse guards (applied to the POC)
Even as a POC, unauthenticated endpoints that proxy to a paid API need basic
hygiene. What's in place:

- **Path containment on ingest.** Supplied `sourcePath` must resolve (after
  symlink follow) under the repo's `knowledge-base/` directory. Blocks
  `../../etc/passwd`, absolute `/etc/...`, and symlink escapes.
- **DoS / wallet-drain caps on chat.** Max 40 messages per request, 8 KB per
  message. A 10 MB size cap on ingest sources prevents a huge file from
  dominating the embedding bill.
- **Prompt-injection delimiters.** Retrieved SOP chunks are wrapped in
  `<sop_chunk section="…" lines="…">…</sop_chunk>` and the system prompt
  instructs the model to treat that content as untrusted data.
- **Role demotion.** If a client request body tries to slip in a `system`
  role, the server logs and demotes it to `user` — the only system prompt
  the model sees is the server's.
- **Embedding dimension guard.** If stored vectors don't match the query
  model's dimension (e.g., someone changed the embedding model without
  re-ingesting), the vector store logs an error and returns no matches
  instead of silently returning zero-score garbage.
- **Dev vs. prod error shapes.** Development uses the developer exception
  page for inline stack traces; production hides exception text behind a
  uniform `{ "error": "…" }` JSON shape.

---

## What I'd ship next (from POC → production)

1. **Hybrid retrieval.** BM25 (keyword) + dense embeddings with reciprocal
   rank fusion. Pure dense retrieval misses exact-token matches for things
   like policy codes and dollar amounts.
2. **Citation filtering.** Right now all retrieved chunks across a turn
   surface as citations (kept max-scored per chunk ID). Next step is to
   post-filter to only the chunks actually referenced in the final answer —
   either via an LLM re-read or by tagging tool results with chunk IDs and
   matching tokens in the response.
3. **Extended prompt-injection hardening.** Delimiters + role demotion are
   in place. A real-world system processing supplier / vendor docs would add
   classifier-based detection of `assistant:`-style patterns before chunks
   hit the model, and ensembled guards (canary tokens, content classifiers).
4. **Persisted conversations.** Currently `conversationId` is a UUID generated
   in the browser; each message carries the full history. At scale, persist
   server-side and send only a window.
5. **Observability.** Structured logs exist; next steps are per-turn traces
   with latency breakdowns (embed, search, LLM, tool), token counting, and a
   basic eval harness (curated question → expected-facts regex) that runs on
   CI.
6. **Timeout budget on OpenAI calls.** Today a stuck OpenAI request sits for
   the SDK's default (~100 s) times 4 tool iterations. Production should wrap
   the loop in a `CancellationTokenSource` with a hard ~30 s overall budget
   and surface a retry-friendly error.
7. **Concurrent ingest guard.** The current TOCTOU (load → check → save) is
   fine for single-writer POC use but two simultaneous `POST /api/ingest`
   calls could race. Production would take a distributed lock or enqueue
   ingests to a single-worker job.
8. **Frontend history windowing.** The client sends the full message history
   each turn. The backend caps at 40, but the UX should trim gracefully and
   show a "start a new conversation" hint before hitting the wall.
9. **Auth + tenancy.** Obvious gaps: auth, per-store scoping of the aisle
   map, admin UI for re-ingesting when the SOP updates, role-based access
   (cashier vs. manager content).
10. **Real vector store once n > 10k.** pgvector or a dedicated store. The
    JSON file works great for a POC; it stops scaling when load-from-disk
    dominates cold start.

---

## Directory layout

```
.
├── backend/
│   └── src/Api/
│       ├── Controllers/         # Health, Ingest, Chat
│       ├── Contracts/           # API DTOs
│       ├── Models/              # TextChunk, VectorRecord, ToolDefinition
│       ├── Options/             # OpenAIOptions
│       ├── Services/            # Chunking, Embedding, VectorStore,
│       │                        #   ToolRegistry, ToolExecutor, ChatService
│       ├── Data/                # vector-store.json (generated, gitignored)
│       ├── appsettings.json
│       └── Program.cs           # DI wiring + CORS
├── frontend/
│   └── src/
│       ├── components/          # ChatTranscript, ChatComposer,
│       │                        #   IngestPanel, CitationsPanel, ToolCallsPanel
│       ├── components/ui/       # Button, Card, Input, Badge primitives
│       ├── pages/ChatPage.tsx
│       ├── services/apiClient.ts
│       └── types/chat.ts
└── knowledge-base/
    └── Grocery_Store_SOP.md
```

---

## Notes on AI assistance

Built using Claude Code with human-driven design decisions. The debugging
loop on the tool-call convergence issue (snake_case mismatch causing the
model to retry the same tool call) is representative of the workflow: observe
behavior → add targeted diagnostic logging → read the logs → fix the root
cause. I kept the diagnostic log line in the code at debug level because it's
useful for the next person who sees a weird tool-calling loop.
