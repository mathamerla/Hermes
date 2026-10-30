using Hermes.Garmin;
using Hermes.Storage;

namespace Hermes.Synchronization;

public sealed class ActivitySyncService(GarminClient garmin, GoogleSheetsRepository sheets, TimeProvider clock)
{
    public async Task<SyncResult> RunAsync(bool fullSync, CancellationToken ct)
    {
        var since = fullSync ? DateTime.MinValue : clock.GetUtcNow().UtcDateTime.AddDays(-14);
        var activities = await garmin.GetActivitiesAsync(since, ct);
        var existing = await sheets.GetGarminRunsAsync(ct);
        var toInsert = new List<IList<object>>();
        var insertedIds = new HashSet<string>(StringComparer.Ordinal);
        var updated = 0;
        foreach (var activity in activities)
        {
            var run = GarminRunMapper.Map(activity);
            if (run is null || !insertedIds.Add(run.Id)) continue;
            var previous = existing.TryGetValue(run.Id, out var old) ? old : default;
            var row = RunRows.ToCells(run, previous.Data, clock.GetUtcNow().UtcDateTime);
            if (previous.Row == 0)
                toInsert.Add(row);
            else if (RunRows.HasTrainingChanges(row, previous.Data))
            {
                await sheets.UpsertExistingAsync(previous.Row, row, ct);
                updated++;
            }
        }
        await sheets.AppendAsync(toInsert, ct);
        await sheets.RecordSyncAsync(clock.GetUtcNow().UtcDateTime, activities.Count,
            fullSync ? "full" : "14d", ct);
        return new SyncResult(activities.Count, toInsert.Count, updated);
    }
}
public sealed record SyncResult(int Scanned, int Inserted, int Updated);
