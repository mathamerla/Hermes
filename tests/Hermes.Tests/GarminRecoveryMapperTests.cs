using System.Text.Json;
using Hermes.Garmin;
using Xunit;

namespace Hermes.Tests;
public sealed class GarminRecoveryMapperTests
{
    [Fact]
    public void MapsDailyRecovery()
    {
        using var sleep = JsonDocument.Parse("""{"dailySleepDTO":{"sleepTimeSeconds":25200,"sleepScores":{"overallScore":{"value":82}}}}""");
        using var hrv = JsonDocument.Parse("""{"hrvSummary":{"lastNightAvg":66}}""");
        using var heartRate = JsonDocument.Parse("""{"restingHeartRate":49}""");
        var result = GarminRecoveryMapper.Map(new RecoveryPayload(
            new DateOnly(2026,10,29), sleep.RootElement, hrv.RootElement, heartRate.RootElement));
        Assert.Equal(7, result.SleepHours);
        Assert.Equal(82, result.SleepScore);
        Assert.Equal(66, result.HrvMs);
        Assert.Equal(49, result.RestingHeartRate);
    }

    [Fact]
    public void MissingDataDoesNotBecomeZero()
    {
        var result = GarminRecoveryMapper.Map(new RecoveryPayload(new DateOnly(2026,10,29), null, null, null));
        Assert.Null(result.HrvMs);
        Assert.Null(result.RestingHeartRate);
        Assert.Null(result.SleepHours);
    }
}
