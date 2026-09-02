using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
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
        public bool TopMost { get; set; } = true;
        public string CustomWarningWav { get; set; } = null;
        public string CustomEndWav { get; set; } = null;
    }

    public class MainForm : Form
    {
        // WinAPI for global hotkey
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        const int HOTKEY_ID_1 = 0x1001;

        Label lblCountdown;
        Label lblInfo;
        NumericUpDown nudCycle;
        NumericUpDown nudWarning;
        TrackBar trkQuickCycle;
        Button btnToggle; // toggle start/stop
        CheckBox chkTopMost;
        TrackBar trkOpacity;
        Button btnChooseWarning;
        Button btnChooseEnd;

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
            Size = new Size(420, 280);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;

            // paths & settings
            var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appdata, "FcrCycleTimer");
            Directory.CreateDirectory(dir);
            settingsPath = Path.Combine(dir, "settings.json");
            LoadSettings();

            // UI - big countdown
            lblCountdown = new Label()
            {
                Font = new Font("Consolas", 40, FontStyle.Bold),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Height = 110
            };
            baseCountdownFont = lblCountdown.Font;
            Controls.Add(lblCountdown);

            var panel = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                RowCount = 5,
                ColumnCount = 4,
                Padding = new Padding(8)
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            Controls.Add(panel);

            // cycle controls: numeric + quick trackbar
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
            nudCycle.ValueChanged += NudCycle_ValueChanged;
            panel.Controls.Add(nudCycle, 1, 0);

            panel.Controls.Add(new Label() { Text = "Quick (1-60s):", AutoSize = true, Anchor = AnchorStyles.Left }, 2, 0);
            trkQuickCycle = new TrackBar() { Minimum = 1, Maximum = 60, Value = (int)Math.Max(1, Math.Round(settings.CycleSeconds)), TickFrequency = 5, Width = 140, Anchor = AnchorStyles.Left };
            trkQuickCycle.Scroll += TrkQuickCycle_Scroll;
            panel.Controls.Add(trkQuickCycle, 3, 0);

            // warning
            panel.Controls.Add(new Label() { Text = "Warning before end (s):", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
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
            nudWarning.ValueChanged += (s, e) => { warningSeconds = (double)nudWarning.Value; settings.WarningSeconds = warningSeconds; SaveSettings(); };
            panel.Controls.Add(nudWarning, 1, 1);

            // Toggle button
            btnToggle = new Button() { Text = "Старт", AutoSize = true, Anchor = AnchorStyles.Left };
            btnToggle.Click += (s, e) => ToggleTimer();
            panel.Controls.Add(btnToggle, 0, 2);

            // TopMost checkbox
            chkTopMost = new CheckBox() { Text = "Поверх окон", Checked = settings.TopMost, AutoSize = true, Anchor = AnchorStyles.Left };
            chkTopMost.CheckedChanged += (s, e) => { this.TopMost = chkTopMost.Checked; settings.TopMost = chkTopMost.Checked; SaveSettings(); };
            panel.Controls.Add(chkTopMost, 1, 2);

            // Opacity
            panel.Controls.Add(new Label() { Text = "Прозрачность:", AutoSize = true, Anchor = AnchorStyles.Left }, 2, 2);
            trkOpacity = new TrackBar() { Minimum = 20, Maximum = 100, TickFrequency = 10, Value = (int)(settings.Opacity * 100), Anchor = AnchorStyles.Left, Width = 140 };
            trkOpacity.Scroll += (s, e) => { this.Opacity = trkOpacity.Value / 100.0; settings.Opacity = this.Opacity; SaveSettings(); };
            panel.Controls.Add(trkOpacity, 3, 2);

            // Sounds select (optional)
            panel.Controls.Add(new Label() { Text = "Звуки (опц.):", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
            var soundPanel = new FlowLayoutPanel() { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            btnChooseWarning = new Button() { Text = "Выбрать предупреждение...", AutoSize = true };
            btnChooseWarning.Click += (s, e) => ChooseCustomWav(true);
            btnChooseEnd = new Button() { Text = "Выбрать конец цикла...", AutoSize = true };
            btnChooseEnd.Click += (s, e) => ChooseCustomWav(false);
            soundPanel.Controls.Add(btnChooseWarning);
            soundPanel.Controls.Add(btnChooseEnd);
            panel.Controls.Add(soundPanel, 1, 3);
            panel.SetColumnSpan(soundPanel, 3);

            lblInfo = new Label() { Text = "", AutoSize = true, Anchor = AnchorStyles.Left, Dock = DockStyle.Fill };
            panel.Controls.Add(lblInfo, 0, 4);
            panel.SetColumnSpan(lblInfo, 4);

            // Timer & Stopwatch
            uiTimer = new System.Windows.Forms.Timer() { Interval = 40 }; // ~25Hz
            uiTimer.Tick += UiTimer_Tick;
            sw = new Stopwatch();

            // load sounds
            LoadSounds();

            // apply settings
            cycleSeconds = settings.CycleSeconds;
            warningSeconds = settings.WarningSeconds;
            this.Opacity = settings.Opacity;
            trkOpacity.Value = (int)(settings.Opacity * 100);
            this.TopMost = settings.TopMost;

            UpdateInfo();
            UpdateCountdownDisplay(0);

            // register global hotkey for '+' (both main and numpad)
            RegisterPlusHotkey();

            // ensure we clean up on close
            FormClosing += (s, e) => { UnregisterHotKey(this.Handle, HOTKEY_ID_1); };
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_HOTKEY = 0x0312;
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == HOTKEY_ID_1)
                {
                    ToggleTimer();
                }
            }
            base.WndProc(ref m);
        }

        void RegisterPlusHotkey()
        {
            // VK_OEM_PLUS = 0xBB, VK_ADD = 0x6B
            // register both via same id: we can register one id per registration, so register two ids if wanted.
            // Simpler: register VK_OEM_PLUS (main keyboard) without modifiers; also register VK_ADD with another id.
            // We'll register two ids.
            const int HOTKEY_ID_2 = HOTKEY_ID_1 + 1;
            RegisterHotKey(this.Handle, HOTKEY_ID_1, 0, 0xBB); // OEM_PLUS
            RegisterHotKey(this.Handle, HOTKEY_ID_2, 0, 0x6B); // NUMPAD_ADD
            // Note: ignore return value — if fails (permission), hotkey will not work; no crash.
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
            warningWav = GenerateSineWav(880.0, 0.10, 0.7);
            endWav = GenerateSineWav(1320.0, 0.26, 0.95);
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

        void NudCycle_ValueChanged(object sender, EventArgs e)
        {
            cycleSeconds = (double)nudCycle.Value;
            settings.CycleSeconds = cycleSeconds;
            SaveSettings();
            // update quick trackbar to nearest int within range
            int intVal = (int)Math.Max(trkQuickCycle.Minimum, Math.Min(trkQuickCycle.Maximum, Math.Round(cycleSeconds)));
            trkQuickCycle.Value = intVal;
            UpdateInfo();
            if (running)
            {
                // restart cycle with new params
                nextCycleEnd = sw.Elapsed + TimeSpan.FromSeconds(cycleSeconds);
                warned = false;
            }
        }

        void TrkQuickCycle_Scroll(object sender, EventArgs e)
        {
            int v = trkQuickCycle.Value;
            nudCycle.Value = (decimal)v;
            // NudCycle_ValueChanged will handle the rest
        }

        void ToggleTimer()
        {
            if (running) StopTimer();
            else StartTimer();
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
            btnToggle.Text = "Стоп";
        }

        void StopTimer()
        {
            uiTimer.Stop();
            sw.Stop();
            running = false;
            btnToggle.Text = "Старт";
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
                // advance cycles in case of delays
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
            lblInfo.Text = $"Цикл: {cycleSeconds:0.##} с    Предупреждение: {warningSeconds:0.##} с    Горячая: '+' (toggle)";
        }

        void UpdateCountdownDisplay(double seconds)
        {
            string text;
            if (seconds >= 10) text = $"{seconds:0.0}s";
            else if (seconds >= 1) text = $"{seconds:0.00}s";
            else text = $"{seconds:0.000}s";
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
            catch { /* ignore playback errors */ }
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
    }
}
