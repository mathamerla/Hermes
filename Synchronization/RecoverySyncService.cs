using Hermes.Garmin;
using Hermes.Storage;

namespace Hermes.Synchronization;

public sealed class RecoverySyncService(
    GarminRecoveryClient garmin, GoogleRecoveryRepository sheets, TimeProvider clock)
{
    public async Task<int> RunAsync(int days, CancellationToken ct)
    {
        var existing = await sheets.GetRowsAsync(ct);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromHours(1)).DateTime);
        var count = 0;
        // Yesterday is the newest complete day; avoid partial today's sleep/HRV.
        for (var daysAgo = days; daysAgo >= 1; daysAgo--)
        {
            var date = today.AddDays(-daysAgo);
            var day = GarminRecoveryMapper.Map(await garmin.GetDailyAsync(date, ct));
            await sheets.UpsertAsync(day, existing, ct);
            count++;
        }
        return count;
    }
}
