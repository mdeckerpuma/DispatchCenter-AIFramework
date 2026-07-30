using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Text;

namespace Domain
{
    public class SummaryRepository : ISummaryRepository
    {
        private readonly IMongoCollection<SummaryRecord> _summaries;

        public SummaryRepository(string connectionString, string databaseName)
        {
            MongoClient client = new MongoClient(connectionString);
            IMongoDatabase database = client.GetDatabase(databaseName);
            _summaries = database.GetCollection<SummaryRecord>("summaries");
        }

        public async Task SaveSummaryAsync(SummaryRecord record)
        {
            await _summaries.InsertOneAsync(record);
        }

        public async Task<SummaryRecord> GetLatestSummaryAsync()
        {
            var filter = Builders<SummaryRecord>.Filter.Empty;
            return await _summaries.Find(filter).SortByDescending(r => r.CreatedAt).FirstOrDefaultAsync();

        }
    }
}
