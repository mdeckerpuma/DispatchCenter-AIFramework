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
using System.Threading.Tasks;

internal class Program
{
    static DispatchCenter main = new DispatchCenter();

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


    static async Task Main(string[] args)
    {
        main.IncidentReported += (_, e) => Console.WriteLine($"\n[INCIDENT] {e.Incident.Id} reported at {e.Incident.Location}");
        main.UnitDispatched += (_, e) => Console.WriteLine($"[DISPATCH] {e.Unit.Id} → {e.Incident.Id}");
        main.AlertBroadcast += (_, msg) => Console.WriteLine($"[ALERT] {msg}");

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

        AIAgent agent = new AzureOpenAIClient(
            new Uri("https://squidopenai.openai.azure.com/"),
            new AzureKeyCredential(apiKey))
            .GetChatClient("gpt-4o-mini")
            .AsAIAgent(new ChatClientAgentOptions
            {
                Name = "DispatchAgent",
                ChatOptions = new()
                {
                    Instructions = "You are a city dispatch center assistant. Use GetStatus first to find current unit and incident IDs before dispatching or resolving. Always use exact IDs. Do ONLY the single action the user explicitly asks for; do not chain extra actions (for example, do not dispatch a unit after reporting an incident) unless the user asks. If the user declines a tool call, do not propose it again — acknowledge and wait for their next instruction.",
                    Tools =
                    [
                        AIFunctionFactory.Create(GetStatus),
                        new ApprovalRequiredAIFunction(AIFunctionFactory.Create(ReportIncident)),
                        new ApprovalRequiredAIFunction(AIFunctionFactory.Create(DispatchUnit)),
                        new ApprovalRequiredAIFunction(AIFunctionFactory.Create(MarkArrived)),
                        new ApprovalRequiredAIFunction(AIFunctionFactory.Create(ResolveIncident)),
                    ]
                }
            });

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

            // Prompt YOU for the calls that answer what you just typed, and submit those
            // decisions exactly ONCE.
            if (approvalRequests.Count > 0)
            {
                List<Microsoft.Extensions.AI.ChatMessage> approvalResponses = approvalRequests.ConvertAll(req =>
                {
                    FunctionCallContent call = (FunctionCallContent)req.ToolCall;
                    string functionName = call.Name;

                    Console.WriteLine("\n══════════════════════════════════════════");
                    Console.WriteLine("  APPROVAL REQUIRED");
                    Console.WriteLine($"  Function  : {functionName}");
                    // List each parameter and the exact value the agent wants to pass,
                    // one per line, instead of dumping the raw arguments object.
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

                    return new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, [req.CreateResponse(approved)]);
                });

                response = await agent.RunAsync(approvalResponses, session);

                // If the agent chained MORE calls after running yours, do NOT prompt and
                // do NOT re-invoke the model now. Stash a decline for each; it ships with
                // your next message. This is what breaks the infinite loop.
                foreach (ToolApprovalRequestContent leftover in response.Messages
                    .SelectMany(m => m.Contents)
                    .OfType<ToolApprovalRequestContent>())
                {
                    Console.WriteLine($"\n(The agent also wanted to call {((FunctionCallContent)leftover.ToolCall).Name} — skipped. Ask me if you want it.)");
                    deferredResponses.Add(new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, [leftover.CreateResponse(false)]));
                }
            }

            Console.WriteLine($"\nAgent: {response}\n");
        }

        Console.WriteLine("Dispatch center closed.");
    }
}

