# How the Agent Framework Works in This Program

---

## Notes

### Where I left off

I left off in the middle of actively improving the design decisions around the agent
framework. I had just gotten past the point of setting up a solid, concrete agent, and I was
about to start hand-coding some of the features that make the framework so good. I did not
get to them.

Every improvement I actually landed came from one place: making the instructions clearer for
this specific agent. That was enough to get real results, but it is a small fraction of what
the framework offers. There are many more ways to improve how your program functions with it.

The best way to implement those other features is to try them. See what works for your
SPECIFIC program and what gives you the best results. While I was designing this I kept
thinking "how can this possibly be what they want," and the conclusion I came to is that this
process is trial and error by nature. You have to go in and find out what works for you.

I think that matters even more when designing this into the Squid. There are so many "right"
ways to go about it, at least as far as I can tell. I am confident that if someone takes the
time to design for the Squid there will be many reshapes along the way, but that the desired
result is reachable through trial and error and nitty-gritty work.

### Future thought process (for Dimitri)

I went over most of this above, but to reiterate: get creative. My one regret is that I did
not use more of what the framework offers on a program I already knew well, which is this
one. Toward the end it got easy to look at a feature and say "yes, that would make sense
here," or "no, that is not needed at all for my program." You will run into all of that when
you apply it to the Squid, and knowing the program is what makes those calls easy.

The other thing I would note is about the first action you pick to implement this on. Do not
only try to solve that action. Think bigger picture: treat it as one sub-action in a field of
many, and ask what happens when the second and third get added. Ideally, adding the next one
means writing only the script for that agent, applying X, and nothing else. Build the first
flow so that the next one is easy.



---

## About this walkthrough

Everything below was produced by going through this repo and annotating every place the
Agent Framework is touched, so each snippet is real code lifted out of the program rather
than an illustrative example. Each one carries a short note on what that framework call
actually does and why it sits where it sits. It is ordered by the program's own lifecycle
rather than by file, so it reads in the order the code executes instead of the order it was
written. The traces in Part 3 are unedited console output from real runs, included because
a few of the framework's behaviors only become visible when you watch them happen.

Read in three passes:

- **Part 1, Assembly.** Everything built once, in the constructor, before a single message moves.
- **Part 2, A turn.** What happens from the moment the dispatcher presses enter.
- **Part 3, A real trace.** Actual console output from a run, mapped back to the code.

Files involved: `Program.cs`, `AI/ChatClientFactory.cs`, `AI/DispatchAgent.cs`,
`AI/SummaryAgent.cs`.

---

# Part 1: Assembly

## 1.1 The tracer, first

Registered before anything else in `Main`, because the `IChatClient` built in the next step
is wrapped in an OpenTelemetry decorator that emits into whatever provider is live when it
runs. `AddSource("DispatchDemo")` is the string that has to match the `sourceName` passed to
`UseOpenTelemetry` below, or the spans are created and dropped on the floor.

```csharp
using var tracerProvider = Sdk.CreateTracerProviderBuilder()
   .AddSource("DispatchDemo")
   .AddAzureMonitorTraceExporter()
   .Build();
```

## 1.2 `IChatClient`, the bottom of the stack

`IChatClient` is the framework's single interface for talking to a model, and
`AsIChatClient()` is what adapts the Azure SDK's own chat client onto it. That adaptation is
the reason nothing above this method knows or cares which provider is in use: Ollama and
Claude implement the same interface, so swapping providers is a change confined to this
file.

```csharp
public static IChatClient Create(IConfiguration config, string deploymentName)
{
    string endpoint = config["AzureOpenAI:Endpoint"]!;
    string apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")
        ?? throw new InvalidOperationException("Set AZURE_OPENAI_API_KEY");

    return new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey))
        .GetChatClient(deploymentName)
        .AsIChatClient()
        .AsBuilder()
        .UseOpenTelemetry(sourceName: "DispatchDemo", configure: cfg => cfg.EnableSensitiveData = true)
        .Build();
}
```

`AsBuilder()` opens a decorator pipeline over the client and `Build()` collapses it back
into one `IChatClient`, so callers cannot tell decorated from undecorated.
`EnableSensitiveData = true` puts prompt and tool-argument content into the traces, which is
what makes them useful in a lab and worth a second thought anywhere else.

## 1.3 One client, two agents

The factory is called once and the resulting client is handed to both agents. They
therefore share a deployment and a telemetry pipeline, and swapping the model in
`appsettings.json` moves both at the same time.

```csharp
string agentDeployment = config["AzureOpenAI:AgentDeployment"]!;
IChatClient chatClient = ChatClientFactory.Create(config, agentDeployment);

...
SummaryAgent sum = new SummaryAgent(repo, summaryrepo, chatClient);
...
await new DispatchAgent(main, repo, sum, chatClient, debug).RunAsync();
```

## 1.4 A method becomes a tool

`AIFunctionFactory.Create` reflects over a method and produces an `AIFunction`. It reads the
parameter names and types to generate the JSON schema the model is shown, and on the way
back in it deserializes the model's arguments into those parameters. A plain private
instance method is all it wants; the method group captures `this`, so the tool body can use
the class's fields.

```csharp
AIFunctionFactory.Create(GetStatus)
```

The method is completely ordinary. Nothing about it is framework-aware.

```csharp
[Description("Call this method to get the status of all units and incidents")]
private string GetStatus()
{
    return _service.GetStatus();
}
```

## 1.5 Descriptions are the whole contract

`[Description]` on the method becomes the tool description; `[Description]` on a parameter
becomes that parameter's description in the generated schema. This text is the only thing
the model has to reason from, so vagueness here surfaces later as wrong arguments. A tool
without a description still works, it just arrives as a bare name and the model has to guess.

```csharp
[Description("Call this method to report an incident")]
private async Task<string> ReportIncident(
    [Description("Type of Incidents:MedicalEmergency, Fire, CrimeInProgress, TrafficIncident, HazardousMaterial")] string type,
    [Description("Priority Level:Low, Medium, High, Critical")] string priority,
    [Description("Location of the incident")] string location,
    [Description("Short description of what is happening")] string description)
```

Note the parameters are `string`, not the enums themselves, with the legal values spelled
out in the description text. The method therefore parses them and returns a message on
failure rather than throwing, because a throw inside a tool is not something the model can
recover from.

```csharp
if (!Enum.TryParse<IncidentType>(type, true, out IncidentType itype)) return $"Invalid type: {type}";
if (!Enum.TryParse<IncidentPriority>(priority, true, out IncidentPriority ipriority)) return $"Invalid priority: {priority}";
```

Descriptions can also carry sequencing rules, which is how `DispatchUnit` tells the model it
needs real IDs before it calls:

```csharp
[Description("Call this method to dispatch a unit. Always call GetStatus first to get the exact unit and incident IDs before calling this.\r\n")]
private async Task<string> DispatchUnit(
    [Description("Get the correct unit id based on what kind of unit is wanted")] string unitId,
    [Description("Get the correct incident id based on the id, location, or description of the incident")] string incidentId)
```

## 1.6 The return value is the model's feedback channel

Whatever a tool returns is serialized and handed back to the model as the tool result. It is
not user-facing text. That is the reason these return specific diagnoses instead of a bare
failure: the model reads the string and corrects itself on the next attempt.

```csharp
bool i = _service.DispatchUnit(unitId, incidentId);
if (!i)
{
    Unit? bad = _service.GetUnits().FirstOrDefault(unit => unit.Id == unitId);
    if (bad == null) return $"No unit with id {unitId}. Call GetStatus for valid unit ids.";
    if (bad.Status != UnitStatus.Available) return $"{unitId} is not available - it is {bad.Status} on {bad.AssignIncidentId}. Choose a different unit.";

    Incident? target = _service.GetActiveIncidents().FirstOrDefault(incident => incident.Id == incidentId);
    if (target == null) return $"No active incident with id {incidentId}. Call GetStatus for valid incident ids.";

    return $"Could not dispatch {unitId} to {incidentId}.";
}
```

## 1.7 Gating a tool behind human approval

`ApprovalRequiredAIFunction` wraps an `AIFunction` so the model may request the call but
cannot execute it. When the model reaches for a wrapped tool, the framework does not run the
method: it returns a `ToolApprovalRequestContent` in the response and holds that call
suspended until a decision is sent back.

In this program the split is on side effects. `GetStatus` only reads, so it runs silently.
Everything that mutates domain state or writes to Mongo is wrapped, including the briefing,
because each briefing inserts a `SummaryRecord`.

```csharp
Tools =
[
    AIFunctionFactory.Create(GetStatus),                                          // reads only, runs immediately
    new ApprovalRequiredAIFunction(AIFunctionFactory.Create(ReportIncident)),     // everything below needs a yes
    new ApprovalRequiredAIFunction(AIFunctionFactory.Create(DispatchUnit)),
    new ApprovalRequiredAIFunction(AIFunctionFactory.Create(MarkArrived)),
    new ApprovalRequiredAIFunction(AIFunctionFactory.Create(ResolveIncident)),
    new ApprovalRequiredAIFunction(AIFunctionFactory.Create(SummarizeIncidents)),
]
```

## 1.8 Instructions

`ChatOptions.Instructions` is the system prompt, resent on every single request. It is the
one lever that applies to every turn without any code, which is why the rules about tool
sequencing, denial handling, and when a briefing is appropriate live here rather than in C#.

```csharp
Instructions = @"You are a city dispatch center AI assistant. You manage incidents and dispatch units on behalf of a human dispatcher who confirms every action.

CONVERSATION RULES:
- Listen carefully and extract all information from what the user says before asking for anything.
- Only ask for information that is genuinely missing. Never ask for something already stated.
...

TOOL CALL RULES:
- Never call ReportIncident unless you have: type, priority, location, and description. All four must be present.
- Never call DispatchUnit or ResolveIncident without calling GetStatus first to confirm exact IDs.
- GetStatus never requires approval. Call it silently and use the result immediately.
- Once a tool call is approved and executes successfully, never call it again for the same incident.
- If a tool call is denied, stop immediately. Ask the user exactly what was wrong before doing anything else.
- Never call multiple tools speculatively. Only call a tool when you are certain all arguments are correct.

AFTER DENIAL:
- Do not retry the same call with the same arguments.
- Do not call GetStatus defensively.
- Do not ask for information already provided.
- Ask one specific question: what was wrong with the previous attempt.
...",
```

Worth knowing when reading this file: almost every line in `TOOL CALL RULES` and
`AFTER DENIAL` exists because the model did that thing. They are fixes, not precautions.

## 1.9 Chat client becomes agent

`AsAIAgent()` is the promotion from "something that answers chat messages" to "something
with a name, standing instructions, a tool surface, and sessions". `ChatClientAgentOptions`
carries the agent's identity, and `ChatOptions` carries what is sent on every request, which
is why `Instructions` and `Tools` live inside it. The result is an `AIAgent`, and that is the
only type the rest of the class talks to.

```csharp
_agent = chatClient
    .AsAIAgent(new ChatClientAgentOptions
    {
        Name = "Dispatch Agent",
        ChatOptions = new()
        {
            Instructions = @"You are a city dispatch center AI assistant. ...",
            Tools =
            [
                AIFunctionFactory.Create(GetStatus),
                new ApprovalRequiredAIFunction(AIFunctionFactory.Create(ReportIncident)),
                new ApprovalRequiredAIFunction(AIFunctionFactory.Create(DispatchUnit)),
                new ApprovalRequiredAIFunction(AIFunctionFactory.Create(MarkArrived)),
                new ApprovalRequiredAIFunction(AIFunctionFactory.Create(ResolveIncident)),
                new ApprovalRequiredAIFunction(AIFunctionFactory.Create(SummarizeIncidents)),
            ]
        }
    }).AsBuilder()
      .Use(LoggingMiddleware)
      .Use(FunctionGateMiddleware)
      .Build();
```

## 1.10 Middleware, layer one: the whole turn

`AsBuilder()` on the agent opens the same style of decorator pipeline the chat client had.
This `Use` overload fires once per `RunAsync` and receives the incoming messages, the
session, the options, and `next`. Everything before `await next(...)` runs before the model
sees the message; everything after runs once the model has finished and all its tool calls
have completed.

```csharp
private static async Task LoggingMiddleware(
    IEnumerable<ChatMessage> messages,
    AgentSession? session,
    AgentRunOptions? options,
    Func<IEnumerable<ChatMessage>, AgentSession?, AgentRunOptions?, CancellationToken, Task> next,
    CancellationToken ct)
{
    Console.WriteLine("[TURN START]");
    await next(messages, session, options, ct);
    Console.WriteLine("[TURN END]");
}
```

This is the layer for turn-level concerns: timing a whole response, enforcing a token
budget, or rejecting a turn outright before the model is ever invoked.

## 1.11 Middleware, layer two: each tool call

The other `Use` overload fires per tool invocation rather than per turn, so it does not run
at all on a plain text answer. `FunctionInvocationContext` carries the call being made, and
`ctx.Function.Name` is how you tell which tool you are inside. Returning something other
than `await next(...)` substitutes the result the model receives without the real method
ever executing, which is the hook for an allow-list or a hard block.

```csharp
private static async ValueTask<object?> FunctionGateMiddleware(
    AIAgent agent,
    FunctionInvocationContext ctx,
    Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next,
    CancellationToken ct)
{
    Console.WriteLine($"[TOOL] Calling: {ctx.Function.Name}");
    object? result = await next(ctx, ct);
    Console.WriteLine($"[TOOL] Completed: {ctx.Function.Name}");
    return result;
}
```

This is the layer for tool-level concerns: argument validation before the method is reached,
per-tool timing, or refusing an unauthorized tool.

## 1.12 The second agent

Same construction, narrower job, no tools at all. Splitting the briefing into its own agent
keeps the dispatch agent's instructions about dispatching, and each agent gets to have one
personality instead of a prompt that tries to be two things.

```csharp
_agent = chatClient
    .AsAIAgent(new ChatClientAgentOptions
    {
        Name = "Summary Agent",
        ChatOptions = new()
        {
            Instructions = "You are a shift handoff assistant. Given raw incident data, produce a compact briefing a dispatcher can read in 30 seconds. Cover status, priority, and which units are committed. ... Do not invent or assume any information not present in the data."
        }
    });
```

It is called with no session, because every briefing is a one-shot request carrying
everything it needs in the message body:

```csharp
AgentResponse response = await _agent.RunAsync(incidentData);

string? text = response.Messages
       .Where(m => m.Role == ChatRole.Assistant).LastOrDefault()?.Text;
```

## 1.13 Reaching the second agent from the first

The dispatch agent gets at the summary agent through an ordinary wrapper method registered
as a tool. From the framework's side there is nothing special about it: a described method
returning a string. This is all "agent as a tool" requires.

```csharp
[Description("Call this method to generate a shift handoff briefing of all active incidents")]
private async Task<string> SummarizeIncidents()
{
    return await _summaryagent.SummarizeAsync();
}
```

---

# Part 2: A turn

## 2.1 Creating a session and priming it

`CreateSessionAsync()` returns an `AgentSession`, which is the conversation memory. Pass it
to every `RunAsync` and history accumulates; omit it and each call is independent. Here one
session is created for the whole shift, and the first thing put into it is the carried-over
briefing, so every sentence the dispatcher types afterwards is read with that context
already in scope.

```csharp
string context = await _summaryagent.SummarizeAsync();

AgentSession session = await _agent.CreateSessionAsync();
await _agent.RunAsync($"[SHIFT START BRIEFING] The following is a summary of active incidents carried over from the previous session. Use this as context for the current shift:\n\n{context}", session);
```

## 2.2 Running a turn

Two overloads are used. The `string` one is shorthand for a single user message. The
`List<ChatMessage>` one is what you need to send structured content back, which is how
approval decisions go in. Both return an `AgentResponse`.

```csharp
AgentResponse response = await _agent.RunAsync(input, session);              // plain text turn
response = await _agent.RunAsync(approvalResponses, session);                // structured turn
```

## 2.3 What comes back

`AgentResponse.Messages` is everything the turn produced, not just the answer: assistant
text, requested tool calls, tool results, and pending approvals are all in there. Each
`ChatMessage` holds a list of `AIContent`, so pulling anything specific out means flattening
`Contents` across messages and filtering by type. That two-line pattern is how you find
anything in a response.

```csharp
List<ToolApprovalRequestContent> approvalRequests = response.Messages
    .SelectMany(m => m.Contents)
    .OfType<ToolApprovalRequestContent>()
    .ToList();
```

## 2.4 Reading a pending approval

This is the least discoverable part of the whole flow. A `ToolApprovalRequestContent`
carries the suspended call on `.ToolCall`, typed as the base content type, so you cast it to
`FunctionCallContent` to reach `.Name` and `.Arguments`. `Arguments` is a dictionary of
parameter name to value, which is exactly what a human needs to see before saying yes.

```csharp
string funcName = ((FunctionCallContent)req.ToolCall).Name;

Console.WriteLine($"\n[APPROVAL NEEDED] for {funcName}");
Console.WriteLine($"Arguments: {string.Join("\n ", ((FunctionCallContent)req.ToolCall).Arguments.Select(a => $"{a.Key}: {a.Value}"))}");
```

## 2.5 Answering it

`req.CreateResponse(bool)` builds the approval content already correlated to that specific
suspended call, so call IDs never have to be tracked by hand. It is wrapped in a
`ChatMessage` with `ChatRole.User`, because the decision belongs to the user, and sent back
through `RunAsync` as the next turn.

```csharp
bool approved = Console.ReadLine()?.Trim().ToLower() == "y";
return new ChatMessage(ChatRole.User, [req.CreateResponse(approved)]);
```

## 2.6 The drain loop

The outer `while` is the important part. Answering one round of approvals can produce
another, because the model gets its tool result, keeps working, and asks for the next call.
Looping until the list comes back empty is what guarantees no call is left suspended and the
conversation cannot wedge.

```csharp
AgentResponse response = await _agent.RunAsync(input, session);

List<ToolApprovalRequestContent> approvalRequests = response.Messages
    .SelectMany(m => m.Contents)
    .OfType<ToolApprovalRequestContent>()
    .ToList();

while(approvalRequests.Count > 0)
{
    List<ChatMessage> approvalResponses = approvalRequests.ConvertAll(req =>
    {
        string funcName = ((FunctionCallContent)req.ToolCall).Name;
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"\n[APPROVAL NEEDED] for {funcName}");
        Console.WriteLine($"Arguments: {string.Join("\n ", ((FunctionCallContent)req.ToolCall).Arguments.Select(a => $"{a.Key}: {a.Value}"))}");
        Console.ResetColor();
        Console.WriteLine("Approve? (y/n)");
        bool approved = Console.ReadLine()?.Trim().ToLower() == "y";
        return new ChatMessage(ChatRole.User, [req.CreateResponse(approved)]);
    });

    response = await _agent.RunAsync(approvalResponses, session);
    approvalRequests = response.Messages
    .SelectMany(m => m.Contents)
    .OfType<ToolApprovalRequestContent>()
    .ToList();
}
```

`ConvertAll` means all approvals in one round are collected and sent back together, which
matters because the model can request more than one call in a single turn.

## 2.7 Getting the text to print

Filter to `ChatRole.Assistant` and take the last one. `LastOrDefault` rather than `Last`,
because a turn can legitimately end with no assistant message at all, for example a denial
that produced only tool results, and `Last()` throws on an empty sequence.

```csharp
string? finalText = response.Messages
    .Where(m => m.Role == ChatRole.Assistant).LastOrDefault()?.Text;

if (string.IsNullOrWhiteSpace(finalText))
{
    Console.WriteLine("[no reply from the agent - rephrase or try again]");
}
else
{
    Console.WriteLine(finalText);
}
```

## 2.8 Seeing everything, on demand

`InspectResponse` exists to make the message walk visible when something is behaving
strangely. These four `AIContent` subtypes are what a tool-using turn produces, and
`FunctionCallContent.CallId` is what correlates a requested call with the
`FunctionResultContent` that answers it. `response.Usage` carries the token counts for the
turn.

```csharp
private static void InspectResponse(AgentResponse response)
{
    Console.WriteLine($"[USAGE] Input: {response.Usage?.InputTokenCount} | Output: {response.Usage?.OutputTokenCount} | Total: {response.Usage?.TotalTokenCount}");

    foreach(ChatMessage message in response.Messages)
    {
        Console.WriteLine($"\n[{message.Role}]");

        foreach(AIContent content in message.Contents)
        {
            switch (content)
            {
                case TextContent t:
                    Console.WriteLine($"  TEXT: {t.Text}");
                    break;

                case FunctionCallContent fc:
                    Console.WriteLine($"  TOOL CALL: {fc.Name} | CallId: {fc.CallId}");
                    foreach (var arg in fc.Arguments!)
                        Console.WriteLine($"    {arg.Key}: {arg.Value}");
                    break;

                case FunctionResultContent fr:
                    Console.WriteLine($"  TOOL RESULT: CallId: {fr.CallId} | Result: {fr.Result}");
                    break;

                case ToolApprovalRequestContent ta:
                    Console.WriteLine($"  APPROVAL REQUEST: {((FunctionCallContent)ta.ToolCall).Name}");
                    break;
            }
        }
    }
}
```

Behind the `--verbose` flag, so the normal shift is not drowned in it:

```csharp
if (_debug) InspectResponse(response);
```

---

# Part 3: A real trace

## 3.1 A read-only tool, one turn

Asking for the status board. `LoggingMiddleware` brackets the turn,
`FunctionGateMiddleware` brackets the call inside it, and because `GetStatus` is not
approval-wrapped it executes without stopping.

```
[TURN START]
[TOOL] Calling: GetStatus
[TOOL] Completed: GetStatus
[TURN END]
```

One turn, one tool, no interruption. This is the simple case.

## 3.2 An approval-gated tool, two turns

Reporting an incident. This trace is the one worth studying, because a single approved
action costs **two** turns and the boundary between them is where the framework suspended
the call.

```
[TURN START]
[TURN END]

[APPROVAL NEEDED] for ReportIncident
Arguments: type: fire
 priority: low
 location: 100 Test Lane
 description: small trash can fire in the parking lot
Approve? (y/n)
[TURN START]
[TOOL] Calling: ReportIncident
[TOOL] Completed: ReportIncident
[TURN END]
```

Notice the first `[TURN START]` and `[TURN END]` have **nothing between them**. No `[TOOL]`
line, because `FunctionGateMiddleware` never fired: the method was never invoked. The model
asked, `ApprovalRequiredAIFunction` intercepted, and the turn ended carrying a
`ToolApprovalRequestContent` instead of a result.

The `[APPROVAL NEEDED]` block is the console printing what section 2.4 pulled off
`.ToolCall`, so the argument list is literally the model's proposed arguments.

The second `[TURN START]` is `RunAsync(approvalResponses, session)` from the drain loop.
Only now does `FunctionGateMiddleware` fire and the real method run.

## 3.3 Startup

The domain replay, then the briefing turn, then the banner. The single bracketed turn before
the banner is the `[SHIFT START BRIEFING]` priming call from section 2.1, and it produces no
tool calls because it is only loading context into the session.

```
[Incident] Responding
 [Unit]Lincroft Med is dispatched to INC-1003
 [Unit]Lincroft Med arrived on scene at INC-1003
[Incident] OnScene
[TURN START]
[TURN END]
╔══════════════════════════════════════════╗
║       CITY DISPATCH CENTER — AI          ║
║       Type a situation. I'll handle it.  ║
║       Type 'quit' to end your shift.     ║
╚══════════════════════════════════════════╝
```

## 3.4 Instructions doing the work

Asking to dispatch a unit that is already committed. There is no `[APPROVAL NEEDED]` here
at all, because the model called `GetStatus`, read that `AMB001` was on scene, and refused
on its own. The guard in `DispatchService` would have caught it, but the turn never got that
far.

```
[TURN START]
[TOOL] Calling: GetStatus
[TOOL] Completed: GetStatus
[TURN END]
I can't do that as requested: AMB001 is currently on scene at INC-1003, so it is not
available to dispatch to INC-1005.
```

A tool that describes state honestly prevents bad calls upstream of the code that would
reject them. That is the whole argument for spending effort on `GetStatus` output and on
tool return strings.

---

# Reference

## Framework surface used in this program

| Type or member | Role |
|---|---|
| `IChatClient` | provider-agnostic model interface |
| `.AsIChatClient()` | adapts the Azure SDK client onto it |
| `.AsBuilder()` / `.Build()` | decorator pipeline, used on both the client and the agent |
| `.UseOpenTelemetry(...)` | traces every model call, `sourceName` must match `AddSource` |
| `.AsAIAgent(...)` | chat client to agent |
| `AIAgent` | the type everything downstream depends on |
| `ChatClientAgentOptions` | agent identity |
| `ChatOptions.Instructions` | system prompt, resent every request |
| `ChatOptions.Tools` | the callable surface |
| `AIFunctionFactory.Create(method)` | method to tool, schema generated by reflection |
| `[Description]` | tool and parameter text the model reasons from |
| `ApprovalRequiredAIFunction` | suspends a call until a human decides |
| `.Use(...)` run overload | wraps a whole turn |
| `.Use(...)` function overload | wraps each tool call |
| `FunctionInvocationContext` | the call being wrapped, `.Function.Name` |
| `CreateSessionAsync()` / `AgentSession` | conversation memory |
| `RunAsync(string, session)` | text turn |
| `RunAsync(List<ChatMessage>, session)` | structured turn, used for approvals |
| `AgentResponse.Messages` | everything the turn produced |
| `AgentResponse.Usage` | input, output, total token counts |
| `ChatMessage` / `ChatRole` | a message and its author |
| `AIContent` | base type of message content |
| `TextContent` | assistant or user text |
| `FunctionCallContent` | requested call: `.Name`, `.Arguments`, `.CallId` |
| `FunctionResultContent` | what the call returned: `.CallId`, `.Result` |
| `ToolApprovalRequestContent` | pending approval: `.ToolCall`, `.CreateResponse(bool)` |

## Where each concern lives

| Concern | Handled by |
|---|---|
| Which provider and model | `ChatClientFactory` only |
| What the agent will and will not do | `Instructions` |
| What the agent can do | `Tools` |
| What each tool means | `[Description]` |
| Who authorizes a side effect | `ApprovalRequiredAIFunction` plus the drain loop |
| Telling the model it got something wrong | the tool's return string |
| Conversation memory | `AgentSession` |
| Observing a turn | run middleware |
| Observing or blocking a call | function middleware |
| Deep debugging | `InspectResponse` with `--verbose` |
| Traces off-box | `UseOpenTelemetry` plus the Azure Monitor exporter |

## Framework surface deliberately not used

Available and untouched, so an absence here is a decision rather than an oversight:
structured output (`RunAsync<T>`, `ChatResponseFormat`), streaming responses, context
providers and retrieval, `ChatHistoryProvider`, workflows and orchestration with
human-in-the-loop executors, and additional agents acting as validators or critics. The
`Anthropic` and `OllamaSharp` package references are present but unused, reserved for
demonstrating that the same `IChatClient` accepts a different provider.
