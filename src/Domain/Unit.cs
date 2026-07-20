using System;

public class Unit
{
    public string Id { get; }
    public string Name { get; }
    public UnitType Type { get; }
    public UnitStatus Status { get; private set; } = UnitStatus.Available;
    public string? AssignIncidentId { get; private set; }

    public event EventHandler<string>? StatusChanged;

    public Unit(string id, string name, UnitType type) { Id = id; Name = name; Type = type; }

    public void Dispatch(string incidentId)
    {
        Status = UnitStatus.Dispatched;
        AssignIncidentId = incidentId;
        StatusChanged?.Invoke(this, $"{Name} is dispatched to {incidentId}");
    }

    public void ArriveOnScene()
    {
        Status = UnitStatus.OnScene;
        StatusChanged?.Invoke(this, $"{Name} arrived on scene at {AssignIncidentId}");
    }

    public void ClearAndReturn()
    {
        Status = UnitStatus.Available;
        var was = AssignIncidentId;
        AssignIncidentId = null;
        StatusChanged?.Invoke(this, $"{Name} cleared from {was} - back in service");
    }
}