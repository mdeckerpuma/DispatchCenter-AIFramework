using Azure;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OllamaSharp;

internal class Program
{
    static DispatchCenter main = new DispatchCenter();

    // #1  Every incident/dispatch captured as a formatted string line (also feeds context).
    static List<string> incidentLog = new();

    // Lets the /switch command and the switch_model tool flip the active LLM at runtime.
    static SwitchableChatClient? clientSwitch;

    // Concept 1: recall is a HUMAN-ONLY action. The AI is hard-blocked from the recall
    // tool by the gate; the only way units get recalled is the one-paste console command
    // /authorize recall <incident or location>, which confirms and executes directly.

    [Description("Report a new incident to the dispatch center.")]
    static string ReportIncident(
     [Description("Type of incident: MedicalEmergency, Fire, CrimeInProgress, TrafficIncident, HazardousMaterial")] string type,
     [Description("Priority level: Low, Medium, High, Critical")] string priority,
     [Description("Location of the incident")] string location,
     [Description("Description of the incident")] string description)
    {
        IncidentType itype;
        if (type == "MedicalEmergency") { itype = IncidentType.MedicalEmergency; }
        else if (type == "Fire") { itype = IncidentType.Fire; }
        else if (type == "CrimeInProgress") { itype = IncidentType.CrimeInProgress; }
        else if (type == "TrafficIncident") { itype = IncidentType.TrafficIncident; }
        else if (type == "HazardousMaterial") { itype = IncidentType.HazardousMaterial; }
        else { return $"Unknown incident type: {type}"; }

        IncidentPriority ipriority;
        if (priority == "Low") { ipriority = IncidentPriority.Low; }
        else if (priority == "Medium") { ipriority = IncidentPriority.Medium; }
        else if (priority == "High") { ipriority = IncidentPriority.High; }
        else if (priority == "Critical") { ipriority = IncidentPriority.Critical; }
        else { return $"Unknown priority: {priority}"; }

        Incident newIncident = main.ReportIncident(itype, ipriority, location, description);
        return $"Incident logged as {newIncident.Id} — {newIncident.Type} at {newIncident.Location} [{newIncident.Priority}]";
    }

    [Description("Dispatch an available unit to an active incident.")]
    static string DispatchUnit(
        [Description("The unit ID to dispatch, e.g. POL111")] string unitId,
        [Description("The incident ID to dispatch to, e.g. INC-1000")] string incidentId)
    {
        bool result = main.DisbatchUnit(unitId, incidentId);
        if (result)
        {
            return $"Unit {unitId} dispatched to {incidentId}.";
        }
        else
        {
            return "Dispatch failed — check unit is available and incident is active.";
        }
    }

    [Description("Mark a dispatched unit as arrived on scene.")]
    static string MarkArrived(
        [Description("The unit ID to mark as arrived, e.g. POL111")] string unitId)
    {
        bool result = main.MarkArrived(unitId);
        if (result)
        {
            return $"Unit {unitId} marked on scene.";
        }
        else
        {
            return "Failed — unit not found or not dispatched.";
        }
    }

    [Description("Resolve an active incident and return all assigned units to service.")]
    static string ResolveIncident(
        [Description("The incident ID to resolve, e.g. INC-1000")] string incidentId)
    {
        bool result = main.ResolveIncident(incidentId);
        if (result)
        {
            return $"Incident {incidentId} resolved. Units returned to service.";
        }
        else
        {
            return "Failed — incident not found or already resolved.";
        }
    }

    [Description("Get the current status of all units and active incidents. Call this first to find unit and incident IDs before dispatching or resolving.")]
    static string GetStatus()
    {
        StringBuilder sb = new StringBuilder();

        sb.AppendLine("ACTIVE INCIDENTS:");
        foreach (Incident active in main.GetActiveIncidents())
        {
            sb.AppendLine($"  [{active.Priority}] {active.Id} — {active.Type} at {active.Location} ({active.Status})");
        }

        sb.AppendLine("\nUNITS:");
        foreach (Unit unit in main.GetUnits())
        {
            if (unit.AssignIncidentId != null)
            {
                sb.AppendLine($"  [{unit.Status}] {unit.Id} — {unit.Name} ({unit.Type}) → {unit.AssignIncidentId}");
            }
            else
            {
                sb.AppendLine($"  [AVAILABLE] {unit.Id} — {unit.Name} ({unit.Type})");
            }
        }

        return sb.ToString();
    }


    // Resolve a user's phrase to a real active incident id: accepts an id ("INC-1000",
    // "inc1000") or a location ("long branch"). Used by /authorize recall so authorization
    // is tied to a specific incident, not a blanket unlock.
    static string? ResolveIncidentId(string query)
    {
        string normQ = query.Replace("-", "").Replace(" ", "").ToLowerInvariant();
        foreach (Incident i in main.GetActiveIncidents())
        {
            string normId = i.Id.Replace("-", "").Replace(" ", "").ToLowerInvariant();
            string normLoc = i.Location.Replace(" ", "").ToLowerInvariant();
            if (normId == normQ) return i.Id;
            if (normLoc.Length > 0 && (normLoc.Contains(normQ) || normQ.Contains(normLoc))) return i.Id;
        }
        return null;
    }

    // #4  Snapshot of live dispatch state, injected into context each turn (see
    //     DispatchContextProvider). This is the "retrieval" the model reads from.
    static string BuildStateSnapshot()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("Units:");
        foreach (Unit u in main.GetUnits())
            sb.AppendLine($"  {u.Id} {u.Type} [{u.Status}]{(u.AssignIncidentId != null ? " -> " + u.AssignIncidentId : "")}");
        sb.AppendLine("Active incidents:");
        foreach (Incident i in main.GetActiveIncidents())
            sb.AppendLine($"  {i.Id} {i.Type} [{i.Priority}] at {i.Location} ({i.Status})");

        return sb.ToString();
    }

    // Concept 1: a PRIVILEGED tool. Recalls every unit assigned to an incident, sending
    // them back in service. The gate refuses it unless the human armed it with
    // /authorize recall (one-shot). High-consequence, so the AI can't trigger it alone.
    [Description("Recall ALL units currently assigned to an incident, returning them to service. Requires prior human authorization.")]
    static string RecallAllUnitsFromIncident([Description("The incident ID to recall all units from, e.g. INC-1000")] string incidentId)
    {
        List<Unit> assigned = main.GetUnits().Where(u => u.AssignIncidentId == incidentId).ToList();
        if (assigned.Count == 0) return $"No units are currently assigned to '{incidentId}'.";
        foreach (Unit u in assigned) u.ClearAndReturn();
        return $"Recalled {assigned.Count} unit(s) from {incidentId}: {string.Join(", ", assigned.Select(u => u.Id))}.";
    }

    // #2  A tool the MODEL can call to change providers, so "switch yourself to ollama"
    //     in plain English works. Mirrors the /switch console command.
    [Description("Switch which LLM provider powers this agent. Valid providers: azure, ollama, claude.")]
    static string switch_model([Description("Provider name: azure, ollama, or claude")] string provider)
    {
        if (clientSwitch == null) return "Model switching is not initialized.";
        bool ok = clientSwitch.SetActive(provider);
        return ok
            ? $"Switched active model to '{clientSwitch.ActiveName}'."
            : $"Unknown or unavailable provider '{provider}'. Options: {clientSwitch.Available}.";
    }

    // ────────────────────────────────────────────────────────────────────────
    //  MIDDLEWARE (new — none of the existing tool methods above are touched)
    // ────────────────────────────────────────────────────────────────────────

    // #5  AGENT-RUN middleware. Wraps every turn. Prints debug info before/after
    //     the model runs. Signature matches AIAgentBuilder.Use(messages, session,
    //     options, next, ct).  (This one is done — my part of the hybrid.)
    static async Task LoggingMiddleware(
        IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        Func<IEnumerable<Microsoft.Extensions.AI.ChatMessage>, AgentSession?, AgentRunOptions?, CancellationToken, Task> next,
        CancellationToken ct)
    {
        foreach (Microsoft.Extensions.AI.ChatMessage m in messages)
        {
            if (!string.IsNullOrWhiteSpace(m.Text))
                Console.WriteLine($"[LOG] → {m.Role}: {m.Text}");
        }

        await next(messages, session, options, ct);

        Console.WriteLine("[LOG] run complete");
    }

    // #6  FUNCTION-CALLING middleware — THE HARD RESTRICTION LAYER (the star).
    //     EVERY tool the model tries to invoke passes through here FIRST, before it
    //     runs. This is where "the AI only calls what it's allowed to" is enforced
    //     in code, not by hoping the prompt behaves. Signature matches
    //     FunctionInvocationDelegatingAgentBuilderExtensions.Use(agent, ctx, next, ct).
    //
    //     >>> YOU WRITE THIS BODY. Right now it just passes everything through. <<<
    static async ValueTask<object?> FunctionGateMiddleware(
        AIAgent agent,
        Microsoft.Extensions.AI.FunctionInvocationContext ctx,
        Func<Microsoft.Extensions.AI.FunctionInvocationContext, CancellationToken, ValueTask<object?>> next,
        CancellationToken ct)
    {
        string requested = ctx.Function.Name;
        Console.WriteLine($"[GATE] agent wants to call: {requested}");

        // LAYER 1 — WHAT can be called: permission check against the allow-list.
        string[] allowed = { "GetStatus", "ReportIncident", "DispatchUnit", "MarkArrived", "ResolveIncident", "switch_model", "RecallAllUnitsFromIncident" };
        if (!allowed.Contains(requested))
        {
            Console.WriteLine($"[GATE] BLOCKED {requested} — not an approved tool.");
            return $"Blocked: '{requested}' is not an approved tool and was not run.";
        }

        // CONCEPT 1 — privileged tool. RecallAllUnitsFromIncident is denied outright unless
        // the human has armed it with /authorize recall. Arming is consumed on use (one-shot),
        // so the AI can never recall units on its own initiative.
        // CONCEPT 1 — the AI is NEVER allowed to recall units itself. If the model proposes
        // it, block outright and point the user at the human command. Recall only happens
        // through /authorize recall <incident or location>.
        if (requested == "RecallAllUnitsFromIncident")
        {
            Console.WriteLine("[GATE] BLOCKED RecallAllUnitsFromIncident — the AI cannot recall units. A human must run /authorize recall <incident or location>.");
            return "Blocked: recall is a human-authorized action. Ask the user to run /authorize recall <incident or location>.";
        }

        // LAYER 2 — HOW it's called: inspect the ARGUMENTS for DispatchUnit and reject
        // calls that are wrong no matter what the model decided. This is code-owned
        // business logic the prompt can't be trusted to enforce.
        if (requested == "DispatchUnit")
        {
            // Pull the values the model chose for the parameters.
            ctx.Arguments.TryGetValue("unitId", out object? unitIdObj);
            ctx.Arguments.TryGetValue("incidentId", out object? incidentIdObj);
            string unitId = unitIdObj?.ToString() ?? "";
            string incidentId = incidentIdObj?.ToString() ?? "";

            // Look up the real unit and the real active incident.
            Unit? unit = main.GetUnits().FirstOrDefault(u => u.Id == unitId);
            Incident? incident = main.GetActiveIncidents().FirstOrDefault(i => i.Id == incidentId);

            if (unit == null)
            {
                Console.WriteLine($"[GATE] BLOCKED DispatchUnit — no such unit '{unitId}'.");
                return $"Blocked: unit '{unitId}' does not exist.";
            }
            if (incident == null)
            {
                Console.WriteLine($"[GATE] BLOCKED DispatchUnit — '{incidentId}' is not an active incident.");
                return $"Blocked: '{incidentId}' is not an active incident.";
            }
            if (unit.Status != UnitStatus.Available)
            {
                Console.WriteLine($"[GATE] BLOCKED DispatchUnit — {unitId} is {unit.Status}, not available.");
                return $"Blocked: unit '{unitId}' is {unit.Status}, not available.";
            }
            // (Unit-type-vs-incident-type restriction removed by request: any available
            // unit may now be sent to any active incident.)
        }

        // Passed every check -> let it through. The real tool runs (and your y/n
        // approval prompt still fires afterward).
        return await next(ctx, ct);
    }

    static async Task Main(string[] args)
    {
        main.IncidentReported += (_, e) => Console.WriteLine($"\n[INCIDENT] {e.Incident.Id} reported at {e.Incident.Location}");
        main.UnitDispatched += (_, e) => Console.WriteLine($"[DISPATCH] {e.Unit.Id} → {e.Incident.Id}");
        main.AlertBroadcast += (_, msg) => Console.WriteLine($"[ALERT] {msg}");

        // #1  Also record each incident/dispatch as a formatted string line.
        main.IncidentReported += (_, e) => incidentLog.Add($"{DateTime.Now:yyyy-MM-dd HH:mm} | INCIDENT | {e.Incident.Id} | {e.Incident.Type} | {e.Incident.Priority} | {e.Incident.Location}");
        main.UnitDispatched += (_, e) => incidentLog.Add($"{DateTime.Now:yyyy-MM-dd HH:mm} | DISPATCH | {e.Unit.Id} -> {e.Incident.Id}");

        main.AddUnit(new Unit("POL111", "Ride Combat", UnitType.Police));
        main.AddUnit(new Unit("POL222", "Zombilla", UnitType.Police));
        main.AddUnit(new Unit("AMB111", "Lincroft", UnitType.Ambulance));
        main.AddUnit(new Unit("AMB222", "Oceanport", UnitType.Ambulance));
        main.AddUnit(new Unit("FFR111", "Lincroft", UnitType.Firefighter));
        main.AddUnit(new Unit("FFR222", "Oceanport", UnitType.Firefighter));

        // Key-based Azure OpenAI auth (DefaultAzureCredential fails in our environment).
        // Set AZURE_OPENAI_API_KEY in your environment before running.
        string apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")
            ?? throw new InvalidOperationException("Set the AZURE_OPENAI_API_KEY environment variable.");

        // #2/#3  Build each provider as the SAME interface (IChatClient), then wrap them
        //        so the active one can be swapped at runtime. The agent is built ONCE over
        //        the wrapper; swapping the model keeps the conversation memory because
        //        history lives in the session, not the model.
        IChatClient azure = new AzureOpenAIClient(
                new Uri("https://squidopenai.openai.azure.com/"),
                new AzureKeyCredential(apiKey))
            .GetChatClient("gpt-4o-mini")
            .AsIChatClient();

        IChatClient ollama = new OllamaApiClient(new Uri("http://localhost:11434"), "llama3.2");

        clientSwitch = new SwitchableChatClient("azure", azure);
        clientSwitch.Register("ollama", ollama);

        // Claude — ready for fold-in. Set ANTHROPIC_API_KEY, then uncomment these lines:
        // string? claudeKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        // if (!string.IsNullOrEmpty(claudeKey))
        //     clientSwitch.Register("claude",
        //         new Anthropic.AnthropicClient(claudeKey).AsIChatClient("claude-sonnet-4-5"));

        AIAgent agent = clientSwitch
            .AsAIAgent(new ChatClientAgentOptions
            {
                Name = "DispatchAgent",
                // #4  Context storage/retrieval: this provider injects the live dispatch
                //     state into the model before EVERY turn, so the agent can answer
                //     "what is POL111 doing?" from retrieved context without a tool call.
                AIContextProviders = new List<AIContextProvider> { new DispatchContextProvider(BuildStateSnapshot) },
                ChatOptions = new()
                {
                    Instructions = "You are a city dispatch center assistant. Use GetStatus first to find current unit and incident IDs before dispatching or resolving. Always use exact IDs. After you report an incident you MAY offer to dispatch an appropriate available unit (report then dispatch is the ONE allowed chain); do not chain any other actions. For everything else, do only the single action the user explicitly asks for. If you just proposed a tool call and the user declined it, do not re-propose that same call again in that same turn; acknowledge and wait. But if the user later explicitly asks for that action again in a new message, go ahead and propose it.",
                    Tools =
                    [
                        AIFunctionFactory.Create(GetStatus),
                        AIFunctionFactory.Create(switch_model),
                        // Recall is exposed so the AI CAN attempt it, but the gate hard-blocks
                        // every AI attempt — recall is human-only via /authorize recall.
                        AIFunctionFactory.Create(RecallAllUnitsFromIncident),
                        new ApprovalRequiredAIFunction(AIFunctionFactory.Create(ReportIncident)),
                        new ApprovalRequiredAIFunction(AIFunctionFactory.Create(DispatchUnit)),
                        new ApprovalRequiredAIFunction(AIFunctionFactory.Create(MarkArrived)),
                        new ApprovalRequiredAIFunction(AIFunctionFactory.Create(ResolveIncident)),
                    ]
                }
            })
            .AsBuilder()
            .Use(LoggingMiddleware)      // #5 run middleware: prints debug info each turn
            .Use(FunctionGateMiddleware) // #6 function-calling middleware: allow-list + arg validation
            .Build();

        AgentSession session = await agent.CreateSessionAsync();

        Console.WriteLine("══════════════════════════════════════════");
        Console.WriteLine("  CITY DISPATCH CENTER — AI MODE");
        Console.WriteLine("══════════════════════════════════════════");
        Console.WriteLine("  Type what you need. Type 'quit' to exit.");
        Console.WriteLine("══════════════════════════════════════════\n");

        // Carries any calls the agent chained AFTER your decision. We never answer them
        // by re-invoking the model in a loop (that just makes it re-propose the same call
        // forever — the infinite loop). Instead we decline them together with your NEXT
        // message, so every turn terminates and control always returns to you.
        List<Microsoft.Extensions.AI.ChatMessage> deferredResponses = new();

        while (true)
        {
            Console.Write("You: ");
            string input = Console.ReadLine()?.Trim() ?? "";
            if (input.ToLower() == "quit") break;
            if (string.IsNullOrEmpty(input)) continue;

            // ---- console commands (handled locally, NOT sent to the model) ----

            // #2  /switch azure|ollama|claude — flip the active model. Memory persists.
            if (input.StartsWith("/switch ", StringComparison.OrdinalIgnoreCase))
            {
                string target = input.Substring("/switch ".Length).Trim();
                bool ok = clientSwitch!.SetActive(target);
                Console.WriteLine(ok
                    ? $"\n[SWITCH] Active model is now '{clientSwitch.ActiveName}'. Conversation memory is preserved.\n"
                    : $"\n[SWITCH] Unknown provider '{target}'. Options: {clientSwitch.Available}.\n");
                continue;
            }

            // CONCEPT 1 — one-paste, human-authorized recall. "/authorize recall <incident
            // or location>" resolves the incident, asks you to confirm, and recalls its
            // units right here. This is the ONLY way units get recalled; the AI is
            // hard-blocked from the recall tool (see the gate).
            // e.g.  /authorize recall INC-1000   or   /authorize recall oceanport
            if (input.StartsWith("/authorize recall", StringComparison.OrdinalIgnoreCase))
            {
                string arg = input.Substring("/authorize recall".Length).Trim();
                foreach (string filler in new[] { "all units from ", "all units ", "units from ", "from " })
                    if (arg.StartsWith(filler, StringComparison.OrdinalIgnoreCase)) { arg = arg.Substring(filler.Length).Trim(); break; }

                if (string.IsNullOrEmpty(arg))
                {
                    Console.WriteLine("\n[AUTHORIZE] Say which incident, e.g. /authorize recall INC-1000  or  /authorize recall oceanport\n");
                    continue;
                }
                string? resolved = ResolveIncidentId(arg);
                if (resolved == null)
                {
                    Console.WriteLine($"\n[AUTHORIZE] No active incident matches '{arg}'.\n");
                    continue;
                }

                Console.WriteLine("\n══════════════════════════════════════════");
                Console.WriteLine("  RECALL — HUMAN AUTHORIZED");
                Console.WriteLine($"  Recall ALL units from {resolved}?");
                Console.WriteLine("══════════════════════════════════════════");
                Console.Write("Approve? (y/n): ");
                if (Console.ReadLine()?.Trim().ToLower() == "y")
                    Console.WriteLine("[RECALL] " + RecallAllUnitsFromIncident(resolved));
                else
                    Console.WriteLine("[RECALL] Cancelled.");
                Console.WriteLine();
                continue;
            }

            // #1  /log — dump the incidents captured as formatted strings.
            if (input.Equals("/log", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("\n[INCIDENT LOG]");
                if (incidentLog.Count == 0) Console.WriteLine("  (empty)");
                foreach (string line in incidentLog) Console.WriteLine("  " + line);
                Console.WriteLine();
                continue;
            }

            // #3  /report <messy text> — structured output. The model extracts a typed
            //     IncidentReport; fields it wasn't told are left null ("idk"), not guessed.
            if (input.StartsWith("/report ", StringComparison.OrdinalIgnoreCase))
            {
                string text = input.Substring("/report ".Length);
                // Extract via the chat client DIRECTLY — no agent, no tools, no approval.
                // (Going through the agent let the model call the approval-gated
                // ReportIncident tool, which left a dangling approval request and crashed.)
                // Instructions force "idk" (null) instead of guessing, and constrain the
                // enum-like fields to real values.
                ChatOptions extractOptions = new ChatOptions
                {
                    Instructions =
                        "Extract an IncidentReport from the user's text. Fill a field ONLY if the " +
                        "user explicitly stated it; otherwise leave it null. Never guess or invent. " +
                        "type must be exactly one of MedicalEmergency, Fire, CrimeInProgress, " +
                        "TrafficIncident, HazardousMaterial, or null if unclear. priority must be " +
                        "exactly one of Low, Medium, High, Critical, or null if the user did not " +
                        "state the urgency."
                };
                ChatResponse<IncidentReport> typed = await clientSwitch!.GetResponseAsync<IncidentReport>(text, extractOptions);
                IncidentReport r = typed.Result;
                Console.WriteLine("\n[STRUCTURED OUTPUT] Extracted a typed object. '(idk)' = model left it null instead of guessing:");
                Console.WriteLine($"  type        = {r.type ?? "(idk)"}");
                Console.WriteLine($"  priority    = {r.priority ?? "(idk)"}");
                Console.WriteLine($"  location    = {r.location ?? "(idk)"}");
                Console.WriteLine($"  description = {r.description ?? "(idk)"}");
                Console.WriteLine();
                continue;
            }

            // This turn's payload = declines for anything the agent left hanging last
            // turn + your new message, sent in ONE call so nothing dangles.
            List<Microsoft.Extensions.AI.ChatMessage> turnMessages = new(deferredResponses)
            {
                new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, input)
            };
            deferredResponses.Clear();

            AgentResponse response = await agent.RunAsync(turnMessages, session);

            List<ToolApprovalRequestContent> approvalRequests = response.Messages
                .SelectMany(m => m.Contents)
                .OfType<ToolApprovalRequestContent>()
                .ToList();

            // CONCEPT 2 — approval by rounds. Round 1 answers what you typed: prompt for
            // everything the model proposed. After that, the ONLY follow-up the agent may
            // chain is DispatchUnit (report -> dispatch); any other chained call is
            // auto-declined. You keep chaining while you say YES; a NO stops it. The round
            // cap guarantees this always terminates (no infinite loop).
            int approvalRound = 0;
            bool stopChaining = false;
            while (approvalRequests.Count > 0 && !stopChaining && approvalRound < 4)
            {
                approvalRound++;
                bool anyRejected = false;
                List<Microsoft.Extensions.AI.ChatMessage> approvalResponses = new();

                foreach (ToolApprovalRequestContent req in approvalRequests)
                {
                    FunctionCallContent call = (FunctionCallContent)req.ToolCall;
                    string functionName = call.Name;

                    // Later rounds are chained follow-ups: only DispatchUnit is allowed.
                    if (approvalRound > 1 && functionName != "DispatchUnit")
                    {
                        Console.WriteLine($"\n(The agent also wanted to call {functionName} — only report->dispatch chains here. Ask me directly for it.)");
                        approvalResponses.Add(new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, [req.CreateResponse(false)]));
                        continue;
                    }

                    Console.WriteLine("\n══════════════════════════════════════════");
                    Console.WriteLine(approvalRound == 1 ? "  APPROVAL REQUIRED" : "  APPROVAL REQUIRED (follow-up: report -> dispatch)");
                    Console.WriteLine($"  Function  : {functionName}");
                    if (call.Arguments is { Count: > 0 })
                    {
                        Console.WriteLine("  Arguments :");
                        foreach (KeyValuePair<string, object?> arg in call.Arguments)
                            Console.WriteLine($"      {arg.Key} = {arg.Value}");
                    }
                    else
                    {
                        Console.WriteLine("  Arguments : (none)");
                    }
                    Console.WriteLine("══════════════════════════════════════════");
                    Console.Write("Approve? (y/n): ");
                    bool approved = Console.ReadLine()?.Trim().ToLower() == "y";
                    if (!approved) anyRejected = true;

                    approvalResponses.Add(new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, [req.CreateResponse(approved)]));
                }

                response = await agent.RunAsync(approvalResponses, session);
                approvalRequests = response.Messages
                    .SelectMany(m => m.Contents)
                    .OfType<ToolApprovalRequestContent>()
                    .ToList();

                if (anyRejected) stopChaining = true; // once you say no, stop chaining
            }

            // Anything still pending (hit the round cap, or stopped after a rejection) is
            // declined with your next message so nothing dangles into the next turn.
            foreach (ToolApprovalRequestContent leftover in approvalRequests)
                deferredResponses.Add(new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, [leftover.CreateResponse(false)]));

            Console.WriteLine($"\nAgent: {response}\n");
        }

        Console.WriteLine("Dispatch center closed.");
    }
}

// #3  Structured-output target. Flat, and EVERY field is nullable so the model can
//     leave a field null ("idk") instead of inventing a value it was never told.
public record IncidentReport(
    string? type,
    string? priority,
    string? location,
    string? description);

// #4  Context provider. Before every agent run the framework calls ProvideAIContextAsync;
//     we return the live dispatch state as extra instructions, merged into the request.
//     That is context "retrieval" — the model sees current state without calling a tool.
public class DispatchContextProvider : AIContextProvider
{
    private readonly Func<string> _snapshot;

    public DispatchContextProvider(Func<string> snapshot) : base(null, null, null)
    {
        _snapshot = snapshot;
    }

    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        AIContext ctx = new AIContext
        {
            Instructions = "LIVE DISPATCH STATE (retrieved from context each turn):\n" + _snapshot()
        };
        return new ValueTask<AIContext>(ctx);
    }
}

// #2/#3  One IChatClient that forwards to whichever provider is "active". The agent is
//        built once over this; SetActive swaps the underlying model at runtime without
//        rebuilding the agent or losing the session's memory.
public class SwitchableChatClient : IChatClient
{
    private readonly Dictionary<string, IChatClient> _clients = new(StringComparer.OrdinalIgnoreCase);
    private IChatClient _active;

    public string ActiveName { get; private set; }
    public string Available => string.Join(", ", _clients.Keys);

    public SwitchableChatClient(string name, IChatClient initial)
    {
        _active = initial;
        ActiveName = name;
        _clients[name] = initial;
    }

    public void Register(string name, IChatClient client) => _clients[name] = client;

    public bool SetActive(string name)
    {
        if (!_clients.TryGetValue(name, out IChatClient? c)) return false;
        _active = c;
        ActiveName = name;
        return true;
    }

    // The three IChatClient members below just forward to the active provider.
    public Task<ChatResponse> GetResponseAsync(IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => _active.GetResponseAsync(messages, options, cancellationToken);

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => _active.GetStreamingResponseAsync(messages, options, cancellationToken);

    public object? GetService(Type serviceType, object? serviceKey = null) => _active.GetService(serviceType, serviceKey);

    public void Dispose()
    {
        foreach (IChatClient c in _clients.Values) c.Dispose();
    }
}

