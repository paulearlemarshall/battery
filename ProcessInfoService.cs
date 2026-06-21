using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

public class ProcessInfoService : IProcessInfoService
{
    private class CpuSnapshot
    {
        public string ProcessName;
        public TimeSpan CpuTime;
        public DateTime SampleUtc;
    }

    private readonly object _lock = new object();
    private readonly Dictionary<int, CpuSnapshot> _lastCpuSnapshots = new Dictionary<int, CpuSnapshot>();

    private string _lastTopCpu = "sampling...";
    private DateTime _lastTopCpuComputedUtc = DateTime.MinValue;

    private string _lastTopRam = "n/a";
    private DateTime _lastTopRamComputedUtc = DateTime.MinValue;

    public string GetTopCpuProcess()
    {
        lock (_lock)
        {
            if ((DateTime.UtcNow - _lastTopCpuComputedUtc) < TimeSpan.FromSeconds(2))
                return _lastTopCpu;
        }

        string computed = ComputeTopCpuProcess();

        lock (_lock)
        {
            _lastTopCpu = computed;
            _lastTopCpuComputedUtc = DateTime.UtcNow;
            return _lastTopCpu;
        }
    }

    public string GetTopRamProcess()
    {
        lock (_lock)
        {
            if ((DateTime.UtcNow - _lastTopRamComputedUtc) < TimeSpan.FromSeconds(5))
                return _lastTopRam;
        }

        string result = "n/a";
        try
        {
            Process p = Process.GetProcesses()
                .OrderByDescending(proc =>
                {
                    try { return proc.WorkingSet64; }
                    catch { return 0; }
                })
                .FirstOrDefault();

            if (p != null)
                result = string.Format("{0} ({1})", p.ProcessName, SystemInfoService.FormatBytes(p.WorkingSet64));
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to compute top RAM process", ex);
            result = "n/a";
        }

        lock (_lock)
        {
            _lastTopRam = result;
            _lastTopRamComputedUtc = DateTime.UtcNow;
            return _lastTopRam;
        }
    }

    private string ComputeTopCpuProcess()
    {
        try
        {
            DateTime nowUtc = DateTime.UtcNow;
            Process[] processes = Process.GetProcesses();
            var current = new Dictionary<int, CpuSnapshot>();

            string bestName = null;
            double bestPercent = 0;

            for (int i = 0; i < processes.Length; i++)
            {
                Process p = processes[i];
                try
                {
                    if (p.Id == 0 || p.Id == 4)
                        continue;

                    CpuSnapshot snap = new CpuSnapshot
                    {
                        ProcessName = p.ProcessName,
                        CpuTime = p.TotalProcessorTime,
                        SampleUtc = nowUtc
                    };
                    current[p.Id] = snap;

                    CpuSnapshot prev;
                    lock (_lock)
                    {
                        _lastCpuSnapshots.TryGetValue(p.Id, out prev);
                    }

                    if (prev == null)
                        continue;

                    double elapsedMs = (nowUtc - prev.SampleUtc).TotalMilliseconds;
                    if (elapsedMs <= 0)
                        continue;

                    double cpuMs = (snap.CpuTime - prev.CpuTime).TotalMilliseconds;
                    double percent = (cpuMs / elapsedMs) * 100.0 / Environment.ProcessorCount;

                    if (percent > bestPercent)
                    {
                        bestPercent = percent;
                        bestName = snap.ProcessName;
                    }
                }
                catch
                {
                    // Ignore per-process failures.
                }
                finally
                {
                    p.Dispose();
                }
            }

            lock (_lock)
            {
                _lastCpuSnapshots.Clear();
                foreach (var kvp in current)
                    _lastCpuSnapshots[kvp.Key] = kvp.Value;
            }

            if (!string.IsNullOrEmpty(bestName) && bestPercent > 0.5)
                return string.Format("{0} ({1:F0}%)", bestName, bestPercent);

            if (_lastCpuSnapshots.Count == 0)
                return "n/a";

            return "low activity";
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to compute top CPU process", ex);
            return "n/a";
        }
    }
}
