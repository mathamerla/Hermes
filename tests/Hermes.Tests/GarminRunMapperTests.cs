using Xunit;
using System.Text.Json;
using Hermes.Garmin;
using Hermes.Storage;

namespace Hermes.Tests;

public sealed class GarminRunMapperTests
{
    [Fact]
    public void MapsRunningActivityToMetricUnits()
    {
        using var json = JsonDocument.Parse("""
        {"activityId":123,"activityType":{"typeKey":"running"},"startTimeGMT":"2026-10-29 16:21:43",
        "startTimeLocal":"2026-10-29 17:21:43","distance":5000,"duration":1500,
        "averageHR":150,"maxHR":175,"elevationGain":35}
        """);
        var run = GarminRunMapper.Map(json.RootElement);
        Assert.NotNull(run);
        Assert.Equal("123", run.Id);
        Assert.Equal(5, run.DistanceKm);
        Assert.Equal(5, run.PaceMinKm);
        Assert.Equal(150, run.AverageHr);
        Assert.Equal("2026-10-29", run.LocalDate);
    }

    [Fact]
    public void IgnoresNonRunningActivity()
    {
        using var json = JsonDocument.Parse("""
        {"activityId":123,"activityType":{"typeKey":"cycling"},"startTimeGMT":"2026-10-29 16:21:43"}
        """);
        Assert.Null(GarminRunMapper.Map(json.RootElement));
    }

    [Fact]
    public void KeepsMissingMeasurementsEmptyAndPreservesManualNotes()
    {
        var run = new Run("1", "2026-10-29T16:21:43.0000000Z", "2026-10-29",
            "running", 5, 1800, 6, null, null, null);
        IList<object> old = new List<object> { "Garmin", "1", "", "", "", "", "", "", "", "", "", "hard", "Great run", "" };
        var row = RunRows.ToCells(run, old, DateTime.UtcNow);
        Assert.Equal("", row[8]);
        Assert.Equal("hard", row[11]);
        Assert.Equal("Great run", row[12]);
        Assert.False(RunRows.HasTrainingChanges(row, row));
    }

    [Fact]
    public void DetectsChangedDistance()
    {
        var run = new Run("1", "2026-10-29T16:21:43.0000000Z", "2026-10-29",
            "running", 5.1, 1800, 5.882, 150, 170, 20);
        var previous = RunRows.ToCells(run, null, DateTime.UtcNow);
        var changed = RunRows.ToCells(run with { DistanceKm = 5.2 }, previous, DateTime.UtcNow);
        Assert.True(RunRows.HasTrainingChanges(changed, previous));
    }
}
