using Domain;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;


public class DispatchService
{
    private List<Unit> _units { get; } = new();
    private List<Incident> _incidents { get; } = new();
    private readonly IDispatchSink _sink; 
    private int _counter = 1000;

    public DispatchService(IDispatchSink sink) { _sink = sink; }

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

        return incident;
    }

    public bool DispatchUnit(string unitId, string incidentId)
    {
        Unit foundUnit = _units.FirstOrDefault(unit => unit.Id == unitId);
        Incident foundIncident = _incidents.FirstOrDefault(incident => incident.Id == incidentId);

        if(foundUnit == null) { return false; }
        if(foundIncident == null) { return false; }

        foundUnit.Dispatch(foundIncident.Id);
        foundIncident.AssignUnit(foundUnit.Id);

        DispatchOrder tosink = new DispatchOrder(unitId, foundUnit.Name, foundUnit.Type, incidentId, foundIncident.Type, foundIncident.Priority,
            foundIncident.Location, DateTime.Now);

        _sink.send(tosink);

        return true;
    }


    public bool MarkArrived(string unitId)
    {
        Unit foundUnit = _units.FirstOrDefault(unit => unit.Status == UnitStatus.Dispatched && unit.Id == unitId);
        if (foundUnit == null) { return false; }

        string incidentId = foundUnit.AssignIncidentId;
        Incident foundIncident = _incidents.FirstOrDefault(incident => incident.Id == incidentId);
        if (foundIncident == null) { return false; }

        foundIncident.MarkOnScene();
        foundUnit.ArriveOnScene();

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

    public string GetStatus()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("ACTIVE INCIDENTS");
        foreach(Incident active in GetActiveIncidents())
        {
            sb.AppendLine($"{active.Id} in {active.Location} IS ACTIVE");
        }
        sb.AppendLine("\nUNITS");
        foreach(Unit unit in GetUnits())
        {
            sb.AppendLine($"{unit.Type} [{unit.Id}] status is {unit.Status}");
        }

        return sb.ToString();
    }

    public void LoadIncident(Incident i)
    {
        _incidents.Add(i);
    }

    public void SetNextId(int id)
    {
        _counter = id;
    }
 }
