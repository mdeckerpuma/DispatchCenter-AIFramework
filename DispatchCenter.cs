using System;
using System.Collections.Generic;
using System.Linq;


class DispatchCenter
{
    private List<Unit> _units { get; } = new();
    private List<Incident> _incidents { get; } = new();
    private int _counter = 1000;

    public event EventHandler<IncidentEventArgs>? IncidentReported;
    public event EventHandler<DispatchEventArgs>? UnitDispatched;
    public event EventHandler<string>? AlertBroadcast;

    public void AddUnit(Unit unit)
    {
        unit.StatusChanged += (_, msg) => Console.WriteLine($" [Unit]{msg}");
        _units.Add(unit);
    }

    public Incident ReportIncident(IncidentType type, IncidentPriority priority, string location, string description)
    {
        string id = $"INC-{_counter}";
        Incident incident = new Incident(id, type, priority, location, description);
        _counter++;

        incident.StatusChanged += (_, msg) => Console.WriteLine($"[Incident] {msg}");

        _incidents.Add(incident);

        IncidentReported?.Invoke(this, new IncidentEventArgs(incident));

        if(priority == IncidentPriority.Critical)
        {
            AlertBroadcast?.Invoke(this, $"{incident.Location} is critical");
        }

        return incident;
    }

    public bool DisbatchUnit(string unitId, string incidentId)
    {
        Unit foundUnit = _units.FirstOrDefault(unit => unit.Id == unitId);
        Incident foundIncident = _incidents.FirstOrDefault(incident => incident.Id == incidentId);

        if(foundUnit == null) { return false; }
        if(foundIncident == null) { return false; }

        foundUnit.Dispatch(foundIncident.Id);
        foundIncident.AssignUnit(foundUnit.Id);

        UnitDispatched?.Invoke(this, new DispatchEventArgs(foundIncident, foundUnit));

        return true;
    }


    public bool MarkArrived(string unitId)
    {
        Unit foundUnit = _units.FirstOrDefault(unit => unit.Status == UnitStatus.Dispatched && unit.Id == unitId);
        if (foundUnit == null) { return false; }

        foundUnit.ArriveOnScene();

        string incidentId = foundUnit.AssignIncidentId;
        Incident foundIncident = _incidents.FirstOrDefault(incident => incident.Id == incidentId);
        if (foundIncident == null) { return false; }
        foundIncident.MarkOnScene();

        return true;
    }

    public bool ResolveIncident(string incidentId)
    {
        Incident foundIncident = _incidents.FirstOrDefault(incident => incident.Id == incidentId && incident.Status != IncidentStatus.Resolved);
        if (foundIncident == null) { return false; }
        foundIncident.Resolve();

        for(int i = 0; i < foundIncident.AssignedUnitIds.Count; i++ )
        {
            string id = foundIncident.AssignedUnitIds[i];
            Unit foundUnit = _units.FirstOrDefault(unit => unit.Id == id);
            foundUnit?.ClearAndReturn();

        }

        return true;
    }

    public IReadOnlyList<Unit> GetUnits()
    {
        return _units.AsReadOnly();
    }

    public List<Incident> GetActiveIncidents()
    {
        List<Incident> unresolved = new List<Incident>();

        foreach(Incident incident in _incidents)
        {
            if(incident.Status != IncidentStatus.Resolved)
            {
                unresolved.Add(incident);
            }
        }

        unresolved.Sort((x, y) => y.Priority.CompareTo(x.Priority));

        return unresolved;
    }

 }