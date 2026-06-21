using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Media;
using System.Text;
using System.Windows.Forms;
using System.Collections.Generic;
using Microsoft.Win32;

public class BatteryTrayApp : ApplicationContext
{
    private NotifyIcon trayIcon;
    private Timer updateTimer;
    private PerformanceCounter cpuCounter;

    private readonly IBatteryHistoryService historyService;
    private readonly INetworkInfoService networkInfoService;
    private readonly ISystemInfoService systemInfoService;
    private readonly IProcessInfoService processInfoService;
    private readonly IConfigStore configStore;

    private int iconSize = 32;
    private int updateIntervalSeconds = 10;
    private int lowBatteryThreshold = 20;
    private int criticalBatteryThreshold = 5;
    private bool fullBatteryAlert = true;
    private bool soundAlerts = true;

    private readonly object menuInfoLock = new object();
    private bool menuInfoRefreshQueued;
    private DateTime menuInfoLastRefreshUtc = DateTime.MinValue;

    private string cachedBatteryHealthText = "Battery Health: --";
    private string cachedCycleCountText = "Battery Cycles: --";
    private string cachedChargeRateText = "Charge Rate: --";
    private string cachedLocalIpText = "Local IP: --";
    private string cachedWifiSsidText = "Wi-Fi SSID: --";
    private string cachedPublicIpText = "Public IP: --";
    private string cachedRamText = "RAM: --";
    private string cachedDiskText = "Disk C: --";
    private string cachedUptimeText = "Uptime: --";
    private string cachedModelText = "Model: --";
    private string cachedCpuTempText = "CPU Temp: --";
    private string cachedTopCpuText = "Top CPU: --";
    private string cachedTopRamText = "Top RAM: --";
    private string cachedGpuText = "GPU: --";
    private string cachedDisplayText = "Display: --";
    private string cachedBrightnessText = "Brightness: --";
    private string cachedPowerPlanText = "Power Plan: --";
    private string cachedBiosText = "BIOS: --";

    // Alert state tracking (avoid repeated alerts)
    private bool lowBatteryAlerted;
    private bool criticalBatteryAlerted;
    private bool fullBatteryAlerted;
    private int lastAlertPercent = -1;

    private ToolStripMenuItem cpuItem;
    private ToolStripMenuItem trendItem;
    private ToolStripMenuItem powerSourceItem;
    private ToolStripMenuItem chargingItem;
    private ToolStripMenuItem timeRemainingItem;
    private ToolStripMenuItem estimatedTimeItem;
    private ToolStripMenuItem batteryHealthItem;
    private ToolStripMenuItem cycleCountItem;
    private ToolStripMenuItem chargeRateItem;
    private ToolStripMenuItem localIpItem;
    private ToolStripMenuItem wifiSsidItem;
    private ToolStripMenuItem publicIpItem;
    private ToolStripMenuItem ramItem;
    private ToolStripMenuItem diskItem;
    private ToolStripMenuItem uptimeItem;
    private ToolStripMenuItem modelItem;
    private ToolStripMenuItem windowsItem;
    private ToolStripMenuItem cpuTempItem;
    private ToolStripMenuItem topCpuItem;
    private ToolStripMenuItem topRamItem;
    private ToolStripMenuItem gpuItem;
    private ToolStripMenuItem displayItem;
    private ToolStripMenuItem powerPlanItem;
    private ToolStripMenuItem biosItem;
    private ToolStripMenuItem brightnessItem;

    private PowerInterop.SYSTEM_POWER_STATUS lastPowerStatus;
    private bool lastPowerStatusValid;

    private static readonly string[] sizeNames = {
        "Small (16px)",
        "Medium (32px)",
        "Large (48px)",
        "Extra Large (64px)"
    };

    private static readonly int[] sizePx = { 16, 32, 48, 64 };

    private static readonly string[] intervalNames = {
        "5 seconds",
        "10 seconds",
        "15 seconds",
        "30 seconds",
        "60 seconds",
        "120 seconds"
    };

    private static readonly int[] intervalValues = { 5, 10, 15, 30, 60, 120 };

    private static readonly int[] lowBatteryOptions = { 5, 10, 15, 20, 25, 30 };
    private static readonly int[] criticalBatteryOptions = { 1, 3, 5, 7, 10, 15, 20 };

    private const string AutoStartRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AutoStartValueName = "BatteryTray";

    public BatteryTrayApp()
        : this(
            new BatteryHistoryService(1, 60, 1440),
            new NetworkInfoService(),
            new SystemInfoService(),
            new ProcessInfoService(),
            new ConfigStore())
    {
    }

    public BatteryTrayApp(
        IBatteryHistoryService historyService,
        INetworkInfoService networkInfoService,
        ISystemInfoService systemInfoService,
        IProcessInfoService processInfoService,
        IConfigStore configStore)
    {
        if (historyService == null) throw new ArgumentNullException("historyService");
        if (networkInfoService == null) throw new ArgumentNullException("networkInfoService");
        if (systemInfoService == null) throw new ArgumentNullException("systemInfoService");
        if (processInfoService == null) throw new ArgumentNullException("processInfoService");
        if (configStore == null) throw new ArgumentNullException("configStore");

        this.historyService = historyService;
        this.networkInfoService = networkInfoService;
        this.systemInfoService = systemInfoService;
        this.processInfoService = processInfoService;
        this.configStore = configStore;

        iconSize = this.configStore.LoadIconSize(32);
        updateIntervalSeconds = this.configStore.LoadUpdateIntervalSeconds(10);
        lowBatteryThreshold = this.configStore.LoadLowBatteryThreshold(20);
        criticalBatteryThreshold = this.configStore.LoadCriticalBatteryThreshold(5);
        fullBatteryAlert = this.configStore.LoadFullBatteryAlert(true);
        soundAlerts = this.configStore.LoadSoundAlerts(true);

        this.historyService.LoadPersistedSamples(this.configStore.LoadBatterySamples());

        trayIcon = new NotifyIcon();
        trayIcon.Visible = true;
        trayIcon.ContextMenuStrip = BuildMenu();
        trayIcon.DoubleClick += delegate { ShowBatteryChart(null, EventArgs.Empty); };

        updateTimer = new Timer();
        updateTimer.Interval = updateIntervalSeconds * 1000;
        updateTimer.Tick += delegate { UpdateTray(); };
        updateTimer.Start();

        UpdateTray();
        QueueMenuInfoRefresh(true);
    }

    private ContextMenuStrip BuildMenu()
    {
        ContextMenuStrip menu = new ContextMenuStrip();

        cpuItem = AddInfoItem(menu, "CPU Load: --%");
        trendItem = AddInfoItem(menu, "Battery Δ 1m: -- | 5m: -- | 10m: --");

        menu.Items.Add(new ToolStripSeparator());

        powerSourceItem = AddInfoItem(menu, "Power Source: --");
        chargingItem = AddInfoItem(menu, "Charging: --");
        timeRemainingItem = AddInfoItem(menu, "Time Remaining: --");
        estimatedTimeItem = AddInfoItem(menu, "Estimated: --");
        batteryHealthItem = AddInfoItem(menu, cachedBatteryHealthText);
        cycleCountItem = AddInfoItem(menu, cachedCycleCountText);
        chargeRateItem = AddInfoItem(menu, cachedChargeRateText);

        menu.Items.Add(new ToolStripSeparator());

        localIpItem = AddInfoItem(menu, cachedLocalIpText);
        wifiSsidItem = AddInfoItem(menu, cachedWifiSsidText);
        publicIpItem = AddInfoItem(menu, cachedPublicIpText);

        menu.Items.Add(new ToolStripSeparator());

        ramItem = AddInfoItem(menu, cachedRamText);
        diskItem = AddInfoItem(menu, cachedDiskText);
        uptimeItem = AddInfoItem(menu, cachedUptimeText);
        modelItem = AddInfoItem(menu, cachedModelText);
        windowsItem = AddInfoItem(menu, "Windows: --");
        cpuTempItem = AddInfoItem(menu, cachedCpuTempText);

        menu.Items.Add(new ToolStripSeparator());

        topCpuItem = AddInfoItem(menu, cachedTopCpuText);
        topRamItem = AddInfoItem(menu, cachedTopRamText);

        menu.Items.Add(new ToolStripSeparator());

        gpuItem = AddInfoItem(menu, cachedGpuText);
        displayItem = AddInfoItem(menu, cachedDisplayText);
        brightnessItem = AddInfoItem(menu, cachedBrightnessText);
        powerPlanItem = AddInfoItem(menu, cachedPowerPlanText);
        biosItem = AddInfoItem(menu, cachedBiosText);

        menu.Items.Add(new ToolStripSeparator());

        // Battery chart submenu with time ranges
        ToolStripMenuItem chartMenu = new ToolStripMenuItem("Battery Chart");
        string[] chartLabels = { "Last 1 Hour", "Last 6 Hours", "Last 24 Hours" };
        int[] chartWindows = { 60, 360, 1440 };
        for (int i = 0; i < chartLabels.Length; i++)
        {
            int windowMin = chartWindows[i];
            ToolStripMenuItem item = new ToolStripMenuItem(chartLabels[i]);
            item.Click += delegate { ShowBatteryChartWithWindow(windowMin); };
            chartMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(chartMenu);

        // Copy diagnostics
        ToolStripMenuItem copyItem = new ToolStripMenuItem("Copy Diagnostics to Clipboard");
        copyItem.Click += CopyDiagnostics;
        menu.Items.Add(copyItem);

        // Generate battery report
        ToolStripMenuItem reportItem = new ToolStripMenuItem("Generate Battery Report");
        reportItem.Click += GenerateBatteryReport;
        menu.Items.Add(reportItem);

        menu.Items.Add(new ToolStripSeparator());

        // Icon size submenu
        ToolStripMenuItem sizeMenu = new ToolStripMenuItem("Icon Size");
        for (int i = 0; i < sizeNames.Length; i++)
        {
            int px = sizePx[i];
            ToolStripMenuItem item = new ToolStripMenuItem(sizeNames[i]);
            item.Checked = (iconSize == px);
            item.Click += delegate
            {
                iconSize = px;
                configStore.SaveIconSize(iconSize);
                RebuildMenuAndUpdate();
            };
            sizeMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(sizeMenu);

        // Update interval submenu
        ToolStripMenuItem intervalMenu = new ToolStripMenuItem("Update Interval");
        for (int i = 0; i < intervalNames.Length; i++)
        {
            int secs = intervalValues[i];
            ToolStripMenuItem item = new ToolStripMenuItem(intervalNames[i]);
            item.Checked = (updateIntervalSeconds == secs);
            item.Click += delegate
            {
                updateIntervalSeconds = secs;
                configStore.SaveUpdateIntervalSeconds(secs);
                if (updateTimer != null)
                    updateTimer.Interval = secs * 1000;
                RebuildMenuAndUpdate();
            };
            intervalMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(intervalMenu);

        // Alerts submenu
        ToolStripMenuItem alertsMenu = new ToolStripMenuItem("Alerts");

        ToolStripMenuItem lowBatMenu = new ToolStripMenuItem("Low Battery Threshold");
        for (int i = 0; i < lowBatteryOptions.Length; i++)
        {
            int threshold = lowBatteryOptions[i];
            ToolStripMenuItem item = new ToolStripMenuItem(threshold + "%");
            item.Checked = (lowBatteryThreshold == threshold);
            item.Click += delegate
            {
                lowBatteryThreshold = threshold;
                configStore.SaveLowBatteryThreshold(threshold);
                lowBatteryAlerted = false;
                RebuildMenuAndUpdate();
            };
            lowBatMenu.DropDownItems.Add(item);
        }
        alertsMenu.DropDownItems.Add(lowBatMenu);

        ToolStripMenuItem criticalBatMenu = new ToolStripMenuItem("Critical Battery Threshold");
        for (int i = 0; i < criticalBatteryOptions.Length; i++)
        {
            int threshold = criticalBatteryOptions[i];
            ToolStripMenuItem item = new ToolStripMenuItem(threshold + "%");
            item.Checked = (criticalBatteryThreshold == threshold);
            item.Click += delegate
            {
                criticalBatteryThreshold = threshold;
                configStore.SaveCriticalBatteryThreshold(threshold);
                criticalBatteryAlerted = false;
                RebuildMenuAndUpdate();
            };
            criticalBatMenu.DropDownItems.Add(item);
        }
        alertsMenu.DropDownItems.Add(criticalBatMenu);

        ToolStripMenuItem fullBatItem = new ToolStripMenuItem("Full Battery Alert");
        fullBatItem.Checked = fullBatteryAlert;
        fullBatItem.Click += delegate
        {
            fullBatteryAlert = !fullBatteryAlert;
            configStore.SaveFullBatteryAlert(fullBatteryAlert);
            fullBatteryAlerted = false;
            RebuildMenuAndUpdate();
        };
        alertsMenu.DropDownItems.Add(fullBatItem);

        ToolStripMenuItem soundItem = new ToolStripMenuItem("Sound Alerts");
        soundItem.Checked = soundAlerts;
        soundItem.Click += delegate
        {
            soundAlerts = !soundAlerts;
            configStore.SaveSoundAlerts(soundAlerts);
            RebuildMenuAndUpdate();
        };
        alertsMenu.DropDownItems.Add(soundItem);

        menu.Items.Add(alertsMenu);

        // Auto-start with Windows
        ToolStripMenuItem autoStartItem = new ToolStripMenuItem("Start with Windows");
        autoStartItem.Checked = IsAutoStartEnabled();
        autoStartItem.Click += delegate
        {
            ToggleAutoStart();
            RebuildMenuAndUpdate();
        };
        menu.Items.Add(autoStartItem);

        // Diagnostics submenu
        ToolStripMenuItem diagMenu = new ToolStripMenuItem("Diagnostics");

        ToolStripMenuItem configItem = new ToolStripMenuItem("Open Config Folder");
        configItem.Click += OpenConfigFolder;
        diagMenu.DropDownItems.Add(configItem);

        ToolStripMenuItem logItem = new ToolStripMenuItem("Open Log File");
        logItem.Click += OpenLogFile;
        diagMenu.DropDownItems.Add(logItem);

        menu.Items.Add(diagMenu);

        // About
        ToolStripMenuItem aboutItem = new ToolStripMenuItem("About BatteryTray");
        aboutItem.Click += ShowAbout;
        menu.Items.Add(aboutItem);

        menu.Items.Add(new ToolStripSeparator());

        ToolStripMenuItem exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += delegate
        {
            Cleanup();
            Application.Exit();
        };
        menu.Items.Add(exitItem);

        menu.Opening += delegate { UpdateMenuInfo(); };

        return menu;
    }

    private ToolStripMenuItem AddInfoItem(ContextMenuStrip menu, string text)
    {
        ToolStripMenuItem item = new ToolStripMenuItem(text);
        item.Enabled = true; // enabled so user can click to copy
        item.Click += delegate
        {
            try
            {
                // Extract value after the first ":"
                string copyText = item.Text;
                int colon = copyText.IndexOf(':');
                if (colon >= 0 && colon < copyText.Length - 1)
                    copyText = copyText.Substring(colon + 1).Trim();

                Clipboard.SetText(copyText);
                trayIcon.ShowBalloonTip(1500, "Copied", copyText, ToolTipIcon.Info);
            }
            catch { }
        };
        menu.Items.Add(item);
        return item;
    }

    private void RebuildMenuAndUpdate()
    {
        ContextMenuStrip oldMenu = trayIcon.ContextMenuStrip;
        trayIcon.ContextMenuStrip = BuildMenu();
        if (oldMenu != null)
            oldMenu.Dispose();
        UpdateTray();
    }

    private void UpdateMenuInfo()
    {
        float cpu = GetCpuLoadPercent();
        cpuItem.Text = cpu >= 0 ? ("CPU Load: " + cpu.ToString("F1") + "%") : "CPU Load: n/a";

        trendItem.Text = historyService.BuildTrendText();

        if (lastPowerStatusValid)
        {
            powerSourceItem.Text = "Power Source: " + GetPowerSourceText(lastPowerStatus);
            chargingItem.Text = "Charging: " + GetChargingText(lastPowerStatus);

            if (lastPowerStatus.BatteryLifeTime != uint.MaxValue)
            {
                uint mins = lastPowerStatus.BatteryLifeTime / 60;
                timeRemainingItem.Text = "Time Remaining: " + (mins / 60) + "h " + (mins % 60) + "m";
            }
            else
            {
                timeRemainingItem.Text = "Time Remaining: n/a";
            }
        }
        else
        {
            powerSourceItem.Text = "Power Source: n/a";
            chargingItem.Text = "Charging: n/a";
            timeRemainingItem.Text = "Time Remaining: n/a";
        }

        estimatedTimeItem.Text = "Estimated: " + historyService.GetEstimatedTimeText();
        QueueMenuInfoRefresh(false);

        lock (menuInfoLock)
        {
            batteryHealthItem.Text = cachedBatteryHealthText;
            cycleCountItem.Text = cachedCycleCountText;
            chargeRateItem.Text = cachedChargeRateText;

            localIpItem.Text = cachedLocalIpText;
            wifiSsidItem.Text = cachedWifiSsidText;
            publicIpItem.Text = cachedPublicIpText;

            ramItem.Text = cachedRamText;
            diskItem.Text = cachedDiskText;
            uptimeItem.Text = cachedUptimeText;
            modelItem.Text = cachedModelText;
            cpuTempItem.Text = cachedCpuTempText;

            topCpuItem.Text = cachedTopCpuText;
            topRamItem.Text = cachedTopRamText;

            gpuItem.Text = cachedGpuText;
            displayItem.Text = cachedDisplayText;
            brightnessItem.Text = cachedBrightnessText;
            powerPlanItem.Text = cachedPowerPlanText;
            biosItem.Text = cachedBiosText;
        }

        windowsItem.Text = "Windows: " + Environment.OSVersion.VersionString;
    }

    private void QueueMenuInfoRefresh(bool force)
    {
        lock (menuInfoLock)
        {
            if (menuInfoRefreshQueued)
                return;

            if (!force && (DateTime.UtcNow - menuInfoLastRefreshUtc) < TimeSpan.FromSeconds(15))
                return;

            menuInfoRefreshQueued = true;
        }

        System.Threading.ThreadPool.QueueUserWorkItem(delegate
        {
            try
            {
                string nextBatteryHealthText = "Battery Health: " + systemInfoService.GetBatteryHealthText();
                string nextCycleCountText = "Battery Cycles: " + systemInfoService.GetBatteryCycleCountText();
                string nextChargeRateText = "Charge Rate: " + systemInfoService.GetBatteryChargeRateText();
                string nextLocalIpText = "Local IP: " + networkInfoService.GetLocalIp();
                string nextWifiSsidText = "Wi-Fi SSID: " + networkInfoService.GetWifiSsid();
                string nextPublicIpText = "Public IP: " + networkInfoService.GetPublicIpDisplay();
                string nextRamText = "RAM: " + systemInfoService.GetRamText();
                string nextDiskText = "Disk C: " + systemInfoService.GetDiskText();
                string nextUptimeText = "Uptime: " + systemInfoService.GetUptimeText();
                string nextModelText = "Model: " + systemInfoService.GetModelText();
                string nextCpuTempText = "CPU Temp: " + systemInfoService.GetCpuTempText();
                string nextTopCpuText = "Top CPU: " + processInfoService.GetTopCpuProcess();
                string nextTopRamText = "Top RAM: " + processInfoService.GetTopRamProcess();
                string nextGpuText = "GPU: " + systemInfoService.GetGpuText();
                string nextDisplayText = "Display: " + systemInfoService.GetDisplayText();
                string nextBrightnessText = "Brightness: " + systemInfoService.GetBrightnessText();
                string nextPowerPlanText = "Power Plan: " + systemInfoService.GetPowerPlanText();
                string nextBiosText = "BIOS: " + systemInfoService.GetBiosText();

                lock (menuInfoLock)
                {
                    cachedBatteryHealthText = nextBatteryHealthText;
                    cachedCycleCountText = nextCycleCountText;
                    cachedChargeRateText = nextChargeRateText;
                    cachedLocalIpText = nextLocalIpText;
                    cachedWifiSsidText = nextWifiSsidText;
                    cachedPublicIpText = nextPublicIpText;
                    cachedRamText = nextRamText;
                    cachedDiskText = nextDiskText;
                    cachedUptimeText = nextUptimeText;
                    cachedModelText = nextModelText;
                    cachedCpuTempText = nextCpuTempText;
                    cachedTopCpuText = nextTopCpuText;
                    cachedTopRamText = nextTopRamText;
                    cachedGpuText = nextGpuText;
                    cachedDisplayText = nextDisplayText;
                    cachedBrightnessText = nextBrightnessText;
                    cachedPowerPlanText = nextPowerPlanText;
                    cachedBiosText = nextBiosText;
                    menuInfoLastRefreshUtc = DateTime.UtcNow;
                }
            }
            finally
            {
                lock (menuInfoLock)
                {
                    menuInfoRefreshQueued = false;
                }
            }
        });
    }

    private ChartForm chartForm;

    private void ShowBatteryChart(object sender, EventArgs e)
    {
        ShowBatteryChartWithWindow(60);
    }

    private void ShowBatteryChartWithWindow(int windowMinutes)
    {
        if (chartForm != null && !chartForm.IsDisposed)
        {
            chartForm.SetWindowMinutes(windowMinutes);

            if (!chartForm.Visible)
                chartForm.Show();

            chartForm.BringToFront();
            chartForm.Activate();
            return;
        }

        chartForm = new ChartForm(this, windowMinutes);
        chartForm.FormClosed += delegate { chartForm = null; };
        chartForm.Show();
    }

    public List<BatterySample> GetSamplesWindow(int windowMinutes)
    {
        return historyService.GetSamplesWindow(windowMinutes);
    }

    public List<BatterySample> GetPredictionSamplesWindow(int horizonMinutes)
    {
        return historyService.GetPredictionSamplesWindow(horizonMinutes);
    }

    // Keep backward compat for default
    public List<BatterySample> GetSamplesWindow()
    {
        return historyService.GetSamplesWindow();
    }

    public List<BatterySample> GetPredictionSamplesWindow()
    {
        return historyService.GetPredictionSamplesWindow();
    }

    private static bool IsBatteryPercentageKnown(PowerInterop.SYSTEM_POWER_STATUS sps)
    {
        return sps.BatteryLifePercent != 255;
    }

    private static bool IsBatteryCharging(PowerInterop.SYSTEM_POWER_STATUS sps)
    {
        return (sps.BatteryFlag & 8) == 8;
    }

    private static string GetPowerSourceText(PowerInterop.SYSTEM_POWER_STATUS sps)
    {
        if (sps.ACLineStatus == 1) return "AC";
        if (sps.ACLineStatus == 0) return "Battery";
        return "Unknown";
    }

    private static string GetChargingText(PowerInterop.SYSTEM_POWER_STATUS sps)
    {
        if ((sps.BatteryFlag & 128) == 128) return "n/a";
        if (sps.BatteryFlag == 255) return "Unknown";
        return IsBatteryCharging(sps) ? "Yes" : "No";
    }

    private float GetCpuLoadPercent()
    {
        try
        {
            if (cpuCounter == null)
            {
                cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                cpuCounter.NextValue();
            }
            return cpuCounter.NextValue();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to read CPU load", ex);
            return -1f;
        }
    }

    private void OpenConfigFolder(object sender, EventArgs e)
    {
        try
        {
            Process.Start("explorer.exe", Path.GetDirectoryName(configStore.ConfigPath));
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not open config folder: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            AppLogger.Error("Open config folder failed", ex);
        }
    }

    private void OpenLogFile(object sender, EventArgs e)
    {
        try
        {
            string logPath = AppLogger.LogPath;
            if (File.Exists(logPath))
                Process.Start("notepad.exe", logPath);
            else
                MessageBox.Show(
                    "No log file found.\nEnable debug logging by setting BATTERYTRAY_DEBUG=1 before launch.",
                    "BatteryTray",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Open log file failed", ex);
        }
    }

    private void CopyDiagnostics(object sender, EventArgs e)
    {
        try
        {
            // Ensure menu info is current
            UpdateMenuInfo();

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== BatteryTray Diagnostics ===");
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Version: " + Program.AppVersion);
            sb.AppendLine();

            sb.AppendLine(cpuItem.Text);
            sb.AppendLine(trendItem.Text);
            sb.AppendLine();

            sb.AppendLine(powerSourceItem.Text);
            sb.AppendLine(chargingItem.Text);
            sb.AppendLine(timeRemainingItem.Text);
            sb.AppendLine(estimatedTimeItem.Text);
            sb.AppendLine(batteryHealthItem.Text);
            sb.AppendLine(cycleCountItem.Text);
            sb.AppendLine(chargeRateItem.Text);
            sb.AppendLine();

            sb.AppendLine(localIpItem.Text);
            sb.AppendLine(wifiSsidItem.Text);
            sb.AppendLine(publicIpItem.Text);
            sb.AppendLine();

            sb.AppendLine(ramItem.Text);
            sb.AppendLine(diskItem.Text);
            sb.AppendLine(uptimeItem.Text);
            sb.AppendLine(modelItem.Text);
            sb.AppendLine(windowsItem.Text);
            sb.AppendLine(cpuTempItem.Text);
            sb.AppendLine();

            sb.AppendLine(topCpuItem.Text);
            sb.AppendLine(topRamItem.Text);
            sb.AppendLine();

            sb.AppendLine(gpuItem.Text);
            sb.AppendLine(displayItem.Text);
            sb.AppendLine(brightnessItem.Text);
            sb.AppendLine(powerPlanItem.Text);
            sb.AppendLine(biosItem.Text);

            Clipboard.SetText(sb.ToString());
            trayIcon.ShowBalloonTip(2000, "BatteryTray", "Diagnostics copied to clipboard.", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to copy diagnostics: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            AppLogger.Error("Copy diagnostics failed", ex);
        }
    }

    private void GenerateBatteryReport(object sender, EventArgs e)
    {
        try
        {
            string reportDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BatteryTray");
            if (!Directory.Exists(reportDir))
                Directory.CreateDirectory(reportDir);

            string reportPath = Path.Combine(reportDir, "battery-report.html");

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "powercfg";
            psi.Arguments = "/batteryreport /output \"" + reportPath + "\"";
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;

            using (Process p = Process.Start(psi))
            {
                p.WaitForExit(10000);
            }

            if (File.Exists(reportPath))
            {
                Process.Start(reportPath);
                trayIcon.ShowBalloonTip(2000, "BatteryTray", "Battery report generated and opened.", ToolTipIcon.Info);
            }
            else
            {
                MessageBox.Show(
                    "Battery report could not be generated.\nThis may require administrator privileges or a battery-equipped device.",
                    "BatteryTray",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to generate battery report: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            AppLogger.Error("Generate battery report failed", ex);
        }
    }

    private void ShowAbout(object sender, EventArgs e)
    {
        MessageBox.Show(
            "BatteryTray v" + Program.AppVersion + "\n\n" +
            "A lightweight Windows system tray battery monitor\n" +
            "with rich diagnostics and live charting.\n\n" +
            "Double-click tray icon to open chart.\n" +
            "Click any menu item to copy its value.\n\n" +
            "Config: " + configStore.ConfigPath,
            "About BatteryTray",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private bool IsAutoStartEnabled()
    {
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(AutoStartRegistryKey, false))
            {
                if (key == null) return false;
                object val = key.GetValue(AutoStartValueName);
                return val != null;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to check auto-start", ex);
            return false;
        }
    }

    private void ToggleAutoStart()
    {
        try
        {
            bool currentlyEnabled = IsAutoStartEnabled();

            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(AutoStartRegistryKey, true))
            {
                if (key == null) return;

                if (currentlyEnabled)
                {
                    key.DeleteValue(AutoStartValueName, false);
                    trayIcon.ShowBalloonTip(2000, "BatteryTray", "Removed from Windows startup.", ToolTipIcon.Info);
                }
                else
                {
                    string exePath = Application.ExecutablePath;
                    key.SetValue(AutoStartValueName, "\"" + exePath + "\"");
                    trayIcon.ShowBalloonTip(2000, "BatteryTray", "Added to Windows startup.", ToolTipIcon.Info);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to toggle auto-start: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            AppLogger.Error("Toggle auto-start failed", ex);
        }
    }

    private void CheckBatteryAlerts(int percent, bool isCharging)
    {
        // Reset alerts when state changes significantly
        if (isCharging)
        {
            // Charging: reset low/critical alerts, check for full
            lowBatteryAlerted = false;
            criticalBatteryAlerted = false;

            if (fullBatteryAlert && percent >= 100 && !fullBatteryAlerted)
            {
                fullBatteryAlerted = true;
                ShowAlert("Battery Full", "Battery is fully charged (100%). You can unplug now.", ToolTipIcon.Info);
            }
        }
        else
        {
            // Discharging: reset full alert, check for low/critical
            fullBatteryAlerted = false;

            if (percent <= criticalBatteryThreshold && !criticalBatteryAlerted)
            {
                criticalBatteryAlerted = true;
                ShowAlert("Critical Battery!", "Battery is critically low at " + percent + "%! Plug in immediately.", ToolTipIcon.Error);
            }
            else if (percent <= lowBatteryThreshold && !lowBatteryAlerted)
            {
                lowBatteryAlerted = true;
                ShowAlert("Low Battery", "Battery is at " + percent + "%. Consider plugging in.", ToolTipIcon.Warning);
            }

            // Reset alerts if battery goes back up (e.g., was plugged in briefly)
            if (percent > lowBatteryThreshold + 5)
                lowBatteryAlerted = false;
            if (percent > criticalBatteryThreshold + 5)
                criticalBatteryAlerted = false;
        }

        lastAlertPercent = percent;
    }

    private void ShowAlert(string title, string message, ToolTipIcon icon)
    {
        try
        {
            trayIcon.ShowBalloonTip(5000, title, message, icon);

            if (soundAlerts)
            {
                if (icon == ToolTipIcon.Error)
                    SystemSounds.Hand.Play();
                else if (icon == ToolTipIcon.Warning)
                    SystemSounds.Exclamation.Play();
                else
                    SystemSounds.Asterisk.Play();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to show alert", ex);
        }
    }

    private void UpdateTray()
    {
        try
        {
            PowerInterop.SYSTEM_POWER_STATUS sps;
            if (!PowerInterop.GetSystemPowerStatus(out sps))
            {
                lastPowerStatusValid = false;

                Icon failedIcon = trayIcon.Icon;
                trayIcon.Icon = TrayIconFactory.CreateUnavailableIcon(iconSize);
                if (failedIcon != null)
                    failedIcon.Dispose();

                trayIcon.Text = "Battery: unavailable";
                return;
            }

            lastPowerStatus = sps;
            lastPowerStatusValid = true;

            bool percentKnown = IsBatteryPercentageKnown(sps);
            int percent = percentKnown ? sps.BatteryLifePercent : 0;
            if (percent > 100) percent = 100;
            bool isCharging = IsBatteryCharging(sps);

            if (percentKnown)
            {
                if (historyService.MaybeAddSample(percent, isCharging))
                    configStore.SaveBatterySamples(historyService.GetAllSamples());

                CheckBatteryAlerts(percent, isCharging);
            }

            QueueMenuInfoRefresh(false);

            Icon oldIcon = trayIcon.Icon;
            trayIcon.Icon = percentKnown
                ? TrayIconFactory.CreateBatteryIcon(percent, isCharging, iconSize)
                : TrayIconFactory.CreateUnavailableIcon(iconSize);
            if (oldIcon != null)
                oldIcon.Dispose();

            string tooltip = percentKnown ? ("Battery: " + percent + "%") : "Battery: unknown";
            if (sps.BatteryLifeTime != uint.MaxValue)
            {
                uint mins = sps.BatteryLifeTime / 60;
                tooltip += " (" + (mins / 60) + "h " + (mins % 60) + "m remaining)";
            }
            else if (isCharging)
            {
                tooltip += " (Charging)";
            }

            trayIcon.Text = tooltip.Length > 63 ? tooltip.Substring(0, 63) : tooltip;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Tray update failed", ex);
        }
    }

    private void Cleanup()
    {
        try
        {
            if (updateTimer != null)
            {
                updateTimer.Stop();
                updateTimer.Dispose();
                updateTimer = null;
            }

            if (chartForm != null && !chartForm.IsDisposed)
            {
                chartForm.Close();
                chartForm = null;
            }

            if (cpuCounter != null)
            {
                cpuCounter.Dispose();
                cpuCounter = null;
            }

            if (trayIcon != null)
            {
                if (trayIcon.ContextMenuStrip != null)
                    trayIcon.ContextMenuStrip.Dispose();

                if (trayIcon.Icon != null)
                    trayIcon.Icon.Dispose();

                trayIcon.Visible = false;
                trayIcon.Dispose();
                trayIcon = null;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Cleanup failed", ex);
        }
    }
}
