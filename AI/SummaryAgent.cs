using Azure;
using Azure.AI.OpenAI;
using Domain;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Text;

namespace UsingAIFramework.AI
{
    public class SummaryAgent
    {
        private readonly IIncidentRepository _incidents;
        private readonly ISummaryRepository _summaries;
        private readonly AIAgent _agent;

        public SummaryAgent(IIncidentRepository incidents, ISummaryRepository summaries, IChatClient chatClient)
        {
            _incidents = incidents;
            _summaries = summaries;

            // Not held as a field: the client is only ever needed here, to build the agent.
            _agent = chatClient
                .AsAIAgent(new ChatClientAgentOptions
                {
                    Name = "Summary Agent",
                    ChatOptions = new()
                    {
                        Instructions = "You are a shift handoff assistant. Given raw incident data, produce a compact briefing a dispatcher can read in 30 seconds. Cover status, priority, and which units are committed. Each record is guaranteed to have IncidentId, Type, Priority, Status, and Location. AssignedUnits may be empty if no units have been dispatched yet. Do not invent or assume any information not present in the data. Be concise — one or two sentences per incident maximum.\r\n"
                    }
                });
        }

        public async Task<string> SummarizeAsync()
        {
            List<IncidentRecord> activeIncidents = await _incidents.GetActiveIncidentAsync();

            if (activeIncidents.Count == 0)
            {
                return "No active incidents carried over.";
            }

            StringBuilder sb = new StringBuilder();

            foreach (IncidentRecord inc in activeIncidents)
            {
                string unitsIsEmpty;
                if (inc.AssignedUnits.Count < 1)
                {
                    unitsIsEmpty = "None Assigned";
                }
                else
                {
                    unitsIsEmpty = string.Join(", ", inc.AssignedUnits.Select(u => u.UnitId));
                }

                sb.AppendLine($"[{inc.IncidentId}] {inc.Type} | {inc.Priority} | {inc.Status} | {inc.Location} | Units: {unitsIsEmpty}");
            }

            string incidentData = sb.ToString();

            
            AgentResponse response = await _agent.RunAsync(incidentData);

            // LastOrDefault, not Last: Last() throws on an empty sequence, so the ?? fallback
            // could never have been reached. This runs at startup, before the console is even
            // drawn, so a throw here took the whole app down before the shift began.
            string? text = response.Messages
                   .Where(m => m.Role == ChatRole.Assistant).LastOrDefault()?.Text;

            string summary = string.IsNullOrWhiteSpace(text) ? "NoSummary Generated" : text;

            SummaryRecord sum = new SummaryRecord
            {
                Summary = summary,
                CreatedAt = DateTime.UtcNow,
                ActiveIncidentCount = activeIncidents.Count
            };

            await _summaries.SaveSummaryAsync(sum);

            return summary;
        }
    }
}
