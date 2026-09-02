using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Media;
using System.Text.Json;
using System.Windows.Forms;

namespace FcrCycleTimer
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
    }

    class Settings
    {
        public double CycleSeconds { get; set; } = 10.0;
        public double WarningSeconds { get; set; } = 3.0;
        public double Opacity { get; set; } = 1.0;
        public int ScalePercent { get; set; } = 100;
        public string CustomWarningWav { get; set; } = null;
        public string CustomEndWav { get; set; } = null;
    }

    public class MainForm : Form
    {
        Label lblCountdown;
        Label lblInfo;
        NumericUpDown nudCycle;
        NumericUpDown nudWarning;
        Button btnStart;
        Button btnStop;
        CheckBox chkTopMost;
        TrackBar trkOpacity;
        NumericUpDown nudScale;
        Button btnChooseWarning;
        Button btnChooseEnd;
        Button btnResetSounds;

        System.Windows.Forms.Timer uiTimer;
        Stopwatch sw;
        double cycleSeconds;
        double warningSeconds;
        TimeSpan nextCycleEnd;
        bool running = false;
        bool warned = false;

        Settings settings;
        string settingsPath;

        byte[] warningWav;
        byte[] endWav;

        Font baseCountdownFont;

        public MainForm()
        {
            Text = "FCR Cycle Timer";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            Size = new Size(380, 260);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;

            // paths
            var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appdata, "FcrCycleTimer");
            Directory.CreateDirectory(dir);
            settingsPath = Path.Combine(dir, "settings.json");

            LoadSettings();

            // Countdown
            lblCountdown = new Label()
            {
                Font = new Font("Consolas", 36, FontStyle.Bold),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Height = 100
            };
            baseCountdownFont = lblCountdown.Font;
            Controls.Add(lblCountdown);

            var panel = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                RowCount = 5,
                ColumnCount = 4,
                Padding = new Padding(8),
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            Controls.Add(panel);

            panel.Controls.Add(new Label() { Text = "Cycle (s):", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            nudCycle = new NumericUpDown()
            {
                Minimum = 0.01M,
                Maximum = 36000M,
                DecimalPlaces = 2,
                Increment = 0.1M,
                Value = (decimal)settings.CycleSeconds,
                Anchor = AnchorStyles.Left,
                Width = 120
            };
            nudCycle.ValueChanged += SettingsChanged;
            panel.Controls.Add(nudCycle, 1, 0);

            panel.Controls.Add(new Label() { Text = "Warning before end (s):", AutoSize = true, Anchor = AnchorStyles.Left }, 2, 0);
            nudWarning = new NumericUpDown()
            {
                Minimum = 0M,
                Maximum = 36000M,
                DecimalPlaces = 2,
                Increment = 0.1M,
                Value = (decimal)settings.WarningSeconds,
                Anchor = AnchorStyles.Left,
                Width = 120
            };
            nudWarning.ValueChanged += SettingsChanged;
            panel.Controls.Add(nudWarning, 3, 0);

            btnStart = new Button() { Text = "Старт", AutoSize = true, Anchor = AnchorStyles.Left };
            btnStart.Click += (s, e) => StartTimer();
            panel.Controls.Add(btnStart, 0, 1);

            btnStop = new Button() { Text = "Стоп", AutoSize = true, Anchor = AnchorStyles.Left, Enabled = false };
            btnStop.Click += (s, e) => StopTimer();
            panel.Controls.Add(btnStop, 1, 1);

            chkTopMost = new CheckBox() { Text = "Поверх окон", Checked = true, AutoSize = true, Anchor = AnchorStyles.Left };
            chkTopMost.CheckedChanged += (s, e) => { this.TopMost = chkTopMost.Checked; };
            panel.Controls.Add(chkTopMost, 2, 1);

            // Opacity
            panel.Controls.Add(new Label() { Text = "Прозрачность:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
            trkOpacity = new TrackBar() { Minimum = 20, Maximum = 100, TickFrequency = 10, Value = (int)(settings.Opacity * 100), Anchor = AnchorStyles.Left, Width = 140 };
            trkOpacity.Scroll += (s, e) => { this.Opacity = trkOpacity.Value / 100.0; settings.Opacity = this.Opacity; SaveSettings(); };
            panel.Controls.Add(trkOpacity, 1, 2);

            // Scale
            panel.Controls.Add(new Label() { Text = "Масштаб (%):", AutoSize = true, Anchor = AnchorStyles.Left }, 2, 2);
            nudScale = new NumericUpDown() { Minimum = 50, Maximum = 200, Increment = 10, Value = settings.ScalePercent, Width = 80 };
            nudScale.ValueChanged += (s, e) => { ApplyScale((int)nudScale.Value); settings.ScalePercent = (int)nudScale.Value; SaveSettings(); };
            panel.Controls.Add(nudScale, 3, 2);

            // Sound config
            panel.Controls.Add(new Label() { Text = "Звуки:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
            var soundPanel = new FlowLayoutPanel() { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            btnChooseWarning = new Button() { Text = "Выбрать предупреждение...", AutoSize = true };
            btnChooseWarning.Click += (s, e) => ChooseCustomWav(true);
            btnChooseEnd = new Button() { Text = "Выбрать конец цикла...", AutoSize = true };
            btnChooseEnd.Click += (s, e) => ChooseCustomWav(false);
            btnResetSounds = new Button() { Text = "Сброс звуков", AutoSize = true };
            btnResetSounds.Click += (s,e) => { settings.CustomWarningWav = null; settings.CustomEndWav = null; LoadBuiltInSounds(); SaveSettings(); MessageBox.Show("Сброшено на встроенные тона."); };
            soundPanel.Controls.Add(btnChooseWarning);
            soundPanel.Controls.Add(btnChooseEnd);
            soundPanel.Controls.Add(btnResetSounds);
            panel.Controls.Add(soundPanel, 1, 3);
            panel.SetColumnSpan(soundPanel, 3);

            lblInfo = new Label() { Text = "", AutoSize = true, Anchor = AnchorStyles.Left, Dock = DockStyle.Fill };
            panel.Controls.Add(lblInfo, 0, 4);
            panel.SetColumnSpan(lblInfo, 4);

            uiTimer = new System.Windows.Forms.Timer() { Interval = 40 }; // ~25Hz
            uiTimer.Tick += UiTimer_Tick;
            sw = new Stopwatch();

            // load sounds (custom or built-in)
            LoadSounds();

            // apply settings
            cycleSeconds = settings.CycleSeconds;
            warningSeconds = settings.WarningSeconds;
            this.Opacity = settings.Opacity;
            trkOpacity.Value = (int)(settings.Opacity * 100);
            ApplyScale(settings.ScalePercent);
            nudScale.Value = settings.ScalePercent;

            UpdateInfo();
            UpdateCountdownDisplay(0);
            chkTopMost.Checked = true;
        }

        void LoadSettings()
        {
            try
            {
                if (File.Exists(settingsPath))
                {
                    var json = File.ReadAllText(settingsPath);
                    settings = JsonSerializer.Deserialize<Settings>(json);
                }
            }
            catch { }
            if (settings == null) settings = new Settings();
        }

        void SaveSettings()
        {
            try
            {
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(settingsPath, json);
            }
            catch { }
        }

        void LoadSounds()
        {
            if (!string.IsNullOrEmpty(settings.CustomWarningWav) && File.Exists(settings.CustomWarningWav))
            {
                try { warningWav = File.ReadAllBytes(settings.CustomWarningWav); }
                catch { warningWav = null; }
            }
            if (!string.IsNullOrEmpty(settings.CustomEndWav) && File.Exists(settings.CustomEndWav))
            {
                try { endWav = File.ReadAllBytes(settings.CustomEndWav); }
                catch { endWav = null; }
            }
            if (warningWav == null || endWav == null)
            {
                LoadBuiltInSounds();
            }
        }

        void LoadBuiltInSounds()
        {
            warningWav = GenerateSineWav(880.0, 0.12, 0.6);
            endWav = GenerateSineWav(1760.0, 0.28, 0.9);
        }

        void ChooseCustomWav(bool forWarning)
        {
            using var ofd = new OpenFileDialog() { Filter = "WAV files|*.wav|All files|*.*" };
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    var bytes = File.ReadAllBytes(ofd.FileName); // validate
                    if (forWarning) { settings.CustomWarningWav = ofd.FileName; warningWav = bytes; }
                    else { settings.CustomEndWav = ofd.FileName; endWav = bytes; }
                    SaveSettings();
                }
                catch { MessageBox.Show("Не удалось загрузить файл."); }
            }
        }

        void SettingsChanged(object sender, EventArgs e)
        {
            cycleSeconds = (double)nudCycle.Value;
            warningSeconds = (double)nudWarning.Value;
            settings.CycleSeconds = cycleSeconds;
            settings.WarningSeconds = warningSeconds;
            SaveSettings();
            UpdateInfo();

            if (running)
            {
                // start a new cycle immediately with updated parameters
                nextCycleEnd = sw.Elapsed + TimeSpan.FromSeconds(cycleSeconds);
                warned = false;
            }
        }

        void StartTimer()
        {
            cycleSeconds = Math.Max(0.0001, (double)nudCycle.Value);
            warningSeconds = Math.Max(0.0, (double)nudWarning.Value);
            settings.CycleSeconds = cycleSeconds;
            settings.WarningSeconds = warningSeconds;
            SaveSettings();

            sw.Restart();
            nextCycleEnd = sw.Elapsed + TimeSpan.FromSeconds(cycleSeconds);
            warned = false;
            running = true;
            uiTimer.Start();
            btnStart.Enabled = false;
            btnStop.Enabled = true;
        }

        void StopTimer()
        {
            uiTimer.Stop();
            sw.Stop();
            running = false;
            btnStart.Enabled = true;
            btnStop.Enabled = false;
            UpdateCountdownDisplay(0);
        }

        void UiTimer_Tick(object sender, EventArgs e)
        {
            if (!running) return;

            var remaining = nextCycleEnd - sw.Elapsed;
            double remSec = remaining.TotalSeconds;
            if (remSec <= 0)
            {
                PlayWav(endWav);
                do
                {
                    nextCycleEnd += TimeSpan.FromSeconds(cycleSeconds);
                    remaining = nextCycleEnd - sw.Elapsed;
                    remSec = remaining.TotalSeconds;
                } while (remSec <= 0);
                warned = false;
            }
            else
            {
                if (!warned && warningSeconds > 0 && remSec <= warningSeconds)
                {
                    PlayWav(warningWav);
                    warned = true;
                }
            }

            UpdateCountdownDisplay(Math.Max(0.0, remSec));
        }

        void UpdateInfo()
        {
            lblInfo.Text = $"Длительность цикла: {cycleSeconds:0.##} с    Предупреждение: {warningSeconds:0.##} с";
        }

        void UpdateCountdownDisplay(double seconds)
        {
            string text;
            if (seconds >= 10)
                text = $"{seconds:0.0}s";
            else if (seconds >= 1)
                text = $"{seconds:0.00}s";
            else
                text = $"{seconds:0.000}s";

            lblCountdown.Text = text;
        }

        void PlayWav(byte[] wavBytes)
        {
            try
            {
                using var ms = new MemoryStream(wavBytes);
                using var sp = new SoundPlayer(ms);
                sp.Play();
            }
            catch { }
        }

        // simple PCM WAV generator: 16-bit mono 44100 Hz
        byte[] GenerateSineWav(double freq, double durationSec, double volume = 0.8)
        {
            int sampleRate = 44100;
            int samples = (int)(sampleRate * durationSec);
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            // RIFF header
            bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(36 + samples * 2); // file size - 8
            bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            // fmt chunk
            bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            bw.Write(16);
            bw.Write((short)1); // PCM
            bw.Write((short)1); // channels
            bw.Write(sampleRate);
            bw.Write(sampleRate * 2); // byte rate = sampleRate * channels * bytesPerSample
            bw.Write((short)2); // block align
            bw.Write((short)16); // bits per sample
            // data chunk
            bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            bw.Write(samples * 2);
            double amp = 32760 * volume;
            for (int n = 0; n < samples; n++)
            {
                double t = (double)n / sampleRate;
                double val = Math.Sin(2 * Math.PI * freq * t);
                short s = (short)(amp * val);
                bw.Write(s);
            }
            return ms.ToArray();
        }

        void ApplyScale(int percent)
        {
            float scale = percent / 100f;
            lblCountdown.Font = new Font(baseCountdownFont.FontFamily, baseCountdownFont.Size * scale, baseCountdownFont.Style);
            // adjust other fonts if needed
            foreach (Control c in Controls)
            {
                if (c is TableLayoutPanel tl)
                {
                    foreach (Control cc in tl.Controls)
                    {
                        if (cc != lblCountdown)
                        {
                            cc.Font = new Font(cc.Font.FontFamily, 8F * scale);
                        }
                    }
                }
            }
        }
    }
}
