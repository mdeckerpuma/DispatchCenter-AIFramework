using System;
using System.Collections.Generic;
using System.Text;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domain
{
    // Persistence shape for a generated briefing. One row per SummarizeAsync run, which is why
    // that call is approval-gated when the model reaches for it.
    public class SummaryRecord
    {
        [BsonId]
        public ObjectId Id { get; set; }
        public string Summary { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public int ActiveIncidentCount { get; set; }
    }
}
