# Data Folder

Holds the local vector-store artifact that `POST /api/ingest` produces.

- `vector-store.json` is generated on ingest and **gitignored**. Run the ingest
  endpoint (or click **Run ingest** in the frontend) to (re)build it.
- The file is a flat JSON array of `VectorRecord` objects: `{ id, source,
  chunkText, embedding[], metadata }`. Cosine similarity search happens
  in-memory against this array.
- Safe to delete — the next ingest rebuilds it.
