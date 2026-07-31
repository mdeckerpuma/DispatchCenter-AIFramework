using System;
using System.Collections.Generic;
using System.Text;

namespace Domain
{
    // The AI layer depends on this interface, never on IncidentRepository, so storage can be
    // swapped or faked without touching the agent.
    public interface IIncidentRepository
    {
        Task SaveIncidentAsync(IncidentRecord Record);
        Task UpdateStatusAsync(string incidentId, string status);
        Task AssignUnitAsync(string incidentId, AssignedUnitRecord unit);
        Task UpdateUnitStatusAsync(string incidentId, string unitId, string status);
        Task<List<IncidentRecord>> GetActiveIncidentAsync();

        // Deliberately spans EVERY incident ever recorded, resolved ones included.
        // The id counter cannot be rebuilt from the active set alone — see the call
        // site in DispatchAgent.RunAsync.
        Task<int> GetHighestIncidentNumberAsync();
    }
}
