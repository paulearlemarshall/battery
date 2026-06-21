using System;
using System.Threading;
using System.Windows.Forms;

public static class Program
{
    public const string AppVersion = "1.1.0";

    [STAThread]
    public static void Main()
    {
        PowerInterop.SetProcessDPIAware();

        bool createdNew = false;
        Mutex singleInstanceMutex = null;

        try
        {
            singleInstanceMutex = new Mutex(true, "Local\\BatteryTray.Singleton", out createdNew);
            if (!createdNew)
            {
                MessageBox.Show(
                    "BatteryTray is already running.",
                    "BatteryTray",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new BatteryTrayApp());
        }
        finally
        {
            if (singleInstanceMutex != null)
            {
                try
                {
                    if (createdNew)
                        singleInstanceMutex.ReleaseMutex();
                }
                catch
                {
                }

                singleInstanceMutex.Dispose();
            }
        }
    }
}
