using SephiriaBuildOverlay.Plugin;

namespace SephiriaBuildOverlay.Tests;

public sealed class RuntimePerformanceTests
{
    [Fact]
    public void DisabledMeasurementDoesNotAccumulateAndEnabledSamplesAreBounded()
    {
        var performance = new RuntimePerformance();
        for (var i = 0; i < 10000; i++) performance.SnapshotCompleted(RuntimePerformance.Start());
        Assert.Equal(0, performance.SampleCount);
        performance.Enabled = true;
        for (var i = 0; i < 10000; i++) performance.SnapshotCompleted(RuntimePerformance.Start());
        Assert.Equal(2048, performance.SampleCount);
        performance.Enabled = false;
        Assert.Equal(0, performance.SampleCount);
    }

    [Fact]
    public void BackgroundFramesAreNotReportedAsGameFps()
    {
        var performance = new RuntimePerformance { Enabled = true };
        performance.Frame(1, false);
        performance.Frame(.01f, true);
        Assert.Contains("focusedFPS=100.0", performance.ReportAndReset(2, 3));
        Assert.Contains("focusedFPS=0.0", performance.ReportAndReset(2, 3));
    }
}
