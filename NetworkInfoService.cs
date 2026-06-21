using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;

public class NetworkInfoService : INetworkInfoService
{
    private readonly object _publicIpLock = new object();
    private string _publicIpCached = "n/a";
    private DateTime _publicIpLastFetchUtc = DateTime.MinValue;
    private bool _publicIpFetching;

    private readonly object _wifiLock = new object();
    private string _wifiCached = "n/a";
    private DateTime _wifiLastFetchUtc = DateTime.MinValue;

    public string GetLocalIp()
    {
        try
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                    continue;

                IPInterfaceProperties props = ni.GetIPProperties();
                foreach (UnicastIPAddressInformation addr in props.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(addr.Address))
                    {
                        return addr.Address.ToString();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to get local IP", ex);
        }
        return "n/a";
    }

    public string GetWifiSsid()
    {
        lock (_wifiLock)
        {
            if ((DateTime.UtcNow - _wifiLastFetchUtc) < TimeSpan.FromSeconds(15) && _wifiCached != "n/a")
                return _wifiCached;
        }

        string ssid = FetchWifiSsid();

        lock (_wifiLock)
        {
            _wifiCached = ssid;
            _wifiLastFetchUtc = DateTime.UtcNow;
        }

        return ssid;
    }

    private string FetchWifiSsid()
    {
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "netsh";
            psi.Arguments = "wlan show interfaces";
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;

            using (Process p = Process.Start(psi))
            {
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(1000);

                string[] lines = output.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("SSID", StringComparison.OrdinalIgnoreCase) && trimmed.Contains(":"))
                    {
                        string[] parts = trimmed.Split(new char[] { ':' }, 2);
                        if (parts.Length == 2)
                            return parts[1].Trim();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to get Wi-Fi SSID", ex);
        }
        return "n/a";
    }

    public string GetPublicIpDisplay()
    {
        EnsurePublicIp();
        lock (_publicIpLock)
        {
            return _publicIpCached;
        }
    }

    private void EnsurePublicIp()
    {
        lock (_publicIpLock)
        {
            if (_publicIpFetching)
                return;

            if ((DateTime.UtcNow - _publicIpLastFetchUtc) < TimeSpan.FromMinutes(5) && _publicIpCached != "n/a")
                return;

            _publicIpFetching = true;
            ThreadPool.QueueUserWorkItem(_ => FetchPublicIpWorker());
        }
    }

    private void FetchPublicIpWorker()
    {
        try
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create("https://api.ipify.org");
            req.Method = "GET";
            req.Timeout = 4000;
            req.ReadWriteTimeout = 4000;

            using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
            using (StreamReader reader = new StreamReader(res.GetResponseStream()))
            {
                string value = reader.ReadToEnd();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    lock (_publicIpLock)
                    {
                        _publicIpCached = value.Trim();
                        _publicIpLastFetchUtc = DateTime.UtcNow;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to fetch public IP", ex);
        }
        finally
        {
            lock (_publicIpLock)
            {
                _publicIpFetching = false;
            }
        }
    }
}
