using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.Windows.Forms;

public class SystemInfoService : ISystemInfoService
{
    private class CacheItem
    {
        public string Value;
        public DateTime ExpiresUtc;
    }

    private readonly object _cacheLock = new object();
    private readonly Dictionary<string, CacheItem> _cache = new Dictionary<string, CacheItem>();

    public string GetRamText()
    {
        return GetCached("ram", TimeSpan.FromSeconds(10), delegate
        {
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    ulong total = Convert.ToUInt64(obj["TotalVisibleMemorySize"]); // KB
                    ulong free = Convert.ToUInt64(obj["FreePhysicalMemory"]); // KB
                    ulong used = total - free;
                    return FormatKb(used) + " / " + FormatKb(total);
                }
            }
            return "n/a";
        });
    }

    public string GetDiskText()
    {
        return GetCached("disk", TimeSpan.FromSeconds(30), delegate
        {
            DriveInfo drive = new DriveInfo("C");
            if (!drive.IsReady) return "n/a";

            long free = drive.TotalFreeSpace;
            long total = drive.TotalSize;
            long used = total - free;
            return FormatBytes(used) + " / " + FormatBytes(total);
        });
    }

    public string GetUptimeText()
    {
        return GetCached("uptime", TimeSpan.FromSeconds(20), delegate
        {
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT LastBootUpTime FROM Win32_OperatingSystem"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    string lastBoot = obj["LastBootUpTime"].ToString();
                    DateTime bootTime = ManagementDateTimeConverter.ToDateTime(lastBoot);
                    TimeSpan up = DateTime.Now - bootTime;
                    return string.Format("{0}d {1}h {2}m", up.Days, up.Hours, up.Minutes);
                }
            }
            return "n/a";
        });
    }

    public string GetModelText()
    {
        return GetCached("model", TimeSpan.FromMinutes(10), delegate
        {
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Manufacturer, Model FROM Win32_ComputerSystem"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    string manu = obj["Manufacturer"].ToString();
                    string model = obj["Model"].ToString();
                    return manu + " " + model;
                }
            }
            return "n/a";
        });
    }

    public string GetCpuTempText()
    {
        return GetCached("cpuTemp", TimeSpan.FromSeconds(20), delegate
        {
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("root\\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    double temp = Convert.ToDouble(obj["CurrentTemperature"]);
                    double celsius = (temp / 10.0) - 273.15;
                    return celsius.ToString("F1") + "°C";
                }
            }
            return "n/a";
        });
    }

    public string GetBatteryHealthText()
    {
        return GetCached("batteryHealth", TimeSpan.FromMinutes(2), delegate
        {
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT DesignCapacity, FullChargeCapacity FROM Win32_Battery"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    if (obj["DesignCapacity"] == null || obj["FullChargeCapacity"] == null)
                        return "n/a";

                    double design = Convert.ToDouble(obj["DesignCapacity"]);
                    double full = Convert.ToDouble(obj["FullChargeCapacity"]);
                    if (design <= 0) return "n/a";

                    double health = (full / design) * 100.0;
                    return health.ToString("F0") + "% (" + (int)full + " / " + (int)design + " mWh)";
                }
            }
            return "n/a";
        });
    }

    public string GetGpuText()
    {
        return GetCached("gpu", TimeSpan.FromMinutes(10), delegate
        {
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
            {
                foreach (ManagementObject obj in searcher.Get())
                    return obj["Name"].ToString();
            }
            return "n/a";
        });
    }

    public string GetDisplayText()
    {
        return GetCached("display", TimeSpan.FromMinutes(5), delegate
        {
            Screen screen = Screen.PrimaryScreen;
            return string.Format("{0}x{1} ({2} bits)", screen.Bounds.Width, screen.Bounds.Height, screen.BitsPerPixel);
        });
    }

    public string GetBrightnessText()
    {
        return GetCached("brightness", TimeSpan.FromSeconds(30), delegate
        {
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("root\\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness"))
            {
                foreach (ManagementObject obj in searcher.Get())
                    return obj["CurrentBrightness"].ToString() + "%";
            }
            return "n/a (Desktop?)";
        });
    }

    public string GetPowerPlanText()
    {
        return GetCached("powerPlan", TimeSpan.FromSeconds(30), delegate
        {
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("root\\cimv2\\power", "SELECT ElementName FROM Win32_PowerPlan WHERE IsActive = True"))
            {
                foreach (ManagementObject obj in searcher.Get())
                    return obj["ElementName"].ToString();
            }
            return "n/a";
        });
    }

    public string GetBiosText()
    {
        return GetCached("bios", TimeSpan.FromMinutes(60), delegate
        {
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT SMBIOSBIOSVersion FROM Win32_BIOS"))
            {
                foreach (ManagementObject obj in searcher.Get())
                    return obj["SMBIOSBIOSVersion"].ToString();
            }
            return "n/a";
        });
    }

    public string GetBatteryCycleCountText()
    {
        return GetCached("cycleCount", TimeSpan.FromMinutes(10), delegate
        {
            // Try WMI BatteryCycleCount (available on some systems)
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("root\\WMI", "SELECT CycleCount FROM BatteryCycleCount"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        object val = obj["CycleCount"];
                        if (val != null)
                        {
                            uint cycles = Convert.ToUInt32(val);
                            return cycles.ToString();
                        }
                    }
                }
            }
            catch { }

            // Fallback: try Win32_Battery EstimatedRunTime as proxy... not available
            // Try portable battery static data
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("root\\WMI", "SELECT CycleCount FROM BatteryStaticData"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        object val = obj["CycleCount"];
                        if (val != null)
                            return Convert.ToUInt32(val).ToString();
                    }
                }
            }
            catch { }

            return "n/a";
        });
    }

    public string GetBatteryChargeRateText()
    {
        return GetCached("chargeRate", TimeSpan.FromSeconds(15), delegate
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("root\\WMI", "SELECT ChargeRate, DischargeRate, Charging FROM BatteryStatus WHERE Active = True"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        bool charging = Convert.ToBoolean(obj["Charging"]);
                        if (charging)
                        {
                            object rate = obj["ChargeRate"];
                            if (rate != null)
                            {
                                int mw = Convert.ToInt32(rate);
                                if (mw > 0)
                                    return "+" + FormatMilliwatts(mw);
                            }
                        }
                        else
                        {
                            object rate = obj["DischargeRate"];
                            if (rate != null)
                            {
                                int mw = Convert.ToInt32(rate);
                                if (mw > 0)
                                    return "-" + FormatMilliwatts(mw);
                            }
                        }
                    }
                }
            }
            catch { }

            return "n/a";
        });
    }

    private string GetCached(string key, TimeSpan ttl, Func<string> factory)
    {
        lock (_cacheLock)
        {
            CacheItem item;
            if (_cache.TryGetValue(key, out item) && item.ExpiresUtc > DateTime.UtcNow)
                return item.Value;
        }

        string value;
        try
        {
            value = factory();
        }
        catch (Exception ex)
        {
            AppLogger.Error("System info query failed for key: " + key, ex);
            value = "n/a";
        }

        lock (_cacheLock)
        {
            _cache[key] = new CacheItem { Value = value, ExpiresUtc = DateTime.UtcNow.Add(ttl) };
        }
        return value;
    }

    public static string FormatKb(ulong kb)
    {
        double gb = kb / 1024.0 / 1024.0;
        if (gb >= 1)
            return gb.ToString("F1") + " GB";

        double mb = kb / 1024.0;
        return mb.ToString("F0") + " MB";
    }

    public static string FormatBytes(long bytes)
    {
        double gb = bytes / 1024.0 / 1024.0 / 1024.0;
        if (gb >= 1)
            return gb.ToString("F1") + " GB";

        double mb = bytes / 1024.0 / 1024.0;
        return mb.ToString("F0") + " MB";
    }

    private static string FormatMilliwatts(int mw)
    {
        if (mw >= 1000)
            return (mw / 1000.0).ToString("F1") + " W";
        return mw + " mW";
    }
}
