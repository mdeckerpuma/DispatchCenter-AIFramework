using System;


public record DispatchOrder(string UnitId, string UnitName, UnitType UnitType,
    string IncidentId, IncidentType IncidentType, IncidentPriority Priority,
    string Location, DateTime SentAt);