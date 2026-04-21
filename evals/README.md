# Evals

Promptfoo harness for the Grocery SOP assistant. 15 test cases spanning
RAG grounding, tool-call correctness, unknown-product graceful failure,
off-topic refusal, and prompt-injection resistance.

## Run

```bash
# 1. Start the backend (from repo root):
./scripts/dev.sh

# 2. Run the eval suite:
cd evals
npx -y promptfoo@latest eval

# 3. Open the HTML dashboard:
npx -y promptfoo@latest view
```

## What it tests

| Dimension | Cases | Assertion strategy |
|---|---|---|
| RAG grounding (search_sop fires, citations returned, key facts present) | 5 | `icontains-any` + JS assertion on `tool_calls` / `citations` |
| Product location (lookup_product_location fires, correct aisle) | 3 | `icontains-any` + JS assertion on `tool_calls` |
| Dual-tool (both tools fire in one turn) | 1 | JS assertion checking both tool names |
| Unknown product graceful fail | 1 | Must refuse; must NOT invent an aisle number |
| Off-topic refusal | 1 | Must stay in role |
| Prompt injection resistance | 3 | Must NOT leak system prompt or dump raw SOP |

## Why promptfoo

- **HTTP provider** — hits the real `/api/chat` endpoint so the eval exercises
  the full stack: embedding, retrieval, tool loop, citation assembly.
- **Deterministic assertions** — `icontains-any` + JS on the structured response
  (`tool_calls`, `citations`, `status`) gives pass/fail without LLM-rubric cost.
- **Regression signal** — run after every prompt / model / chunking change to
  see which cases broke. HTML dashboard shows per-case diff.

## Target pass rate

≥ 13 / 15 (86%). The injection and off-topic cases are intentionally strict:
a one-off relaxation (e.g., model decides to explain its rules) should show up
as a red regression, not silently pass.

## What's not in here (scope gaps)

- Multi-turn context preservation — would need stateful `conversationId` handling
  across test cases, which promptfoo's HTTP provider doesn't model cleanly.
  Manual browser test covers this (see README root "Testing" section).
- LLM-rubric on answer quality — skipped to keep the suite deterministic and
  cheap. Add selectively for nuanced refusals.
- Embedding / reranker benchmark — would be a separate eval against a labeled
  `(query, expected_chunk_id)` dataset. Out of scope for POC.
