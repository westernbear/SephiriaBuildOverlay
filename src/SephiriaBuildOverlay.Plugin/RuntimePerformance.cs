using System.Diagnostics;
using System.Globalization;

namespace SephiriaBuildOverlay.Plugin;

internal sealed class RuntimePerformance
{
    private readonly List<double> _snapshotMs = new();
    private int _focusedFrames;
    private double _focusedSeconds;
    private bool _enabled;
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            _snapshotMs.Clear(); _focusedFrames = 0; _focusedSeconds = 0;
        }
    }
    internal int SampleCount => _snapshotMs.Count;

    public static long Start() => Stopwatch.GetTimestamp();
    public void SnapshotCompleted(long started)
    {
        if (_enabled && _snapshotMs.Count < 2048)
            _snapshotMs.Add((Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency);
    }
    public void Frame(float seconds, bool focused)
    {
        if (!_enabled || !focused || seconds <= 0) return;
        _focusedFrames++; _focusedSeconds += seconds;
    }
    public string ReportAndReset(int discoveryPasses, int memberTypes)
    {
        _snapshotMs.Sort();
        var count = _snapshotMs.Count;
        var average = count == 0 ? 0 : _snapshotMs.Average();
        var p95 = count == 0 ? 0 : _snapshotMs[(int)Math.Ceiling(count * .95) - 1];
        var maximum = count == 0 ? 0 : _snapshotMs[count - 1];
        var fps = _focusedSeconds == 0 ? 0 : _focusedFrames / _focusedSeconds;
        var result = string.Format(CultureInfo.InvariantCulture,
            "Performance: snapshots={0}, avg={1:F3}ms, p95={2:F3}ms, max={3:F3}ms, focusedFPS={4:F1}, discoveryPasses={5}, reflectionTypes={6}",
            count, average, p95, maximum, fps, discoveryPasses, memberTypes);
        _snapshotMs.Clear(); _focusedFrames = 0; _focusedSeconds = 0;
        return result;
    }
}
