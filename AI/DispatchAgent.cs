using Domain;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.ComponentModel;

namespace UsingAIFramework.AI
{
    internal class DispatchAgent
    {
        private readonly DispatchService _service;
        private readonly IIncidentRepository _repository;
        private readonly SummaryAgent _summaryagent;
        private readonly AIAgent _agent;
        private readonly bool _debug;


        public DispatchAgent(DispatchService service, IIncidentRepository repository, SummaryAgent summaryagent, IChatClient chatClient,bool debug = false)
        {
            _service = service;
            _repository = repository;
            _summaryagent = summaryagent;
            _debug = debug;

            // Not held as a field: the client is only ever needed here, to build the agent.
            _agent = chatClient
                .AsAIAgent(new ChatClientAgentOptions
                {
                    Name = "Dispatch Agent",
                    ChatOptions = new()
                    {
                        Instructions = @"You are a city dispatch center AI assistant. You manage incidents and dispatch units on behalf of a human dispatcher who confirms every action.

CONVERSATION RULES:
- Listen carefully and extract all information from what the user says before asking for anything.
- Only ask for information that is genuinely missing. Never ask for something already stated.
- Infer priority from context when obvious: any life-threatening situation, large fire, bomb, or mass casualty is Critical. Minor property damage or non-urgent situations are Low or Medium.
- Handle one incident completely (report + dispatch) before starting the next.
- Never make assumptions about unit IDs, incident IDs, or locations. These must come from the user or GetStatus.

TOOL CALL RULES:
- Never call ReportIncident unless you have: type, priority, location, and description. All four must be present.
- Never call DispatchUnit or ResolveIncident without calling GetStatus first to confirm exact IDs.
- Never call GetStatus for any other reason. Do not call it before ReportIncident.
- GetStatus never requires approval. Call it silently and use the result immediately.
- Once a tool call is approved and executes successfully, never call it again for the same incident.
- If a tool call is denied, stop immediately. Ask the user exactly what was wrong before doing anything else. Do not retry until the user has explained the problem and you have adjusted.
- Never call multiple tools speculatively. Only call a tool when you are certain all arguments are correct.

AFTER DENIAL:
- Do not retry the same call with the same arguments.
- Do not call GetStatus defensively.
- Do not ask for information already provided.
- Ask one specific question: what was wrong with the previous attempt.

PRIORITY INFERENCE GUIDE:
- Critical: life threat, mass casualties, large fire, bomb, weapon, serious injury
- High: significant property damage, escalating situation, multiple people at risk
- Medium: contained situation, minor injuries, non-escalating
- Low: minor incident, no immediate danger",
                        Tools =
                        [
                            AIFunctionFactory.Create(GetStatus),
                            AIFunctionFactory.Create(_summaryagent.SummarizeAsync),
                            new ApprovalRequiredAIFunction(AIFunctionFactory.Create(ReportIncident)),
                            new ApprovalRequiredAIFunction(AIFunctionFactory.Create(DispatchUnit)),
                            new ApprovalRequiredAIFunction(AIFunctionFactory.Create(MarkArrived)),
                            new ApprovalRequiredAIFunction(AIFunctionFactory.Create(ResolveIncident)),
                        ]
                    }
                }).AsBuilder()
                  .Use(LoggingMiddleware)
                  .Use(FunctionGateMiddleware)
                  .Build();

            
        }

        //MIDDLEWARE

        // Run middleware — wraps the entire agent turn, firing once per user message.
        // Everything before `await next(...)` runs before the model sees the message.
        // Everything after runs once the model has finished and all tool calls are complete.
        // This is the right layer for turn-level concerns: timing a full response, logging
        // the incoming messages, enforcing a token budget, or rejecting a turn entirely
        // before the model ever gets to think.
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

        // Function-calling middleware — wraps each individual tool call the model emits.
        // Fires only when the model decides to call a tool, not on plain text responses.
        // Everything before `await next(ctx, ct)` runs before the function executes.
        // Everything after runs once the function has returned its result.
        // This is the right layer for tool-level concerns: logging which tools are called
        // and how long they take, building an allow-list that blocks unauthorized tools,
        // validating arguments before they reach the function, or overriding the result
        // the model receives back. ctx.Function.Name identifies which tool is being called.
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

        // TOOL APPROVALS FOR THE AGENT. FUNCTIONS THAT SHOULD BE CALLED BY THE AGENT

        [Description("Call this method to report an incident")]
        private async Task<string> ReportIncident(
            [Description("Type of Incidents:MedicalEmergency, Fire, CrimeInProgress, TrafficIncident, HazardousMaterial")] string type,
            [Description("Priority Level:Low, Medium, High, Critical")] string priority,
            [Description("Location of the incident")] string location,
            [Description("Short description of what is happening")] string description)
        {
            // ignoreCase: true on both. The model hands these over as free text and is only
            // usually careful about capitalization — "fire" instead of "Fire" would otherwise
            // bounce the whole report back as an error string mid-conversation.
            if (!Enum.TryParse<IncidentType>(type, true, out IncidentType itype)) return $"Invalid type: {type}";
            if (!Enum.TryParse<IncidentPriority>(priority, true, out IncidentPriority ipriority)) return $"Invalid priority: {priority}";

            Incident i = _service.ReportIncident(itype, ipriority, location, description);

            IncidentRecord increc = new IncidentRecord
            {
                IncidentId = i.Id,
                Type = i.Type.ToString(),
                Priority = i.Priority.ToString(),
                Location = i.Location.ToString(),
                Description = i.Description.ToString(),
                Status = IncidentStatus.Pending.ToString(),
                CreatedAt = DateTime.UtcNow
            };

            await _repository.SaveIncidentAsync(increc);

            return $"Incident logged as {i.Id} - {i.Type} at {i.Location} Priority: {i.Priority}";
        }

        [Description("Call this method to dispatch a unit. Always call GetStatus first to get the exact unit and incident IDs before calling this.\r\n")]
        private async Task<string> DispatchUnit(
            [Description("Get the correct unit id based on what kind of unit is wanted")] string unitId,
            [Description("Get the correct incident id based on the id, location, or description of the incident")] string incidentId)
        {           
            bool i = _service.DispatchUnit(unitId, incidentId);
            if (!i)
            {
                // Say WHICH guard tripped. The old catch-all listed every possible cause at
                // once, so the model could only guess and usually retried the same bad call.
                Unit? bad = _service.GetUnits().FirstOrDefault(unit => unit.Id == unitId);
                if (bad == null) return $"No unit with id {unitId}. Call GetStatus for valid unit ids.";
                if (bad.Status != UnitStatus.Available) return $"{unitId} is not available - it is {bad.Status} on {bad.AssignIncidentId}. Choose a different unit.";

                Incident? target = _service.GetActiveIncidents().FirstOrDefault(incident => incident.Id == incidentId);
                if (target == null) return $"No active incident with id {incidentId}. Call GetStatus for valid incident ids.";

                return $"Could not dispatch {unitId} to {incidentId}.";
            }

            Unit unit = _service.GetUnits().FirstOrDefault(unit => unit.Id == unitId)!;

            AssignedUnitRecord urec = new AssignedUnitRecord
            {
                UnitId = unit.Id,
                UnitName = unit.Name,
                UnitType = unit.Type.ToString(),
                UnitStatus = unit.Status.ToString()
            };

            await _repository.AssignUnitAsync(incidentId, urec);
            await _repository.UpdateStatusAsync(incidentId, IncidentStatus.Responding.ToString());
            await _repository.UpdateUnitStatusAsync(incidentId, unit.Id, UnitStatus.Dispatched.ToString());

            return $"Distpatching {unitId} to {incidentId}";
        }

        [Description("Call this method to mark a unit as arrived to an incident")]
        private async Task<string> MarkArrived(
            [Description("Get the correct unit id for the incident")] string unitId)
        {
            bool i = _service.MarkArrived(unitId);
            if (!i) return "Wrong Unit or Incident";

            Unit unit = _service.GetUnits().FirstOrDefault(unit => unit.Id == unitId)!;
            string incidentId = unit.AssignIncidentId!;
            await _repository.UpdateStatusAsync(incidentId, IncidentStatus.OnScene.ToString());
            await _repository.UpdateUnitStatusAsync(incidentId, unit.Id, UnitStatus.OnScene.ToString());

            return $"{unitId} is marked on scene";
        }

        [Description("Call this method to resolve an incident")]
        private async Task<string> ResolveIncident(
            [Description("get the correct incident id for the incident that is to be resolved")] string incidentId)
        {
            Incident? foundIncident = _service.GetActiveIncidents().FirstOrDefault(incident => incident.Id == incidentId);
            bool i = _service.ResolveIncident(incidentId);
            if (!i) return "Incident not found";

            await _repository.UpdateStatusAsync(incidentId, IncidentStatus.Resolved.ToString());
            
            foreach (string unitId in foundIncident!.AssignedUnitIds)
            {
                await _repository.UpdateUnitStatusAsync(incidentId, unitId, UnitStatus.Available.ToString());
            }

            return $"{incidentId} is resolved";
        }

        [Description("Call this method to get the status of all units and incidents")]
        private string GetStatus()
        {
            return _service.GetStatus();
        }

        // Manual response inspector — call explicitly after a RunAsync to dump the full
        // message walk, token usage, and tool call/result pairs for that one response.
        // Use this for targeted debugging. For automatic per-turn logging, see LoggingMiddleware.
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

        //Running the console
        public async Task RunAsync()
        {
            List<IncidentRecord> increc = await _repository.GetActiveIncidentAsync();
            if (increc.Count == 0) { Console.WriteLine("No Active Incidents from previous session."); }
            else
            {
                foreach (IncidentRecord i in increc)
                {
                    if (!Enum.TryParse<IncidentType>(i.Type, out IncidentType type)) { continue; }
                    if (!Enum.TryParse<IncidentPriority>(i.Priority, out IncidentPriority priority)) { continue; }

                    Incident temp = new Incident(i.IncidentId, type, priority, i.Location, i.Description);
                    _service.LoadIncident(temp);

                    foreach (AssignedUnitRecord u in i.AssignedUnits)
                    {
                        // An assignment has TWO sides: the unit points at the incident, and the
                        // incident lists the unit. Both have to come back or the restore is a lie.
                        // AssignedUnitIds is the only path from an incident back to its units, and
                        // ResolveIncident walks it to free them — leave it empty and a carried-over
                        // incident resolves without releasing anything, in memory or in Mongo.
                        // Done before the roster lookup on purpose: even if a unit is no longer in
                        // the fleet, keeping its id here is what lets ResolveIncident reset its row.
                        temp.AssignUnit(u.UnitId);

                        Unit? unit = _service.GetUnits().FirstOrDefault(unit => unit.Id == u.UnitId);
                        if (unit == null) continue;

                        // Dispatch() is what sets AssignIncidentId, so an OnScene unit has to be
                        // walked through Dispatch first. ArriveOnScene() on its own leaves the unit
                        // on scene at nothing — it printed "arrived on scene at " with a blank id.
                        if (u.UnitStatus == UnitStatus.Dispatched.ToString())
                        {
                            unit.Dispatch(i.IncidentId);
                        }
                        else if (u.UnitStatus == UnitStatus.OnScene.ToString())
                        {
                            unit.Dispatch(i.IncidentId);
                            unit.ArriveOnScene();
                        }
                    }

                    // AssignUnit only ever lifts Pending -> Responding, so an incident that was
                    // already OnScene has to say so explicitly or it comes back reporting Pending.
                    if (i.Status == IncidentStatus.OnScene.ToString())
                    {
                        temp.MarkOnScene();
                    }

                }
            }

            // The id counter has to be rebuilt from EVERY incident ever recorded, which is
            // why this sits outside the else and asks the repository rather than counting
            // the active set. Resolved documents are invisible to GetActiveIncidentAsync,
            // so resolving the whole board left the counter at its 1000 default and the next
            // incident re-minted an id that already existed in Mongo. Nothing enforces
            // uniqueness on IncidentId, so that inserted a duplicate, and UpdateOneAsync
            // then wrote to whichever document matched first — usually the old resolved one,
            // which resurrected it as a ghost incident on the following boot.
            // Left alone when the collection is empty so a fresh database still starts at
            // INC-1000 instead of INC-1.
            int highest = await _repository.GetHighestIncidentNumberAsync();
            if (highest > 0)
            {
                _service.SetNextId(highest + 1);
            }

            string context = await _summaryagent.SummarizeAsync();

            AgentSession session = await _agent.CreateSessionAsync();
            await _agent.RunAsync($"[SHIFT START BRIEFING] The following is a summary of active incidents carried over from the previous session. Use this as context for the current shift:\n\n{context}", session);


            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("╔══════════════════════════════════════════╗");
            Console.WriteLine("║       CITY DISPATCH CENTER — AI          ║");
            Console.WriteLine("║       Type a situation. I'll handle it.  ║");
            Console.WriteLine("║       Type 'quit' to end your shift.     ║");
            Console.WriteLine("╚══════════════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine();


            while (true)
            {
                string? input = Console.ReadLine();
                if(input == "quit" || input == null) { break; }

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

                // LastOrDefault, not Last. A turn can legitimately come back with no assistant
                // message at all — a denial that produced only tool results, for instance — and
                // Last() throws InvalidOperationException on an empty sequence, which would end
                // the shift on an exception instead of a reply. The ?. was already written as if
                // this could be null; now it actually can be.
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
                if (_debug) InspectResponse(response);


            }
        }


    }
}
