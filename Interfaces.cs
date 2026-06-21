using System.Collections.Generic;

public interface IBatteryHistoryService
{
    void LoadPersistedSamples(List<BatterySample> samples);
    bool MaybeAddSample(int percent, bool isCharging);
    List<BatterySample> GetSamplesWindow();
    List<BatterySample> GetSamplesWindow(int windowMinutes);
    List<BatterySample> GetPredictionSamplesWindow();
    List<BatterySample> GetPredictionSamplesWindow(int windowMinutes);
    List<BatterySample> GetAllSamples();
    string BuildTrendText();
    string GetEstimatedTimeText();
}

public interface INetworkInfoService
{
    string GetLocalIp();
    string GetWifiSsid();
    string GetPublicIpDisplay();
}

public interface ISystemInfoService
{
    string GetRamText();
    string GetDiskText();
    string GetUptimeText();
    string GetModelText();
    string GetCpuTempText();
    string GetBatteryHealthText();
    string GetGpuText();
    string GetDisplayText();
    string GetBrightnessText();
    string GetPowerPlanText();
    string GetBiosText();
    string GetBatteryCycleCountText();
    string GetBatteryChargeRateText();
}

public interface IProcessInfoService
{
    string GetTopCpuProcess();
    string GetTopRamProcess();
}

public interface IConfigStore
{
    string ConfigPath { get; }
    int LoadIconSize(int defaultSize);
    void SaveIconSize(int iconSize);
    int LoadUpdateIntervalSeconds(int defaultValue);
    void SaveUpdateIntervalSeconds(int seconds);
    List<BatterySample> LoadBatterySamples();
    void SaveBatterySamples(List<BatterySample> samples);
    int LoadLowBatteryThreshold(int defaultValue);
    void SaveLowBatteryThreshold(int threshold);
    int LoadCriticalBatteryThreshold(int defaultValue);
    void SaveCriticalBatteryThreshold(int threshold);
    bool LoadFullBatteryAlert(bool defaultValue);
    void SaveFullBatteryAlert(bool enabled);
    bool LoadSoundAlerts(bool defaultValue);
    void SaveSoundAlerts(bool enabled);
}
