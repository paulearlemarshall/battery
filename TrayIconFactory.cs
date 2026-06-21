using System;
using System.Drawing;
using System.Drawing.Text;

public static class TrayIconFactory
{
    public static Icon CreateBatteryIcon(int percentage, bool isCharging, int iconSize)
    {
        return CreateTextIcon(percentage.ToString(), GetBatteryColor(percentage, isCharging), iconSize,
            percentage == 100 ? iconSize * 0.38f : (percentage >= 10 ? iconSize * 0.55f : iconSize * 0.70f));
    }

    public static Icon CreateUnavailableIcon(int iconSize)
    {
        return CreateTextIcon("?", Color.LightGray, iconSize, iconSize * 0.70f);
    }

    private static Color GetBatteryColor(int percentage, bool isCharging)
    {
        if (isCharging) return Color.Cyan;
        if (percentage > 50) return Color.Lime;
        if (percentage > 20) return Color.Yellow;
        return Color.Red;
    }

    private static Icon CreateTextIcon(string text, Color color, int iconSize, float fontSize)
    {
        using (Bitmap bitmap = new Bitmap(iconSize, iconSize))
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            using (Font font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Brush brush = new SolidBrush(color))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                g.DrawString(text, font, brush, new RectangleF(0, 0, iconSize, iconSize), sf);
            }

            IntPtr hIcon = bitmap.GetHicon();
            try
            {
                Icon tmp = Icon.FromHandle(hIcon);
                return (Icon)tmp.Clone();
            }
            finally
            {
                PowerInterop.DestroyIcon(hIcon);
            }
        }
    }
}
