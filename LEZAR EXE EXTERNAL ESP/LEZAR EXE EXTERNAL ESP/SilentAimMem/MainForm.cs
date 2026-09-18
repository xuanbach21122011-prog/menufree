using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace SilentAimMem
{


    /// <summary>
    /// ////////    Discord : https://discord.gg/NyxGKqMrKy  
    /// </summary>


    //////   Lezar Exe  



    public class MainForm : Form
    {
        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        private BlueStacks bluestacks;
        private OverlayForm overlay;

        private Panel pnlHeader;
        private Panel pnlLogo;
        private Panel pnlStatus;
        private Panel pnlContainer;
        private Panel pnlScrollContent;
        private NeonScrollBar neonScroll;
        private Panel pnlLogFloating;
        private Button btnLogToggle;

        private TextBox txtProcessName, txtPackageName;
        private Button btnConnect, btnOverlay;
        private Label lblStatus, lblAttempt;
        private RichTextBox rtbLog;

        private Bitmap _cachedBackground;
        private Bitmap _cachedHeader;

        private readonly Font fontRegular = new Font("Segoe UI", 9f);
        private readonly Font fontBold = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        private readonly Font fontTitle = new Font("Segoe UI", 14f, FontStyle.Bold);
        private readonly Font fontSub = new Font("Segoe UI", 8f, FontStyle.Bold);
        private readonly Font fontTiny = new Font("Segoe UI", 7f, FontStyle.Bold);
        private readonly Font fontMono = new Font("Consolas", 8.5f);
        private readonly Font fontStatus = new Font("Segoe UI", 7.5f, FontStyle.Bold);

        private readonly Color bgDeep = Color.FromArgb(8, 10, 18);
        private readonly Color bgDark = Color.FromArgb(15, 18, 28);
        private readonly Color cardBg = Color.FromArgb(22, 26, 38);
        private readonly Color cardBgLight = Color.FromArgb(28, 33, 48);
        private readonly Color inputBg = Color.FromArgb(30, 36, 52);
        private readonly Color neonCyan = Color.FromArgb(0, 230, 255);
        private readonly Color neonPurple = Color.FromArgb(150, 80, 255);
        private readonly Color neonPink = Color.FromArgb(255, 60, 150);
        private readonly Color neonGreen = Color.FromArgb(0, 255, 160);
        private readonly Color textMain = Color.FromArgb(235, 240, 255);
        private readonly Color textDim = Color.FromArgb(120, 130, 155);
        private readonly Color borderGlow = Color.FromArgb(45, 55, 80);

        private System.Windows.Forms.Timer animTimer;
        private float pulsePhase = 0f;
        private float borderTravel = 0f;
        private float pulseSin = 0.5f;

        // Layout constants
        private const int SCROLLBAR_WIDTH = 6;   // slim neon bar
        private const int SCROLLBAR_GAP = 4;     // gap between content and bar
        private const int FORM_WIDTH = 400;
        private const int FORM_HEIGHT = 700;

        public MainForm()
        {
            bluestacks = new BlueStacks();
            InitializeWindow();
            BuildUI();
            SetupLogRedirect();
            StartPulseAnimation();
        }

        private void StartPulseAnimation()
        {
            animTimer = new System.Windows.Forms.Timer { Interval = 40 };
            animTimer.Tick += (s, e) =>
            {
                pulsePhase += 0.13f;
                if (pulsePhase > (float)(Math.PI * 2)) pulsePhase -= (float)(Math.PI * 2);
                pulseSin = (float)((Math.Sin(pulsePhase) + 1) / 2);

                borderTravel += 0.015f;
                if (borderTravel > 1f) borderTravel -= 1f;

                pnlHeader?.Invalidate(new Rectangle(0, 0, pnlHeader.Width, 3));
                pnlLogo?.Invalidate();
                pnlStatus?.Invalidate(new Rectangle(0, 0, 20, pnlStatus.Height));
                neonScroll?.Invalidate();
            };
            animTimer.Start();
        }

        private void SetupLogRedirect()
        {
            if (rtbLog != null)
            {
                var writer = new LogWriter(rtbLog);
                Console.SetOut(writer);
                Console.SetError(writer);
            }
        }

        private class LogWriter : TextWriter
        {
            private readonly RichTextBox box;
            public LogWriter(RichTextBox rtb) { box = rtb; }
            public override void Write(char[] buffer, int index, int count) => Write(new string(buffer, index, count));
            public override void Write(string value)
            {
                if (box != null && box.IsHandleCreated)
                {
                    try
                    {
                        box.BeginInvoke((MethodInvoker)delegate
                        {
                            box.AppendText(value);
                            box.ScrollToCaret();
                        });
                    }
                    catch { }
                }
            }
            public override void WriteLine(string value) => Write(value + "\n");
            public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
        }

        private void InitializeWindow()
        {
            Text = "LEZAR EXE";
            Size = new Size(FORM_WIDTH, FORM_HEIGHT);
            MinimumSize = new Size(FORM_WIDTH, FORM_HEIGHT);
            MaximumSize = new Size(FORM_WIDTH, FORM_HEIGHT);
            FormBorderStyle = FormBorderStyle.None;
            BackColor = bgDeep;
            ForeColor = textMain;
            StartPosition = FormStartPosition.CenterScreen;

            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint, true);

            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };

            Paint += (s, e) =>
            {
                if (_cachedBackground == null || _cachedBackground.Size != ClientSize)
                {
                    _cachedBackground?.Dispose();
                    _cachedBackground = new Bitmap(ClientSize.Width, ClientSize.Height);
                    using (Graphics g = Graphics.FromImage(_cachedBackground))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        using (LinearGradientBrush bg = new LinearGradientBrush(
                            ClientRectangle, bgDeep, Color.FromArgb(20, 15, 35), 135f))
                        {
                            g.FillRectangle(bg, ClientRectangle);
                        }
                        DrawGlowOrb(g, new Point(380, 80), 200, Color.FromArgb(35, neonPurple));
                        DrawGlowOrb(g, new Point(20, 620), 240, Color.FromArgb(28, neonCyan));
                    }
                }

                e.Graphics.DrawImageUnscaled(_cachedBackground, 0, 0);

                using (Pen p = new Pen(Color.FromArgb(80, neonCyan), 1.5f))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
                }
            };
        }

        private void DrawGlowOrb(Graphics g, Point center, int radius, Color color)
        {
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(center.X - radius / 2, center.Y - radius / 2, radius, radius);
                using (PathGradientBrush pgb = new PathGradientBrush(path))
                {
                    pgb.CenterColor = color;
                    pgb.SurroundColors = new[] { Color.Transparent };
                    g.FillPath(pgb, path);
                }
            }
        }

        private void EnableDrag(Control ctrl)
        {
            ctrl.MouseDown += DragWindow;
            foreach (Control child in ctrl.Controls)
                EnableDrag(child);
        }

        private void DragWindow(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, 0xA1, 0x2, 0);
            }
        }

        private void EnableDoubleBuffer(Control ctrl)
        {
            typeof(Control).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(ctrl, true, null);
        }

        private void BuildUI()
        {
            // ===== HEADER =====
            pnlHeader = new Panel
            {
                Location = new Point(14, 14),
                Size = new Size(372, 92),
                BackColor = Color.Transparent
            };
            EnableDoubleBuffer(pnlHeader);

            pnlHeader.Paint += (s, e) =>
            {
                if (_cachedHeader == null || _cachedHeader.Size != pnlHeader.Size)
                {
                    _cachedHeader?.Dispose();
                    _cachedHeader = new Bitmap(pnlHeader.Width, pnlHeader.Height);
                    using (Graphics g = Graphics.FromImage(_cachedHeader))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        using (GraphicsPath path = GetRoundedRect(new Rectangle(0, 0, pnlHeader.Width - 1, pnlHeader.Height - 1), 14))
                        {
                            using (LinearGradientBrush brush = new LinearGradientBrush(
                                pnlHeader.ClientRectangle, Color.FromArgb(240, cardBg), Color.FromArgb(215, cardBgLight), 90f))
                            {
                                g.FillPath(brush, path);
                            }
                            using (Pen pen = new Pen(Color.FromArgb(120, neonCyan), 1.2f))
                            {
                                g.DrawPath(pen, path);
                            }
                        }
                    }
                }

                e.Graphics.DrawImageUnscaled(_cachedHeader, 0, 0);

                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                int travelW = 100;
                int travelX = (int)(borderTravel * (pnlHeader.Width + travelW)) - travelW;
                using (LinearGradientBrush lb = new LinearGradientBrush(
                    new Rectangle(travelX, 0, travelW, 3),
                    Color.Transparent, neonCyan, 0f))
                {
                    lb.InterpolationColors = new ColorBlend
                    {
                        Colors = new[] { Color.Transparent, Color.FromArgb(255, neonCyan), Color.Transparent },
                        Positions = new[] { 0f, 0.5f, 1f }
                    };
                    e.Graphics.FillRectangle(lb, new Rectangle(travelX, 0, travelW, 3));
                }
            };
            pnlHeader.MouseDown += DragWindow;

            pnlLogo = new Panel
            {
                Location = new Point(16, 20),
                Size = new Size(52, 52),
                BackColor = Color.Transparent,
                Cursor = Cursors.SizeAll
            };
            EnableDoubleBuffer(pnlLogo);
            pnlLogo.Paint += (s, e) => DrawCleanLogo(e.Graphics, pnlLogo.Width, pnlLogo.Height);
            pnlHeader.Controls.Add(pnlLogo);

            var lblTitle = new Label
            {
                Text = "LEZAR EXE",
                Location = new Point(80, 22),
                Size = new Size(220, 26),
                Font = fontTitle,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Cursor = Cursors.SizeAll
            };
            pnlHeader.Controls.Add(lblTitle);

            var lblSubTitle = new Label
            {
                Text = "ESP   AIMBOT   AI",
                Location = new Point(82, 50),
                Size = new Size(220, 16),
                Font = fontTiny,
                ForeColor = neonCyan,
                BackColor = Color.Transparent,
                Cursor = Cursors.SizeAll
            };
            pnlHeader.Controls.Add(lblSubTitle);

            pnlStatus = new Panel
            {
                Location = new Point(240, 60),
                Size = new Size(120, 20),
                BackColor = Color.Transparent,
                Cursor = Cursors.SizeAll
            };
            EnableDoubleBuffer(pnlStatus);
            pnlStatus.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                int alpha = (int)(pulseSin * 155 + 100);
                int dotSize = 8;
                using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, neonGreen)))
                    e.Graphics.FillEllipse(b, 2, 6, dotSize, dotSize);
                using (SolidBrush tb = new SolidBrush(neonGreen))
                    e.Graphics.DrawString("STANDBY", fontStatus, tb, 16, 3);
            };
            pnlHeader.Controls.Add(pnlStatus);

            var btnClose = new Label
            {
                Text = "✕",
                Location = new Point(344, 10),
                Size = new Size(22, 22),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = textDim,
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand,
                BackColor = Color.Transparent
            };
            btnClose.Click += (s, e) => Close();
            btnClose.MouseEnter += (s, e) => btnClose.ForeColor = neonPink;
            btnClose.MouseLeave += (s, e) => btnClose.ForeColor = textDim;
            pnlHeader.Controls.Add(btnClose);

            EnableDrag(pnlHeader);
            btnClose.MouseDown -= DragWindow;

            Controls.Add(pnlHeader);

            // ===== CONTAINER (no native scroll, custom neon scrollbar) =====
            int containerX = 14;
            int containerY = 116;
            int containerW = 372;
            int containerH = 544;

            pnlContainer = new Panel
            {
                Location = new Point(containerX, containerY),
                Size = new Size(containerW, containerH),
                BackColor = Color.Transparent,
                AutoScroll = false   // ✅ No native scroll
            };
            EnableDoubleBuffer(pnlContainer);

            // Content width = container width - scrollbar area
            int contentWidth = containerW - SCROLLBAR_WIDTH - SCROLLBAR_GAP;

            pnlScrollContent = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(contentWidth, containerH),
                BackColor = Color.Transparent
            };
            EnableDoubleBuffer(pnlScrollContent);
            pnlContainer.Controls.Add(pnlScrollContent);

            // Custom neon scrollbar
            neonScroll = new NeonScrollBar
            {
                Location = new Point(containerW - SCROLLBAR_WIDTH, 0),
                Size = new Size(SCROLLBAR_WIDTH, containerH),
                Visible = false
            };
            neonScroll.ValueChanged += (s, e) =>
            {
                pnlScrollContent.Location = new Point(0, -neonScroll.Value);
            };
            pnlContainer.Controls.Add(neonScroll);

            // Mouse wheel — manual
            pnlContainer.MouseWheel += (s, e) => HandleMouseWheel(e.Delta);
            pnlScrollContent.MouseWheel += (s, e) => HandleMouseWheel(e.Delta);
            EnableMouseWheelRecursive(pnlScrollContent);

            Controls.Add(pnlContainer);

            // ===== FLOATING LOG BUTTON =====
            btnLogToggle = new Button
            {
                Text = "◈",
                Location = new Point(346, 620),
                Size = new Size(40, 40),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                ForeColor = neonGreen,
                BackColor = Color.FromArgb(25, 30, 45),
                Cursor = Cursors.Hand
            };
            btnLogToggle.FlatAppearance.BorderSize = 1;
            btnLogToggle.FlatAppearance.BorderColor = neonGreen;
            btnLogToggle.Click += (s, e) => ToggleFloatingLog();
            Controls.Add(btnLogToggle);

            // ===== FLOATING LOG PANEL =====
            pnlLogFloating = new Panel
            {
                Location = new Point(14, 116),
                Size = new Size(372, 544),
                BackColor = Color.FromArgb(240, 8, 10, 16),
                Visible = false
            };
            EnableDoubleBuffer(pnlLogFloating);
            pnlLogFloating.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = GetRoundedRect(new Rectangle(0, 0, pnlLogFloating.Width - 1, pnlLogFloating.Height - 1), 12))
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(245, 12, 14, 22)))
                        e.Graphics.FillPath(b, path);
                    using (Pen p = new Pen(Color.FromArgb(150, neonGreen), 1f))
                        e.Graphics.DrawPath(p, path);
                }
            };

            var lblLogTitle = new Label
            {
                Text = "◈  CONSOLE LOGS",
                Location = new Point(18, 14),
                Size = new Size(200, 20),
                Font = fontSub,
                ForeColor = neonGreen,
                BackColor = Color.Transparent
            };
            pnlLogFloating.Controls.Add(lblLogTitle);

            var btnLogClose = new Label
            {
                Text = "✕",
                Location = new Point(340, 12),
                Size = new Size(22, 22),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = textDim,
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand,
                BackColor = Color.Transparent
            };
            btnLogClose.Click += (s, e) => ToggleFloatingLog();
            btnLogClose.MouseEnter += (s, e) => btnLogClose.ForeColor = neonPink;
            btnLogClose.MouseLeave += (s, e) => btnLogClose.ForeColor = textDim;
            pnlLogFloating.Controls.Add(btnLogClose);

            rtbLog = new RichTextBox
            {
                Location = new Point(14, 44),
                Size = new Size(344, 486),
                BackColor = Color.FromArgb(8, 10, 16),
                ForeColor = neonGreen,
                Font = fontMono,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                ScrollBars = RichTextBoxScrollBars.Vertical
            };
            pnlLogFloating.Controls.Add(rtbLog);
            Controls.Add(pnlLogFloating);

            // ===== FOOTER =====
            var pnlFooter = new Panel
            {
                Location = new Point(14, 666),
                Size = new Size(372, 22),
                BackColor = Color.Transparent
            };
            var lblVer = new Label
            {
                Text = "◈ v3.2.1 LEZAR EXE",
                Location = new Point(4, 3),
                Size = new Size(180, 16),
                Font = fontTiny,
                ForeColor = textDim
            };
            var lblEngine = new Label
            {
                Text = "●  IL2CPP ENGINE",
                Location = new Point(220, 3),
                Size = new Size(148, 16),
                Font = fontTiny,
                ForeColor = neonGreen,
                TextAlign = ContentAlignment.MiddleRight
            };
            pnlFooter.Controls.Add(lblVer);
            pnlFooter.Controls.Add(lblEngine);
            Controls.Add(pnlFooter);

            LoadMainTabPage();
            SetupLogRedirect();
        }

        // Enable mouse wheel events on all children recursively
        private void EnableMouseWheelRecursive(Control ctrl)
        {
            ctrl.MouseWheel += (s, e) => HandleMouseWheel(e.Delta);
            foreach (Control child in ctrl.Controls)
                EnableMouseWheelRecursive(child);
        }

        private void HandleMouseWheel(int delta)
        {
            if (neonScroll == null || !neonScroll.Visible) return;
            int newVal = neonScroll.Value - delta / 3;
            int max = neonScroll.Maximum - neonScroll.LargeChange;
            if (max < 0) max = 0;
            if (newVal < 0) newVal = 0;
            if (newVal > max) newVal = max;
            neonScroll.Value = newVal;
        }

        private void DrawCleanLogo(Graphics g, int w, int h)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int cx = w / 2;
            int cy = h / 2;
            int size = 36;

            Point[] diamond = {
                new Point(cx, cy - size / 2),
                new Point(cx + size / 2, cy),
                new Point(cx, cy + size / 2),
                new Point(cx - size / 2, cy)
            };

            int glowAlpha = (int)(pulseSin * 60 + 40);
            using (SolidBrush gb = new SolidBrush(Color.FromArgb(glowAlpha, neonCyan)))
            {
                g.FillEllipse(gb, cx - 24, cy - 24, 48, 48);
            }

            using (Pen borderPen = new Pen(neonCyan, 2.5f))
            {
                borderPen.LineJoin = LineJoin.Round;
                g.DrawPolygon(borderPen, diamond);
            }

            Point[] innerDiamond = {
                new Point(cx, cy - 12),
                new Point(cx + 12, cy),
                new Point(cx, cy + 12),
                new Point(cx - 12, cy)
            };
            using (LinearGradientBrush b = new LinearGradientBrush(
                new Rectangle(0, 0, w, h), neonCyan, neonPurple, 45f))
            {
                g.FillPolygon(b, innerDiamond);
            }

            Point[] bolt = {
                new Point(cx + 2, cy - 8),
                new Point(cx - 5, cy + 2),
                new Point(cx, cy + 2),
                new Point(cx - 2, cy + 9),
                new Point(cx + 6, cy - 1),
                new Point(cx + 1, cy - 1)
            };
            using (SolidBrush sb = new SolidBrush(Color.White))
            {
                g.FillPolygon(sb, bolt);
            }
        }

        private void ToggleFloatingLog()
        {
            pnlLogFloating.Visible = !pnlLogFloating.Visible;
            if (pnlLogFloating.Visible) pnlLogFloating.BringToFront();
        }

        private void UpdateScrollBar()
        {
            int contentHeight = 0;
            foreach (Control c in pnlScrollContent.Controls)
                contentHeight = Math.Max(contentHeight, c.Bottom + 10);

            int viewportHeight = pnlContainer.Height;
            int finalHeight = Math.Max(viewportHeight, contentHeight);
            pnlScrollContent.Size = new Size(pnlScrollContent.Width, finalHeight);

            // Configure neon scrollbar
            if (contentHeight > viewportHeight)
            {
                neonScroll.Visible = true;
                neonScroll.Minimum = 0;
                neonScroll.Maximum = contentHeight;
                neonScroll.LargeChange = viewportHeight;
                neonScroll.SmallChange = 40;
                int max = neonScroll.Maximum - neonScroll.LargeChange;
                if (neonScroll.Value > max) neonScroll.Value = Math.Max(0, max);
            }
            else
            {
                neonScroll.Visible = false;
                neonScroll.Value = 0;
                pnlScrollContent.Location = new Point(0, 0);
            }
            neonScroll.Invalidate();
        }

        private void LoadMainTabPage()
        {
            pnlScrollContent.SuspendLayout();
            try
            {
                int yPos = 5;

                // 1. CONNECTION
                yPos = AddCardSection(pnlScrollContent, yPos, "🔗  CONNECTION", out Panel pnlConn, 235);
                AddInputRow(pnlConn, 42, "PROCESS", out txtProcessName, "HD-Player.exe");
                AddInputRow(pnlConn, 82, "PACKAGE", out txtPackageName, "com.dts.freefireth");

                btnConnect = new Button
                {
                    Text = "⚡  CONNECT",
                    Location = new Point(14, 122),
                    Size = new Size(324, 38),
                    BackColor = neonCyan,
                    ForeColor = Color.Black,
                    FlatStyle = FlatStyle.Flat,
                    Font = fontBold,
                    Cursor = Cursors.Hand
                };
                btnConnect.FlatAppearance.BorderSize = 0;
                btnConnect.Paint += (s, e) =>
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    using (GraphicsPath path = GetRoundedRect(new Rectangle(0, 0, btnConnect.Width - 1, btnConnect.Height - 1), 8))
                    {
                        using (LinearGradientBrush lb = new LinearGradientBrush(
                            btnConnect.ClientRectangle, neonCyan, neonPurple, 45f))
                        {
                            e.Graphics.FillPath(lb, path);
                        }
                    }
                    TextRenderer.DrawText(e.Graphics, btnConnect.Text, btnConnect.Font,
                        btnConnect.ClientRectangle, btnConnect.ForeColor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                };
                btnConnect.Click += BtnConnect_Click;
                pnlConn.Controls.Add(btnConnect);

                lblStatus = new Label
                {
                    Text = "● Ready",
                    Location = new Point(14, 168),
                    Size = new Size(160, 16),
                    ForeColor = Color.FromArgb(255, 200, 60),
                    Font = fontSub,
                    BackColor = Color.Transparent
                };
                pnlConn.Controls.Add(lblStatus);

                lblAttempt = new Label
                {
                    Text = "",
                    Location = new Point(180, 168),
                    Size = new Size(158, 16),
                    ForeColor = textDim,
                    Font = fontSub,
                    TextAlign = ContentAlignment.MiddleRight,
                    BackColor = Color.Transparent
                };
                pnlConn.Controls.Add(lblAttempt);

                btnOverlay = new Button
                {
                    Text = "⊙  Toggle Overlay",
                    Location = new Point(14, 192),
                    Size = new Size(324, 32),
                    BackColor = inputBg,
                    ForeColor = textDim,
                    FlatStyle = FlatStyle.Flat,
                    Font = fontRegular,
                    Enabled = false,
                    Cursor = Cursors.Hand
                };
                btnOverlay.FlatAppearance.BorderSize = 1;
                btnOverlay.FlatAppearance.BorderColor = borderGlow;
                btnOverlay.Click += BtnOverlay_Click;
                pnlConn.Controls.Add(btnOverlay);
                yPos += 6;

                // 2. AIMBOT
                yPos = AddCardSection(pnlScrollContent, yPos, "🎯  AIMBOT", out Panel pnlAim, 280);
                int cy = 42;
                AddSlimRow(pnlAim, cy, "Enable Aim Assist", Config.enableAimBot, v => Config.enableAimBot = v); cy += 38;
                AddSlimRow(pnlAim, cy, "AI Assist (Visible)", Config.AimbotVisible, v => Config.AimbotVisible = v); cy += 38;
                AddSlimRow(pnlAim, cy, "No Recoil", Config.NoRecoil, v => Config.NoRecoil = v); cy += 38;
                AddSlimRow(pnlAim, cy, "FOV Circle", Config.FOV, v => Config.FOV = v); cy += 46;

                // FOV RADIUS slider — properly spaced
                var lblFov = new Label
                {
                    Text = "FOV RADIUS",
                    Location = new Point(18, cy + 4),
                    Size = new Size(100, 18),
                    Font = fontSub,
                    ForeColor = textDim,
                    BackColor = Color.Transparent
                };
                var sliderFov = new CustomSlider
                {
                    Location = new Point(105, cy + 2),
                    Size = new Size(170, 22),
                    Minimum = 1,
                    Maximum = 500,
                    Value = (int)Config.Fov,
                    AccentColor = neonCyan
                };
                var lblFovValue = new Label
                {
                    Text = ((int)Config.Fov).ToString(),
                    Location = new Point(285, cy + 4),
                    Size = new Size(50, 18),
                    Font = fontSub,
                    ForeColor = neonCyan,
                    TextAlign = ContentAlignment.MiddleRight,
                    BackColor = Color.Transparent
                };
                sliderFov.ValueChanged += (s, e) =>
                {
                    Config.Fov = sliderFov.Value;
                    lblFovValue.Text = sliderFov.Value.ToString();
                };
                pnlAim.Controls.Add(lblFov);
                pnlAim.Controls.Add(sliderFov);
                pnlAim.Controls.Add(lblFovValue);
                pnlAim.Height = cy + 42;
                yPos += 6;

                // 3. ESP VISUALS
                yPos = AddCardSection(pnlScrollContent, yPos, "👁  ESP VISUALS", out Panel pnlEsp, 370);
                cy = 42;
                AddSlimRow(pnlEsp, cy, "Box ESP", Config.ESPBox, v => Config.ESPBox = v); cy += 36;
                AddSlimRow(pnlEsp, cy, "Cornered Box", Config.ESPCorneredBox, v => Config.ESPCorneredBox = v); cy += 36;
                AddSlimRow(pnlEsp, cy, "Snap Lines", Config.ESPLine, v => Config.ESPLine = v); cy += 36;
                AddSlimRow(pnlEsp, cy, "Snapline", Config.Snapline, v => Config.Snapline = v); cy += 36;
                AddSlimRow(pnlEsp, cy, "Player Name", Config.ESPName, v => Config.ESPName = v); cy += 36;
                AddSlimRow(pnlEsp, cy, "Health Bar", Config.ESPHealth, v => Config.ESPHealth = v); cy += 36;
                AddSlimRow(pnlEsp, cy, "Distance", Config.ESPDistance, v => Config.ESPDistance = v); cy += 36;
                AddSlimRow(pnlEsp, cy, "Crosshair", Config.Crosshair, v => Config.Crosshair = v); cy += 36;
                //AddSlimRow(pnlEsp, cy, "FOV Circle", Config.FOV, v => Config.FOV = v);
                pnlEsp.Height = cy + 24;

                UpdateScrollBar();
            }
            finally
            {
                pnlScrollContent.ResumeLayout(false);
            }
        }

        private int AddCardSection(Control parent, int y, string title, out Panel cardPanel, int height)
        {
            // Card width = content width (no horizontal overflow)
            int cardWidth = parent.Width;

            var localPanel = new Panel
            {
                Location = new Point(0, y),
                Size = new Size(cardWidth, height),
                BackColor = Color.Transparent
            };
            EnableDoubleBuffer(localPanel);

            Bitmap cardCache = null;
            int cachedW = 0, cachedH = 0;
            localPanel.Paint += (s, e) =>
            {
                if (cardCache == null || cachedW != localPanel.Width || cachedH != localPanel.Height)
                {
                    cardCache?.Dispose();
                    cachedW = localPanel.Width;
                    cachedH = localPanel.Height;
                    cardCache = new Bitmap(cachedW, cachedH);
                    using (Graphics g = Graphics.FromImage(cardCache))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        using (GraphicsPath path = GetRoundedRect(new Rectangle(0, 0, cachedW - 1, cachedH - 1), 12))
                        {
                            using (LinearGradientBrush brush = new LinearGradientBrush(
                                new Rectangle(0, 0, cachedW, cachedH),
                                Color.FromArgb(245, cardBg), Color.FromArgb(235, cardBgLight), 135f))
                            {
                                g.FillPath(brush, path);
                            }
                            using (Pen pen = new Pen(Color.FromArgb(55, borderGlow), 1f))
                            {
                                g.DrawPath(pen, path);
                            }
                            using (LinearGradientBrush lb = new LinearGradientBrush(
                                new Rectangle(0, 12, 3, cachedH - 24), neonCyan, neonPurple, 90f))
                            {
                                using (GraphicsPath accentPath = GetRoundedRect(
                                    new Rectangle(0, 12, 3, cachedH - 24), 2))
                                {
                                    g.FillPath(lb, accentPath);
                                }
                            }
                        }
                    }
                }
                e.Graphics.DrawImageUnscaled(cardCache, 0, 0);
            };

            var lbl = new Label
            {
                Text = title,
                Location = new Point(18, 12),
                Size = new Size(cardWidth - 30, 20),
                Font = fontSub,
                ForeColor = neonCyan,
                BackColor = Color.Transparent
            };
            localPanel.Controls.Add(lbl);
            parent.Controls.Add(localPanel);

            cardPanel = localPanel;
            return y + height + 10;
        }

        private void AddInputRow(Panel parent, int y, string labelText, out TextBox txtBox, string defaultVal)
        {
            var lbl = new Label
            {
                Text = labelText,
                Location = new Point(14, y + 4),
                Size = new Size(80, 18),
                Font = fontTiny,
                ForeColor = textDim,
                BackColor = Color.Transparent
            };
            int inputWidth = parent.Width - 96 - 14;
            var container = new Panel
            {
                Location = new Point(96, y),
                Size = new Size(inputWidth, 28),
                BackColor = inputBg
            };
            EnableDoubleBuffer(container);
            container.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = GetRoundedRect(new Rectangle(0, 0, container.Width - 1, container.Height - 1), 6))
                {
                    using (Pen p = new Pen(borderGlow, 1f))
                        e.Graphics.DrawPath(p, path);
                }
            };
            txtBox = new TextBox
            {
                Text = defaultVal,
                Location = new Point(8, 4),
                Size = new Size(inputWidth - 16, 22),
                BackColor = inputBg,
                ForeColor = Color.White,
                Font = fontRegular,
                BorderStyle = BorderStyle.None
            };
            container.Controls.Add(txtBox);
            parent.Controls.Add(lbl);
            parent.Controls.Add(container);
        }

        private void AddSlimRow(Panel parent, int y, string labelText, bool initialValue, Action<bool> onChanged)
        {
            var lbl = new Label
            {
                Text = labelText,
                Location = new Point(18, y + 6),
                Size = new Size(parent.Width - 80, 20),
                Font = fontRegular,
                ForeColor = textMain,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };

            bool isChecked = initialValue;
            float animProgress = isChecked ? 1f : 0f;
            System.Windows.Forms.Timer animTimer2 = null;

            var pnlSwitch = new Panel
            {
                Location = new Point(parent.Width - 58, y + 4),
                Size = new Size(40, 20),
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            EnableDoubleBuffer(pnlSwitch);

            Action toggle = () =>
            {
                isChecked = !isChecked;
                onChanged(isChecked);

                animTimer2?.Stop();
                animTimer2 = new System.Windows.Forms.Timer { Interval = 15 };
                float target = isChecked ? 1f : 0f;
                animTimer2.Tick += (ts, te) =>
                {
                    float diff = target - animProgress;
                    if (Math.Abs(diff) < 0.05f)
                    {
                        animProgress = target;
                        animTimer2.Stop();
                    }
                    else
                    {
                        animProgress += diff * 0.3f;
                    }
                    pnlSwitch.Invalidate();
                };
                animTimer2.Start();
            };

            pnlSwitch.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Color trackColor = LerpColor(Color.FromArgb(45, 52, 70), neonCyan, animProgress);
                using (GraphicsPath path = GetRoundedRect(new Rectangle(0, 0, pnlSwitch.Width - 1, pnlSwitch.Height - 1), 10))
                {
                    using (SolidBrush b = new SolidBrush(trackColor))
                        e.Graphics.FillPath(b, path);
                    if (isChecked)
                    {
                        using (Pen p = new Pen(Color.FromArgb(150, Color.White), 1f))
                            e.Graphics.DrawPath(p, path);
                    }
                }
                int thumbX = (int)(2 + animProgress * 20);
                int thumbSize = 14;

                using (SolidBrush thumbBrush = new SolidBrush(Color.White))
                    e.Graphics.FillEllipse(thumbBrush, thumbX, 3, thumbSize, thumbSize);
            };

            pnlSwitch.Click += (s, e) => toggle();
            lbl.Click += (s, e) => toggle();

            parent.Controls.Add(lbl);
            parent.Controls.Add(pnlSwitch);
        }

        private Color LerpColor(Color a, Color b, float t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        private GraphicsPath GetRoundedRect(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            if (d <= 0) d = 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.X + rect.Width - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.X + rect.Width - d, rect.Y + rect.Height - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Y + rect.Height - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void BtnConnect_Click(object sender, EventArgs e)
        {
            string packageName = txtPackageName.Text.Trim();
            if (string.IsNullOrEmpty(packageName)) return;

            btnConnect.Enabled = false;
            btnConnect.Text = "CONNECTING...";
            lblStatus.Text = "● Connecting...";
            lblStatus.ForeColor = Color.Yellow;

            new Thread(() =>
            {
                try
                {
                    bool ok = bluestacks.Setup(packageName, false, (attempt, total) =>
                    {
                        this.Invoke((MethodInvoker)delegate
                        {
                            lblAttempt.Text = $"Attempt {attempt}/{total}";
                            lblStatus.Text = $"● Searching... ({attempt}/{total})";
                        });
                    });

                    if (ok)
                    {
                        ulong il2cpp = bluestacks.GetModuleBase("libil2cpp.so", 1);
                        if (il2cpp != 0)
                        {
                            Offsets.Il2CppBase = il2cpp;
                            new Thread(Data.Work) { IsBackground = true }.Start();
                            new Thread(Aimbot.Work) { IsBackground = true }.Start();
                            new Thread(AimbotAi.Work) { IsBackground = true }.Start();

                            this.Invoke((MethodInvoker)delegate
                            {
                                lblStatus.Text = $"● Linked (0x{il2cpp:X})";
                                lblStatus.ForeColor = neonGreen;
                                lblAttempt.Text = "";
                                btnOverlay.Enabled = true;
                                btnOverlay.BackColor = Color.FromArgb(20, 45, 40);
                                btnOverlay.ForeColor = neonGreen;
                                btnOverlay.FlatAppearance.BorderColor = neonGreen;
                                btnConnect.Text = "CONNECTED";
                            });

                            Log($"[OK] Connected! IL2CPP Base: 0x{il2cpp:X}");
                        }
                        else
                        {
                            ResetConnectionUI("● libil2cpp not found!");
                            Log("[ERR] libil2cpp.so module missing!");
                        }
                    }
                    else
                    {
                        ResetConnectionUI("● Connection Failed!");
                        Log("[ERR] Target link failed.");
                    }
                }
                catch (Exception ex)
                {
                    ResetConnectionUI($"● Error: {ex.Message}");
                    Log($"[EXC] {ex.Message}");
                }
            })
            { IsBackground = true }.Start();
        }

        private void ResetConnectionUI(string statusMsg)
        {
            this.Invoke((MethodInvoker)delegate
            {
                lblStatus.Text = statusMsg;
                lblStatus.ForeColor = neonPink;
                lblAttempt.Text = "";
                btnConnect.Enabled = true;
                btnConnect.Text = "⚡  CONNECT";
            });
        }

        private void BtnOverlay_Click(object sender, EventArgs e)
        {
            if (overlay == null || overlay.IsDisposed)
            {
                string processName = txtProcessName.Text.Trim().Replace(".exe", "");
                Process[] procs = Process.GetProcessesByName(processName);
                if (procs.Length > 0)
                {
                    IntPtr mainWnd = procs[0].MainWindowHandle;
                    IntPtr gameViewport = FindGameViewport(mainWnd);
                    IntPtr targetWnd = gameViewport != IntPtr.Zero ? gameViewport : mainWnd;

                    overlay = new OverlayForm();
                    overlay.SetTarget(targetWnd);
                    overlay.Show();
                    btnOverlay.Text = "⊙  Hide Overlay";
                }
                else
                {
                    Log($"[ERR] Process '{processName}' not found.");
                }
            }
            else
            {
                overlay.Stop();
                overlay.Close();
                overlay = null;
                btnOverlay.Text = "⊙  Toggle Overlay";
            }
        }

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")]
        private static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string lpszClass, string lpszWindow);
        private delegate bool EnumChildProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        private IntPtr FindGameViewport(IntPtr emulatorWnd)
        {
            if (emulatorWnd == IntPtr.Zero) return IntPtr.Zero;
            string[] viewportClasses = { "BlueStacksApp", "MSIAppPlayer" };
            foreach (string cls in viewportClasses)
            {
                IntPtr child = FindWindowEx(emulatorWnd, IntPtr.Zero, cls, null);
                if (child != IntPtr.Zero && IsWindowVisible(child))
                {
                    GetClientRect(child, out RECT rc);
                    if ((rc.Right - rc.Left) > 100 && (rc.Bottom - rc.Top) > 100)
                        return child;
                }
            }
            IntPtr bestChild = IntPtr.Zero;
            int bestArea = 0;
            EnumChildWindows(emulatorWnd, (IntPtr hwnd, IntPtr lParam) =>
            {
                if (!IsWindowVisible(hwnd)) return true;
                GetClientRect(hwnd, out RECT rc);
                int area = (rc.Right - rc.Left) * (rc.Bottom - rc.Top);
                if (area > bestArea) { bestChild = hwnd; bestArea = area; }
                return true;
            }, IntPtr.Zero);
            return bestChild;
        }

        internal void Log(string msg)
        {
            if (InvokeRequired)
            {
                try { Invoke((MethodInvoker)delegate { Log(msg); }); } catch { }
                return;
            }
            if (rtbLog != null && !rtbLog.IsDisposed)
            {
                rtbLog.AppendText(msg + "\n");
                rtbLog.ScrollToCaret();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            animTimer?.Stop();
            overlay?.Stop();
            overlay?.Close();
            bluestacks?.Destroy();
            _cachedBackground?.Dispose();
            _cachedHeader?.Dispose();
            base.OnFormClosing(e);
        }
    }

    // ============================================================
    // NEON CUSTOM SCROLLBAR (slim, gradient, glow, drag support)
    // ============================================================
    public class NeonScrollBar : Control
    {
        private int _value = 0;
        private int _minimum = 0;
        private int _maximum = 100;
        private int _largeChange = 10;
        private int _smallChange = 1;
        private bool _dragging = false;
        private int _dragOffset = 0;
        private bool _hover = false;

        public Color AccentColor { get; set; } = Color.FromArgb(0, 230, 255);
        public Color AccentColor2 { get; set; } = Color.FromArgb(150, 80, 255);

        public event EventHandler ValueChanged;

        public NeonScrollBar()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.SupportsTransparentBackColor |
                     ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
        }

        public int Value
        {
            get => _value;
            set
            {
                int max = Math.Max(_minimum, _maximum - _largeChange);
                int clamped = Math.Max(_minimum, Math.Min(max, value));
                if (clamped != _value)
                {
                    _value = clamped;
                    Invalidate();
                    ValueChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public int Minimum { get => _minimum; set { _minimum = value; Invalidate(); } }
        public int Maximum { get => _maximum; set { _maximum = value; Invalidate(); } }
        public int LargeChange { get => _largeChange; set { _largeChange = Math.Max(1, value); Invalidate(); } }
        public int SmallChange { get => _smallChange; set { _smallChange = value; } }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            int w = Width;
            int h = Height;

            // Track (subtle)
            Rectangle trackRect = new Rectangle(w / 2 - 1, 0, 2, h);
            using (SolidBrush tb = new SolidBrush(Color.FromArgb(25, 35, 50)))
            {
                e.Graphics.FillRectangle(tb, trackRect);
            }

            // Compute thumb
            int totalRange = _maximum;
            if (totalRange <= 0) return;

            float visibleRatio = (float)_largeChange / totalRange;
            if (visibleRatio > 1f) visibleRatio = 1f;
            int thumbHeight = Math.Max(40, (int)(h * visibleRatio));

            int maxThumbY = h - thumbHeight;
            float scrollRatio = (float)(_value - _minimum) / Math.Max(1, _maximum - _largeChange);
            if (scrollRatio < 0) scrollRatio = 0;
            if (scrollRatio > 1) scrollRatio = 1;
            int thumbY = (int)(scrollRatio * maxThumbY);

            // Thumb rect (slim, rounded)
            int thumbWidth = _hover || _dragging ? w : Math.Max(4, w - 2);
            int thumbX = (w - thumbWidth) / 2;
            Rectangle thumbRect = new Rectangle(thumbX, thumbY, thumbWidth, thumbHeight);

            // Outer glow (only when hover/drag)
            if (_hover || _dragging)
            {
                using (GraphicsPath glowPath = GetRoundedRect(
                    new Rectangle(thumbRect.X - 3, thumbRect.Y - 3, thumbRect.Width + 6, thumbRect.Height + 6),
                    (thumbRect.Width + 6) / 2))
                {
                    using (PathGradientBrush pgb = new PathGradientBrush(glowPath))
                    {
                        pgb.CenterColor = Color.FromArgb(120, AccentColor);
                        pgb.SurroundColors = new[] { Color.Transparent };
                        e.Graphics.FillPath(pgb, glowPath);
                    }
                }
            }

            // Thumb fill (gradient)
            using (GraphicsPath thumbPath = GetRoundedRect(thumbRect, thumbRect.Width / 2))
            {
                using (LinearGradientBrush lb = new LinearGradientBrush(
                    thumbRect, AccentColor, AccentColor2, 90f))
                {
                    e.Graphics.FillPath(lb, thumbPath);
                }

                // Inner highlight
                using (Pen hp = new Pen(Color.FromArgb(100, Color.White), 1f))
                {
                    e.Graphics.DrawPath(hp, thumbPath);
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            // Check if click on thumb
            int totalRange = _maximum;
            if (totalRange <= 0) return;

            float visibleRatio = (float)_largeChange / totalRange;
            int thumbHeight = Math.Max(40, (int)(Height * visibleRatio));
            int maxThumbY = Height - thumbHeight;
            float scrollRatio = (float)(_value - _minimum) / Math.Max(1, _maximum - _largeChange);
            int thumbY = (int)(scrollRatio * maxThumbY);

            if (e.Y >= thumbY && e.Y <= thumbY + thumbHeight)
            {
                _dragging = true;
                _dragOffset = e.Y - thumbY;
                Capture = true;
            }
            else
            {
                // Page jump
                if (e.Y < thumbY)
                    Value -= _largeChange;
                else
                    Value += _largeChange;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging) return;

            int totalRange = _maximum;
            if (totalRange <= 0) return;

            float visibleRatio = (float)_largeChange / totalRange;
            int thumbHeight = Math.Max(40, (int)(Height * visibleRatio));
            int maxThumbY = Height - thumbHeight;

            int newThumbY = Math.Max(0, Math.Min(maxThumbY, e.Y - _dragOffset));
            float scrollRatio = (float)newThumbY / Math.Max(1, maxThumbY);
            Value = _minimum + (int)(scrollRatio * (_maximum - _largeChange));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
            Capture = false;
            Invalidate();
        }

        private GraphicsPath GetRoundedRect(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            if (d <= 0) d = 2;
            if (d > rect.Width) d = rect.Width;
            if (d > rect.Height) d = rect.Height;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.X + rect.Width - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.X + rect.Width - d, rect.Y + rect.Height - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Y + rect.Height - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    // ============================================================
    // CUSTOM SLIDER
    // ============================================================
    public class CustomSlider : Control
    {
        private int _value = 50;
        private int _minimum = 0;
        private int _maximum = 100;
        private bool _dragging = false;

        public Color AccentColor { get; set; } = Color.FromArgb(0, 230, 255);
        public event EventHandler ValueChanged;

        public CustomSlider()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        public int Value
        {
            get => _value;
            set
            {
                int clamped = Math.Max(_minimum, Math.Min(_maximum, value));
                if (clamped != _value)
                {
                    _value = clamped;
                    Invalidate();
                    ValueChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public int Minimum { get => _minimum; set { _minimum = value; Invalidate(); } }
        public int Maximum { get => _maximum; set { _maximum = value; Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            int cy = Height / 2;
            int trackLeft = 8;
            int trackRight = Width - 8;
            int trackWidth = trackRight - trackLeft;

            Rectangle trackRect = new Rectangle(trackLeft, cy - 3, trackWidth, 6);
            using (GraphicsPath tp = GetRoundedRect(trackRect, 3))
            {
                using (SolidBrush tb = new SolidBrush(Color.FromArgb(40, 48, 68)))
                    e.Graphics.FillPath(tb, tp);
            }

            float ratio = (float)(_value - _minimum) / (_maximum - _minimum);
            int fillWidth = (int)(trackWidth * ratio);
            if (fillWidth > 0)
            {
                Rectangle fillRect = new Rectangle(trackLeft, cy - 3, fillWidth, 6);
                using (GraphicsPath fp = GetRoundedRect(fillRect, 3))
                {
                    using (LinearGradientBrush lb = new LinearGradientBrush(
                        fillRect, AccentColor, Color.FromArgb(150, 80, 255), 0f))
                    {
                        e.Graphics.FillPath(lb, fp);
                    }
                }
            }

            int thumbX = trackLeft + fillWidth;
            int thumbSize = 14;
            Rectangle thumbRect = new Rectangle(thumbX - thumbSize / 2, cy - thumbSize / 2, thumbSize, thumbSize);

            using (SolidBrush tb = new SolidBrush(Color.White))
                e.Graphics.FillEllipse(tb, thumbRect);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                UpdateFromMouse(e.X);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging) UpdateFromMouse(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
        }

        private void UpdateFromMouse(int mouseX)
        {
            int trackLeft = 8;
            int trackRight = Width - 8;
            int trackWidth = trackRight - trackLeft;
            float ratio = (float)(mouseX - trackLeft) / trackWidth;
            ratio = Math.Max(0, Math.Min(1, ratio));
            Value = _minimum + (int)(ratio * (_maximum - _minimum));
        }

        private GraphicsPath GetRoundedRect(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            if (d <= 0) d = 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.X + rect.Width - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.X + rect.Width - d, rect.Y + rect.Height - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Y + rect.Height - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}