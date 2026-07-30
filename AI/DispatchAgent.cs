using Anthropic.Models.Beta.Messages;
using Azure;
using Azure.AI.OpenAI;
using Azure.AI.Projects;
using Azure.Identity;
using Domain;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.VisualBasic;
using OllamaSharp;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.Intrinsics.X86;
using System.Text.Json;
using System.Timers;
using static Google.Protobuf.Reflection.SourceCodeInfo.Types;
using static System.Net.Mime.MediaTypeNames;

namespace UsingAIFramework.AI
{
    internal class DispatchAgent
    {
        private readonly DispatchService _service;
        private readonly IIncidentRepository _repository;
        private readonly SummaryAgent _summaryagent;
        private readonly AIAgent _agent;


        public DispatchAgent(DispatchService service, IIncidentRepository repository, SummaryAgent summaryagent) 
        {
            _service = service;
            _repository = repository;
            _summaryagent = summaryagent;

            string apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")
                ?? throw new InvalidOperationException("Set AZURE_OPENAI_API_KEY");

            var endpoint = "https://squidopenai.openai.azure.com/";
            var deploymentName = "gpt-4o-mini";

            _agent = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey)).GetChatClient(deploymentName).AsIChatClient()
                .AsAIAgent(new ChatClientAgentOptions
                {
                    Name = "Dispatch Agent",
                    ChatOptions = new()
                    {
                        Instructions = "You are a city dispatch center AI assistant. You manage incidents and dispatch units on behalf of a human dispatcher who confirms every action.\\n\\nCONVERSATION RULES:\\n- Listen carefully and extract all information from what the user says before asking for anything.\\n- Only ask for information that is genuinely missing. Never ask for something already stated.\\n- Infer priority from context when obvious: any life-threatening situation, large fire, bomb, or mass casualty is Critical. Minor property damage or non-urgent situations are Low or Medium.\\n- Handle one incident completely (report + dispatch) before starting the next.\\n- Never make assumptions about unit IDs, incident IDs, or locations. These must come from the user or GetStatus.\\n\\nTOOL CALL RULES:\\n- Never call ReportIncident unless you have: type, priority, location, and description. All four must be present.\\n- Never call DispatchUnit or ResolveIncident without calling GetStatus first to confirm exact IDs.\\n- Never call GetStatus for any other reason. Do not call it before ReportIncident.\\n- GetStatus never requires approval. Call it silently and use the result immediately.\\n- Once a tool call is approved and executes successfully, never call it again for the same incident.\\n- If a tool call is denied, stop immediately. Ask the user exactly what was wrong before doing anything else. Do not retry until the user has explained the problem and you have adjusted.\\n- Never call multiple tools speculatively. Only call a tool when you are certain all arguments are correct.\\n\\nAFTER DENIAL:\\n- Do not retry the same call with the same arguments.\\n- Do not call GetStatus defensively.\\n- Do not ask for information already provided.\\n- Ask one specific question: what was wrong with the previous attempt.\\n\\nPRIORITY INFERENCE GUIDE:\\n- Critical: life threat, mass casualties, large fire, bomb, weapon, serious injury\\n- High: significant property damage, escalating situation, multiple people at risk\\n- Medium: contained situation, minor injuries, non-escalating\\n- Low: minor incident, no immediate danger\r\n",
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
                });

            
        }

        [Description("Call this method to report an incident")]
        private async Task<string> ReportIncident(
            [Description("Type of Incidents:MedicalEmergency, Fire, CrimeInProgress, TrafficIncident, HazardousMaterial")] string type,
            [Description("Priority Level:Low, Medium, High, Critical")] string priority,
            [Description("Location of the incident")] string location,
            [Description("Short description of what is happening")] string description)
        {
            if (!Enum.TryParse<IncidentType>(type, out IncidentType itype)) return $"Invalid type: {type}";
            if (!Enum.TryParse<IncidentPriority>(priority, out IncidentPriority ipriority)) return $"Invalid type: {priority}";

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
            if (!i) return $"Either the unit is already active or incident doesn't exist / resolved";

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
        public async Task RunAsync()
        {
            List<IncidentRecord> increc = await _repository.GetActiveIncidentAsync();
            if (increc.Count == 0) { Console.WriteLine("No Active Incidents from previous session."); }
            else
            {
                int highest = 0;
                foreach (IncidentRecord i in increc)
                {
                    if (!Enum.TryParse<IncidentType>(i.Type, out IncidentType type)) { continue; }
                    if (!Enum.TryParse<IncidentPriority>(i.Priority, out IncidentPriority priority)) { continue; }

                    Incident temp = new Incident(i.IncidentId, type, priority, i.Location, i.Description);
                    _service.LoadIncident(temp);

                    int num = int.Parse(i.IncidentId.Split("-")[1]);
                    if(num > highest)
                    {
                        highest = num;
                    }
                }
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

                string? finalText = response.Messages
                    .Where(m => m.Role == ChatRole.Assistant).Last()?.Text;
                Console.WriteLine(finalText);
            

            }
        }


    }
}
