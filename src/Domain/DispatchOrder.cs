using System;


// The payload crossing IDispatchSink: a flat, immutable snapshot of a dispatch at the moment
// it was sent, so nothing downstream holds a live Unit or Incident it could mutate.
public record DispatchOrder(string UnitId, string UnitName, UnitType UnitType,
    string IncidentId, IncidentType IncidentType, IncidentPriority Priority,
    string Location, DateTime SentAt);