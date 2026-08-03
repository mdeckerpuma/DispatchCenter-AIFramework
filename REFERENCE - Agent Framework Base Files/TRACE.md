# Tracing the chain: how an AIAgent is built and where it actually talks to the model

Two hierarchies meet in the middle — they are NOT levels of one ladder:

- **AIAgent** is the root of the *agent* hierarchy (inheritance). It is the
  foundation we build FROM: the contract every agent shares (run, sessions,
  serialize). It is abstract and has no idea how to talk to any model.
- **IChatClient** is the root of the *communication* stack (composition). It is
  the foundation we build ON: a concrete implementation (Azure OpenAI, Ollama,
  Anthropic) is the only piece that actually touches the wire.
- **ChatClientAgent** is where they meet: it *inherits from* AIAgent and
  *composes* an IChatClient.

## 1. Construction (where IChatClient comes in)

| Hop | File / line | What happens |
|---|---|---|
| 1 | `Program.cs` — `.GetChatClient("gpt-4o-mini").AsAIAgent(options)` | Provider SDK gives a wire-level client; `AsAIAgent` builds a `ChatClientAgent` around it |
| 2 | `ChatClientAgent.cs:118` (ctor) | Agent receives the `IChatClient` + `ChatClientAgentOptions` |
| 3 | `ChatClientAgent.cs:131` | The client is wrapped with default agent middleware (`WithDefaultAgentMiddleware`) — this is where `FunctionInvokingChatClient` (the tool loop) and the context-provider client decorator get layered on, unless `UseProvidedChatClientAsIs = true` |
| 4 | `ChatClientAgent.cs:136` | History store defaults: `options?.ChatHistoryProvider ?? new InMemoryChatHistoryProvider()` |

Result: `agent.ChatClient` (property at `ChatClientAgent.cs:158`) is not the raw
provider client — it is a decorated *pipeline* ending in the provider client.

## 2. A run (where the agent sends/gets its info)

| Hop | File / line | What happens |
|---|---|---|
| 1 | `AIAgent.cs:251-334` | Public `RunAsync` overloads normalize input (string / messages / none) |
| 2 | `AIAgent.cs:366` | Abstract `RunCoreAsync` — the subclass takes over |
| 3 | `ChatClientAgent.cs` (`RunCoreAsync`) | Prepares the turn: history pulled via `ChatHistoryProvider.InvokingAsync` (`:1040`), extra context injected via each `AIContextProvider.InvokingAsync` (`:784`) |
| 4 | `ChatClientAgent.cs:233` | **The actual send**: `chatClient.GetResponseAsync(inputMessagesForChatClient, chatOptions, ...)` — one call into the decorated pipeline |
| 5 | `FunctionInvokingChatClient.cs:277` | The pipeline's tool loop: forwards to the inner (wire) client, inspects the response for function calls, executes them (`ProcessFunctionCallsAsync`, `:1162`), appends results, loops until no more calls |
| 6 | inner `IChatClient` (`IChatClient.cs:42`) | Wire-level `GetResponseAsync` — the only hop that talks to the model service |
| 7 | back up in `ChatClientAgent.cs:486-527` | Providers are notified (`InvokedAsync`): history stores the new messages, context providers observe the turn |
| 8 | `AgentResponse.cs` | Everything returns as messages → typed content items |

Streaming is the same chain through `AIAgent.cs:382-502` (`RunStreamingAsync` /
`RunCoreStreamingAsync`) and `ChatClientAgent.cs:324`, yielding
`AgentResponseUpdate` chunks (see `AgentResponseUpdate.cs`) instead of one
`AgentResponse`.

## File inventory (what each file is in the chain)

| File | Role |
|---|---|
| `AIAgent.cs` | Root of the agent hierarchy — the contract (run, sessions, serialize) |
| `ChatClientAgent.cs` | The bridge: AIAgent implemented over a composed IChatClient |
| `ChatClientAgentOptions.cs` / `AgentRunOptions.cs` / `ChatClientAgentRunOptions.cs` | Where all configuration enters (instructions, tools, providers; per-run overrides) |
| `IChatClient.cs` | Root of the communication stack — the two-method wire contract |
| `FunctionInvokingChatClient.cs` | The tool loop, as a client decorator (from Microsoft.Extensions.AI) |
| `AITool.cs` / `ApprovalRequiredAIFunction.cs` | What the model can request; the approval-gate wrapper |
| `AgentSession.cs` | The conversation (per-conversation state, StateBag) |
| `ChatHistoryProvider.cs` / `InMemoryChatHistoryProvider.cs` | Where history actually lives (default = in-memory) |
| `AIContextProvider.cs` | Pre-model-call context injection (memory, RAG, keyed lookups) |
| `AgentResponse.cs` / `AgentResponseUpdate.cs` | What comes back: messages → typed contents (non-streaming / streaming) |
| `HarnessAgent.cs` | The assembled composite: a ChatClientAgent plus batteries |
| `IEmbeddingGenerator.cs` / `IEmbeddingGeneratorTInputTEmbedding.cs` / `Embedding.cs` / `EmbeddingT.cs` | The embeddings sibling of IChatClient — vectors in place of chat (RAG/memory); not part of the agent run chain |

## Provenance / drift warning

Files copied from `microsoft/agent-framework` @ main (dotnet/src) and
`dotnet/extensions` @ main (FunctionInvokingChatClient, ApprovalRequiredAIFunction).
The project pins Microsoft.Agents.AI 1.13.0 / Microsoft.Extensions.AI 10.8.0, so
line numbers and minor signatures may drift from the shipped binaries. These files
are for READING; the compiler and the pinned packages are the arbiter of truth.
