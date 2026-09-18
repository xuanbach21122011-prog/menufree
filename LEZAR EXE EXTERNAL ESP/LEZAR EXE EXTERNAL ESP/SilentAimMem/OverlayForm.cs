using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace SilentAimMem
{
    public class OverlayForm : Form
    {
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_TOOLWINDOW = 0x80;
        private const int WS_EX_TOPMOST = 0x8;

        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

        private IntPtr targetHwnd;
        private bool running = true;
        private Thread renderThread;
        private System.Windows.Forms.Timer repaintTimer;
        private System.Windows.Forms.Timer hideTimer;
        private float rotationAngle = 0f;

        private static readonly Font nameFont = new Font("Segoe UI", 10f, FontStyle.Bold);
        private static readonly Font distFont = new Font("Segoe UI", 9f, FontStyle.Bold);
        private static readonly Font weaponFont = new Font("Segoe UI", 8f, FontStyle.Bold);
        private static readonly Font statusFont = new Font("Segoe UI", 11f, FontStyle.Bold);
        private static readonly Font hpFont = new Font("Segoe UI", 7f, FontStyle.Bold);

        private static readonly SolidBrush blackBrush = new SolidBrush(Color.Black);

        public OverlayForm()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);

            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.Black;
            TransparencyKey = Color.Black;
            TopMost = true;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(1, 1);

            repaintTimer = new System.Windows.Forms.Timer();
            repaintTimer.Interval = 16;
            repaintTimer.Tick += (s, e) => { if (Visible) Invalidate(); };

            hideTimer = new System.Windows.Forms.Timer();
            hideTimer.Interval = 50;
            hideTimer.Tick += (s, e) =>
            {
                hideTimer.Stop();
                if (!IsDisposed && IsHandleCreated)
                    Hide();
            };
        }

        public void SetTarget(IntPtr hwnd) => targetHwnd = hwnd;

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            int exStyle = GetWindowLong(Handle, GWL_EXSTYLE);
            SetWindowLong(Handle, GWL_EXSTYLE, exStyle | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_TOPMOST);
            renderThread = new Thread(RenderLoop) { IsBackground = true };
            renderThread.Start();
            repaintTimer.Start();
        }

        private void RenderLoop()
        {
            while (running)
            {
                try
                {
                    if (targetHwnd != IntPtr.Zero && IsWindow(targetHwnd) && IsWindowVisible(targetHwnd))
                    {
                        GetClientRect(targetHwnd, out RECT clientRect);
                        int w = clientRect.Right - clientRect.Left;
                        int h = clientRect.Bottom - clientRect.Top;
                        POINT tl = new POINT { X = clientRect.Left, Y = clientRect.Top };
                        ClientToScreen(targetHwnd, ref tl);

                        if (w > 10 && h > 10)
                        {
                            Core.Width = w;
                            Core.Height = h;
                            Invoke((MethodInvoker)delegate
                            {
                                Location = new Point(tl.X, tl.Y);
                                Size = new Size(w, h);
                                hideTimer.Stop();
                                if (!Visible) Show();
                            });
                        }
                    }
                }
                catch { }
                Thread.Sleep(100);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            int w = Core.Width;
            int h = Core.Height;
            if (w <= 0 || h <= 0) return;

            float mx = w / 2f;
            float my = h / 2f;

            // === FOV CIRCLE (always render, no matrix needed) ===
            if (Config.FOV)
            {
                float fovRadius = Config.Fov;
                if (Config.EnableGlow)
                    DrawGlowCircle(g, mx, my, fovRadius, Color.FromArgb(100, 255, 255, 255));
                using (Pen fovPen = new Pen(Color.FromArgb(200, 255, 255, 255), 1.5f))
                    g.DrawEllipse(fovPen, mx - fovRadius, my - fovRadius, fovRadius * 2, fovRadius * 2);
            }

            // === CROSSHAIR (always render, no matrix needed) ===
            if (Config.Crosshair)
            {
                rotationAngle += 0.1f;
                if (rotationAngle >= 360f) rotationAngle -= 360f;

                float angleRad = MathF.PI * rotationAngle / 180f;
                float size = 10f;
                float cos = MathF.Cos(angleRad);
                float sin = MathF.Sin(angleRad);

                using (Pen chPen = new Pen(Color.Red, 2.0f))
                {
                    g.DrawLine(chPen, mx + size * cos, my - size * sin, mx - size * cos, my + size * sin);
                    g.DrawLine(chPen, mx - size * sin, my - size * cos, mx + size * sin, my + size * cos);
                }
            }

            if (!Core.HaveMatrix) return;

            int enemyCount = 0;
            var entities = Core.Entities;

            foreach (var kv in entities)
            {
                Entity entity = kv.Value;
                if (!entity.IsKnown || entity.IsDead) continue;
                if (entity.IsTeam == Bool3.True) continue;

                float dist = entity.Distance;
                if (dist > 200 || dist < 0.1f) continue;
                if (entity.Health < 1 || entity.Health > 200) continue;

                Vector2 head2d = W2S.WorldToScreen(Core.CameraMatrix, entity.Head, w, h);
                Vector2 root2d = W2S.WorldToScreen(Core.CameraMatrix, entity.Root, w, h);

                if (head2d.X < 5 || head2d.Y < 5 || head2d.X > w - 5 || head2d.Y > h - 5) continue;
                if (root2d.X < 5 || root2d.Y < 5 || root2d.X > w - 5 || root2d.Y > h - 5) continue;

                float boxHeight = MathF.Abs(head2d.Y - root2d.Y);
                if (boxHeight < 6 || boxHeight > h * 0.75f) continue;
                float boxWidth = boxHeight * 0.65f;
                if (boxWidth < 4 || boxWidth > w * 0.75f) continue;

                float cx = head2d.X;
                float topY = head2d.Y;
                float left = cx - boxWidth / 2;
                float right = left + boxWidth;
                float bottom = topY + boxHeight;

                Color boxColor = entity.IsKnocked ? Color.Red : Color.White;
                enemyCount++;

                // === BOX ===
                if (Config.ESPCorneredBox)
                {
                    if (Config.EnableGlow) DrawGlowCorners(g, left, topY, boxWidth, boxHeight, boxColor);
                    using (Pen pen = new Pen(boxColor, 2f))
                        DrawCornerBox(g, pen, left, topY, boxWidth, boxHeight);
                }
                else if (Config.ESPBox || Config.ESPFullBox)
                {
                    if (Config.EnableGlow) DrawGlowRect(g, left, topY, boxWidth, boxHeight, boxColor);
                    using (Pen pen = new Pen(boxColor, 2f))
                        g.DrawRectangle(pen, left, topY, boxWidth, boxHeight);
                }

                // === LINE (snapline from top/center/bottom) ===
                if (Config.ESPLine)
                {
                    Color lineColor = entity.IsKnocked ? Color.Red : Color.White;
                    float lineStartY = Config.ESPMode == "1" ? 10f : (Config.ESPMode == "2" ? my : h - 10f);

                    using (Pen pen = new Pen(lineColor, 1.5f))
                        g.DrawLine(pen, mx, lineStartY, cx, topY);

                    if (Config.ESPMode == "1" || Config.ESPMode == "3")
                    {
                        using (SolidBrush br = new SolidBrush(Color.Red))
                            g.FillEllipse(br, mx - 4, lineStartY - 4, 8, 8);
                        using (Pen dp = new Pen(Color.Black, 1f))
                            g.DrawEllipse(dp, mx - 4, lineStartY - 4, 8, 8);
                    }
                }

                // === SNAPLINE ===
                if (Config.Snapline)
                {
                    using (Pen pen = new Pen(entity.IsKnocked ? Color.Red : Color.White, 1f))
                        g.DrawLine(pen, mx, h, cx, topY);
                }

                // === NAME (above box) ===
                if (Config.ESPName && !string.IsNullOrEmpty(entity.Name))
                {
                    SizeF sz = g.MeasureString(entity.Name, nameFont);
                    float nx = cx - sz.Width / 2;
                    float ny = topY - sz.Height - 2;
                    DrawText(g, entity.Name, nameFont, Color.White, nx, ny);
                }

                // === DISTANCE (below box) ===
                if (Config.ESPDistance)
                {
                    string distText = $"{dist:F0}m";
                    SizeF sz = g.MeasureString(distText, distFont);
                    float dx = cx - sz.Width / 2;
                    float dy = bottom + 2;
                    DrawText(g, distText, distFont, Color.Yellow, dx, dy);
                }

                // === HEALTH BAR (guna style — gradient green→orange→red) ===
                if (Config.ESPHealth)
                {
                    float healthPercentage = Math.Clamp(entity.Health / 200f, 0f, 1f);
                    float barHeight = boxHeight * healthPercentage;

                    float barX = right + 4;
                    float barY = topY;

                    // More vivid colors
                    Vector4 startColor = new Vector4(0.0f, 1.0f, 0.0f, 1.0f);   // Neon Green
                    Vector4 midColor = new Vector4(1.0f, 0.65f, 0.0f, 1.0f);    // Bright Orange
                    Vector4 endColor = new Vector4(1.0f, 0.0f, 0.0f, 1.0f);     // Deep Red

                    // Dynamic color (green → orange → red)
                    Vector4 barColor;
                    if (healthPercentage > 0.6f)
                        barColor = Vector4.Lerp(startColor, midColor, (1.0f - healthPercentage) * 2.5f);
                    else
                        barColor = Vector4.Lerp(midColor, endColor, (0.6f - healthPercentage) * 1.66f);

                    if (entity.IsKnocked)
                        barColor = startColor;

                    Color barCol = Vec4ToColor(barColor);

                    // Gradient bands
                    int barBands = Math.Min((int)barHeight, 20);
                    float bandHeight = barHeight / Math.Max(barBands, 1);
                    for (int i = 0; i < barBands; i++)
                    {
                        float t = (float)i / barBands;
                        Vector4 gradientColor = Vector4.Lerp(startColor, endColor, 1.0f - t);
                        Color gradientCol = Vec4ToColor(gradientColor);

                        float lineY = barY + boxHeight - i * bandHeight;
                        float lineEndY = barY + boxHeight - (i + 1) * bandHeight;

                        using (SolidBrush br = new SolidBrush(gradientCol))
                            g.FillRectangle(br, barX, lineEndY, 1, lineY - lineEndY);
                    }

                    // HP text above name
                    string healthText = $"{entity.Health} HP";
                    SizeF hpSz = g.MeasureString(healthText, hpFont);
                    float nameY = topY - 2;
                    if (Config.ESPName && !string.IsNullOrEmpty(entity.Name))
                    {
                        SizeF nameSz = g.MeasureString(entity.Name, nameFont);
                        nameY = topY - nameSz.Height - 2;
                    }
                    float hpX = cx - hpSz.Width / 2;
                    float hpY = nameY - hpSz.Height - 2;
                    DrawTextSmall(g, healthText, hpFont, barCol, hpX, hpY);
                }

                // === WEAPON ===
                if (Config.ESPWeapon && !string.IsNullOrEmpty(entity.WeaponName))
                {
                    SizeF sz = g.MeasureString(entity.WeaponName, weaponFont);
                    DrawText(g, entity.WeaponName, weaponFont, Color.White, cx - sz.Width / 2, bottom + 14);
                }

                // === SKELETON ===
                if (Config.ESPSkeleton)
                {
                    using (Pen skelPen = new Pen(Color.White, 1.5f))
                    {
                        DrawBone(g, skelPen, entity.Head, entity.Breast);
                        DrawBone(g, skelPen, entity.Breast, entity.Hip);
                        DrawBone(g, skelPen, entity.Breast, entity.LeftShoulder);
                        DrawBone(g, skelPen, entity.Breast, entity.RightShoulder);
                        DrawBone(g, skelPen, entity.LeftShoulder, entity.LeftHand);
                        DrawBone(g, skelPen, entity.RightShoulder, entity.RightHand);
                        DrawBone(g, skelPen, entity.Hip, entity.LeftFoot);
                        DrawBone(g, skelPen, entity.Hip, entity.RightFoot);
                    }
                }
            }

            // === ENEMY COUNT ===
            if (Config.EnemyCount)
            {
                using (Font f = new Font("Segoe UI", 11f, FontStyle.Bold))
                using (SolidBrush br = new SolidBrush(Color.Cyan))
                {
                    string t = $"Enemy Count - {enemyCount}";
                    SizeF sz = g.MeasureString(t, f);
                    g.DrawString(t, f, br, mx - sz.Width / 2, 30);
                }
            }
        }

        private static void DrawText(Graphics g, string text, Font font, Color color, float x, float y)
        {
            using (SolidBrush tb = new SolidBrush(color))
            {
                g.DrawString(text, font, blackBrush, x - 1, y);
                g.DrawString(text, font, blackBrush, x + 1, y);
                g.DrawString(text, font, blackBrush, x, y - 1);
                g.DrawString(text, font, blackBrush, x, y + 1);
                g.DrawString(text, font, tb, x, y);
            }
        }

        private static void DrawTextSmall(Graphics g, string text, Font font, Color color, float x, float y)
        {
            using (SolidBrush tb = new SolidBrush(color))
            {
                g.DrawString(text, font, blackBrush, x - 1, y);
                g.DrawString(text, font, blackBrush, x + 1, y);
                g.DrawString(text, font, blackBrush, x, y - 1);
                g.DrawString(text, font, blackBrush, x, y + 1);
                g.DrawString(text, font, tb, x, y);
            }
        }

        private static Color Vec4ToColor(Vector4 v)
        {
            return Color.FromArgb(
                (int)(Math.Clamp(v.W, 0f, 1f) * 255),
                (int)(Math.Clamp(v.X, 0f, 1f) * 255),
                (int)(Math.Clamp(v.Y, 0f, 1f) * 255),
                (int)(Math.Clamp(v.Z, 0f, 1f) * 255)
            );
        }

        private static void DrawGlowCircle(Graphics g, float cx, float cy, float radius, Color color)
        {
            for (int i = 0; i < 2; i++)
            {
                int spread = (i + 1) * 3;
                int alpha = 12 - i * 5;
                using (Pen p = new Pen(Color.FromArgb(alpha, color.R, color.G, color.B), 1.5f + spread))
                    g.DrawEllipse(p, cx - radius, cy - radius, radius * 2, radius * 2);
            }
        }

        private static void DrawGlowCorners(Graphics g, float x, float y, float w, float h, Color color)
        {
            float cornerLen = MathF.Min(w, h) / 3f;
            for (int i = 0; i < 2; i++)
            {
                int spread = (i + 1) * 3;
                int alpha = 15 - i * 6;
                using (Pen p = new Pen(Color.FromArgb(alpha, color.R, color.G, color.B), 2 + spread))
                {
                    g.DrawLine(p, x, y + cornerLen, x, y);
                    g.DrawLine(p, x, y, x + cornerLen, y);
                    g.DrawLine(p, x + w - cornerLen, y, x + w, y);
                    g.DrawLine(p, x + w, y, x + w, y + cornerLen);
                    g.DrawLine(p, x, y + h - cornerLen, x, y + h);
                    g.DrawLine(p, x, y + h, x + cornerLen, y + h);
                    g.DrawLine(p, x + w - cornerLen, y + h, x + w, y + h);
                    g.DrawLine(p, x + w, y + h, x + w, y + h - cornerLen);
                }
            }
        }

        private static void DrawGlowRect(Graphics g, float x, float y, float w, float h, Color color)
        {
            for (int i = 0; i < 2; i++)
            {
                int spread = (i + 1) * 3;
                int alpha = 15 - i * 6;
                using (Pen p = new Pen(Color.FromArgb(alpha, color.R, color.G, color.B), 2 + spread))
                    g.DrawRectangle(p, x, y, w, h);
            }
        }

        private static void DrawBone(Graphics g, Pen pen, Vector3 from, Vector3 to)
        {
            if (from == Vector3.Zero || to == Vector3.Zero) return;
            Vector2 s1 = W2S.WorldToScreen(Core.CameraMatrix, from, Core.Width, Core.Height);
            Vector2 s2 = W2S.WorldToScreen(Core.CameraMatrix, to, Core.Width, Core.Height);
            if (s1.X > 0 && s1.Y > 0 && s2.X > 0 && s2.Y > 0)
                g.DrawLine(pen, s1.X, s1.Y, s2.X, s2.Y);
        }

        private static void DrawCornerBox(Graphics g, Pen pen, float x, float y, float w, float h)
        {
            float cornerLen = MathF.Min(w, h) / 3f;
            g.DrawLine(pen, x, y + cornerLen, x, y);
            g.DrawLine(pen, x, y, x + cornerLen, y);
            g.DrawLine(pen, x + w - cornerLen, y, x + w, y);
            g.DrawLine(pen, x + w, y, x + w, y + cornerLen);
            g.DrawLine(pen, x, y + h - cornerLen, x, y + h);
            g.DrawLine(pen, x, y + h, x + cornerLen, y + h);
            g.DrawLine(pen, x + w - cornerLen, y + h, x + w, y + h);
            g.DrawLine(pen, x + w, y + h, x + w, y + h - cornerLen);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_TOPMOST;
                return cp;
            }
        }

        public void Stop() => running = false;
    }
}
