using System;
using System.Collections.Generic;
using System.Text;

namespace Domain
{
    public interface IIncidentRepository
    {
        Task SaveIncidentAsync(IncidentRecord Record);
        Task UpdateStatusAsync(string incidentId, string status);
        Task AssignUnitAsync(string incidentId, AssignedUnitRecord unit);
        Task UpdateUnitStatusAsync(string incidentId, string unitId, string status);
        Task<List<IncidentRecord>> GetActiveIncidentAsync();
    }
}
