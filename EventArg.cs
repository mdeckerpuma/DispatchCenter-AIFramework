using System;


public class IncidentEventArgs: EventArgs
{
    public Incident Incident { get; }
    public IncidentEventArgs(Incident i) { Incident = i; }
}

public class DispatchEventArgs : EventArgs
{
    public Incident Incident { get; }
    public Unit Unit { get; }
    public DispatchEventArgs(Incident i, Unit u) { Incident = i; Unit = u; }
}