using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

public class ConfigStore : IConfigStore
{
    private readonly string _configPath;

    private const string IconSizeKey = "icon_size";
    private const string UpdateIntervalKey = "update_interval_seconds";
    private const string BatterySampleKey = "history_sample";
    private const string LowBatteryKey = "low_battery_threshold";
    private const string CriticalBatteryKey = "critical_battery_threshold";
    private const string FullBatteryAlertKey = "full_battery_alert";
    private const string SoundAlertsKey = "sound_alerts";

    public static readonly int[] AllowedIconSizes = new int[] { 16, 32, 48, 64 };

    public ConfigStore()
    {
        _configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BatteryTray",
            "config.txt");
    }

    public string ConfigPath
    {
        get { return _configPath; }
    }

    public int LoadIconSize(int defaultSize)
    {
        try
        {
            Dictionary<string, string> settings = ReadAllSettings();

            string value;
            if (!settings.TryGetValue(IconSizeKey, out value))
                return defaultSize;

            int parsed;
            if (int.TryParse(value, out parsed) && IsAllowedSize(parsed))
                return parsed;

            AppLogger.Warn("Invalid icon size in config: '" + value + "'. Falling back to default.");
            return defaultSize;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to load icon size", ex);
            return defaultSize;
        }
    }

    public void SaveIconSize(int iconSize)
    {
        if (!IsAllowedSize(iconSize))
        {
            AppLogger.Warn("Attempted to save unsupported icon size: " + iconSize);
            return;
        }

        try
        {
            Dictionary<string, string> settings = ReadAllSettings();
            settings[IconSizeKey] = iconSize.ToString();
            WriteAllSettings(settings, LoadBatterySamples());
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to save icon size", ex);
        }
    }

    public int LoadUpdateIntervalSeconds(int defaultValue)
    {
        try
        {
            Dictionary<string, string> settings = ReadAllSettings();

            string value;
            if (!settings.TryGetValue(UpdateIntervalKey, out value))
                return defaultValue;

            int parsed;
            if (int.TryParse(value, out parsed))
            {
                if (parsed < 5) parsed = 5;
                if (parsed > 300) parsed = 300;
                return parsed;
            }

            AppLogger.Warn("Invalid update interval in config: '" + value + "'. Falling back to default.");
            return defaultValue;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to load update interval", ex);
            return defaultValue;
        }
    }

    public void SaveUpdateIntervalSeconds(int seconds)
    {
        try
        {
            if (seconds < 5) seconds = 5;
            if (seconds > 300) seconds = 300;

            Dictionary<string, string> settings = ReadAllSettings();
            settings[UpdateIntervalKey] = seconds.ToString();
            WriteAllSettings(settings, LoadBatterySamples());
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to save update interval", ex);
        }
    }

    public int LoadLowBatteryThreshold(int defaultValue)
    {
        return LoadIntSetting(LowBatteryKey, defaultValue, 5, 50);
    }

    public void SaveLowBatteryThreshold(int threshold)
    {
        SaveIntSetting(LowBatteryKey, threshold, 5, 50);
    }

    public int LoadCriticalBatteryThreshold(int defaultValue)
    {
        return LoadIntSetting(CriticalBatteryKey, defaultValue, 1, 20);
    }

    public void SaveCriticalBatteryThreshold(int threshold)
    {
        SaveIntSetting(CriticalBatteryKey, threshold, 1, 20);
    }

    public bool LoadFullBatteryAlert(bool defaultValue)
    {
        return LoadBoolSetting(FullBatteryAlertKey, defaultValue);
    }

    public void SaveFullBatteryAlert(bool enabled)
    {
        SaveBoolSetting(FullBatteryAlertKey, enabled);
    }

    public bool LoadSoundAlerts(bool defaultValue)
    {
        return LoadBoolSetting(SoundAlertsKey, defaultValue);
    }

    public void SaveSoundAlerts(bool enabled)
    {
        SaveBoolSetting(SoundAlertsKey, enabled);
    }

    public List<BatterySample> LoadBatterySamples()
    {
        List<BatterySample> samples = new List<BatterySample>();

        try
        {
            if (!File.Exists(_configPath))
                return samples;

            string[] lines = File.ReadAllLines(_configPath);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                    continue;

                int sep = line.IndexOf('=');
                if (sep <= 0)
                    continue;

                string key = line.Substring(0, sep).Trim();
                if (!string.Equals(key, BatterySampleKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                string value = line.Substring(sep + 1).Trim();
                BatterySample sample;
                if (TryParseBatterySample(value, out sample))
                    samples.Add(sample);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to load battery samples", ex);
        }

        return samples;
    }

    public void SaveBatterySamples(List<BatterySample> samples)
    {
        try
        {
            Dictionary<string, string> settings = ReadAllSettings();
            WriteAllSettings(settings, samples);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to save battery samples", ex);
        }
    }

    private int LoadIntSetting(string key, int defaultValue, int min, int max)
    {
        try
        {
            Dictionary<string, string> settings = ReadAllSettings();
            string value;
            if (!settings.TryGetValue(key, out value))
                return defaultValue;

            int parsed;
            if (int.TryParse(value, out parsed))
            {
                if (parsed < min) parsed = min;
                if (parsed > max) parsed = max;
                return parsed;
            }
            return defaultValue;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to load setting: " + key, ex);
            return defaultValue;
        }
    }

    private void SaveIntSetting(string key, int value, int min, int max)
    {
        try
        {
            if (value < min) value = min;
            if (value > max) value = max;

            Dictionary<string, string> settings = ReadAllSettings();
            settings[key] = value.ToString();
            WriteAllSettings(settings, LoadBatterySamples());
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to save setting: " + key, ex);
        }
    }

    private bool LoadBoolSetting(string key, bool defaultValue)
    {
        try
        {
            Dictionary<string, string> settings = ReadAllSettings();
            string value;
            if (!settings.TryGetValue(key, out value))
                return defaultValue;

            if (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase))
                return true;
            if (value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase))
                return false;

            return defaultValue;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to load bool setting: " + key, ex);
            return defaultValue;
        }
    }

    private void SaveBoolSetting(string key, bool value)
    {
        try
        {
            Dictionary<string, string> settings = ReadAllSettings();
            settings[key] = value ? "1" : "0";
            WriteAllSettings(settings, LoadBatterySamples());
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to save bool setting: " + key, ex);
        }
    }

    private Dictionary<string, string> ReadAllSettings()
    {
        Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(_configPath))
            return map;

        string[] lines = File.ReadAllLines(_configPath);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith("#"))
                continue;

            int sep = line.IndexOf('=');
            if (sep > 0)
            {
                string key = line.Substring(0, sep).Trim();
                string value = line.Substring(sep + 1).Trim();
                if (key.Length > 0 && !string.Equals(key, BatterySampleKey, StringComparison.OrdinalIgnoreCase))
                    map[key] = value;
            }
            else
            {
                int legacySize;
                if (int.TryParse(line, out legacySize))
                    map[IconSizeKey] = legacySize.ToString();
            }
        }

        return map;
    }

    private void WriteAllSettings(Dictionary<string, string> settings, List<BatterySample> samples)
    {
        string dir = Path.GetDirectoryName(_configPath);
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("# BatteryTray config (key=value)");

        // Write known keys in order
        string[] orderedKeys = new string[]
        {
            IconSizeKey, UpdateIntervalKey,
            LowBatteryKey, CriticalBatteryKey,
            FullBatteryAlertKey, SoundAlertsKey
        };

        foreach (string key in orderedKeys)
        {
            string val;
            if (settings.TryGetValue(key, out val))
                sb.AppendLine(key + "=" + val);
        }

        // Write any other keys not in ordered list
        foreach (var kvp in settings)
        {
            bool found = false;
            for (int i = 0; i < orderedKeys.Length; i++)
            {
                if (string.Equals(kvp.Key, orderedKeys[i], StringComparison.OrdinalIgnoreCase))
                { found = true; break; }
            }
            if (!found)
                sb.AppendLine(kvp.Key + "=" + kvp.Value);
        }

        if (samples != null && samples.Count > 0)
        {
            sb.AppendLine("# Persisted battery history samples: history_sample=utc|percent|charging");
            for (int i = 0; i < samples.Count; i++)
            {
                BatterySample sample = samples[i];
                sb.AppendLine(BatterySampleKey + "=" + SerializeBatterySample(sample));
            }
        }

        File.WriteAllText(_configPath, sb.ToString());
    }

    private static string SerializeBatterySample(BatterySample sample)
    {
        return sample.TimeUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)
            + "|"
            + sample.Percent.ToString(CultureInfo.InvariantCulture)
            + "|"
            + (sample.IsCharging ? "1" : "0");
    }

    private static bool TryParseBatterySample(string value, out BatterySample sample)
    {
        sample = null;

        string[] parts = value.Split('|');
        if (parts.Length != 3)
            return false;

        DateTime timeUtc;
        int percent;

        if (!DateTime.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out timeUtc))
            return false;

        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out percent))
            return false;

        if (percent < 0) percent = 0;
        if (percent > 100) percent = 100;

        bool isCharging = parts[2] == "1" || parts[2].Equals("true", StringComparison.OrdinalIgnoreCase);

        sample = new BatterySample
        {
            TimeUtc = timeUtc.ToUniversalTime(),
            Percent = percent,
            IsCharging = isCharging
        };
        return true;
    }

    private bool IsAllowedSize(int size)
    {
        for (int i = 0; i < AllowedIconSizes.Length; i++)
        {
            if (AllowedIconSizes[i] == size)
                return true;
        }
        return false;
    }
}
