# Labs — Agent Framework codealong (dispatch domain)

One lab per layer of the framework, bottom-up. Each lab is (or will be) a small
console project referencing `src/Domain`; every explanation lives here as
commentary, not in a separate guide. All labs share the dispatch domain so the
only thing that changes between labs is the framework concept.

| Lab | Layer | Key types | Status |
|---|---|---|---|
| 0 | Baseline contracts (docs-reading, no build) | `AIAgent`, `IChatClient`, `AITool`, `AgentSession`, `AgentResponse(Update)` | planned |
| 1 | Chat abstraction (Microsoft.Extensions.AI) | `IChatClient` providers (Azure OpenAI / Ollama / Claude), decorator pipeline | planned |
| 2 | The agent + concurrency | `ChatClientAgent`, `AsAIAgent` construction routes, backgrounded runs | planned |
| 3 | Tools + structured output | `AIFunctionFactory`, `RunAsync<T>`, `ChatResponseFormat` | planned |
| 4 | State + retrieval | `AgentSession`, `ChatHistoryProvider`, context providers (keyed + a minimal RAG) | planned |
| 5 | Middleware | agent-run / function-calling / IChatClient interception, approvals | planned |
| 6 | Orchestration | workflows: executors, edges, events, `RequestInfoExecutor` (HITL) | planned |

Provider packages are already referenced by the root project: `Azure.AI.OpenAI`
+ `Microsoft.Agents.AI.OpenAI` (Azure), `OllamaSharp` (local Ollama), and
`Anthropic` (Claude) — all three implement `Microsoft.Extensions.AI.IChatClient`,
which is the Lab 1 point.

The root `Program.cs` is the integrated demo these labs decompose; `BaseVersion/`
is the pre-AI comparison point.
