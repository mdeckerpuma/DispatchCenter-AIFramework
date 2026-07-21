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
        if (!Enum.TryParse<IncidentType>(type, out var itype)) return $"Unknown type: {type}";
        if (!Enum.TryParse<IncidentPriority>(priority, out var ipriority)) return $"Unknown priority: {priority}";
        Incident i = main.ReportIncident(itype, ipriority, location, description);
        return $"Incident logged as {i.Id} — {i.Type} at {i.Location} [{i.Priority}]";
    }

    [Description("Dispatch an available unit to an active incident.")]
    static string DispatchUnit(
        [Description("The unit ID to dispatch, e.g. POL111")] string unitId,
        [Description("The incident ID to dispatch to, e.g. INC-1000")] string incidentId)
    {
        bool result = main.DisbatchUnit(unitId, incidentId);
        return result ? $"Unit {unitId} dispatched to {incidentId}." : "Dispatch failed — check unit is available and incident is active.";
    }

    [Description("Mark a dispatched unit as arrived on scene.")]
    static string MarkArrived(
        [Description("The unit ID to mark as arrived, e.g. POL111")] string unitId)
    {
        bool result = main.MarkArrived(unitId);
        return result ? $"Unit {unitId} marked on scene." : "Failed — unit not found or not dispatched.";
    }

    [Description("Resolve an active incident and return all assigned units to service.")]
    static string ResolveIncident(
        [Description("The incident ID to resolve, e.g. INC-1000")] string incidentId)
    {
        bool result = main.ResolveIncident(incidentId);
        return result ? $"Incident {incidentId} resolved. Units returned to service." : "Failed — incident not found or already resolved.";
    }

    [Description("Get the current status of all units and active incidents. Call this first to find unit and incident IDs before dispatching or resolving.")]
    static string GetStatus()
    {
        var sb = new StringBuilder();
        sb.AppendLine("UNITS:");
        foreach (var u in main.GetUnits())
            sb.AppendLine($"  {u.Id} — {u.Name} ({u.Type}) [{u.Status}]{(u.AssignIncidentId != null ? $" → {u.AssignIncidentId}" : "")}");

        sb.AppendLine("\nACTIVE INCIDENTS:");
        var incidents = main.GetActiveIncidents();
        if (!incidents.Any())
            sb.AppendLine("  None.");
        else
            foreach (var i in incidents)
                sb.AppendLine($"  {i.Id} — {i.Type} at {i.Location} [{i.Priority}] ({i.Status})");

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
            ?? throw new InvalidOperationException("Set AZURE_OPENAI_API_KEY before running.");

        AIAgent agent = new AzureOpenAIClient(
            new Uri("https://squidopenai.openai.azure.com/"),
            new AzureKeyCredential(apiKey))
            .GetChatClient("gpt-4o-mini")
            .AsAIAgent(new ChatClientAgentOptions
            {
                Name = "DispatchAgent",
                ChatOptions = new()
                {
                    Instructions = "You are a city dispatch center assistant. Use GetStatus first to find current unit and incident IDs before dispatching or resolving. Always use exact IDs.",
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

        while (true)
        {
            Console.Write("You: ");
            string input = Console.ReadLine()?.Trim() ?? "";
            if (input.ToLower() == "quit") break;
            if (string.IsNullOrEmpty(input)) continue;

            AgentResponse response = await agent.RunAsync(input, session);

            List<ToolApprovalRequestContent> approvalRequests = response.Messages
                .SelectMany(m => m.Contents)
                .OfType<ToolApprovalRequestContent>()
                .ToList();

            while (approvalRequests.Count > 0)
            {
                List<Microsoft.Extensions.AI.ChatMessage> approvalResponses = approvalRequests.ConvertAll(req =>
                {
                    string functionName = ((FunctionCallContent)req.ToolCall).Name;
                    string arguments = ((FunctionCallContent)req.ToolCall).Arguments?.ToString() ?? "";

                    Console.WriteLine("\n══════════════════════════════════════════");
                    Console.WriteLine("  APPROVAL REQUIRED");
                    Console.WriteLine($"  Function  : {functionName}");
                    Console.WriteLine($"  Arguments : {arguments}");
                    Console.WriteLine("══════════════════════════════════════════");
                    Console.Write("Approve? (y/n): ");
                    bool approved = Console.ReadLine()?.Trim().ToLower() == "y";

                    return new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, [req.CreateResponse(approved)]);
                });

                response = await agent.RunAsync(approvalResponses, session);
                approvalRequests = response.Messages
                    .SelectMany(m => m.Contents)
                    .OfType<ToolApprovalRequestContent>()
                    .ToList();
            }

            Console.WriteLine($"\nAgent: {response}\n");
        }

        Console.WriteLine("Dispatch center closed.");
    }
}

