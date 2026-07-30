using System;
using System.Collections.Generic;
using System.Text;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domain
{
    public class IncidentRecord
    {
        [BsonId]
        public ObjectId Id { get; set; }
        public string? IncidentId { get; set; }
        public string? Type { get; set; }
        public string? Priority { get; set; }
        public string? Location { get; set; }
        public string? Description { get; set; }
        public string? Status { get; set; }
        public List<AssignedUnitRecord> AssignedUnits { get; set; } = new();
        public DateTime CreatedAt { get; set; }
    }
}
