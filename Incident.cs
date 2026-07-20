using System;
using System.Collections.Generic;

public class Incident
{
    public string Id { get; }
    public IncidentType Type { get; }
    public IncidentPriority Priority { get; }
    public string Location { get; }
    public string Description { get; }
    public IncidentStatus Status { get; private set; } = IncidentStatus.Pending;
    public List<string> AssignedUnitIds { get; } = new();
    public DateTime CreatedAt { get; } = DateTime.Now;

    public event EventHandler<IncidentStatus>? StatusChanged;

    public Incident(string id, IncidentType type, IncidentPriority priority, string location, string description) { Id = id; Type = type; Priority = priority;Location = location; Description = description; }

    public void AssignUnit(string UnitId)
    {
        AssignedUnitIds.Add(UnitId);
        if(Status == IncidentStatus.Pending)
        {
            Status = IncidentStatus.Responding;
            StatusChanged?.Invoke(this, Status);
        }
    }

    public void MarkOnScene()
    {
        Status = IncidentStatus.OnScene;
        StatusChanged?.Invoke(this, Status);
    }

    public void Resolve()
    {
        Status = IncidentStatus.Resolved;
        StatusChanged?.Invoke(this, Status);
    }


}
