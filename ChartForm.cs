using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

public class ChartForm : Form
{
    private readonly BatteryTrayApp app;
    private Chart chart;
    private Timer refreshTimer;
    private Series chargingSeries;
    private Series dischargingSeries;
    private Series predictionSeries;
    private Panel toolbarPanel;
    private Label statsLabel;
    private int windowMinutes;

    private static readonly Color BgDark = Color.FromArgb(20, 20, 20);
    private static readonly Color BgChart = Color.FromArgb(25, 25, 25);
    private static readonly Color GridColor = Color.FromArgb(40, 40, 40);
    private static readonly Color AxisColor = Color.FromArgb(60, 60, 60);
    private static readonly Color ChargingColor = Color.FromArgb(0, 255, 200);
    private static readonly Color DischargingColor = Color.FromArgb(255, 60, 100);
    private static readonly Color PredictionColor = Color.FromArgb(160, 180, 180, 180);

    public ChartForm(BatteryTrayApp app) : this(app, 60) { }

    public ChartForm(BatteryTrayApp app, int windowMinutes)
    {
        this.app = app;
        this.windowMinutes = windowMinutes;

        Text = "Battery Analytics (Live)";
        Width = 900;
        Height = 520;
        MinimumSize = new Size(600, 400);
        BackColor = BgDark;
        ForeColor = Color.White;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;

        InitializeComponents();
        UpdateChartData();

        refreshTimer = new Timer();
        refreshTimer.Interval = 30000;
        refreshTimer.Tick += delegate { UpdateChartData(); };
        refreshTimer.Start();

        FormClosed += delegate
        {
            refreshTimer.Stop();
            refreshTimer.Dispose();
        };
    }

    public void SetWindowMinutes(int minutes)
    {
        windowMinutes = minutes;
        UpdateToolbarChecks();
        UpdateChartData();
    }

    private void InitializeComponents()
    {
        // Toolbar panel with time range buttons
        toolbarPanel = new Panel();
        toolbarPanel.Dock = DockStyle.Top;
        toolbarPanel.Height = 40;
        toolbarPanel.BackColor = Color.FromArgb(30, 30, 30);
        toolbarPanel.Padding = new Padding(8, 6, 8, 6);

        string[] btnLabels = { "1H", "6H", "24H" };
        int[] btnWindows = { 60, 360, 1440 };

        for (int i = 0; i < btnLabels.Length; i++)
        {
            int w = btnWindows[i];
            Button btn = new Button();
            btn.Text = btnLabels[i];
            btn.Tag = w;
            btn.Width = 60;
            btn.Height = 28;
            btn.Left = 8 + i * 68;
            btn.Top = 6;
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 60, 60);
            btn.BackColor = (w == windowMinutes) ? Color.FromArgb(50, 130, 200) : Color.FromArgb(45, 45, 45);
            btn.ForeColor = Color.White;
            btn.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            btn.Cursor = Cursors.Hand;
            btn.Click += delegate
            {
                windowMinutes = w;
                UpdateToolbarChecks();
                UpdateChartData();
            };
            toolbarPanel.Controls.Add(btn);
        }

        // Stats label on right side of toolbar
        statsLabel = new Label();
        statsLabel.AutoSize = false;
        statsLabel.Dock = DockStyle.Right;
        statsLabel.Width = 500;
        statsLabel.ForeColor = Color.FromArgb(180, 180, 180);
        statsLabel.Font = new Font("Segoe UI", 9, FontStyle.Regular);
        statsLabel.TextAlign = ContentAlignment.MiddleRight;
        statsLabel.Text = "";
        toolbarPanel.Controls.Add(statsLabel);

        Controls.Add(toolbarPanel);

        // Chart
        chart = new Chart();
        chart.Dock = DockStyle.Fill;
        chart.BackColor = BgDark;
        chart.ForeColor = Color.White;

        ChartArea area = new ChartArea("Main");
        area.BackColor = BgChart;

        area.AxisX.LabelStyle.Format = "HH:mm";
        area.AxisX.LabelStyle.ForeColor = Color.Gray;
        area.AxisX.IntervalType = DateTimeIntervalType.Minutes;
        area.AxisX.MajorGrid.LineColor = GridColor;
        area.AxisX.LineColor = AxisColor;
        area.AxisX.LabelStyle.Font = new Font("Segoe UI", 8);

        area.AxisY.Minimum = 0;
        area.AxisY.Maximum = 100;
        area.AxisY.Interval = 20;
        area.AxisY.LabelStyle.ForeColor = Color.Gray;
        area.AxisY.MajorGrid.LineColor = GridColor;
        area.AxisY.LineColor = AxisColor;
        area.AxisY.LabelStyle.Font = new Font("Segoe UI", 8);
        area.AxisY.LabelStyle.Format = "0'%'";

        chart.ChartAreas.Add(area);

        Legend legend = new Legend("MainLegend");
        legend.Docking = Docking.Top;
        legend.Alignment = StringAlignment.Center;
        legend.BackColor = Color.Transparent;
        legend.ForeColor = Color.White;
        legend.Font = new Font("Segoe UI", 9, FontStyle.Regular);
        chart.Legends.Add(legend);

        chargingSeries = new Series("Charging");
        chargingSeries.ChartType = SeriesChartType.Spline;
        chargingSeries.XValueType = ChartValueType.DateTime;
        chargingSeries.BorderWidth = 3;
        chargingSeries.Color = ChargingColor;
        chargingSeries.ShadowOffset = 2;

        dischargingSeries = new Series("Discharging");
        dischargingSeries.ChartType = SeriesChartType.Spline;
        dischargingSeries.XValueType = ChartValueType.DateTime;
        dischargingSeries.BorderWidth = 3;
        dischargingSeries.Color = DischargingColor;
        dischargingSeries.ShadowOffset = 2;

        predictionSeries = new Series("Prediction");
        predictionSeries.ChartType = SeriesChartType.Spline;
        predictionSeries.XValueType = ChartValueType.DateTime;
        predictionSeries.BorderWidth = 2;
        predictionSeries.BorderDashStyle = ChartDashStyle.Dash;
        predictionSeries.Color = PredictionColor;
        predictionSeries.ShadowOffset = 0;

        chart.Series.Add(chargingSeries);
        chart.Series.Add(dischargingSeries);
        chart.Series.Add(predictionSeries);

        Controls.Add(chart);

        // Ensure chart is behind toolbar
        chart.BringToFront();
        toolbarPanel.BringToFront();
    }

    private void UpdateToolbarChecks()
    {
        foreach (Control c in toolbarPanel.Controls)
        {
            Button btn = c as Button;
            if (btn != null && btn.Tag is int)
            {
                int w = (int)btn.Tag;
                btn.BackColor = (w == windowMinutes) ? Color.FromArgb(50, 130, 200) : Color.FromArgb(45, 45, 45);
            }
        }
    }

    private void UpdateChartData()
    {
        List<BatterySample> samples = app.GetSamplesWindow(windowMinutes);

        // Compute prediction horizon proportional to window
        int predictionHorizon = Math.Max(30, windowMinutes / 4);
        List<BatterySample> prediction = app.GetPredictionSamplesWindow(predictionHorizon);

        chargingSeries.Points.Clear();
        dischargingSeries.Points.Clear();
        predictionSeries.Points.Clear();

        // Set appropriate X axis interval based on window
        ChartArea area = chart.ChartAreas["Main"];
        if (windowMinutes <= 60)
            area.AxisX.Interval = 10;
        else if (windowMinutes <= 360)
            area.AxisX.Interval = 60;
        else
            area.AxisX.Interval = 180;

        for (int i = 0; i < samples.Count; i++)
        {
            BatterySample s = samples[i];
            DateTime t = s.TimeUtc.ToLocalTime();

            if (s.IsCharging)
            {
                chargingSeries.Points.AddXY(t, s.Percent);
                dischargingSeries.Points.AddXY(t, double.NaN);
            }
            else
            {
                chargingSeries.Points.AddXY(t, double.NaN);
                dischargingSeries.Points.AddXY(t, s.Percent);
            }
        }

        for (int i = 0; i < prediction.Count; i++)
        {
            BatterySample s = prediction[i];
            predictionSeries.Points.AddXY(s.TimeUtc.ToLocalTime(), s.Percent);
        }

        if (samples.Count == 0)
        {
            chargingSeries.Points.AddXY(DateTime.Now, 0);
            chargingSeries.Points[0].IsEmpty = true;
        }

        // Update stats
        UpdateStatsLabel(samples);

        // Add min/max annotations
        UpdateAnnotations(samples);

        chart.Invalidate();
    }

    private void UpdateStatsLabel(List<BatterySample> samples)
    {
        if (samples.Count == 0)
        {
            statsLabel.Text = "No data";
            return;
        }

        int min = samples.Min(s => s.Percent);
        int max = samples.Max(s => s.Percent);
        double avg = samples.Average(s => s.Percent);

        int chargingCount = samples.Count(s => s.IsCharging);
        int dischargingCount = samples.Count - chargingCount;

        string rangeLabel = windowMinutes <= 60 ? "1h" : (windowMinutes <= 360 ? "6h" : "24h");

        statsLabel.Text = string.Format(
            "Range: {0}  |  Min: {1}%  |  Max: {2}%  |  Avg: {3:F0}%  |  Samples: {4}  |  ⚡{5} 🔋{6}",
            rangeLabel, min, max, avg, samples.Count, chargingCount, dischargingCount);
    }

    private void UpdateAnnotations(List<BatterySample> samples)
    {
        chart.Annotations.Clear();

        if (samples.Count < 2)
            return;

        // Find min and max samples
        BatterySample minSample = null;
        BatterySample maxSample = null;
        int minVal = 101;
        int maxVal = -1;

        for (int i = 0; i < samples.Count; i++)
        {
            if (samples[i].Percent < minVal)
            {
                minVal = samples[i].Percent;
                minSample = samples[i];
            }
            if (samples[i].Percent > maxVal)
            {
                maxVal = samples[i].Percent;
                maxSample = samples[i];
            }
        }

        // Only annotate if there's meaningful variation
        if (maxVal - minVal < 3)
            return;

        if (minSample != null)
        {
            try
            {
                TextAnnotation minAnnotation = new TextAnnotation();
                minAnnotation.Text = "▼ " + minVal + "%";
                minAnnotation.ForeColor = DischargingColor;
                minAnnotation.Font = new Font("Segoe UI", 8, FontStyle.Bold);
                minAnnotation.AnchorDataPoint = FindDataPoint(minSample);
                minAnnotation.AnchorOffsetY = 3;
                if (minAnnotation.AnchorDataPoint != null)
                    chart.Annotations.Add(minAnnotation);
            }
            catch { }
        }

        if (maxSample != null)
        {
            try
            {
                TextAnnotation maxAnnotation = new TextAnnotation();
                maxAnnotation.Text = "▲ " + maxVal + "%";
                maxAnnotation.ForeColor = ChargingColor;
                maxAnnotation.Font = new Font("Segoe UI", 8, FontStyle.Bold);
                maxAnnotation.AnchorDataPoint = FindDataPoint(maxSample);
                maxAnnotation.AnchorOffsetY = -3;
                if (maxAnnotation.AnchorDataPoint != null)
                    chart.Annotations.Add(maxAnnotation);
            }
            catch { }
        }
    }

    private DataPoint FindDataPoint(BatterySample sample)
    {
        DateTime localTime = sample.TimeUtc.ToLocalTime();
        double xVal = localTime.ToOADate();

        Series targetSeries = sample.IsCharging ? chargingSeries : dischargingSeries;

        DataPoint best = null;
        double bestDist = double.MaxValue;

        foreach (DataPoint dp in targetSeries.Points)
        {
            if (double.IsNaN(dp.YValues[0]))
                continue;

            double dist = Math.Abs(dp.XValue - xVal);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = dp;
            }
        }

        return best;
    }
}
