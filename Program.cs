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
        const int HOTKEY_ID_2 = HOTKEY_ID_1 + 1;

        Label lblCountdown;
        Label lblInfo;
        ComboBox cmbPresetCycles;
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
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(320, 220);
            Size = new Size(520, 320);
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
                Font = new Font("Consolas", 48, FontStyle.Bold),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Height = 140
            };
            baseCountdownFont = lblCountdown.Font;
            Controls.Add(lblCountdown);

            // Main container under countdown
            var mainPanel = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                RowCount = 5,
                ColumnCount = 4,
                Padding = new Padding(8),
                AutoSize = false
            };
            mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(mainPanel);

            // Preset cycles combo
            mainPanel.Controls.Add(new Label() { Text = "Выбор цикла:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            cmbPresetCycles = new ComboBox() { DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left, Width = 140 };
            cmbPresetCycles.Items.AddRange(new object[] { "3 s", "5 s", "10 s", "11 s", "Custom..." });
            cmbPresetCycles.SelectedIndexChanged += CmbPresetCycles_SelectedIndexChanged;
            // select nearest preset or Custom
            var presetIndex = GetPresetIndex(settings.CycleSeconds);
            cmbPresetCycles.SelectedIndex = presetIndex;
            mainPanel.Controls.Add(cmbPresetCycles, 1, 0);

            // precise numeric for cycle
            mainPanel.Controls.Add(new Label() { Text = "Cycle (s):", AutoSize = true, Anchor = AnchorStyles.Left }, 2, 0);
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
            mainPanel.Controls.Add(nudCycle, 3, 0);

            // Quick trackbar 1..60
            mainPanel.Controls.Add(new Label() { Text = "Quick (1-60s):", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
            trkQuickCycle = new TrackBar() { Minimum = 1, Maximum = 60, Value = (int)Math.Max(1, Math.Round(settings.CycleSeconds)), TickFrequency = 5, Width = 260, Anchor = AnchorStyles.Left };
            trkQuickCycle.Scroll += TrkQuickCycle_Scroll;
            mainPanel.Controls.Add(trkQuickCycle, 1, 1);
            mainPanel.SetColumnSpan(trkQuickCycle, 3);

            // warning numeric
            mainPanel.Controls.Add(new Label() { Text = "Warning before end (s):", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
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
            nudWarning.ValueChanged += (s, e) => { warningSeconds = (double)nudWarning.Value; settings.WarningSeconds = warningSeconds; SaveSettings(); UpdateInfo(); };
            mainPanel.Controls.Add(nudWarning, 1, 2);

            // Toggle button & TopMost & Opacity
            btnToggle = new Button() { Text = "Старт", AutoSize = true, Anchor = AnchorStyles.Left, Width = 100 };
            btnToggle.Click += (s, e) => ToggleTimer();
            mainPanel.Controls.Add(btnToggle, 0, 3);

            chkTopMost = new CheckBox() { Text = "Поверх окон", Checked = settings.TopMost, AutoSize = true, Anchor = AnchorStyles.Left };
            chkTopMost.CheckedChanged += (s, e) => { this.TopMost = chkTopMost.Checked; settings.TopMost = chkTopMost.Checked; SaveSettings(); };
            mainPanel.Controls.Add(chkTopMost, 1, 3);

            mainPanel.Controls.Add(new Label() { Text = "Прозрачность:", AutoSize = true, Anchor = AnchorStyles.Left }, 2, 3);
            trkOpacity = new TrackBar() { Minimum = 20, Maximum = 100, TickFrequency = 5, Value = (int)(settings.Opacity * 100), Anchor = AnchorStyles.Left, Width = 260 };
            trkOpacity.Scroll += (s, e) => { this.Opacity = trkOpacity.Value / 100.0; settings.Opacity = this.Opacity; SaveSettings(); };
            mainPanel.Controls.Add(trkOpacity, 3, 3);

            // Sounds select (optional)
            mainPanel.Controls.Add(new Label() { Text = "Звуки (опц.):", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 4);
            var soundPanel = new FlowLayoutPanel() { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            btnChooseWarning = new Button() { Text = "Выбрать предупреждение...", AutoSize = true };
            btnChooseWarning.Click += (s, e) => ChooseCustomWav(true);
            btnChooseEnd = new Button() { Text = "Выбрать конец цикла...", AutoSize = true };
            btnChooseEnd.Click += (s, e) => ChooseCustomWav(false);
            soundPanel.Controls.Add(btnChooseWarning);
            soundPanel.Controls.Add(btnChooseEnd);
            mainPanel.Controls.Add(soundPanel, 1, 4);
            mainPanel.SetColumnSpan(soundPanel, 3);

            lblInfo = new Label() { Text = "", AutoSize = true, Anchor = AnchorStyles.Left, Dock = DockStyle.Fill };
            mainPanel.Controls.Add(lblInfo, 0, 5);
            mainPanel.SetColumnSpan(lblInfo, 4);

            // Timer & Stopwatch
            uiTimer = new System.Windows.Forms.Timer() { Interval = 40 }; // ~25Hz
            uiTimer.Tick += UiTimer_Tick;
            sw = new Stopwatch();

            // sounds
            LoadSounds();

            // apply settings
            cycleSeconds = settings.CycleSeconds;
            warningSeconds = settings.WarningSeconds;
            this.Opacity = settings.Opacity;
            trkOpacity.Value = (int)(settings.Opacity * 100);
            this.TopMost = settings.TopMost;

            UpdateInfo();
            UpdateCountdownDisplay(0);

            // register global hotkey '+' (main and numpad)
            RegisterPlusHotkey();

            // cleanup
            FormClosing += (s, e) => { UnregisterHotKey(this.Handle, HOTKEY_ID_1); UnregisterHotKey(this.Handle, HOTKEY_ID_2); };

            // resize handling: dynamic font scaling
            this.Resize += MainForm_Resize;
            AdjustCountdownFont();
        }

        int GetPresetIndex(double sec)
        {
            if (Math.Abs(sec - 3.0) < 0.001) return 0;
            if (Math.Abs(sec - 5.0) < 0.001) return 1;
            if (Math.Abs(sec - 10.0) < 0.001) return 2;
            if (Math.Abs(sec - 11.0) < 0.001) return 3;
            return 4; // Custom
        }

        void CmbPresetCycles_SelectedIndexChanged(object sender, EventArgs e)
        {
            switch (cmbPresetCycles.SelectedIndex)
            {
                case 0: nudCycle.Value = 3.00M; break;
                case 1: nudCycle.Value = 5.00M; break;
                case 2: nudCycle.Value = 10.00M; break;
                case 3: nudCycle.Value = 11.00M; break;
                default: /* Custom - do nothing */ break;
            }
            // NudCycle_ValueChanged handles the rest
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_HOTKEY = 0x0312;
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == HOTKEY_ID_1 || id == HOTKEY_ID_2)
                {
                    ToggleTimer();
                }
            }
            base.WndProc(ref m);
        }

        void RegisterPlusHotkey()
        {
            // VK_OEM_PLUS = 0xBB, VK_ADD = 0x6B
            RegisterHotKey(this.Handle, HOTKEY_ID_1, 0, 0xBB); // OEM_PLUS
            RegisterHotKey(this.Handle, HOTKEY_ID_2, 0, 0x6B); // NUMPAD_ADD
        }

        void MainForm_Resize(object sender, EventArgs e)
        {
            AdjustCountdownFont();
        }

        void AdjustCountdownFont()
        {
            // scale font by client height
            float newSize = Math.Max(12f, this.ClientSize.Height * 0.28f);
            try
            {
                lblCountdown.Font = new Font(baseCountdownFont.FontFamily, newSize, FontStyle.Bold);
            }
            catch { /* ignore font errors */ }
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
            if (warningWav == null || endWav == null) LoadBuiltInSounds();
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
            // update preset selection
            cmbPresetCycles.SelectedIndex = GetPresetIndex(cycleSeconds);
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
