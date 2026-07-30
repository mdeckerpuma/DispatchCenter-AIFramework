using System;
using System.Collections.Generic;
using System.Text;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domain
{
    public class SummaryRecord
    {
        [BsonId]
        public ObjectId Id { get; set; }
        public string Summary { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public int ActiveIncidentCount { get; set; }
    }
}
