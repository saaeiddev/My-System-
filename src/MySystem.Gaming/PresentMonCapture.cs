using System.Diagnostics;
using MySystem.Core;

namespace MySystem.Gaming;

public sealed record GamingSnapshot(double? Fps, double? OnePercentLow, double? FrameTimeMs,
    IReadOnlyList<double> FrameTimes, string Status, DateTimeOffset Timestamp, int? ProcessId);

/// <summary>Runs the bundled ETW console collector. No process injection, service, or elevation is performed.</summary>
public sealed class PresentMonCapture : IDisposable
{
    private sealed record FramePoint(long ReceivedAt, double Milliseconds);
    private sealed class CaptureRun(Process process, CancellationTokenSource cancellation, string sessionName, int pid)
    {
        public Process Process { get; } = process;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public string SessionName { get; } = sessionName;
        public int ProcessId { get; } = pid;
        public string LastError { get; set; } = "";
        public int Stopping;
    }

    private readonly object _gate = new();
    private readonly string _exePath;
    private readonly Dictionary<string, Queue<FramePoint>> _chains = new(StringComparer.Ordinal);
    private CaptureRun? _run;
    private string _status = "Select a running graphics process to begin a real ETW capture.";
    private DateTimeOffset _timestamp = DateTimeOffset.UtcNow;
    private bool _disposed;
    private const int MaxFramesPerChain = 100_000;
    private const int MaxSwapChains = 16;

    public PresentMonCapture(string exePath) => _exePath = Path.GetFullPath(exePath);

    public GamingSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                long now = Stopwatch.GetTimestamp();
                Prune(now);
                // The most active swap chain in the last five seconds is the primary render stream.
                var primary = _chains.Values.Where(q => q.Count > 0)
                    .OrderByDescending(q => q.Count(p => Age(now, p.ReceivedAt) <= 5))
                    .ThenByDescending(q => q.Last().ReceivedAt).FirstOrDefault();
                if (_run is null || primary is null || Age(now, primary.Last().ReceivedAt) > 3)
                {
                    string state = _run is not null && primary is not null
                        ? "No new frame events for 3 seconds. The process may be paused, minimized, protected, or no longer presenting."
                        : _status;
                    return new(null, null, null, Array.Empty<double>(), state, _timestamp, _run?.ProcessId);
                }
                var times = primary.Select(p => p.Milliseconds).ToArray();
                var stats = FrameStatistics.Calculate(times);
                return new(stats.AverageFps, stats.OnePercentLowFps, times[^1], times,
                    $"Live PresentMon ETW · {times.Length:N0} frames · rolling 60 s · primary swap chain",
                    _timestamp, _run.ProcessId);
            }
        }
    }

    public Task StartAsync(int processId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processId);
        Stop();
        if (!File.Exists(_exePath))
        {
            SetStatus("PresentMon is unavailable. Reinstall the complete My System distribution.");
            return Task.CompletedTask;
        }

        string session = $"MySystem-{Environment.ProcessId}-{Guid.NewGuid():N}";
        var start = new ProcessStartInfo(_exePath)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(_exePath)!
        };
        foreach (string arg in new[] { "--process_id", processId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--output_stdout", "--no_console_stats", "--v1_metrics", "--session_name", session,
            "--terminate_on_proc_exit", "--no_track_gpu", "--no_track_input", "--no_track_display" })
            start.ArgumentList.Add(arg);
        var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) throw new InvalidOperationException("The collector could not be started.");
            var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var run = new CaptureRun(process, linked, session, processId);
            lock (_gate)
            {
                _run = run;
                _chains.Clear();
                _timestamp = DateTimeOffset.UtcNow;
                _status = "Waiting for PresentMon frame events. ETW capture requires appropriate Windows permissions.";
            }
            _ = MonitorAsync(run);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            process.Dispose();
            SetStatus($"Frame capture could not start: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    private async Task MonitorAsync(CaptureRun run)
    {
        using var registration = run.Cancellation.Token.Register(() => _ = TerminateAsync(run));
        try
        {
            await Task.WhenAll(ReadFramesAsync(run), ReadErrorsAsync(run), run.Process.WaitForExitAsync()).ConfigureAwait(false);
            lock (_gate)
            {
                if (ReferenceEquals(_run, run))
                {
                    _status = run.Process.ExitCode == 0
                        ? "Capture ended. The selected process exited or stopped presenting."
                        : $"PresentMon exited ({run.Process.ExitCode}). {run.LastError} Check ETW permissions (Performance Log Users), process access, and capture compatibility.";
                    _run = null;
                    _chains.Clear();
                    _timestamp = DateTimeOffset.UtcNow;
                }
            }
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                if (ReferenceEquals(_run, run))
                {
                    _run = null;
                    _chains.Clear();
                    _status = $"Frame capture unavailable: {ex.Message}";
                }
            }
            await TerminateAsync(run).ConfigureAwait(false);
        }
        finally { run.Cancellation.Dispose(); run.Process.Dispose(); }
    }

    private async Task ReadFramesAsync(CaptureRun run)
    {
        var parser = new PresentMonCsvParser();
        while (await run.Process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            var frame = parser.Parse(line);
            if (frame is null || frame.ProcessId != run.ProcessId) continue;
            lock (_gate)
            {
                if (!ReferenceEquals(_run, run)) return;
                long now = Stopwatch.GetTimestamp();
                Prune(now);
                if (!_chains.TryGetValue(frame.SwapChain, out var queue))
                {
                    if (_chains.Count >= MaxSwapChains)
                        _chains.Remove(_chains.OrderBy(p => p.Value.Count == 0 ? 0 : p.Value.Last().ReceivedAt).First().Key);
                    _chains[frame.SwapChain] = queue = new();
                }
                queue.Enqueue(new(now, frame.FrameTimeMs));
                while (queue.Count > MaxFramesPerChain) queue.Dequeue();
                _timestamp = DateTimeOffset.UtcNow;
            }
        }
    }

    private static async Task ReadErrorsAsync(CaptureRun run)
    {
        while (await run.Process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
            run.LastError = line.Length <= 1600 ? line : line[..1600];
    }

    private void Prune(long now)
    {
        foreach (var queue in _chains.Values)
            while (queue.Count > 0 && Age(now, queue.Peek().ReceivedAt) > 60) queue.Dequeue();
    }

    private static double Age(long now, long then) => (now - then) / (double)Stopwatch.Frequency;

    public void Stop()
    {
        CaptureRun? run;
        lock (_gate)
        {
            run = _run;
            _run = null;
            _chains.Clear();
            _status = "Capture stopped. Select a process to start again.";
            _timestamp = DateTimeOffset.UtcNow;
        }
        if (run is not null)
        {
            try { run.Cancellation.Cancel(); } catch (ObjectDisposedException) { }
        }
    }

    private async Task TerminateAsync(CaptureRun run)
    {
        if (Interlocked.Exchange(ref run.Stopping, 1) != 0) return;
        // Stop only our unique ETW session. Cleanup is asynchronous so the UI never blocks on a collector.
        try
        {
            var info = new ProcessStartInfo(_exePath) { UseShellExecute = false, CreateNoWindow = true };
            foreach (string arg in new[] { "--session_name", run.SessionName, "--terminate_existing_session" }) info.ArgumentList.Add(arg);
            using var terminator = Process.Start(info);
            if (terminator is not null)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { await terminator.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { try { terminator.Kill(); } catch (InvalidOperationException) { } }
            }
            if (!run.Process.HasExited)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                try { await run.Process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { if (!run.Process.HasExited) run.Process.Kill(entireProcessTree: true); }
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or ObjectDisposedException)
        {
            try { if (!run.Process.HasExited) run.Process.Kill(entireProcessTree: true); }
            catch (Exception fallback) when (fallback is System.ComponentModel.Win32Exception or InvalidOperationException or ObjectDisposedException) { }
        }
    }

    private void SetStatus(string value) { lock (_gate) { _status = value; _timestamp = DateTimeOffset.UtcNow; } }
    public void Dispose() { if (_disposed) return; _disposed = true; Stop(); }
}
