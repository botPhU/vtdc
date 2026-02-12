using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AccountLauncher
{
    public class Form1 : Form
    {
        // Win32 dark title bar
        [DllImport("dwmapi.dll", PreserveSig = true)]
        internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        // Win32 for sending keys to game windows
        [DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")]
        static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);
        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hWnd);
        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        const uint WM_KEYDOWN = 0x0100;
        const uint WM_KEYUP = 0x0101;

        // State
        private List<AccountInfo> accounts = new List<AccountInfo>();
        private AppSettings settings = new AppSettings();
        private string configPath;
        private string gamePath;
        private string gameExeName = "Vũ Trụ Đại Chiến.exe";
        private Dictionary<int, Process> runningProcesses = new Dictionary<int, Process>();
        private System.Windows.Forms.Timer processMonitorTimer;

        // UI
        private Panel sidebarPanel, contentPanel, headerPanel;
        private Panel accountsPage, settingsPage, inspectorPage, dashboardPage, guidePage;
        private DataGridView accountGrid;
        private Label statusLabel;
        private ComboBox cboWindowSize;
        private CheckBox chkShadows, chkParticles, chkLowQuality, chkMinimize, chkAutoCleanRAM, chkUltraLowRes;
        private CheckBox chkAutoQuest, chkSmartDialog, chkCollectQuest, chkAutoPathfind;
        private NumericUpDown nudFps, nudGameSpeed;
        private Button btnNavAccounts, btnNavSettings, btnNavInspector, btnNavDashboard, btnNavGuide;
        private ListBox inspectorFileList;
        private FlowLayoutPanel dashboardFlow;
        private System.Windows.Forms.Timer dashboardTimer;
        private Label dashboardBossLabel;
        private ComboBox cmdAccountCombo;
        private TextBox cmdResponseBox;
        private TextBox cmdLogBox;   // New: dedicated log area
        private CheckBox chkAutoLog; // Added for live log
        private TextBox inspectorContent;

        // Colors
        private readonly Color BG = Color.FromArgb(24, 24, 32);
        private readonly Color SIDEBAR = Color.FromArgb(18, 18, 26);
        private readonly Color CARD = Color.FromArgb(32, 32, 44);
        private readonly Color ACCENT = Color.FromArgb(99, 102, 241);
        private readonly Color TEXT = Color.FromArgb(220, 220, 230);
        private readonly Color DIM = Color.FromArgb(140, 140, 160);
        private readonly Color GREEN = Color.FromArgb(34, 197, 94);
        private readonly Color RED = Color.FromArgb(239, 68, 68);
        private readonly Color ORANGE = Color.FromArgb(249, 115, 22);
        private readonly Color CYAN = Color.FromArgb(6, 182, 212);
        private readonly Color YELLOW = Color.FromArgb(234, 179, 8);
        private readonly Color PURPLE = Color.FromArgb(168, 85, 247);

        public Form1()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            gamePath = ResolveGamePath(baseDir);
            configPath = Path.Combine(gamePath, "tools", "LauncherV3", "accounts.json");
            // Fallback: if accounts.json not at expected path, try next to exe
            if (!File.Exists(configPath))
            {
                string altConfig = Path.Combine(baseDir, "accounts.json");
                if (File.Exists(altConfig))
                    configPath = altConfig;
            }

            Text = $"Account Launcher v42 (Debug Dialog) — {gamePath}";
            Size = new Size(850, 620);
            MinimumSize = new Size(700, 500);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BG;
            ForeColor = TEXT;
            Font = new Font("Segoe UI", 9.5f);

            EnableDarkTitleBar();
            BuildUI();
            BuildAccountsPage();
            BuildSettingsPage();
            BuildInspectorPage();
            BuildGuidePage();

            LoadAccounts();
            LoadSettings();
            SwitchPage("accounts");

            processMonitorTimer = new System.Windows.Forms.Timer { Interval = 3000 };
            processMonitorTimer.Tick += ProcessMonitor_Tick;
            processMonitorTimer.Start();
        }

        /// <summary>
        /// Tìm thư mục gốc của game với nhiều chiến lược fallback.
        /// Dùng Directory.GetFiles thay vì File.Exists để tránh lỗi Unicode.
        /// </summary>
        private string ResolveGamePath(string baseDir)
        {
            // Thêm cả assembly location vì self-contained publish có thể khác
            var baseDirs = new HashSet<string>();
            baseDirs.Add(baseDir.TrimEnd('\\'));
            try
            {
                string asmDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(asmDir))
                    baseDirs.Add(asmDir.TrimEnd('\\'));
            }
            catch { }

            var candidates = new List<string>();
            foreach (var bd in baseDirs)
            {
                candidates.Add(bd);                                           // same dir
                candidates.Add(Path.GetFullPath(Path.Combine(bd, "..")));     // 1 up
                candidates.Add(Path.GetFullPath(Path.Combine(bd, "..", ".."))); // 2 up
                candidates.Add(Path.GetFullPath(Path.Combine(bd, "..", "..", ".."))); // 3 up
                candidates.Add(Path.GetFullPath(Path.Combine(bd, "..", "..", "..", "..", ".."))); // 5 up (debug)
            }

            foreach (var candidate in candidates)
            {
                if (GameExistsAt(candidate))
                    return candidate;
            }

            // Không tìm thấy → tìm bằng cách scan tất cả thư mục cha
            string current = baseDir;
            for (int i = 0; i < 8; i++)
            {
                try
                {
                    current = Path.GetFullPath(Path.Combine(current, ".."));
                    if (GameExistsAt(current)) return current;
                }
                catch { break; }
            }

            // Cuối cùng: hiện thông báo debug
            string tested = string.Join("\n  ", candidates.Distinct().Select(c => $"{c} → {(Directory.Exists(c) ? "exists" : "NOT FOUND")}"));
            MessageBox.Show(
                $"⚠️ Không tìm thấy game!\n\n" +
                $"BaseDir: {baseDir}\n\n" +
                $"Đã tìm ở:\n  {tested}\n\n" +
                $"Tìm: {gameExeName} hoặc GameAssembly.dll",
                "Không tìm thấy Game",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );

            return Path.GetFullPath(Path.Combine(baseDir, "..", ".."));
        }

        private bool GameExistsAt(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return false;
                // Tìm GameAssembly.dll (đặc trưng Unity IL2CPP) — tên ASCII, không lỗi encoding
                var gaFiles = Directory.GetFiles(dir, "GameAssembly.dll", SearchOption.TopDirectoryOnly);
                if (gaFiles.Length > 0) return true;
                // Tìm bằng tên chính xác
                if (File.Exists(Path.Combine(dir, gameExeName))) return true;
                // Tìm bằng pattern
                var exeFiles = Directory.GetFiles(dir, "*.exe", SearchOption.TopDirectoryOnly);
                foreach (var f in exeFiles)
                {
                    string name = Path.GetFileName(f);
                    if (name.Contains("Tr") && name.Contains("Chi")) return true; // "Trụ" + "Chiến"
                }
                return false;
            }
            catch { return false; }
        }

        private void EnableDarkTitleBar()
        {
            try
            {
                int val = 1;
                DwmSetWindowAttribute(Handle, 20, ref val, sizeof(int));
            }
            catch { }
        }

        // ======================== UI BUILD ========================

        private void BuildUI()
        {
            // Sidebar
            sidebarPanel = new Panel
            {
                Dock = DockStyle.Left,
                Width = 180,
                BackColor = SIDEBAR,
                Padding = new Padding(8, 16, 8, 8)
            };
            sidebarPanel.Paint += Sidebar_Paint;

            var logo = new Label
            {
                Text = "🎮 Launcher",
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                ForeColor = ACCENT,
                Dock = DockStyle.Top,
                Height = 50,
                TextAlign = ContentAlignment.MiddleCenter
            };

            btnNavAccounts = CreateNavButton("👤 Tài khoản", 60);
            btnNavAccounts.Click += (s, e) => SwitchPage("accounts");
            btnNavDashboard = CreateNavButton("📊 Dashboard", 110);
            btnNavDashboard.Click += (s, e) => SwitchPage("dashboard");
            btnNavSettings = CreateNavButton("⚙️ Cài đặt", 160);
            btnNavSettings.Click += (s, e) => SwitchPage("settings");
            btnNavInspector = CreateNavButton("🔍 Inspector", 210);
            btnNavInspector.Click += (s, e) => SwitchPage("inspector");
            btnNavGuide = CreateNavButton("💡 Hướng dẫn", 260);
            btnNavGuide.Click += (s, e) => SwitchPage("guide");

            sidebarPanel.Controls.Add(btnNavGuide);
            sidebarPanel.Controls.Add(btnNavInspector);
            sidebarPanel.Controls.Add(btnNavSettings);
            sidebarPanel.Controls.Add(btnNavDashboard);
            sidebarPanel.Controls.Add(btnNavAccounts);
            sidebarPanel.Controls.Add(logo);

            // Content area
            contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = BG,
                Padding = new Padding(16, 0, 16, 16)
            };

            // Header
            headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 40,
                BackColor = Color.Transparent
            };
            statusLabel = new Label
            {
                Text = "Sẵn sàng",
                Dock = DockStyle.Fill,
                ForeColor = DIM,
                Font = new Font("Segoe UI", 10),
                TextAlign = ContentAlignment.MiddleLeft
            };
            headerPanel.Controls.Add(statusLabel);
            contentPanel.Controls.Add(headerPanel);

            Controls.Add(contentPanel);
            Controls.Add(sidebarPanel);
        }

        private void BuildAccountsPage()
        {
            accountsPage = new Panel { Dock = DockStyle.Fill };

            // Use TableLayoutPanel to guarantee layout (Row 1: Action Bar, Row 2: Grid)
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = BG,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70F)); // Fixed height for Action Bar
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // Grid fills rest

            // === 1. Action Bar (Top) ===
            var actionBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(28, 28, 38),
                Padding = new Padding(10, 15, 10, 10), // Padding rộng hơn
                WrapContents = false,
                AutoSize = false
            };

            Button MakeBtn(string text, Color bg, int width)
            {
                var btn = new Button
                {
                    Text = text,
                    Size = new Size(Math.Max(width, 110), 40),
                    MinimumSize = new Size(110, 40),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = bg,
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI Semibold", 9.5f),
                    Cursor = Cursors.Hand,
                    Margin = new Padding(0, 0, 10, 0)
                };
                btn.FlatAppearance.BorderSize = 0;
                btn.FlatAppearance.MouseOverBackColor = ControlPaint.Light(bg, 0.2f);
                return btn;
            }

            // Left group
            var btnAdd = MakeBtn("＋ Thêm", ACCENT, 110);
            btnAdd.Click += (s, e) => AddAccount();
            var btnEdit = MakeBtn("✏️ Sửa", ORANGE, 110);
            btnEdit.Click += (s, e) => EditAccount();
            var btnDel = MakeBtn("🗑️ Xóa", RED, 110);
            btnDel.Click += (s, e) => DeleteAccount();

            // Separator
            var sep = new Panel { Width = 2, Height = 40, BackColor = Color.FromArgb(50, 50, 65), Margin = new Padding(10, 0, 20, 0) };

            // Right group
            var btnLaunch = MakeBtn("▶ Chạy", GREEN, 120);
            btnLaunch.Click += (s, e) => LaunchSelected();
            var btnLaunchAll = MakeBtn("▶▶ Tất cả", Color.FromArgb(16, 185, 129), 130);
            btnLaunchAll.Click += (s, e) => LaunchAll();
            var btnStop = MakeBtn("⏹ Dừng Hết", RED, 130);
            btnStop.Click += (s, e) => StopAll();

            actionBar.Controls.AddRange(new Control[] { btnAdd, btnEdit, btnDel, sep, btnLaunch, btnLaunchAll, btnStop });

            // === 2. Grid (Fill remaining space) ===
            accountGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = BG,
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor = Color.FromArgb(45, 45, 60),
                RowHeadersVisible = false,
                AllowUserToAddRows = false,
                AllowUserToResizeRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                ReadOnly = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                ColumnHeadersHeight = 45,
                RowTemplate = { Height = 42 },
                Font = new Font("Segoe UI", 10f),
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = BG,
                    ForeColor = TEXT,
                    SelectionBackColor = Color.FromArgb(45, 45, 70),
                    SelectionForeColor = Color.White,
                    Padding = new Padding(10, 0, 0, 0)
                },
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = Color.FromArgb(30, 30, 42),
                    ForeColor = DIM,
                    Font = new Font("Segoe UI Semibold", 9.5f),
                    Alignment = DataGridViewContentAlignment.MiddleLeft,
                    Padding = new Padding(10, 0, 0, 0)
                },
                EnableHeadersVisualStyles = false
            };
            accountGrid.Columns.AddRange(new DataGridViewColumn[]
            {
                new DataGridViewTextBoxColumn { Name = "STT", HeaderText = "#", FillWeight = 18 },
                new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Tên", FillWeight = 55 },
                new DataGridViewTextBoxColumn { Name = "Username", HeaderText = "Tài khoản", FillWeight = 55 },
                new DataGridViewTextBoxColumn { Name = "Server", HeaderText = "Sv", FillWeight = 22 },
                new DataGridViewTextBoxColumn { Name = "Char", HeaderText = "NV", FillWeight = 22 },
                new DataGridViewTextBoxColumn { Name = "Map", HeaderText = "🗺 Map", FillWeight = 65 },
                new DataGridViewTextBoxColumn { Name = "Quest", HeaderText = "📜 Nhiệm vụ", FillWeight = 110 },
                new DataGridViewCheckBoxColumn { Name = "AutoQuest", HeaderText = "Auto", FillWeight = 28 },
                new DataGridViewTextBoxColumn { Name = "RAM", HeaderText = "RAM", FillWeight = 38 },
                new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Trạng thái", FillWeight = 50 }
            });
            accountGrid.CellFormatting += AccountGrid_CellFormatting;

            layout.Controls.Add(actionBar, 0, 0);
            layout.Controls.Add(accountGrid, 0, 1);
            
            accountsPage.Controls.Add(layout);
        }

        private void BuildSettingsPage()
        {
            settingsPage = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            int y = 10;

            // Window Size Card
            var windowCard = CreateSettingsCard("🖥️ Kích thước cửa sổ game", ref y, 80);
            cboWindowSize = new ComboBox
            {
                Location = new Point(20, 35),
                Size = new Size(200, 30),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = BG,
                ForeColor = TEXT,
                Font = new Font("Segoe UI", 10)
            };
            cboWindowSize.Items.AddRange(new object[] {
                "Mặc định", "320x240", "480x360", "640x480", "800x600", "1024x768"
            });
            cboWindowSize.SelectedIndex = 0;
            cboWindowSize.SelectedIndexChanged += CboWindowSize_Changed;
            windowCard.Controls.Add(cboWindowSize);
            settingsPage.Controls.Add(windowCard);

            // Graphics Card
            var gfxCard = CreateSettingsCard("🎨 Đồ họa", ref y, 170);
            int cy = 35;
            chkShadows = new CheckBox
            {
                Text = "Tắt bóng (Shadows)",
                Location = new Point(20, cy),
                Size = new Size(250, 25),
                ForeColor = TEXT,
                Checked = true
            };
            gfxCard.Controls.Add(chkShadows);
            cy += 30;
            chkParticles = new CheckBox
            {
                Text = "Tắt hiệu ứng (Particles)",
                Location = new Point(20, cy),
                Size = new Size(250, 25),
                ForeColor = TEXT,
                Checked = true
            };
            gfxCard.Controls.Add(chkParticles);
            cy += 30;
            chkLowQuality = new CheckBox
            {
                Text = "Chất lượng thấp nhất",
                Location = new Point(20, cy),
                Size = new Size(250, 25),
                ForeColor = TEXT,
                Checked = true
            };
            gfxCard.Controls.Add(chkLowQuality);
            cy += 35;
            AddSettingsLabel(gfxCard, "Target FPS:", 20, cy);
            nudFps = new NumericUpDown
            {
                Location = new Point(120, cy - 3),
                Size = new Size(80, 28),
                Minimum = 1,
                Maximum = 120,
                Value = 10,
                BackColor = BG,
                ForeColor = TEXT
            };
            gfxCard.Controls.Add(nudFps);
            settingsPage.Controls.Add(gfxCard);

            // Multi-Account Optimization Card
            var optCard = CreateSettingsCard("🚀 Tối ưu Multi-Account", ref y, 100);
            chkAutoCleanRAM = new CheckBox
            {
                Text = "Tự dọn RAM định kỳ (mỗi 60 giây)",
                Location = new Point(20, 35),
                Size = new Size(300, 25),
                ForeColor = TEXT,
                Checked = true
            };
            optCard.Controls.Add(chkAutoCleanRAM);
            chkUltraLowRes = new CheckBox
            {
                Text = "Giảm resolution tối đa (160x120) khi Headless",
                Location = new Point(20, 62),
                Size = new Size(350, 25),
                ForeColor = TEXT,
                Checked = true
            };
            optCard.Controls.Add(chkUltraLowRes);
            settingsPage.Controls.Add(optCard);

            // Misc Card
            var miscCard = CreateSettingsCard("⚡ Khác", ref y, 70);
            chkMinimize = new CheckBox
            {
                Text = "Thu nhỏ launcher khi chạy game",
                Location = new Point(20, 35),
                Size = new Size(300, 25),
                ForeColor = TEXT
            };
            miscCard.Controls.Add(chkMinimize);
            settingsPage.Controls.Add(miscCard);

            // Auto Quest AI — Bật/Tắt tính năng bot
            var botCard = CreateSettingsCard("🤖 Auto Quest (Phase 2)", ref y, 280);
            int by = 35;

            chkAutoQuest = new CheckBox
            {
                Text = "🚀 Tự động bật Auto Quest khi vào game",
                Location = new Point(20, by),
                Size = new Size(380, 25),
                ForeColor = Color.FromArgb(129, 140, 248),
                Font = new Font("Segoe UI", 9.5f),
                Checked = true
            };
            botCard.Controls.Add(chkAutoQuest);
            by += 30;

            chkSmartDialog = new CheckBox
            {
                Text = "❗ Smart Dialog: ưu tiên nút ! ? + click liên tục",
                Location = new Point(20, by),
                Size = new Size(380, 25),
                ForeColor = Color.FromArgb(248, 113, 113),
                Font = new Font("Segoe UI", 9.5f),
                Checked = true
            };
            botCard.Controls.Add(chkSmartDialog);
            by += 30;

            chkCollectQuest = new CheckBox
            {
                Text = "📦 Collect Quest: auto interact khi idle",
                Location = new Point(20, by),
                Size = new Size(380, 25),
                ForeColor = Color.FromArgb(52, 211, 153),
                Font = new Font("Segoe UI", 9.5f),
                Checked = true
            };
            botCard.Controls.Add(chkCollectQuest);
            by += 30;

            chkAutoPathfind = new CheckBox
            {
                Text = "🔄 Re-pathfind khi đóng dialog + chuyển map",
                Location = new Point(20, by),
                Size = new Size(380, 25),
                ForeColor = Color.FromArgb(196, 181, 253),
                Font = new Font("Segoe UI", 9.5f),
                Checked = true
            };
            botCard.Controls.Add(chkAutoPathfind);
            by += 35;

            AddSettingsLabel(botCard, "Tốc độ game mặc định:", 20, by);
            nudGameSpeed = new NumericUpDown
            {
                Location = new Point(180, by - 3),
                Size = new Size(80, 28),
                Minimum = 1,
                Maximum = 10,
                Value = 1,
                BackColor = BG,
                ForeColor = YELLOW
            };
            botCard.Controls.Add(nudGameSpeed);
            by += 35;

            var lblSpeedNote = new Label
            {
                Text = "⚡ Mẹo: Bấm F6 trong game để tăng tốc độ nhanh x2, x3!",
                Location = new Point(20, by),
                Size = new Size(450, 25),
                ForeColor = Color.Gold,
                Font = new Font("Segoe UI", 9, FontStyle.Italic)
            };
            botCard.Controls.Add(lblSpeedNote);
            settingsPage.Controls.Add(botCard);

            // Save Button
            var btnSave = new Button
            {
                Text = "💾 Lưu cài đặt",
                Location = new Point(10, y),
                Size = new Size(160, 42),
                FlatStyle = FlatStyle.Flat,
                BackColor = ACCENT,
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 10),
                Cursor = Cursors.Hand
            };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Click += (s, e) =>
            {
                SaveSettings();
                UpdateStatus("✅ Đã lưu cài đặt!");
            };
            settingsPage.Controls.Add(btnSave);
            y += 60;
        }

        // ======================== PAGE SWITCHING ========================

        private void SwitchPage(string page)
        {
            contentPanel.Controls.Clear();
            
            // Add headerPanel FIRST so it gets processed FIRST in Layout (Bottom-Z)
            // It will Dock=Top and take 40px properly.
            contentPanel.Controls.Add(headerPanel);

            btnNavAccounts.BackColor = Color.Transparent;
            btnNavSettings.BackColor = Color.Transparent;
            btnNavInspector.BackColor = Color.Transparent;
            btnNavDashboard.BackColor = Color.Transparent;
            btnNavGuide.BackColor = Color.Transparent;

            // Stop dashboard timer if leaving dashboard
            if (dashboardTimer != null) dashboardTimer.Stop();

            Control content = null;

            if (page == "accounts")
            {
                content = accountsPage;
                btnNavAccounts.BackColor = Color.FromArgb(35, 35, 50);
            }
            else if (page == "settings")
            {
                content = settingsPage;
                btnNavSettings.BackColor = Color.FromArgb(35, 35, 50);
            }
            else if (page == "inspector")
            {
                content = inspectorPage;
                btnNavInspector.BackColor = Color.FromArgb(35, 35, 50);
                RefreshInspectorLogs();
            }
            else if (page == "dashboard")
            {
                if (dashboardPage == null) BuildDashboardPage();
                content = dashboardPage;
                btnNavDashboard.BackColor = Color.FromArgb(35, 35, 50);
                RefreshDashboard();
                // Start auto-refresh timer
                if (dashboardTimer == null)
                {
                    dashboardTimer = new System.Windows.Forms.Timer();
                    dashboardTimer.Interval = 3000; // Refresh mỗi 3s
                    dashboardTimer.Tick += (s, ev) => RefreshDashboard();
                }
                dashboardTimer.Start();
            }
            else if (page == "guide")
            {
                content = guidePage;
                btnNavGuide.BackColor = Color.FromArgb(35, 35, 50);
            }

            if (content != null)
            {
                // Add page LAST so it gets processed LAST (Top-Z)
                // It will Dock=Fill the REMAINING space.
                content.Dock = DockStyle.Fill;
                contentPanel.Controls.Add(content);
                content.BringToFront(); // Ensure it's top Z (docks last) just in case
            }
        }

        // ======================== GRID FORMATTING ========================

        private void AccountGrid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (accountGrid.Columns[e.ColumnIndex].Name == "Status")
            {
                string val = e.Value?.ToString() ?? "";
                if (val.Contains("Đang chạy"))
                {
                    e.CellStyle.ForeColor = GREEN;
                    e.CellStyle.Font = new Font("Segoe UI", 9, FontStyle.Bold);
                }
                else
                {
                    e.CellStyle.ForeColor = DIM;
                }
            }
            else if (accountGrid.Columns[e.ColumnIndex].Name == "RAM")
            {
                e.CellStyle.ForeColor = ORANGE;
                e.CellStyle.Font = new Font("Consolas", 9);
            }
        }

        // ======================== DATA ========================

        private void LoadAccounts()
        {
            try
            {
                if (File.Exists(configPath))
                {
                    var config = JsonSerializer.Deserialize<AccountConfig>(File.ReadAllText(configPath));
                    if (config?.accounts != null)
                        accounts = config.accounts;
                    if (config?.settings != null)
                        settings = config.settings;
                }
            }
            catch { }
            RefreshGrid();
        }

        private void SaveAccounts()
        {
            try
            {
                var config = new AccountConfig
                {
                    accounts = accounts,
                    settings = settings
                };
                var dir = Path.GetDirectoryName(configPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(configPath, JsonSerializer.Serialize(config, options));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi lưu: {ex.Message}");
            }
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(configPath))
                {
                    var config = JsonSerializer.Deserialize<AccountConfig>(File.ReadAllText(configPath));
                    if (config?.settings != null)
                    {
                        settings = config.settings;
                        chkShadows.Checked = settings.disableShadows;
                        chkParticles.Checked = settings.disableParticles;
                        chkLowQuality.Checked = settings.lowQuality;
                        nudFps.Value = Math.Clamp(settings.targetFps, 1, 120);
                        chkMinimize.Checked = settings.minimizeOnLaunch;
                        chkAutoCleanRAM.Checked = settings.autoCleanRAM;
                        chkUltraLowRes.Checked = settings.ultraLowRes;
                        chkAutoQuest.Checked = settings.autoQuest;
                        chkSmartDialog.Checked = settings.smartDialog;
                        chkCollectQuest.Checked = settings.collectQuest;
                        chkAutoPathfind.Checked = settings.autoPathfind;
                        nudGameSpeed.Value = Math.Clamp(settings.gameSpeed, 1, 10);

                        // Select window size
                        string sizeStr = $"{settings.windowWidth}x{settings.windowHeight}";
                        for (int i = 0; i < cboWindowSize.Items.Count; i++)
                        {
                            if (cboWindowSize.Items[i].ToString() == sizeStr)
                            {
                                cboWindowSize.SelectedIndex = i;
                                break;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private void SaveSettings()
        {
            settings.disableShadows = chkShadows.Checked;
            settings.disableParticles = chkParticles.Checked;
            settings.lowQuality = chkLowQuality.Checked;
            settings.targetFps = (int)nudFps.Value;
            settings.minimizeOnLaunch = chkMinimize.Checked;
            settings.autoCleanRAM = chkAutoCleanRAM.Checked;
            settings.ultraLowRes = chkUltraLowRes.Checked;
            settings.autoQuest = chkAutoQuest.Checked;
            settings.smartDialog = chkSmartDialog.Checked;
            settings.collectQuest = chkCollectQuest.Checked;
            settings.autoPathfind = chkAutoPathfind.Checked;
            settings.gameSpeed = (int)nudGameSpeed.Value;
        }

        private void ParseWindowSize()
        {
            string sel = cboWindowSize.SelectedItem?.ToString() ?? "Mặc định";
            if (sel == "Mặc định")
            {
                settings.windowWidth = 0;
                settings.windowHeight = 0;
            }
            else
            {
                var parts = sel.Split('x');
                if (parts.Length == 2)
                {
                    int.TryParse(parts[0], out int w);
                    int.TryParse(parts[1], out int h);
                    settings.windowWidth = w;
                    settings.windowHeight = h;
                }
            }
        }

        private void RefreshGrid()
{
    accountGrid.Rows.Clear();
    for (int i = 0; i < accounts.Count; i++)
    {
        var a = accounts[i];
        bool running = runningProcesses.ContainsKey(i) && !runningProcesses[i].HasExited;
        a.running = running;
        string ramText = "—";
        if (running && runningProcesses.ContainsKey(i))
        {
            try { ramText = $"{runningProcesses[i].WorkingSet64 / 1024 / 1024} MB"; }
            catch { ramText = "?"; }
        }

        // Read bot status file
        string mapText = "—";
        string questText = "—";
        if (running)
        {
            var status = ReadBotStatus(a.username);
            if (status != null)
            {
                mapText = status.Item1;   // scene
                questText = status.Item2; // quest
            }
        }

        accountGrid.Rows.Add(
            i + 1,
            a.name,
            a.username,
            a.server,
            a.character,
            mapText,
            questText,
            a.autoQuest,
            ramText,
            running ? "🟢 Đang chạy" : "⚪ Dừng"
        );
    }
}

/// <summary>
/// Đọc file trạng thái bot từ plugin. Returns (scene, quest) hoặc null.
/// </summary>
private Tuple<string, string> ReadBotStatus(string username)
{
    try
    {
        string statusDir = Path.Combine(gamePath, "BepInEx", "plugins", "bot_status");
        string statusFile = Path.Combine(statusDir, $"status_{username}.json");
        if (!File.Exists(statusFile)) return null;

        // Only read if file was modified within last 30 seconds
        var lastWrite = File.GetLastWriteTime(statusFile);
        if ((DateTime.Now - lastWrite).TotalSeconds > 30) return null;

        string json = File.ReadAllText(statusFile);

        // Simple JSON parsing
        string scene = ExtractJsonValue(json, "scene");
        string quest = ExtractJsonValue(json, "quest");
        string hint = ExtractJsonValue(json, "questHint");

        // Format map name
        string mapDisplay = scene ?? "—";
        if (mapDisplay == "MainGameScene") mapDisplay = "🎮 Main";
        else if (mapDisplay == "LoginScene") mapDisplay = "🔑 Login";
        else if (mapDisplay == "InitScene") mapDisplay = "⏳ Init";

        // Format quest - truncate if too long
        string questDisplay = quest ?? "—";
        if (!string.IsNullOrEmpty(hint))
            questDisplay = $"{quest} \u2192 {hint}";
        if (questDisplay.Length > 50)
            questDisplay = questDisplay.Substring(0, 47) + "...";

        return new Tuple<string, string>(mapDisplay, questDisplay);
    }
    catch
    {
        return null;
    }
}

private string ExtractJsonValue(string json, string key)
{
    string search = $"\"{key}\": \"";
    int start = json.IndexOf(search);
    if (start < 0) return null;
    start += search.Length;
    int end = json.IndexOf('"', start);
    if (end < 0) return null;
    return json.Substring(start, end - start)
        .Replace("\\n", " ")
        .Replace("\\\"", "\"")
        .Replace("\\\\", "\\");
}
        private void UpdateStatus(string msg) => statusLabel.Text = msg;

        // ======================== ACCOUNT ACTIONS ========================

        private void AddAccount()
        {
            var dlg = new AccountDialog();
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                accounts.Add(dlg.Account);
                SaveAccounts();
                RefreshGrid();
                UpdateStatus($"✅ Đã thêm: {dlg.Account.name}");
            }
        }

        private void EditAccount()
        {
            if (accountGrid.CurrentRow == null) return;
            int idx = accountGrid.CurrentRow.Index;
            if (idx < 0 || idx >= accounts.Count) return;
            var dlg = new AccountDialog(accounts[idx]);
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                accounts[idx] = dlg.Account;
                SaveAccounts();
                RefreshGrid();
            }
        }

        private void DeleteAccount()
        {
            if (accountGrid.CurrentRow == null) return;
            int idx = accountGrid.CurrentRow.Index;
            if (idx < 0 || idx >= accounts.Count) return;
            if (MessageBox.Show($"Xóa '{accounts[idx].name}'?", "Xác nhận",
                MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                accounts.RemoveAt(idx);
                SaveAccounts();
                RefreshGrid();
            }
        }

        // ======================== LAUNCH ========================

        private void LaunchSelected()
        {
            if (accountGrid.CurrentRow == null) return;
            int idx = accountGrid.CurrentRow.Index;
            if (idx >= 0 && idx < accounts.Count)
                LaunchInstance(idx);
        }

        private void LaunchAll()
        {
            for (int i = 0; i < accounts.Count; i++)
            {
                if (!runningProcesses.ContainsKey(i) || runningProcesses[i].HasExited)
                {
                    LaunchInstance(i);
                    System.Threading.Thread.Sleep(2000); // delay between launches
                }
            }
        }

        private void StopAll()
        {
            foreach (var kvp in runningProcesses)
            {
                try
                {
                    if (!kvp.Value.HasExited)
                    {
                        kvp.Value.Kill();
                        UpdateStatus($"⏹ Đã dừng process {kvp.Value.Id}");
                    }
                }
                catch { }
            }
            runningProcesses.Clear();
            RefreshGrid();
        }

        private void LaunchInstance(int accountIndex)
        {
            if (accountIndex < 0 || accountIndex >= accounts.Count) return;

            string exePath = Path.Combine(gamePath, gameExeName);
            if (!File.Exists(exePath))
            {
                MessageBox.Show($"Không tìm thấy: {exePath}", "Lỗi");
                return;
            }

            // Save current account index to config
            var config = new AccountConfig
            {
                accounts = accounts,
                currentAccountIndex = accountIndex,
                settings = settings
            };
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var dir = Path.GetDirectoryName(configPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(configPath, JsonSerializer.Serialize(config, options));
            }
            catch { }

            // Also write accounts.json to BepInEx/plugins for the plugin to read
            try
            {
                string pluginDir = Path.Combine(gamePath, "BepInEx", "plugins");
                if (Directory.Exists(pluginDir))
                {
                    var pluginConfig = new AccountConfig
                    {
                        accounts = accounts,
                        currentAccountIndex = accountIndex,
                        settings = settings
                    };
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    File.WriteAllText(Path.Combine(pluginDir, "accounts.json"),
                        JsonSerializer.Serialize(pluginConfig, options));
                }
            }
            catch { }

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = gamePath,
                UseShellExecute = true
            };

            // Set window size args
            if (settings.windowWidth > 0 && settings.windowHeight > 0)
            {
                psi.Arguments = $"-screen-width {settings.windowWidth} -screen-height {settings.windowHeight} -screen-fullscreen 0";
            }

            // Set env for multi-instance
            psi.UseShellExecute = false;
            psi.EnvironmentVariables["VTDC_ACCOUNT_INDEX"] = accountIndex.ToString();

            try
            {
                var proc = Process.Start(psi);
                if (proc != null)
                {
                    runningProcesses[accountIndex] = proc;
                    RefreshGrid();
                    UpdateStatus($"▶ Đã chạy: {accounts[accountIndex].name} (PID: {proc.Id})");

                    if (settings.minimizeOnLaunch)
                        this.WindowState = FormWindowState.Minimized;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi chạy game: {ex.Message}", "Lỗi");
            }
        }

        // ======================== PROCESS MONITOR ========================

        private void ProcessMonitor_Tick(object sender, EventArgs e)
        {
            bool changed = false;
            var toRemove = new List<int>();
            foreach (var kvp in runningProcesses)
            {
                try
                {
                    if (kvp.Value.HasExited)
                    {
                        toRemove.Add(kvp.Key);
                        changed = true;
                    }
                }
                catch { toRemove.Add(kvp.Key); changed = true; }
            }
            foreach (var k in toRemove)
            {
                runningProcesses.Remove(k);
                if (k < accounts.Count) accounts[k].running = false;
            }
            // Always refresh to update RAM column
            RefreshGrid();
        }

        private void CboWindowSize_Changed(object sender, EventArgs e)
        {
            ParseWindowSize();
        }


        // ======================== DASHBOARD PAGE ========================

        private void BuildDashboardPage()
        {
            dashboardPage = new Panel { Dock = DockStyle.Fill, BackColor = BG };

            // === Header ===
            var dashHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 50,
                BackColor = Color.FromArgb(0, 100, 0), // Dark Green for visual confirmation
                Padding = new Padding(8, 8, 8, 0)
            };

            var titleLabel = new Label
            {
                Text = "📊 Bot Dashboard — Giám Sát Tài Khoản",
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                ForeColor = ACCENT,
                Dock = DockStyle.Left,
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft
            };

            var refreshBtn = new Button
            {
                Text = "🔄 Refresh",
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(40, 40, 60),
                ForeColor = CYAN,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Size = new Size(100, 32),
                Dock = DockStyle.Right,
                Cursor = Cursors.Hand
            };
            refreshBtn.FlatAppearance.BorderColor = CYAN;
            refreshBtn.FlatAppearance.BorderSize = 1;
            refreshBtn.Click += (s, e) => RefreshDashboard();

            dashHeader.Controls.Add(refreshBtn);
            dashHeader.Controls.Add(titleLabel);

            // === Boss notification banner (REMOVED v40) ===
            // Removed to prevent covering content.
            // Information is available in console logs if needed.

            // ======================== COMMAND CONSOLE ========================
            var cmdPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(20, 20, 30),
                Padding = new Padding(10, 6, 10, 6)
            };
            cmdPanel.Paint += (s, pe) =>
            {
                using (var pen = new Pen(Color.FromArgb(50, ACCENT.R, ACCENT.G, ACCENT.B), 1))
                    pe.Graphics.DrawLine(pen, 0, 0, cmdPanel.Width, 0);
            };

            // Console title bar
            var cmdTitleBar = new Panel { Dock = DockStyle.Top, Height = 36 };

            var cmdTitle = new Label
            {
                Text = "💻 Command Console",
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                ForeColor = ACCENT,
                Dock = DockStyle.Left,
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft
            };

            // Account selector
            var cmdAccLabel = new Label
            {
                Text = "Acc:",
                Font = new Font("Segoe UI", 9),
                ForeColor = DIM,
                Size = new Size(32, 28),
                TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(cmdTitleBar.Width - 360, 4) // This will be adjusted by Dock.Right later
            };
            
            
            // Auto Log Checkbox
            chkAutoLog = new CheckBox
            {
                Text = "🔄 Auto Log",
                ForeColor = Color.Yellow,
                Font = new Font("Segoe UI", 9),
                AutoSize = true,
                Location = new Point(cmdTitleBar.Width - 460, 4), // This will be adjusted by Dock.Right later
                Checked = true
            };
            cmdTitleBar.Controls.Add(chkAutoLog);
            cmdAccountCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(36, 36, 52),
                ForeColor = TEXT,
                Font = new Font("Segoe UI", 9),
                Size = new Size(150, 28),
                Dock = DockStyle.Right,
                FlatStyle = FlatStyle.Flat
            };

            cmdTitleBar.Controls.Add(cmdAccountCombo);
            cmdTitleBar.Controls.Add(cmdAccLabel); // Add label before combo for correct order
            cmdTitleBar.Controls.Add(cmdTitle);

            // Command buttons bar
            var cmdButtonBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 60,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 2, 0, 2),
                AutoScroll = false
            };

            Action<string, string, Color> addCmdBtn = (label, cmd, color) =>
            {
                var btn = new Button
                {
                    Text = label,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(30, 30, 45),
                    ForeColor = color,
                    Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                    Size = new Size(Math.Max(60, label.Length * 9 + 15), 32),
                    Cursor = Cursors.Hand,
                    Margin = new Padding(3, 3, 3, 3)
                };
                btn.FlatAppearance.BorderColor = Color.FromArgb(60, color.R, color.G, color.B);
                btn.FlatAppearance.BorderSize = 1;
                btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(50, color.R, color.G, color.B);
                btn.Click += (s, e) => SendBotCommand(cmd);
                cmdButtonBar.Controls.Add(btn);
            };

            addCmdBtn("📡 Ping", "ping", GREEN);
            addCmdBtn("📊 Status", "status", CYAN);
            addCmdBtn("📋 Quest", "quest", TEXT);
            addCmdBtn("🗺️ Map", "map", ORANGE);
            addCmdBtn("🐉 Boss", "boss", RED);
            addCmdBtn("📜 Log", "log", YELLOW);
            addCmdBtn("⚡ Toggle Auto", "toggle auto", ACCENT);

            // Split container for Command Output vs Live Log
            var splitConsole = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent
            };
            splitConsole.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45f)); // Commands
            splitConsole.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f)); // Logs

            // --- Left side: Manual Command Results ---
            var responseContainer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 4, 0) };
            var responseHeader = new Label { 
                Text = "▶ COMMAND OUTPUT", 
                Dock = DockStyle.Top, 
                Height = 20, 
                Font = new Font("Segoe UI", 8, FontStyle.Bold), 
                ForeColor = DIM 
            };
            cmdResponseBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(14, 14, 20),
                ForeColor = CYAN,
                Font = new Font("Consolas", 10),
                BorderStyle = BorderStyle.None,
                Text = "⚡ NEW VERSION LOADED. Command results will appear here."
            };
            responseContainer.Controls.Add(cmdResponseBox);
            responseContainer.Controls.Add(responseHeader);

            // --- Right side: Live Activity Logs ---
            var logContainer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4, 0, 0, 0) };
            var logHeader = new Label { 
                Text = "📜 LIVE ACTIVITY LOG", 
                Dock = DockStyle.Top, 
                Height = 20, 
                Font = new Font("Segoe UI", 8, FontStyle.Bold), 
                ForeColor = YELLOW 
            };
            cmdLogBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(10, 10, 15),
                ForeColor = GREEN,
                Font = new Font("Consolas", 9),
                BorderStyle = BorderStyle.None,
                Text = "⚡ NEW VERSION LOADED. Waiting for bot logs..."
            };
            logContainer.Controls.Add(cmdLogBox);
            logContainer.Controls.Add(logHeader);

            splitConsole.Controls.Add(responseContainer, 0, 0);
            splitConsole.Controls.Add(logContainer, 1, 0);

            // Assembly order (bottom panel)
            cmdPanel.Controls.Add(splitConsole);
            cmdPanel.Controls.Add(cmdButtonBar);
            cmdPanel.Controls.Add(cmdTitleBar);

            // === Account cards FlowLayout ===
            dashboardFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                BackColor = BG,
                Padding = new Padding(4)
            };

            // === ASSEMBLY WITH SPLITCONTAINER ===
            var dashboardSplitter = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 300,
                SplitterWidth = 8,
                BackColor = Color.FromArgb(40, 40, 60)
            };
            
            dashboardSplitter.Panel1.Controls.Add(dashboardFlow);
            dashboardSplitter.Panel2.Controls.Add(cmdPanel);

            // ASSEMBLY: Add Top panels FIRST, then Fill panels
            // Note: dashHeader (Green) removed to save space for content
            // Note: bossPanel removed in v40
            dashboardPage.Controls.Add(dashboardSplitter); // Fill remaining space

            // Set initial splitter relative to form height (delayed)
            dashboardPage.HandleCreated += (s, e) => {
                if (dashboardPage.Height > 400)
                {
                    dashboardSplitter.SplitterDistance = dashboardPage.Height - 200; // Give 200px to console
                }
            };
        }

        /// <summary>
        /// Reads all bot status JSON files and updates dashboard cards
        /// </summary>
        private void RefreshDashboard()
        {
            if (dashboardFlow == null) return;
            try
            {
                string statusDir = Path.Combine(gamePath, "BepInEx", "plugins", "bot_status");
                if (!Directory.Exists(statusDir)) { statusLabel.Text = "📂 bot_status folder not found"; return; }

                var statusFiles = Directory.GetFiles(statusDir, "status_*.json");
                dashboardFlow.SuspendLayout();
                dashboardFlow.Controls.Clear();

                string bossText = "";
                int totalRunning = 0;
                int cardWidth = Math.Max(320, (dashboardFlow.ClientSize.Width - 40) / 2);

                foreach (var file in statusFiles)
                {
                    try
                    {
                        var lastWrite = File.GetLastWriteTime(file);
                        bool isRecent = (DateTime.Now - lastWrite).TotalSeconds < 30;
                        string json = File.ReadAllText(file);

                        string username = ExtractJsonValue(json, "username") ?? Path.GetFileNameWithoutExtension(file).Replace("status_", "");
                        string scene = ExtractJsonValue(json, "scene") ?? "—";
                        string map = ExtractJsonValue(json, "map") ?? "";
                        string zone = ExtractJsonValue(json, "zone") ?? "";
                        string posX = ExtractJsonValue(json, "posX") ?? "0";
                        string posY = ExtractJsonValue(json, "posY") ?? "0";
                        string quest = ExtractJsonValue(json, "quest") ?? "—";
                        string questHint = ExtractJsonValue(json, "questHint") ?? "";
                        string timestamp = ExtractJsonValue(json, "timestamp") ?? "—";
                        bool autoEnabled = json.Contains("\"autoEnabled\": true");
                        bool attackEnabled = json.Contains("\"attackEnabled\": true");
                        bool headless = json.Contains("\"headless\": true");
                        bool collectQuest = json.Contains("\"collectQuest\": true");
                        string lastBoss = ExtractJsonValue(json, "lastBoss") ?? "";
                        string lastBossMap = ExtractJsonValue(json, "lastBossMap") ?? "";

                        if (!string.IsNullOrEmpty(lastBoss))
                            bossText = $"🐉 Boss: {lastBoss}" + (!string.IsNullOrEmpty(lastBossMap) ? $" | 🗺️ {lastBossMap}" : "");

                        if (isRecent) totalRunning++;

                        // === Build Account Card ===
                        var card = new Panel
                        {
                            Size = new Size(cardWidth, 200),
                            Margin = new Padding(6),
                            BackColor = Color.FromArgb(28, 28, 42),
                            Padding = new Padding(0)
                        };
                        card.Paint += (s, pe) =>
                        {
                            using (var brush = new LinearGradientBrush(card.ClientRectangle,
                                Color.FromArgb(32, 32, 48), Color.FromArgb(24, 24, 36), 135f))
                                pe.Graphics.FillRectangle(brush, card.ClientRectangle);
                            Color borderColor = isRecent ? GREEN : Color.FromArgb(60, 60, 80);
                            using (var pen = new Pen(borderColor, isRecent ? 2 : 1))
                                pe.Graphics.DrawRectangle(pen, 1, 1, card.Width - 3, card.Height - 3);
                        };

                        int y = 10;

                        // Row 1: Username + Status badge
                        var lblUser = new Label
                        {
                            Text = $"👤 {username}",
                            Font = new Font("Segoe UI", 13, FontStyle.Bold),
                            ForeColor = isRecent ? Color.White : DIM,
                            Location = new Point(14, y),
                            AutoSize = true
                        };
                        card.Controls.Add(lblUser);

                        var lblStatus = new Label
                        {
                            Text = isRecent ? "● ONLINE" : "○ OFFLINE",
                            Font = new Font("Segoe UI", 9, FontStyle.Bold),
                            ForeColor = isRecent ? GREEN : RED,
                            Location = new Point(cardWidth - 100, y + 4),
                            AutoSize = true
                        };
                        card.Controls.Add(lblStatus);
                        y += 30;

                        // Row 2: Map & Zone
                        string mapDisplay = !string.IsNullOrEmpty(map) ? map : (scene == "MainGameScene" ? "🎮 In Game" : scene);
                        string locationText = $"🗺️ {mapDisplay}";
                        if (!string.IsNullOrEmpty(zone)) locationText += $" | 📍 {zone}";
                        locationText += $" ({posX}, {posY})";
                        var lblMap = new Label
                        {
                            Text = locationText,
                            Font = new Font("Segoe UI", 9),
                            ForeColor = CYAN,
                            Location = new Point(14, y),
                            Size = new Size(cardWidth - 30, 18),
                            AutoEllipsis = true
                        };
                        card.Controls.Add(lblMap);
                        y += 22;

                        // Row 3: Quest
                        string questDisplay = quest.Length > 50 ? quest.Substring(0, 47) + "..." : quest;
                        var lblQuest = new Label
                        {
                            Text = $"📋 {questDisplay}",
                            Font = new Font("Segoe UI", 9),
                            ForeColor = collectQuest ? ORANGE : TEXT,
                            Location = new Point(14, y),
                            Size = new Size(cardWidth - 30, 18),
                            AutoEllipsis = true
                        };
                        card.Controls.Add(lblQuest);
                        y += 20;

                        // Row 4: Quest hint
                        if (!string.IsNullOrEmpty(questHint))
                        {
                            string hintDisplay = questHint.Length > 55 ? questHint.Substring(0, 52) + "..." : questHint;
                            var lblHint = new Label
                            {
                                Text = $"   💡 {hintDisplay}",
                                Font = new Font("Segoe UI", 8),
                                ForeColor = DIM,
                                Location = new Point(14, y),
                                Size = new Size(cardWidth - 30, 16),
                                AutoEllipsis = true
                            };
                            card.Controls.Add(lblHint);
                            y += 18;
                        }

                        // Row 5: Status badges
                        y += 6;
                        int badgeX = 14;
                        Action<string, Color, bool, string> addBadge = (text, color, active, cmd) =>
                        {
                            var badge = new Label
                            {
                                Text = text,
                                Font = new Font("Segoe UI", 8, FontStyle.Bold),
                                ForeColor = active ? Color.White : Color.FromArgb(80, 80, 100),
                                BackColor = active ? Color.FromArgb(60, color.R, color.G, color.B) : Color.FromArgb(20, 20, 30),
                                TextAlign = ContentAlignment.MiddleCenter,
                                AutoSize = true,
                                MinimumSize = new Size(60, 24),
                                Location = new Point(badgeX, y),
                                Padding = new Padding(8, 2, 8, 2),
                                Cursor = Cursors.Hand,
                                BorderStyle = active ? BorderStyle.FixedSingle : BorderStyle.None
                            };
                            
                            // Make it interactive if a command is provided
                            if (!string.IsNullOrEmpty(cmd))
                            {
                                badge.Click += (s, e) => {
                                    SendBotCommandToAccount(username, cmd);
                                };
                                // Hover effect
                                badge.MouseEnter += (s, e) => badge.BackColor = Color.FromArgb(100, color.R, color.G, color.B);
                                badge.MouseLeave += (s, e) => badge.BackColor = active ? Color.FromArgb(60, color.R, color.G, color.B) : Color.FromArgb(20, 20, 30);
                            }

                            card.Controls.Add(badge);
                            badgeX += badge.Width + 4;
                        };

                        addBadge("AUTO QUEST", ACCENT, autoEnabled, "toggle auto");
                        addBadge("HEADLESS", PURPLE, headless, null);
                        if (collectQuest) addBadge("COLLECT", ORANGE, true, null);
                        y += 30;

                        // Row 6: Timestamp
                        var lblTime = new Label
                        {
                            Text = $"🕐 Cập nhật: {timestamp}",
                            Font = new Font("Segoe UI", 8),
                            ForeColor = DIM,
                            Location = new Point(14, y),
                            AutoSize = true
                        };
                        card.Controls.Add(lblTime);

                        dashboardFlow.Controls.Add(card);
                    }
                    catch { continue; }
                }

                // Boss label updates removed in v40


                dashboardFlow.ResumeLayout();
                statusLabel.Text = $"📊 Dashboard: {totalRunning} acc đang chạy | {statusFiles.Length} acc tổng | {DateTime.Now:HH:mm:ss}";

                // Populate account combo for command console
                if (cmdAccountCombo != null)
                {
                    string selectedItem = cmdAccountCombo.SelectedItem as string;
                    var accountNames = new List<string>();
                    foreach (var file in statusFiles)
                    {
                        string name = Path.GetFileNameWithoutExtension(file).Replace("status_", "");
                        accountNames.Add(name);
                    }
                    // Only update if list changed
                    bool changed = accountNames.Count != cmdAccountCombo.Items.Count;
                    if (!changed)
                    {
                        for (int i = 0; i < accountNames.Count; i++)
                            if (i >= cmdAccountCombo.Items.Count || (string)cmdAccountCombo.Items[i] != accountNames[i])
                            { changed = true; break; }
                    }
                    if (changed)
                    {
                        cmdAccountCombo.Items.Clear();
                        foreach (var name in accountNames)
                            cmdAccountCombo.Items.Add(name);
                         
                        if (!string.IsNullOrEmpty(selectedItem) && cmdAccountCombo.Items.Contains(selectedItem))
                            cmdAccountCombo.SelectedItem = selectedItem;
                        else if (cmdAccountCombo.Items.Count > 0)
                            cmdAccountCombo.SelectedIndex = 0;
                    }
                }
                
                // === Auto Update Log ===
                // Use field chkAutoLog directly
                if (chkAutoLog != null && chkAutoLog.Checked && cmdAccountCombo.SelectedItem != null)
                {
                    string accName = cmdAccountCombo.SelectedItem.ToString();
                    string logFile = Path.Combine(statusDir, $"activity_log_{accName}.txt");
                    if (File.Exists(logFile)) 
                    {
                        try {
                            using (var fs = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            using (var sr = new StreamReader(fs)) {
                                string content = sr.ReadToEnd();
                                // Avoid refreshing if content same length (optimization)
                                if (cmdLogBox != null && (cmdLogBox.Text.Length != content.Length || !cmdLogBox.Text.EndsWith(content.Substring(Math.Max(0, content.Length - 10)))))
                                {
                                    cmdLogBox.Text = content;
                                    cmdLogBox.SelectionStart = cmdLogBox.Text.Length;
                                    cmdLogBox.ScrollToCaret();
                                }
                            }
                        } catch {}
                    }
                }
            }
            catch (Exception ex) { statusLabel.Text = $"Dashboard error: {ex.Message}"; }
        }

        /// <summary>
        /// Send a command to the currently selected account in the dropdown
        /// </summary>
        private void SendBotCommand(string command)
        {
            if (cmdAccountCombo == null || cmdAccountCombo.SelectedItem == null)
            {
                if (cmdResponseBox != null)
                {
                    cmdResponseBox.ForeColor = Color.IndianRed;
                    cmdResponseBox.Text = "❌ Chưa chọn tài khoản! Hãy chọn acc từ dropdown.";
                }
                return;
            }

            string username = cmdAccountCombo.SelectedItem.ToString();
            SendBotCommandToAccount(username, command);
        }

        /// <summary>
        /// Send a command to a specific bot account via file IPC
        /// </summary>
        private void SendBotCommandToAccount(string username, string command)
        {
            string statusDir = Path.Combine(gamePath, "BepInEx", "plugins", "bot_status");

            try
            {
                // Write command file
                string cmdFile = Path.Combine(statusDir, $"cmd_{username}.txt");
                File.WriteAllText(cmdFile, command);

                if (cmdResponseBox != null)
                {
                    cmdResponseBox.ForeColor = Color.Yellow;
                    cmdResponseBox.Text = $"📡 [{username}] Đang gửi: '{command}'...\n⏳ Đang chờ phản hồi...";
                }

                // Poll for response after a short delay
                var responseTimer = new System.Windows.Forms.Timer { Interval = 400 };
                int attempts = 0;
                responseTimer.Tick += (s, e) =>
                {
                    attempts++;
                    string respFile = Path.Combine(statusDir, $"resp_{username}.txt");

                    if (File.Exists(respFile))
                    {
                        try
                        {
                            string response = File.ReadAllText(respFile);
                            File.Delete(respFile); // Cleanup
                            if (cmdResponseBox != null)
                            {
                                cmdResponseBox.ForeColor = CYAN;
                                cmdResponseBox.Text = $"📬 [{username}] Response:\n━━━━━━━━━━━━━━━━━━━━━━━━━━\n{response}";
                            }
                        }
                        catch { }
                        responseTimer.Stop();
                        responseTimer.Dispose();
                    }
                    else if (attempts > 15) // Timeout ~6s
                    {
                        if (cmdResponseBox != null)
                        {
                            cmdResponseBox.ForeColor = Color.IndianRed;
                            cmdResponseBox.Text = $"⚠️ [{username}] Timeout: Bot không phản hồi lệnh '{command}'.";
                        }
                        responseTimer.Stop();
                        responseTimer.Dispose();
                    }
                };
                responseTimer.Start();
            }
            catch (Exception ex)
            {
                if (cmdResponseBox != null)
                    cmdResponseBox.Text = $"❌ Lỗi gửi lệnh: {ex.Message}";
            }
        }
        // ======================== INSPECTOR PAGE ========================

        private void BuildInspectorPage()
        {
            inspectorPage = new Panel { Dock = DockStyle.Fill };

            // === TOP section: Use a TableLayoutPanel for proper auto-sizing ===
            var topLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Padding = new Padding(0, 4, 0, 4)
            };
            topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            topLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Scan card
            topLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Bot card
            topLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Deep card

            // === CARD 1: Inspector Scan (F3-F8) ===
            var scanCard = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = CARD,
                Height = 130,
                Margin = new Padding(4, 2, 4, 2),
                Padding = new Padding(10, 4, 10, 6)
            };
            var scanTitle = new Label
            {
                Text = "⚡ Inspector — Quét Game Objects",
                Dock = DockStyle.Top,
                Height = 22,
                ForeColor = TEXT,
                Font = new Font("Segoe UI Semibold", 10),
                TextAlign = ContentAlignment.MiddleLeft
            };
            scanCard.Controls.Add(scanTitle);

            // Inner layout for scan card content
            var scanInner = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            scanInner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            scanInner.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f)); // "Quét TẤT CẢ" button
            scanInner.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // 2x3 grid

            // Quét TẤT CẢ
            var btnScanAll = new Button
            {
                Text = "★ Quét TẤT CẢ",
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 2, 0, 2),
                FlatStyle = FlatStyle.Flat,
                BackColor = ORANGE,
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 10),
                Cursor = Cursors.Hand
            };
            btnScanAll.FlatAppearance.BorderSize = 0;
            btnScanAll.Click += (s, e) => SendScanAll();
            scanInner.Controls.Add(btnScanAll, 0, 0);

            var scanTable = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 2,
                CellBorderStyle = TableLayoutPanelCellBorderStyle.None,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            scanTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            scanTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            scanTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
            scanTable.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            scanTable.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

            var scanBtnDefs = new (string text, int vk, Color color)[] {
                ("🎯 Scene Objects", 0x72, ACCENT),
                ("🔧 MonoBehaviours", 0x73, ACCENT),
                ("📝 UI Text", 0x74, GREEN),
                ("🔘 Buttons", 0x75, GREEN),
                ("⚡ Singletons", 0x76, Color.FromArgb(234, 179, 8)),
                ("🌐 Network", 0x77, Color.FromArgb(234, 179, 8)),
            };
            for (int i = 0; i < scanBtnDefs.Length; i++)
            {
                var (text, vk, color) = scanBtnDefs[i];
                var btn = new Button
                {
                    Text = text,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(2),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = color,
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 9, FontStyle.Bold),
                    Cursor = Cursors.Hand
                };
                btn.FlatAppearance.BorderSize = 0;
                int keyCode = vk;
                btn.Click += (s, e) => SendScanKey(keyCode, text);
                scanTable.Controls.Add(btn, i % 3, i / 3);
            }
            scanInner.Controls.Add(scanTable, 0, 1);
            scanCard.Controls.Add(scanInner);
            topLayout.Controls.Add(scanCard, 0, 0);

            // === CARD 2: Bot Controller ===
            var botCard = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = CARD,
                Height = 74,
                Margin = new Padding(4, 2, 4, 2),
                Padding = new Padding(10, 4, 10, 6)
            };
            var botTitle = new Label
            {
                Text = "🤖 Bot Controller",
                Dock = DockStyle.Top,
                Height = 22,
                ForeColor = TEXT,
                Font = new Font("Segoe UI Semibold", 10),
                TextAlign = ContentAlignment.MiddleLeft
            };
            botCard.Controls.Add(botTitle);

            var botTable = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                CellBorderStyle = TableLayoutPanelCellBorderStyle.None,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            botTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f));
            botTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            botTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22f));
            botTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 23f));
            botTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var botBtnDefs = new (string text, int vk, Color color)[] {
                ("★ AUTO ALL", 0x70, Color.FromArgb(220, 50, 47)),
                ("🔕 Headless", 0x71, ACCENT),
                ("🔑 Login", 0x7A, Color.FromArgb(234, 179, 8)),
                ("📊 Status", 0x7B, GREEN),
            };
            for (int i = 0; i < botBtnDefs.Length; i++)
            {
                var (text, vk, color) = botBtnDefs[i];
                var btn = new Button
                {
                    Text = text,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(2),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = color,
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 9, FontStyle.Bold),
                    Cursor = Cursors.Hand
                };
                btn.FlatAppearance.BorderSize = 0;
                int keyCode = vk;
                btn.Click += (s, e) => SendScanKey(keyCode, text);
                botTable.Controls.Add(btn, i, 0);
            }
            botCard.Controls.Add(botTable);
            topLayout.Controls.Add(botCard, 0, 1);

            // === CARD 3: DeepScanner ===
            var deepCard = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = CARD,
                Height = 74,
                Margin = new Padding(4, 2, 4, 2),
                Padding = new Padding(10, 4, 10, 6)
            };
            var deepTitle = new Label
            {
                Text = "🔬 DeepScanner — Phân Tích Class",
                Dock = DockStyle.Top,
                Height = 22,
                ForeColor = TEXT,
                Font = new Font("Segoe UI Semibold", 10),
                TextAlign = ContentAlignment.MiddleLeft
            };
            deepCard.Controls.Add(deepTitle);

            var deepTable = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                CellBorderStyle = TableLayoutPanelCellBorderStyle.None,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            deepTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28f));
            deepTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28f));
            deepTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22f));
            deepTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22f));
            deepTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var deepBtnDefs = new (string text, int vk, Color color)[] {
                ("🎯 AutoMission", 0x78, Color.FromArgb(168, 85, 247)),
                ("👤 PlayerData", 0x79, Color.FromArgb(168, 85, 247)),
                ("🔄 Cycle", 0x60, Color.FromArgb(59, 130, 246)),
                ("📋 All Mgrs", 0x61, Color.FromArgb(59, 130, 246)),
            };
            for (int i = 0; i < deepBtnDefs.Length; i++)
            {
                var (text, vk, color) = deepBtnDefs[i];
                var btn = new Button
                {
                    Text = text,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(2),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = color,
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 9, FontStyle.Bold),
                    Cursor = Cursors.Hand
                };
                btn.FlatAppearance.BorderSize = 0;
                int keyCode = vk;
                btn.Click += (s, e) => SendScanKey(keyCode, text);
                deepTable.Controls.Add(btn, i, 0);
            }
            deepCard.Controls.Add(deepTable);
            topLayout.Controls.Add(deepCard, 0, 2);

            // === BOTTOM section for Log Viewer (fills remaining space) ===
            var logPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = CARD,
                Padding = new Padding(10, 6, 10, 6)
            };

            // Log header bar
            var logHeaderBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 32,
                BackColor = Color.Transparent
            };
            var logTitle = new Label
            {
                Text = "📂 Inspector Logs",
                Location = new Point(4, 6),
                AutoSize = true,
                ForeColor = TEXT,
                Font = new Font("Segoe UI Semibold", 10)
            };
            logHeaderBar.Controls.Add(logTitle);

            var btnRefreshLogs = new Button
            {
                Text = "🔄",
                Location = new Point(150, 2),
                Size = new Size(40, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = ACCENT,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9),
                Cursor = Cursors.Hand
            };
            btnRefreshLogs.FlatAppearance.BorderSize = 0;
            btnRefreshLogs.Click += (s, e) => RefreshInspectorLogs();
            logHeaderBar.Controls.Add(btnRefreshLogs);

            var btnOpenFolder = new Button
            {
                Text = "📁 Mở",
                Location = new Point(195, 2),
                Size = new Size(70, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(50, 50, 65),
                ForeColor = DIM,
                Font = new Font("Segoe UI", 8),
                Cursor = Cursors.Hand
            };
            btnOpenFolder.FlatAppearance.BorderSize = 0;
            btnOpenFolder.Click += (s, e) => OpenInspectorLogs();
            logHeaderBar.Controls.Add(btnOpenFolder);

            logPanel.Controls.Add(logHeaderBar);

            // Split container for file list + content
            var splitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 180,
                SplitterWidth = 4,
                BackColor = Color.FromArgb(40, 40, 55),
                FixedPanel = FixedPanel.Panel1
            };

            inspectorFileList = new ListBox
            {
                Dock = DockStyle.Fill,
                BackColor = BG,
                ForeColor = TEXT,
                Font = new Font("Consolas", 8.5f),
                BorderStyle = BorderStyle.None
            };
            inspectorFileList.SelectedIndexChanged += (s, e) => ViewLogFile();
            splitContainer.Panel1.Controls.Add(inspectorFileList);

            inspectorContent = new TextBox
            {
                Dock = DockStyle.Fill,
                BackColor = BG,
                ForeColor = GREEN,
                Font = new Font("Consolas", 8.5f),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                BorderStyle = BorderStyle.None
            };
            splitContainer.Panel2.Controls.Add(inspectorContent);

            // Add split after header (Dock order: Fill added first, then Top)
            logPanel.Controls.Add(splitContainer);

            // Assemble: logPanel fills remaining space, topLayout docks top
            inspectorPage.Controls.Add(logPanel);
            inspectorPage.Controls.Add(topLayout);
        }

        private void BuildGuidePage()
        {
            guidePage = new Panel { Dock = DockStyle.Fill, Visible = false };
            
            var lblTitle = new Label
            {
                Text = "💡 Hướng dẫn sử dụng Launcher v3.7",
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                ForeColor = ACCENT,
                Dock = DockStyle.Top,
                Height = 60,
                Padding = new Padding(0, 10, 0, 0),
                TextAlign = ContentAlignment.TopCenter
            };
            guidePage.Controls.Add(lblTitle);

            var rtfGuide = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BackColor = BG,
                ForeColor = TEXT,
                BorderStyle = BorderStyle.None,
                ReadOnly = true,
                Font = new Font("Segoe UI", 11),
                Padding = new Padding(30)
            };

            string guideText = @"
🌟 TỔNG QUAN HỆ THỐNG
Bot được thiết kế để tự động hóa tối đa việc làm nhiệm vụ và treo up quái. Tính năng 'Tấn công' đã được loại bỏ vì game đã hỗ trợ cơ chế này tốt hơn.

📌 DASHBOARD (GIÁM SÁT)
- Mỗi tài khoản online sẽ hiện một thẻ (card) thông tin riêng.
- [AUTO QUEST]: Nút bật/tắt tự làm nhiệm vụ. Khi bật (Màu tím sáng), bot sẽ tự tìm đường, đánh quái quest và đối thoại.
- [SPEED]: Tốc độ game (1x, 2x, 3x). Ảnh hưởng toàn bộ game.
- [HEADLESS]: Trạng thái ẩn cửa sổ game để tiết kiệm tài nguyên.
- [COLLECT]: Tự tương tác với NPC/Vật phẩm khi đứng gần.
- 🐉 Boss: Banner trên cùng sẽ báo Boss đang xuất hiện tại đâu.

⌨️ PHÍM TẮT & ĐIỀU KHIỂN
Dùng tab 'Inspector' để điều khiển trực tiếp các cửa sổ game đang mở:
- Click các nút trên UI (F1, F2...): Cách an toàn nhất để điều khiển bot.
- F4: Tự động tương tác (Collect)
- F5: Tự động đối thoại (NPC Dialog)
- F6: TĂNG TỐC ĐỘ GAME (x1 -> x2 -> x3)
- Xem log chi tiết từng account tại cột bên trái tab Inspector.

💻 COMMAND CONSOLE (BẢNG LỆNH)
Dưới Dashboard có bảng lệnh chia làm 2 phần:
- Bên trái (Command Output): Hiện kết quả khi bạn bấm các nút như Ping, Status, Quest...
- Bên phải (Live Activity Log): Hiện nhật ký hành động thực tế của bot (ví dụ: '[15:30] Đang đánh quái...').

⚙️ CÀI ĐẶT
- Tự dọn RAM: Rất quan trọng khi chạy nhiều acc 24/7.
- FPS: Nên để 10-15 khi treo máy số lượng lớn để giảm tải CPU.
- Smart Dialog: Tự chọn các tùy chọn hội thoại có dấu ! hoặc ? để tiếp tục nhiệm vụ.

⚠️ LƯU Ý
Nếu Dashboard không cập nhật, hãy bấm nút '🔄 Refresh' ở góc trên bên phải Dashboard.
";
            rtfGuide.Text = guideText;
            guidePage.Controls.Add(rtfGuide);
        }

        private List<IntPtr> FindGameWindows()
        {
            var windows = new List<IntPtr>();

            // Strategy 1: Tìm từ các process đang chạy (reliable nhất)
            foreach (var proc in runningProcesses.Values)
            {
                try
                {
                    if (proc != null && !proc.HasExited && proc.MainWindowHandle != IntPtr.Zero)
                    {
                        windows.Add(proc.MainWindowHandle);
                    }
                }
                catch { }
            }

            // Strategy 2: Tìm window bằng title (exact + partial match)
            if (windows.Count == 0)
            {
                string targetTitle = "Vũ Trụ Đại Chiến";
                EnumWindows((hWnd, lParam) =>
                {
                    if (IsWindowVisible(hWnd))
                    {
                        var sb = new System.Text.StringBuilder(256);
                        GetWindowText(hWnd, sb, 256);
                        string title = sb.ToString();
                        // Exact match
                        if (title.Contains(targetTitle))
                        {
                            windows.Add(hWnd);
                        }
                        // Partial ASCII match (handles encoding issues)
                        else if (title.Contains("Tr") && title.Contains("Chi") && title.Contains("i"))
                        {
                            windows.Add(hWnd);
                        }
                    }
                    return true;
                }, IntPtr.Zero);
            }

            // Strategy 3: Tìm bằng process name trong system
            if (windows.Count == 0)
            {
                try
                {
                    var gameProcs = Process.GetProcesses()
                        .Where(p =>
                        {
                            try { return p.ProcessName.Contains("Tr") && p.ProcessName.Contains("Chi"); }
                            catch { return false; }
                        });
                    foreach (var p in gameProcs)
                    {
                        try
                        {
                            if (p.MainWindowHandle != IntPtr.Zero)
                                windows.Add(p.MainWindowHandle);
                        }
                        catch { }
                    }
                }
                catch { }
            }

            return windows;
        }

        private void SendScanKey(int vkCode, string label)
        {
            var windows = FindGameWindows();
            if (windows.Count == 0)
            {
                UpdateStatus("⚠️ Không tìm thấy game!");
                return;
            }

            foreach (var hwnd in windows)
            {
                try
                {
                    PostMessage(hwnd, WM_KEYDOWN, (IntPtr)vkCode, IntPtr.Zero);
                    System.Threading.Thread.Sleep(50);
                    PostMessage(hwnd, WM_KEYUP, (IntPtr)vkCode, IntPtr.Zero);
                }
                catch { }
            }
            UpdateStatus($"✅ Đã gửi {label} → {windows.Count} cửa sổ");

            // Auto refresh logs after delay
            var timer = new System.Windows.Forms.Timer { Interval = 3000 };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                timer.Dispose();
                RefreshInspectorLogs();
            };
            timer.Start();
        }

        private async void SendScanAll()
        {
            var windows = FindGameWindows();
            if (windows.Count == 0)
            {
                UpdateStatus("⚠️ Không tìm thấy game!");
                return;
            }

            UpdateStatus($"★ Đang quét TẤT CẢ ({windows.Count} cửa sổ)...");

            var scanKeys = new (int vk, string label)[] {
                (0x72, "F3"), (0x73, "F4"), (0x74, "F5"),
                (0x75, "F6"), (0x76, "F7"), (0x77, "F8")
            };

            await Task.Run(() =>
            {
                foreach (var hwnd in windows)
                {
                    foreach (var (vk, label) in scanKeys)
                    {
                        PostMessage(hwnd, WM_KEYDOWN, (IntPtr)vk, IntPtr.Zero);
                        System.Threading.Thread.Sleep(50);
                        PostMessage(hwnd, WM_KEYUP, (IntPtr)vk, IntPtr.Zero);
                        System.Threading.Thread.Sleep(1000);
                    }
                }
            });

            UpdateStatus("★ Hoàn tất quét TẤT CẢ!");

            var timer = new System.Windows.Forms.Timer { Interval = 2000 };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                timer.Dispose();
                RefreshInspectorLogs();
            };
            timer.Start();
        }

        private string GetInspectorLogDir()
        {
            return Path.Combine(gamePath, "BepInEx", "plugins", "inspector_logs");
        }

        private void OpenInspectorLogs()
        {
            string dir = GetInspectorLogDir();
            if (Directory.Exists(dir))
                Process.Start("explorer.exe", dir);
            else
                MessageBox.Show($"Thư mục chưa tồn tại:\n{dir}", "Chưa có logs");
        }

        private void RefreshInspectorLogs()
        {
            inspectorFileList.Items.Clear();
            inspectorContent.Clear();
            string dir = GetInspectorLogDir();
            if (!Directory.Exists(dir)) return;

            var files = Directory.GetFiles(dir, "*.txt");
            Array.Sort(files);
            Array.Reverse(files); // newest first
            foreach (var f in files)
            {
                inspectorFileList.Items.Add(Path.GetFileName(f));
            }
            if (inspectorFileList.Items.Count > 0)
                UpdateStatus($"📂 {inspectorFileList.Items.Count} log files");
        }

        private void ViewLogFile()
        {
            if (inspectorFileList.SelectedItem == null) return;
            string fileName = inspectorFileList.SelectedItem.ToString();
            string filePath = Path.Combine(GetInspectorLogDir(), fileName);
            try
            {
                if (File.Exists(filePath))
                {
                    inspectorContent.Text = File.ReadAllText(filePath);
                    UpdateStatus($"📄 {fileName}");
                }
            }
            catch (Exception ex)
            {
                inspectorContent.Text = $"Lỗi đọc file: {ex.Message}";
            }
        }

        // ======================== UI HELPERS ========================

        private void Sidebar_Paint(object sender, PaintEventArgs e)
        {
            using var pen = new Pen(Color.FromArgb(40, 40, 55), 1);
            e.Graphics.DrawLine(pen, sidebarPanel.Width - 1, 0, sidebarPanel.Width - 1, sidebarPanel.Height);
        }

        private Button CreateNavButton(string text, int top)
        {
            var btn = new Button
            {
                Text = text,
                Location = new Point(0, top),
                Size = new Size(164, 36),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = TEXT,
                Font = new Font("Segoe UI", 10),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 0, 0),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(40, 40, 55);
            return btn;
        }

        private Button CreateSolidButton(string text, Color bgColor, ref int x, int width)
        {
            var btn = new Button
            {
                Text = text,
                Location = new Point(x, 8),
                Size = new Size(width, 36),
                FlatStyle = FlatStyle.Flat,
                BackColor = bgColor,
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 9),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = ControlPaint.Light(bgColor, 0.2f);
            x += width + 6;
            return btn;
        }

        private Panel CreateSettingsCard(string title, ref int y, int height)
        {
            var card = new Panel
            {
                Location = new Point(10, y),
                Size = new Size(600, height),
                BackColor = CARD
            };
            var lbl = new Label
            {
                Text = title,
                Location = new Point(15, 8),
                AutoSize = true,
                ForeColor = TEXT,
                Font = new Font("Segoe UI Semibold", 11)
            };
            card.Controls.Add(lbl);
            y += height + 12;
            return card;
        }

        private void AddSettingsLabel(Panel card, string text, int x, int y)
        {
            var lbl = new Label
            {
                Text = text,
                Location = new Point(x, y),
                AutoSize = true,
                ForeColor = DIM,
                Font = new Font("Segoe UI", 9.5f)
            };
            card.Controls.Add(lbl);
        }
    }

    // ======================== DATA MODELS ========================

    public class AccountInfo
    {
        public string name { get; set; } = "";
        public string username { get; set; } = "";
        public string password { get; set; } = "";
        public int server { get; set; } = 0;
        public int character { get; set; } = 0;
        public bool autoQuest { get; set; } = true;
        public bool headless { get; set; } = true;
        public bool running { get; set; } = false;
    }

    public class AppSettings
    {
        public int windowWidth { get; set; } = 0;
        public int windowHeight { get; set; } = 0;
        public bool disableShadows { get; set; } = true;
        public bool disableParticles { get; set; } = true;
        public bool lowQuality { get; set; } = true;
        public int targetFps { get; set; } = 10;
        public bool minimizeOnLaunch { get; set; } = false;
        public bool autoCleanRAM { get; set; } = true;
        public bool ultraLowRes { get; set; } = true;
        public bool autoQuest { get; set; } = true;
        public bool smartDialog { get; set; } = true;
        public bool collectQuest { get; set; } = true;
        public bool autoPathfind { get; set; } = true;
        public int gameSpeed { get; set; } = 1;
    }

    public class AccountConfig
    {
        public List<AccountInfo> accounts { get; set; } = new();
        public int currentAccountIndex { get; set; } = 0;
        public AppSettings settings { get; set; } = new();
    }

    // ======================== ACCOUNT DIALOG ========================

    public class AccountDialog : Form
    {
        public AccountInfo Account { get; private set; }

        private readonly Color BG = Color.FromArgb(24, 24, 32);
        private readonly Color CARD = Color.FromArgb(32, 32, 44);
        private readonly Color TEXT = Color.FromArgb(220, 220, 230);
        private readonly Color DIM = Color.FromArgb(140, 140, 160);
        private readonly Color ACCENT = Color.FromArgb(99, 102, 241);

        public AccountDialog(AccountInfo existing = null)
        {
            Account = existing != null
                ? new AccountInfo
                {
                    name = existing.name, username = existing.username,
                    password = existing.password, server = existing.server,
                    character = existing.character, autoQuest = existing.autoQuest,
                    headless = existing.headless
                }
                : new AccountInfo();

            BuildDialogUI(existing != null);
        }

        private void BuildDialogUI(bool isEdit)
        {
            Text = isEdit ? "Sửa tài khoản" : "Thêm tài khoản";
            Size = new Size(400, 420);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = BG;
            ForeColor = TEXT;

            int y = 20;

            AddLabel("Tên hiển thị:", y);
            var txtName = AddTextBox(Account.name, y);
            y += 50;

            AddLabel("Username:", y);
            var txtUser = AddTextBox(Account.username, y);
            y += 50;

            AddLabel("Password:", y);
            var txtPass = AddTextBox(Account.password, y);
            txtPass.UseSystemPasswordChar = true;
            y += 50;

            AddLabel("Server:", y);
            var nudServer = new NumericUpDown
            {
                Location = new Point(130, y),
                Size = new Size(220, 28),
                Minimum = 0,
                Maximum = 99,
                Value = Account.server,
                BackColor = CARD,
                ForeColor = TEXT
            };
            Controls.Add(nudServer);
            y += 50;

            AddLabel("Character:", y);
            var nudChar = new NumericUpDown
            {
                Location = new Point(130, y),
                Size = new Size(220, 28),
                Minimum = 0,
                Maximum = 10,
                Value = Account.character,
                BackColor = CARD,
                ForeColor = TEXT
            };
            Controls.Add(nudChar);
            y += 50;

            var chkAuto = new CheckBox
            {
                Text = "Auto Quest",
                Location = new Point(20, y),
                Size = new Size(150, 25),
                ForeColor = TEXT,
                Checked = Account.autoQuest
            };
            Controls.Add(chkAuto);

            var chkHeadless = new CheckBox
            {
                Text = "Headless Mode",
                Location = new Point(180, y),
                Size = new Size(150, 25),
                ForeColor = TEXT,
                Checked = Account.headless
            };
            Controls.Add(chkHeadless);
            y += 40;

            var btnOk = new Button
            {
                Text = isEdit ? "💾 Cập nhật" : "＋ Thêm",
                Location = new Point(130, y),
                Size = new Size(120, 38),
                FlatStyle = FlatStyle.Flat,
                BackColor = ACCENT,
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 10),
                Cursor = Cursors.Hand,
                DialogResult = DialogResult.OK
            };
            btnOk.FlatAppearance.BorderSize = 0;
            btnOk.Click += (s, e) =>
            {
                Account.name = txtName.Text;
                Account.username = txtUser.Text;
                Account.password = txtPass.Text;
                Account.server = (int)nudServer.Value;
                Account.character = (int)nudChar.Value;
                Account.autoQuest = chkAuto.Checked;
                Account.headless = chkHeadless.Checked;
            };
            Controls.Add(btnOk);

            var btnCancel = new Button
            {
                Text = "Hủy",
                Location = new Point(260, y),
                Size = new Size(80, 38),
                FlatStyle = FlatStyle.Flat,
                BackColor = CARD,
                ForeColor = DIM,
                Font = new Font("Segoe UI", 10),
                Cursor = Cursors.Hand,
                DialogResult = DialogResult.Cancel
            };
            btnCancel.FlatAppearance.BorderSize = 0;
            Controls.Add(btnCancel);

            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }

        private void AddLabel(string text, int y)
        {
            var lbl = new Label
            {
                Text = text,
                Location = new Point(20, y + 4),
                AutoSize = true,
                ForeColor = DIM,
                Font = new Font("Segoe UI", 9.5f)
            };
            Controls.Add(lbl);
        }

        private TextBox AddTextBox(string value, int y)
        {
            var txt = new TextBox
            {
                Text = value,
                Location = new Point(130, y),
                Size = new Size(220, 28),
                BackColor = CARD,
                ForeColor = TEXT,
                Font = new Font("Segoe UI", 10),
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(txt);
            return txt;
        }
    }
}
