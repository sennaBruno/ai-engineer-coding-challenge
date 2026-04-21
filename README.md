# Grocery Store SOP Assistant

An internal chatbot POC that lets grocery store employees ask questions about
Standard Operating Procedures and get grounded, cited answers. Built as a
vertical slice: document ingestion, chunking and embedding, a local JSON vector
store, RAG-based chat with tool-calling, and multi-turn context.

**Stack:** .NET 10 Web API + OpenAI SDK for C# · React 19 + Vite + Tailwind v4
· `text-embedding-3-small` · `gpt-4o-mini`

> Original challenge brief archived at `CHALLENGE.md`.

## Demo video

Walkthrough covering the features, how to run it, design decisions,
shortcuts, and a production roadmap. Under 5 minutes.

**Watch:** https://share.descript.com/view/mefkNNhVKWs

## What it does

Employees ask questions in a chat UI. The assistant:

1. Decides whether a tool call is warranted.
2. For procedural, policy, or safety questions, calls `search_sop`, a semantic
   search over chunks of the ingested SOP.
3. For "where is X?" product-location questions, calls
   `lookup_product_location`, a deterministic aisle-map lookup that is faster
   and more reliable than vector search for this class of question.
4. Returns a concise answer grounded in retrieved SOP passages with citations,
   plus a trace of which tools fired.
5. Preserves conversation context so follow-ups like "and what time does that
   happen?" resolve against prior turns.

## Running it locally

Two supported paths. Docker is the reviewer-friendly default. The dev script
is faster for iteration.

### Path A: Docker (recommended for reviewers)

No local .NET SDK or Node required.

```bash
cp .env.example .env         # then set OPENAI_API_KEY
docker compose up --build
```

Open `http://localhost:5173`. Backend is at `http://localhost:5181`.

What's in the stack:

- **Backend image.** Multi-stage .NET 10 SDK build into a slim `aspnet:10.0`
  runtime. Non-root user (UID 64198, baked into the base image), baked-in SOP
  document, writable named volume for the JSON vector store.
- **Frontend image.** Bun builds the Vite bundle, served by `vite preview`.
  No nginx sidecar; the Vite static server is enough for a local POC.
  Non-root user, health-checked.
- **Wiring.** Backend exposes 5181, frontend 5173, both published to the host.
  `depends_on: service_healthy` gates frontend startup on the backend
  healthcheck.

```bash
docker compose down          # stop, keep vector store
docker compose down -v       # also drop the named volume (forces re-ingest)
```

### Path B: Local dev script (faster iteration)

Prerequisites:

- **.NET 10 SDK** (tested on 10.0.202). https://dotnet.microsoft.com/download/dotnet/10.0
- **Node.js 22 LTS** (pinned in `.nvmrc`; 20 LTS also works). If you use `fnm`
  or `nvm`, the script switches versions for you.
- Package manager: `bun` preferred, falls back to `pnpm` or `npm`.
- An OpenAI API key.

```bash
export OPENAI_API_KEY=sk-...
# or: echo 'sk-...' > .local/openai-key   (gitignored)

./scripts/dev.sh
```

The script activates Node LTS, locates `dotnet`, resolves the key, starts the
.NET API on 5181, starts Vite on the first free port in 5173–5176, tails both
logs to `scripts/logs/`, and prints both URLs when ready.

Other commands: `./scripts/dev.sh stop` · `./scripts/dev.sh logs`.

### First-run checklist

1. Open the frontend URL.
2. Click **Run ingest** in the right panel. Chunks the SOP into 25 sections
   and embeds them in ~3 seconds. Tick **Force re-ingest** if you edit the SOP
   or change the embedding model.
3. Try the starter suggestions under the transcript:
   - "What are the opening checklist steps for the manager on duty?"
   - "How much can a cashier refund without a manager?"
   - "Where is the milk?"
   - "What are the food safety temperature rules for the deli?"

### Endpoints

- `GET  /api/health`: liveness.
- `POST /api/ingest`: chunks, embeds, and persists
  `backend/src/Api/Data/vector-store.json`. Rate-limited: 5 req/min per IP.
- `POST /api/chat`: multi-turn chat with tool-calling. Rate-limited: 20
  req/min per IP.

### Running the eval harness

Promptfoo runs 14 cases against a live `/api/chat` covering RAG grounding,
tool-call routing, unknown-product refusal, off-topic deflection, and three
prompt-injection attempts. Target: ≥ 85% pass. Current: **14/14 (100%)**.

```bash
# with the backend up:
./scripts/eval.sh              # first run installs promptfoo locally
./scripts/eval.sh view         # opens the HTML dashboard
```

The script installs promptfoo inside `evals/` so the native `better-sqlite3`
binary pins to the active Node version, avoiding the `NODE_MODULE_VERSION`
errors you hit with a cached global `npx`. Coverage details: `evals/README.md`.

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
| `backend/src/Api/Services/OpenAIRetrievalChatService.cs` | Orchestrates the chat completion + tool-calling loop; strips tools on the last iteration to force a final answer |
| `frontend/src/pages/ChatPage.tsx` | Chat state, conversation id, starter suggestions, ingest trigger |

## Design decisions and tradeoffs

### Header-aware chunking over fixed-size windows

Splitting on `## ` boundaries gives each chunk one coherent SOP section. The
citation anchor becomes meaningful ("Section 6. Cash Handling"), not an
arbitrary offset. Oversized sections are sub-split on paragraph boundaries
with a 2000-char cap. A 33 KB document produces ~25 chunks, plenty of recall
for quiz-style questions in this domain.

Tradeoff: questions that span multiple sections can miss context. A larger
corpus would want an overlapping sliding window on top of the header split.

### Two tools, two classes of question

The brief asked for tool-calling. The interesting call was *which tools*.
`search_sop` is the obvious one, the RAG primitive. I added
`lookup_product_location` specifically to show the judgment of *when NOT to
RAG*: "where is the milk?" is a structured lookup, not a vector search. The
assistant picks correctly thanks to explicit tool descriptions and a system
prompt that names both paths.

The catalog is hardcoded in `SopToolExecutor.BuildCatalog()`. In production
it lives in the product database joined with inventory.

### Deterministic termination: strip tools on the last iteration

A naive tool-calling loop can run forever if the model keeps calling tools
without producing a final answer. I cap at 4 iterations and remove tools from
the options on iteration 4, so the model is *forced* to produce content. Small
change, meaningful safety rail. I hit this during development: a snake_case
bug on tool args made the executor return `{"error": "item_name is required"}`
and the model kept retrying the same tool call. The iteration cap saved the
day; stripping tools on the last iter is what makes it deterministic.

### JSON vector store with atomic writes

For ≤ a few thousand vectors, cosine similarity over an in-memory array is
fast enough and eliminates operational complexity. Persistence is
write-to-temp-then-rename so a crash mid-save can't corrupt the artifact.
`LoadAsync` is lazy and cached behind a semaphore.

### Provider-agnostic tool schema

`IToolRegistryService` returns `ToolDefinition` (name, description, JSON
Schema string), not OpenAI-specific types. The chat service converts to
`ChatTool` at the boundary. Swapping to Anthropic or a local model doesn't
touch the registry.

### Snake-case for tool I/O

The OpenAI tool schema uses `snake_case` fields (`item_name`, `top_k`). The
tool executor's deserializer must match. I configured
`JsonNamingPolicy.SnakeCaseLower` for inbound args. Outbound payloads to the
model also use snake_case so the model reads results in the same shape it
reasoned about when generating the call.

### Frontend kept intentionally small

Tailwind v4 plus ~150 lines of custom primitives (Button, Card, Input, Badge)
instead of shadcn CLI. The scaffold's visual language is kept but rebuilt
with utility classes so the UI is easy to theme. Starter questions surface
both tool paths so a reviewer can sanity-check behavior in under a minute.

### Security and abuse guards (applied to the POC)

Unauthenticated endpoints that proxy to a paid API need basic hygiene even
in a POC. What's in place:

- **Path containment on ingest.** Supplied `sourcePath` must resolve (after
  symlink follow) under the repo's `knowledge-base/` directory. Blocks
  `../../etc/passwd`, absolute `/etc/...`, and symlink escapes.
- **Rate limiting.** `/api/chat` caps at 20 req/min per IP, `/api/ingest` at
  5 req/min. A 429 with no queue prevents looping to drain the OpenAI budget.
- **DoS and wallet-drain caps on chat.** Max 40 messages per request, 8 KB
  per message. A 10 MB cap on ingest sources prevents a huge file from
  dominating the embedding bill.
- **Prompt-injection delimiters.** Retrieved SOP chunks are wrapped in
  `<sop_chunk section="…" lines="…">…</sop_chunk>`. The system prompt tells
  the model that content inside is untrusted data.
- **Role demotion.** If a client request body tries to slip in a `system`
  role, the server logs and demotes it to `user`. The only system prompt the
  model sees is the server's.
- **Embedding dimension guard.** If stored vectors don't match the query
  model's dimension (someone changed the embedding model without
  re-ingesting), the vector store logs an error and returns no matches
  instead of silently serving zero-score garbage.
- **Narrow CORS.** Bounded origin list, `GET/POST/OPTIONS` only,
  `Content-Type` only. No credentials.
- **Security response headers.** `X-Content-Type-Options: nosniff`,
  `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer` on every response.
- **Dev vs. prod error shapes.** Development uses the developer exception
  page for inline stack traces; production hides exception text behind a
  uniform `{ "error": "…" }` JSON shape.

### Known POC-scope security gaps (deliberate)

Called out explicitly so they're understood as scope decisions, not oversights:

- **No authentication or authorization.** The brief lists auth as a
  non-requirement. The rate limiter gates abuse by IP as a stopgap. A real
  deployment adds OIDC (Auth0, Entra, Clerk) and per-employee scoping of the
  aisle map before exposing this externally.
- **No per-user token accounting.** Rate limiting bounds request count, not
  tokens-per-request. Production needs OpenAI usage headers surfaced plus a
  per-principal daily budget.
- **No explicit request-body cap on `/api/ingest`.** The 10 MB cap is on the
  resolved file, not the request body. Kestrel's default 30 MB applies. An
  explicit `[RequestSizeLimit]` belongs here before shipping.

## What I'd ship next (POC → production)

1. **Hybrid retrieval.** BM25 keyword search + dense embeddings with
   reciprocal rank fusion. Pure dense retrieval misses exact-token matches
   for policy codes and dollar amounts.
2. **Citation filtering.** Today all retrieved chunks surface as citations
   (max-scored per chunk id). Next step: post-filter to only the chunks the
   final answer actually references, either via an LLM re-read or by tagging
   tool results with chunk ids and matching tokens in the response.
3. **Extended prompt-injection hardening.** Delimiters and role demotion are
   in place. A production system processing supplier or vendor docs would
   add classifier-based detection of `assistant:`-style patterns before
   chunks hit the model, plus ensembled guards (canary tokens, content
   classifiers).
4. **Persisted conversations.** Today `conversationId` is a UUID generated
   in the browser and each message carries the full history. At scale,
   persist server-side and send only a window.
5. **Observability.** Structured logs exist. Next: per-turn traces with
   latency breakdowns (embed, search, LLM, tool), token counting, and a
   basic regex-based eval ("answer must contain X") that runs on CI.
6. **Outer-loop timeout.** Each OpenAI call is already wrapped in a 30 s
   `CancellationTokenSource`, so the worst case is 4 × 30 s per turn.
   Production wants a single outer-loop budget (~45 s total) and a
   retry-friendly error shape for the client.
7. **Concurrent ingest guard.** The current TOCTOU (load, check, save) is
   fine for single-writer POC use. Two simultaneous `POST /api/ingest` calls
   could race. Production takes a distributed lock or enqueues ingests to a
   single-worker job.
8. **Frontend history windowing.** The client sends full history each turn.
   The backend caps at 40. The UX should trim gracefully and prompt "start a
   new conversation" before hitting the wall.
9. **Auth and tenancy.** Obvious gaps: auth, per-store scoping of the aisle
   map, admin UI for re-ingesting when the SOP updates, role-based access
   (cashier vs. manager content).
10. **Real vector store once n > 10k.** pgvector or a dedicated store. The
    JSON file works great for a POC. It stops scaling when load-from-disk
    dominates cold start.

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
├── evals/                       # promptfoo harness (14 cases)
└── knowledge-base/
    └── Grocery_Store_SOP.md
```

## Notes on AI assistance

Built with Claude Code driving keystrokes, with design decisions owned by me.
The best example of the workflow is the tool-call convergence bug: a
snake_case mismatch on tool arguments made the model retry the same call on a
loop because the error payload told it what was missing. I observed the
behavior, added a targeted diagnostic log line, read the logs, fixed the root
cause, and kept the log line at debug level because it will help the next
person who sees a weird tool-calling loop. That pattern, observe → log →
diagnose → fix → leave breadcrumbs, is the shape of every non-trivial
decision in this repo.
