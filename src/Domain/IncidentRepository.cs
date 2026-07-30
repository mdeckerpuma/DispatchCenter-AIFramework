using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Text;

namespace Domain
{
    public class IncidentRepository : IIncidentRepository
    {
        private readonly IMongoCollection<IncidentRecord> _incidents;
        public IncidentRepository(string connectionString, string databaseName)
        {
            MongoClient client = new MongoClient(connectionString);
            IMongoDatabase database = client.GetDatabase(databaseName);
            _incidents = database.GetCollection<IncidentRecord>("incidents");
        }

        public async Task SaveIncidentAsync(IncidentRecord record)
        {
            await _incidents.InsertOneAsync(record);
        }

        public async Task UpdateStatusAsync(string incidentId, string status)
        {
            var filter = Builders<IncidentRecord>.Filter.Eq(r => r.IncidentId, incidentId);
            var update = Builders<IncidentRecord>.Update.Set(r => r.Status, status);
            await _incidents.UpdateOneAsync(filter, update);
        }

        public async Task AssignUnitAsync(string incidentId, AssignedUnitRecord unit)
        {
            var filter = Builders<IncidentRecord>.Filter.Eq(r => r.IncidentId, incidentId);
            var update = Builders<IncidentRecord>.Update.Push(r => r.AssignedUnits, unit);
            await _incidents.UpdateOneAsync(filter, update);
        }

        public async Task UpdateUnitStatusAsync(string incidentId, string unitId, string status)
        {
            var filter = Builders<IncidentRecord>.Filter.And(
                Builders<IncidentRecord>.Filter.Eq(r => r.IncidentId, incidentId),
                Builders<IncidentRecord>.Filter.ElemMatch(r => r.AssignedUnits, u => u.UnitId == unitId));
            var update = Builders<IncidentRecord>.Update.Set("AssignedUnits.$.UnitStatus", status);
            await _incidents.UpdateOneAsync(filter, update);
        }

        public async Task<List<IncidentRecord>> GetActiveIncidentAsync()
        {
            var filter = Builders<IncidentRecord>.Filter.Ne(r => r.Status, "Resolved");
            return await _incidents.Find(filter).ToListAsync();
        }
    }
}
