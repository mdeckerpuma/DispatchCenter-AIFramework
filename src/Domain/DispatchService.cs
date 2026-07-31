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

        // The guards the caller's error message always claimed but the code never
        // enforced. Without them a Dispatched unit could be sent somewhere else,
        // silently overwriting AssignIncidentId and orphaning it from the incident
        // it was already committed to, and a Resolved incident would happily take
        // on new units.
        if(foundUnit.Status != UnitStatus.Available) { return false; }
        if(foundIncident.Status == IncidentStatus.Resolved) { return false; }

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

    // This is the agent's only window onto real state, and the system prompt forbids
    // dispatching or resolving without calling it first. It used to print every incident
    // as "IS ACTIVE" and drop both Status and AssignedUnitIds, so the model could not
    // tell Pending from OnScene and had to guess which units were committed where.
    public string GetStatus()
    {
        StringBuilder sb = new StringBuilder();

        sb.AppendLine("ACTIVE INCIDENTS");
        List<Incident> active = GetActiveIncidents();
        if(active.Count == 0)
        {
            sb.AppendLine("none");
        }
        foreach(Incident incident in active)
        {
            string assigned;
            if(incident.AssignedUnitIds.Count == 0)
            {
                assigned = "none";
            }
            else
            {
                assigned = string.Join(", ", incident.AssignedUnitIds);
            }

            sb.AppendLine($"{incident.Id} | {incident.Type} | {incident.Priority} | {incident.Status} | {incident.Location} | Units: {assigned}");
        }

        // Committed units go one per line so the model can see exactly what is tied up
        // and where. Available ones are grouped by type to keep a 50-unit roster from
        // flooding the context on every single call.
        sb.AppendLine("\nUNITS ON ASSIGNMENT");
        int committed = 0;
        foreach(Unit unit in GetUnits())
        {
            if(unit.Status == UnitStatus.Available) { continue; }

            sb.AppendLine($"{unit.Id} | {unit.Name} | {unit.Type} | {unit.Status} | on {unit.AssignIncidentId}");
            committed++;
        }
        if(committed == 0)
        {
            sb.AppendLine("none");
        }

        sb.AppendLine("\nAVAILABLE UNITS");
        foreach(UnitType type in Enum.GetValues<UnitType>())
        {
            List<string> ids = new List<string>();
            foreach(Unit unit in GetUnits())
            {
                if(unit.Type == type && unit.Status == UnitStatus.Available)
                {
                    ids.Add(unit.Id);
                }
            }

            if(ids.Count == 0)
            {
                sb.AppendLine($"{type}: none");
            }
            else
            {
                sb.AppendLine($"{type}: {string.Join(", ", ids)}");
            }
        }

        return sb.ToString();
    }

    public void LoadIncident(Incident i)
    {
        // Same StatusChanged wiring ReportIncident does. Without it a carried-over
        // incident is silent for the rest of the shift — resolving it printed nothing
        // while a live incident printed "[Incident] Resolved".
        i.StatusChanged += (_, msg) => Console.WriteLine($"[Incident] {msg}");

        _incidents.Add(i);
    }

    public void SetNextId(int id)
    {
        _counter = id;
    }
 }
