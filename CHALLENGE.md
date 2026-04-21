# Grocery Store SOP Assistant Coding Challenge

## Scenario

You are building an internal chatbot for a grocery store chain. Store employees should be able to ask questions about operating procedures and receive grounded answers based on the SOP document in `knowledge-base/Grocery_Store_SOP.md`. The team just wants a POC to demonstrate the concept, so you should focus on building a clear vertical slice that connects document ingestion, retrieval, and a simple chat interface.

This challenge is intentionally scoped for **2-3 hours** of focused work with AI assistance. The goal is not to build a production-ready system. The goal is to demonstrate technical judgment, pragmatic scoping, and the ability to connect backend, frontend, and AI-oriented application patterns.

## Goals

Build a working vertical slice that demonstrates:

- A C# / .NET 10 Web API backend.
- A React frontend for a simple multi-turn chat experience.
- Document ingestion from the provided SOP file.
- Chunking and embedding of document text.
- A local vector-store concept that is held in memory during runtime and persisted as a JSON array on disk.
- Retrieval-augmented generation (RAG) using the SOP content.
- Tool-calling or agentic behavior where the assistant can decide to use supporting actions when appropriate.
- Multi-turn chat that preserves enough conversation context to answer follow-up questions.

## Provided Input

- Ingestion source: `knowledge-base/Grocery_Store_SOP.md`

You should treat the SOP as the primary knowledge source for the chatbot.

## Required Features

### Backend

- Expose HTTP endpoints for ingestion and chat.
- Read the SOP document from the repository.
- Ingest the document so that it can be used for retrieval. 
- Persist vector records locally as a JSON array file.
- Build a chat endpoint that uses retrieved context to answer questions.
- Support multi-turn conversation inputs.
- Include a clear place for tool-calling or agent orchestration logic.

### Frontend

- Provide a simple chat UI.
- Allow the user to trigger ingestion.
- Show a transcript of the conversation.
- Support multiple user/assistant turns.

### AI-Oriented Expectations

- **RAG:** Ground responses in retrieved SOP content and return citations for the chunks you used.
- **Tool-calling / agentic behavior:** Define one or two tools (e.g., a tool that searches SOP chunks by query, a tool that looks up store hours) and wire them into your chat flow using OpenAI function-calling (or equivalent). The assistant should be able to decide *when* to invoke a tool versus answer directly, and incorporate the tool's result into its response. You do not need to build a multi-step agent loop or a full orchestration framework — a single-turn tool-use decision is sufficient. We're looking for evidence that you understand how to **design tool schemas, let the model select tools, execute them, and feed results back** into the conversation.
- **Multi-turn chat:** Keep enough state to handle follow-up questions without rebuilding the entire system.

## Non-Requirements

- No authentication or authorization.
- No external vector database.
- No production deployment.
- No need to support multiple documents unless you want to.

## Freedom for Design and Creativity

- You have freedom to design the API, data structures, and frontend as you see fit. Although boilerplate code is provided, you are not required to use it. You can start from scratch if you prefer and use any React component library or styling approach you like. If you have another C# library such as Microsoft Agent Framework or other Agentic SDK you would like to use you can absolutely do that as well. The most important thing is to demonstrate a clear vertical slice that connects the core concepts of document ingestion, retrieval, RAG, and a chat interface and that it works.
- Some specifications were intentionally omitted to give you freedom to make design decisions so that we can gauge some of your engineering judgment and see how you connect the dots.

## Constraints

- Backend stack must be **C# / .NET 10 Web API**.
- Frontend stack must be **React**.
- The vector store must be **local/in-memory and persisted as JSON**, not an external database.
- The code should be locally runnable without complex setup. The only external dependencies should be for AI services and standard libraries.
- Keep the codebase easy to understand and easy to extend.

## Suggested Approach

1. Get the backend and frontend running locally.
2. Add ingestion for the SOP file.
3. Store chunks and embeddings in a local JSON artifact.
4. Implement retrieval.
5. Add a chat flow that uses retrieved context.
6. Add one lightweight agentic or tool-calling behavior.
7. Polish developer experience and document tradeoffs.

## Evaluation Criteria

We will evaluate submissions on:

- A working POC that demonstrates the required features.
- Code quality and organization.
- Clarity of API and frontend structure.
- Practical handling of document ingestion, retrieval, and RAG.
- Evidence of sound engineering judgment and tradeoff awareness.
- Reasonable use of AI assistance without losing code clarity.
- Basic usability of the chat experience.

## Submission Notes

Please include a link to a public repo with your completed work. Include a video in the repo or a link to a video of you walking through the application and narrating the following:

- Demo of what you built demonstrating the required features.
- How to run the app.
- Key design decisions you made and why.
- Any assumptions or shortcuts you made.
- What you would prioritize improving if moving from POC to a more production-ready system.

The video should be under 5 minutes. We are looking for clear communication and insight into your thought process, not a polished presentation.

## What Good Scope Looks Like

A strong submission is not the most complex submission. A strong submission is one that makes sensible tradeoffs, keeps the implementation understandable, and clearly demonstrates the core competencies this role requires.

## Final Notes

You will be provided a budgeted OpenAI API key for this challenge. Please be mindful of token usage and try to build a system that is efficient in its calls. Focus on demonstrating the core concepts rather than building a fully featured system. We are looking for clear evidence of good engineering judgment, practical design decisions, and the ability to connect the dots between document ingestion, retrieval, RAG, and a chat interface.

## AI Anticipated Questions

**Q: Can I use AI coding assistants (Copilot, Claude Code, Cursor, etc.)?**
A: Yes — this challenge is designed to be completed with AI assistance. We expect you to use these tools. What we evaluate is whether you understand the code you produce, make sound design decisions, and can explain your tradeoffs. AI-generated code that you clearly don't understand will count against you.

**Q: Do I have to use the provided boilerplate?**
A: No. You can modify it, extend it, or start from scratch. If you prefer Semantic Kernel, Microsoft Agent Framework, or another SDK, go ahead. The boilerplate is there to reduce setup time, not to constrain your approach. If you deviate significantly, briefly explain why in your submission notes.

**Q: How polished does the frontend need to be?**
A: Functional over beautiful. We're evaluating whether the chat works end-to-end, not CSS craftsmanship. If the UI clearly shows the conversation, citations, and an ingest trigger, that's sufficientbut we aren't opposed to a beautiful UI if that's your strength and you want to show it off.

**Q: Do I need to write tests?**
A: Tests are not required within the scope. If you have time and want to add a few unit tests for your chunking or retrieval logic, that's a nice bonus — but a working vertical slice takes priority.

**Q: What does "citations" mean in this context?**
A: When the assistant answers a question using retrieved SOP chunks, include references to which chunks were used — the source section name and/or a text snippet. The boilerplate `CitationDto` has fields for `source`, `snippet`, and optional line numbers. You don't need a formal citation format; we just want to see that responses are traceable back to the source material.

**Q: For tool-calling, do I need OpenAI's function-calling specifically?**
A: No. OpenAI function-calling is the most straightforward approach, but if you prefer to use Semantic Kernel's plugin system, or Agent Framework or a manual dispatch pattern, or another mechanism, that's fine. What matters is that the model can decide when to use a tool, the tool executes, and the result feeds back into the response.

**Q: What if I run out of time?**
A: This assessment is not timed. You can take as long or as little time as you would like. It is scoped to take between 2-3 hours of focused work for an experienced engineer familiar with RAG. Some may finish in an hour and some may take longer. 