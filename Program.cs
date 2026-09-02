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
            // Логирование ошибок в %AppData%\FcrCycleTimer\startup-error.txt
            void Log(Exception ex)
            {
                try
                {
                    var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    var dir = Path.Combine(appdata, "FcrCycleTimer");
                    Directory.CreateDirectory(dir);
                    var path = Path.Combine(dir, "startup-error.txt");
                    File.WriteAllText(path, DateTime.UtcNow.ToString("s") + " UTC\n" + ex.ToString());
                }
                catch { }
            }

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                Log(e.Exception);
                MessageBox.Show("Произошла ошибка при запуске. Смотрите файл логов в %AppData%\\FcrCycleTimer\\startup-error.txt", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                if (e.ExceptionObject is Exception ex) Log(ex);
                else
                {
                    try
                    {
                        var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                        var dir = Path.Combine(appdata, "FcrCycleTimer");
                        Directory.CreateDirectory(dir);
                        File.WriteAllText(Path.Combine(dir, "startup-error.txt"), DateTime.UtcNow.ToString("s") + " UTC\nUnknown unhandled exception object");
                    }
                    catch { }
                }
                MessageBox.Show("Произошла критическая ошибка при запуске. Смотрите файл логов в %AppData%\\FcrCycleTimer\\startup-error.txt", "Критическая ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            try
            {
                ApplicationConfiguration.Initialize();
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                Log(ex);
                MessageBox.Show("Ошибка при запуске приложения. Лог: %AppData%\\FcrCycleTimer\\startup-error.txt", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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
                RowCount = 6,
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
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(mainPanel);

            // Preset cycles combo
            mainPanel.Controls.Add(new Label() { Text = "Выбор цикла:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            cmbPresetCycles = new ComboBox() { DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left, Width = 140 };
            cmbPresetCycles.Items.AddRange(new object[] { "3 s", "5 s", "10 s", "11 s", "Custom..."

