using System;
using System.Collections.Generic;
using System.Linq;

public class BatterySample
{
    public DateTime TimeUtc;
    public int Percent;
    public bool IsCharging;
}

public class BatteryHistoryService : IBatteryHistoryService
{
    private readonly object _samplesLock = new object();
    private readonly List<BatterySample> _samples = new List<BatterySample>();
    private DateTime _lastSampleTimeUtc = DateTime.MinValue;

    private readonly int _sampleIntervalMinutes;
    private readonly int _chartWindowMinutes;
    private readonly int _retentionMinutes;

    public BatteryHistoryService(int sampleIntervalMinutes, int chartWindowMinutes, int retentionMinutes)
    {
        _sampleIntervalMinutes = sampleIntervalMinutes;
        _chartWindowMinutes = chartWindowMinutes;
        _retentionMinutes = retentionMinutes < chartWindowMinutes ? chartWindowMinutes : retentionMinutes;
    }

    public void LoadPersistedSamples(List<BatterySample> samples)
    {
        if (samples == null)
            return;

        lock (_samplesLock)
        {
            _samples.Clear();

            List<BatterySample> normalized = samples
                .Where(s => s != null)
                .OrderBy(s => s.TimeUtc)
                .ToList();

            DateTime cutoff = DateTime.UtcNow.AddMinutes(-_retentionMinutes);
            for (int i = 0; i < normalized.Count; i++)
            {
                BatterySample sample = normalized[i];
                if (sample.TimeUtc < cutoff)
                    continue;

                int percent = sample.Percent;
                if (percent < 0) percent = 0;
                if (percent > 100) percent = 100;

                _samples.Add(new BatterySample
                {
                    TimeUtc = sample.TimeUtc.ToUniversalTime(),
                    Percent = percent,
                    IsCharging = sample.IsCharging
                });
            }

            _lastSampleTimeUtc = _samples.Count > 0 ? _samples[_samples.Count - 1].TimeUtc : DateTime.MinValue;
        }
    }

    public bool MaybeAddSample(int percent, bool isCharging)
    {
        DateTime nowUtc = DateTime.UtcNow;

        lock (_samplesLock)
        {
            if ((nowUtc - _lastSampleTimeUtc) < TimeSpan.FromMinutes(_sampleIntervalMinutes))
                return false;

            _samples.Add(new BatterySample
            {
                TimeUtc = nowUtc,
                Percent = percent,
                IsCharging = isCharging
            });

            PruneSamplesUnsafe(nowUtc.AddMinutes(-_retentionMinutes));
            _lastSampleTimeUtc = nowUtc;
            return true;
        }
    }

    public List<BatterySample> GetSamplesWindow()
    {
        return GetSamplesWindow(_chartWindowMinutes);
    }

    public List<BatterySample> GetSamplesWindow(int windowMinutes)
    {
        DateTime cutoff = DateTime.UtcNow.AddMinutes(-windowMinutes);
        lock (_samplesLock)
        {
            return _samples.Where(s => s.TimeUtc >= cutoff).Select(CloneSample).ToList();
        }
    }

    public List<BatterySample> GetPredictionSamplesWindow()
    {
        lock (_samplesLock)
        {
            return BuildPredictionSamplesUnsafe(30).Select(CloneSample).ToList();
        }
    }

    public List<BatterySample> GetPredictionSamplesWindow(int horizonMinutes)
    {
        lock (_samplesLock)
        {
            return BuildPredictionSamplesUnsafe(horizonMinutes).Select(CloneSample).ToList();
        }
    }

    public List<BatterySample> GetAllSamples()
    {
        lock (_samplesLock)
        {
            return _samples.Select(CloneSample).ToList();
        }
    }

    public string BuildTrendText()
    {
        lock (_samplesLock)
        {
            if (_samples.Count == 0)
                return "Battery Δ 1m: -- | 5m: -- | 10m: --";

            BatterySample latest = _samples[_samples.Count - 1];
            string d1 = FormatDelta(GetDeltaMinutesUnsafe(1, latest));
            string d5 = FormatDelta(GetDeltaMinutesUnsafe(5, latest));
            string d10 = FormatDelta(GetDeltaMinutesUnsafe(10, latest));
            return "Battery Δ 1m: " + d1 + " | 5m: " + d5 + " | 10m: " + d10;
        }
    }

    public string GetEstimatedTimeText()
    {
        lock (_samplesLock)
        {
            if (_samples.Count < 2)
                return "n/a";

            BatterySample latest = _samples[_samples.Count - 1];
            double slopePerMinute = ComputeSlopeUnsafe(latest);

            if (latest.IsCharging)
            {
                if (slopePerMinute <= 0.01)
                    return "n/a";

                double remaining = 100.0 - latest.Percent;
                double minutesToFull = remaining / slopePerMinute;
                if (minutesToFull > 1440) return "n/a";
                return "~" + FormatMinutes((int)Math.Round(minutesToFull)) + " to full";
            }
            else
            {
                if (slopePerMinute >= -0.01)
                    return "n/a";

                double remaining = latest.Percent;
                double minutesToEmpty = remaining / Math.Abs(slopePerMinute);
                if (minutesToEmpty > 1440) return "n/a";
                return "~" + FormatMinutes((int)Math.Round(minutesToEmpty)) + " to empty";
            }
        }
    }

    private double ComputeSlopeUnsafe(BatterySample latest)
    {
        List<BatterySample> segment = new List<BatterySample>();
        segment.Add(latest);

        DateTime segmentCutoff = latest.TimeUtc.AddMinutes(-30);
        for (int i = _samples.Count - 2; i >= 0; i--)
        {
            BatterySample sample = _samples[i];
            if (sample.IsCharging != latest.IsCharging)
                break;
            if (sample.TimeUtc < segmentCutoff)
                break;

            segment.Insert(0, sample);
            if (segment.Count >= 8)
                break;
        }

        if (segment.Count < 2)
            return 0.0;

        BatterySample first = segment[0];
        double minutes = (latest.TimeUtc - first.TimeUtc).TotalMinutes;
        if (minutes <= 0.0)
            return 0.0;

        return (latest.Percent - first.Percent) / minutes;
    }

    private static string FormatMinutes(int totalMinutes)
    {
        if (totalMinutes < 1) return "< 1m";
        int h = totalMinutes / 60;
        int m = totalMinutes % 60;
        if (h > 0)
            return h + "h " + m + "m";
        return m + "m";
    }

    private int? GetDeltaMinutesUnsafe(int minutes, BatterySample latest)
    {
        DateTime target = latest.TimeUtc.AddMinutes(-minutes);
        BatterySample past = FindSampleAtOrBeforeUnsafe(target);
        if (past == null)
            return null;

        return latest.Percent - past.Percent;
    }

    private BatterySample FindSampleAtOrBeforeUnsafe(DateTime targetUtc)
    {
        for (int i = _samples.Count - 1; i >= 0; i--)
        {
            if (_samples[i].TimeUtc <= targetUtc)
                return _samples[i];
        }
        return null;
    }

    private List<BatterySample> BuildPredictionSamplesUnsafe(int horizonMinutes)
    {
        List<BatterySample> prediction = new List<BatterySample>();
        if (_samples.Count < 2)
            return prediction;

        BatterySample latest = _samples[_samples.Count - 1];
        double slopePerMinute = ComputeSlopeUnsafe(latest);

        if (latest.IsCharging && slopePerMinute <= 0.0)
            return prediction;
        if (!latest.IsCharging && slopePerMinute >= 0.0)
            return prediction;

        prediction.Add(CloneSample(latest));

        DateTime stepTime = latest.TimeUtc;
        double predictedPercent = latest.Percent;
        int stepMinutes = _sampleIntervalMinutes < 1 ? 1 : _sampleIntervalMinutes;

        while ((stepTime - latest.TimeUtc).TotalMinutes < horizonMinutes)
        {
            stepTime = stepTime.AddMinutes(stepMinutes);
            predictedPercent += slopePerMinute * stepMinutes;

            if (predictedPercent < 0.0)
                predictedPercent = 0.0;
            if (predictedPercent > 100.0)
                predictedPercent = 100.0;

            prediction.Add(new BatterySample
            {
                TimeUtc = stepTime,
                Percent = (int)Math.Round(predictedPercent),
                IsCharging = latest.IsCharging
            });

            if (predictedPercent <= 0.0 || predictedPercent >= 100.0)
                break;
        }

        return prediction;
    }

    private string FormatDelta(int? delta)
    {
        if (!delta.HasValue) return "--";
        if (delta.Value > 0) return "+" + delta.Value + "%";
        if (delta.Value < 0) return delta.Value + "%";
        return "0%";
    }

    private void PruneSamplesUnsafe(DateTime cutoffUtc)
    {
        _samples.RemoveAll(s => s.TimeUtc < cutoffUtc);
    }

    private static BatterySample CloneSample(BatterySample sample)
    {
        return new BatterySample
        {
            TimeUtc = sample.TimeUtc,
            Percent = sample.Percent,
            IsCharging = sample.IsCharging
        };
    }
}
