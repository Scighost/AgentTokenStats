using AgentTokenStats.Models;

namespace AgentTokenStats.Aggregation;

public static class UsageAggregator
{
    public static AggregationSnapshot Reduce(
        string agentId,
        string? dataRootPath,
        bool manualPath,
        IEnumerable<UnifiedUsageRecord> records,
        int skippedRecords = 0)
    {
        var snap = new AggregationSnapshot
        {
            AgentId = agentId,
            DataRootPath = dataRootPath,
            ManualPath = manualPath,
            ScannedAt = DateTimeOffset.UtcNow,
            SkippedRecords = skippedRecords
        };

        foreach (var record in records)
            Add(snap, record);

        return snap;
    }

    public static void Add(AggregationSnapshot snap, UnifiedUsageRecord record)
    {
        snap.RecordCount++;
        snap.Summary.Add(record);

        var local = record.Timestamp.ToLocalTime().DateTime;
        var day = DateOnly.FromDateTime(local);
        GetDay(snap, day).Add(record);
        GetHour(snap, day, local.Hour).Add(record);

        var modelKey = string.IsNullOrEmpty(record.NormalizedModelKey)
            ? "(unknown)"
            : record.NormalizedModelKey;
        if (!snap.ByModel.TryGetValue(modelKey, out var model))
        {
            model = new ModelBucket { NormalizedModelKey = modelKey };
            snap.ByModel[modelKey] = model;
        }

        model.ModelId = record.ModelId;
        model.ProviderId = record.ProviderId ?? model.ProviderId;
        model.Metrics.Add(record);

        var dayModelKey = (day, modelKey);
        if (!snap.ByDayModel.TryGetValue(dayModelKey, out var dayModel))
        {
            dayModel = new MetricBucket();
            snap.ByDayModel[dayModelKey] = dayModel;
        }

        dayModel.Add(record);

        if (!snap.BySession.TryGetValue(record.SessionId, out var session))
        {
            session = new SessionBucket
            {
                SessionId = record.SessionId,
                AgentId = snap.AgentId,
                Title = record.Title,
                Cwd = record.Cwd,
                IsArchived = record.IsArchived,
                FirstSeen = record.Timestamp,
                LastSeen = record.Timestamp
            };
            snap.BySession[record.SessionId] = session;
        }

        if (record.Timestamp < session.FirstSeen)
            session.FirstSeen = record.Timestamp;
        if (record.Timestamp > session.LastSeen)
            session.LastSeen = record.Timestamp;
        if (!string.IsNullOrEmpty(record.Title))
            session.Title = record.Title;
        if (!string.IsNullOrEmpty(record.Cwd))
            session.Cwd = record.Cwd;
        session.IsArchived = session.IsArchived || record.IsArchived;
        session.Metrics.Add(record);
        if (!session.ByModel.TryGetValue(modelKey, out var sessionModel))
        {
            sessionModel = new ModelBucket { NormalizedModelKey = modelKey };
            session.ByModel[modelKey] = sessionModel;
        }

        sessionModel.ModelId = record.ModelId;
        sessionModel.ProviderId = record.ProviderId ?? sessionModel.ProviderId;
        sessionModel.Metrics.Add(record);
    }

    private static MetricBucket GetDay(AggregationSnapshot snap, DateOnly day)
    {
        if (!snap.ByDay.TryGetValue(day, out var bucket))
        {
            bucket = new MetricBucket();
            snap.ByDay[day] = bucket;
        }

        return bucket;
    }

    private static MetricBucket GetHour(AggregationSnapshot snap, DateOnly day, int hour)
    {
        var key = (day, hour);
        if (!snap.ByDayHour.TryGetValue(key, out var bucket))
        {
            bucket = new MetricBucket();
            snap.ByDayHour[key] = bucket;
        }

        return bucket;
    }
}
