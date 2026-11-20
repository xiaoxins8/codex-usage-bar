using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace CodexUsageBar
{
    internal enum DetailSection { Session, Tokens, Quota }
    internal enum Ink { Text, Muted, Green, Blue }

    internal sealed class Palette
    {
        internal Color Fill, Edge, Text, Muted, Green, Blue, Dot, ActiveSession, ActiveTokens;

        internal static Palette For(bool dark)
        {
            return dark
                ? new Palette { Fill = Color.FromArgb(39, 40, 44), Edge = Color.FromArgb(63, 65, 71),
                    Text = Color.FromArgb(237, 239, 242), Muted = Color.FromArgb(170, 175, 184),
                    Green = Color.FromArgb(102, 220, 153), Blue = Color.FromArgb(141, 188, 255), Dot = Color.FromArgb(177, 158, 255),
                    ActiveSession = Color.FromArgb(47, 63, 89), ActiveTokens = Color.FromArgb(41, 68, 55) }
                : new Palette { Fill = Color.FromArgb(253, 253, 254), Edge = Color.FromArgb(232, 233, 237),
                    Text = Color.FromArgb(38, 44, 55), Muted = Color.FromArgb(92, 101, 114),
                    Green = Color.FromArgb(16, 128, 68), Blue = Color.FromArgb(45, 104, 172), Dot = Color.FromArgb(135, 106, 238),
                    ActiveSession = Color.FromArgb(232, 241, 255), ActiveTokens = Color.FromArgb(229, 246, 235) };
        }

        internal Color ColorFor(Ink ink)
        {
            switch (ink)
            {
                case Ink.Muted: return Muted;
                case Ink.Green: return Green;
                case Ink.Blue: return Blue;
                default: return Text;
            }
        }
    }

    internal sealed class Capsule
    {
        private sealed class Segment
        {
            internal string Text;
            internal float X;
            internal Ink Ink;
        }

        private readonly List<Segment> segments = new List<Segment>();
        private float edge, divider, quotaDivider;
        private bool hasSession, hasTokens, hasQuota;
        private readonly float scale;
        internal int Width { get { return (int)Math.Ceiling(edge + 10 * scale); } }
        internal int Height { get { return (int)Math.Ceiling(30 * scale); } }
        internal int Split { get { return (int)Math.Round(divider); } }
        internal int QuotaSplit { get { return (int)Math.Round(quotaDivider); } }
        internal bool CanOpen { get { return hasTokens || hasQuota; } }
        internal bool CanShow(DetailSection section)
        {
            return section == DetailSection.Session ? hasSession : section == DetailSection.Quota ? hasQuota : hasTokens;
        }

        private Capsule(float scale)
        {
            this.scale = scale;
            edge = 22 * scale;
        }

        internal static Capsule Fit(Graphics graphics, Font font, UsageMetrics reading, int available, float scale,
            BarSettings settings = null, QuotaSnapshot quota = null)
        {
            bool quotaEnabled = settings != null && settings.ShowsQuota;
            for (int density = 0; density < (quotaEnabled ? 5 : 4); density++)
            {
                var result = new Capsule(scale);
                if (reading.Pinned)
                {
                    result.Add(graphics, font, "固定", Ink.Muted);
                    result.edge += 9 * scale;
                }
                if (!reading.Total.HasValue)
                {
                    result.Add(graphics, font, reading.Status ?? "等待用量", Ink.Muted);
                    result.hasTokens = reading.Context != null;
                    if (quotaEnabled && density == 0) result.AddQuota(graphics, font, settings, quota);
                    if (result.Width <= available) return result;
                    if (!quotaEnabled || density > 0) return null;
                    continue;
                }
                if (density == 0 && reading.Rounds > 0)
                {
                    result.Add(graphics, font, reading.RoundLabel, Ink.Muted);
                    result.hasSession = true;
                }
                if (density <= 1 && reading.Speed.HasValue)
                {
                    if (result.hasSession) result.edge += 9 * scale;
                    result.Add(graphics, font, "↗ " + reading.SpeedLabel, Ink.Blue);
                    result.edge += 3 * scale;
                    result.Add(graphics, font, "tok/s", Ink.Muted);
                    result.hasSession = true;
                }
                if (result.hasSession)
                {
                    result.divider = result.edge + 7 * scale;
                    result.edge += 15 * scale;
                }
                result.Add(graphics, font, UsageMetrics.Compact(reading.Total.Value), Ink.Green);
                result.edge += 3 * scale;
                result.Add(graphics, font, "tok", Ink.Muted);
                result.hasTokens = true;
                if (density <= 2)
                {
                    result.edge += 10 * scale;
                    result.Add(graphics, font, "缓存", Ink.Muted);
                    result.edge += 4 * scale;
                    result.Add(graphics, font, reading.HitLabel, Ink.Text);
                }
                if (quotaEnabled && density < 4) result.AddQuota(graphics, font, settings, quota);
                if (result.Width <= available) return result;
            }
            return null;
        }

        private void AddQuota(Graphics graphics, Font font, BarSettings settings, QuotaSnapshot quota)
        {
            hasQuota = true;
            quotaDivider = edge + 7 * scale;
            edge += 15 * scale;
            if (settings.ShowFiveHour)
            {
                Add(graphics, font, "5h", Ink.Muted);
                edge += 4 * scale;
                Add(graphics, font, quota != null && quota.FiveHour != null ? quota.FiveHour.Remaining : "—", Ink.Blue);
            }
            if (settings.ShowWeek)
            {
                if (settings.ShowFiveHour) edge += 8 * scale;
                Add(graphics, font, "周", Ink.Muted);
                edge += 4 * scale;
                Add(graphics, font, quota != null && quota.Week != null ? quota.Week.Remaining : "—", Ink.Blue);
            }
        }

        private void Add(Graphics graphics, Font font, string text, Ink ink)
        {
            segments.Add(new Segment { X = edge, Text = text, Ink = ink });
            edge += graphics.MeasureString(text, font, Int32.MaxValue, StringFormat.GenericTypographic).Width;
        }

        internal DetailSection SectionAt(int localX)
        {
            if (hasQuota && (!hasTokens || localX >= QuotaSplit)) return DetailSection.Quota;
            return hasSession && (!hasTokens || localX < Split) ? DetailSection.Session : DetailSection.Tokens;
        }

        internal void Draw(Graphics graphics, Font font, Palette palette, float x, float y, DetailSection? selection = null)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (var outline = Rounded(new RectangleF(x + 0.5f, y + 0.5f, Width - 1f, Height - 1f), 10 * scale))
            using (var fill = new SolidBrush(palette.Fill))
            using (var border = new Pen(palette.Edge, scale))
            using (var dot = new SolidBrush(palette.Dot))
            {
                graphics.FillPath(fill, outline);
                if (selection.HasValue)
                {
                    bool token = selection.Value == DetailSection.Tokens;
                    float start = selection.Value == DetailSection.Quota ? quotaDivider : token ? divider : 0;
                    float end = selection.Value == DetailSection.Session && divider > 0 ? divider
                        : token && quotaDivider > 0 ? quotaDivider : Width;
                    float left = start + 3 * scale;
                    float right = end - 3 * scale;
                    if (right - left > 16 * scale)
                    {
                        using (var active = Rounded(new RectangleF(x + left, y + 3 * scale,
                            right - left, Height - 6 * scale), 7 * scale))
                        using (var tint = new SolidBrush(token ? palette.ActiveTokens : palette.ActiveSession))
                        using (var signal = new Pen(token ? palette.Green : palette.Blue, 1.5f * scale))
                        {
                            graphics.FillPath(tint, active);
                            float midpoint = x + (left + right) / 2;
                            graphics.DrawLine(signal, midpoint - 8 * scale, y + Height - 4 * scale,
                                midpoint + 8 * scale, y + Height - 4 * scale);
                        }
                    }
                }
                graphics.DrawPath(border, outline);
                graphics.FillEllipse(dot, x + 10 * scale, y + (Height - 5 * scale) / 2, 5 * scale, 5 * scale);
                if (divider > 0) graphics.DrawLine(border, x + divider, y + 7 * scale, x + divider, y + Height - 7 * scale);
                if (quotaDivider > 0) graphics.DrawLine(border, x + quotaDivider, y + 7 * scale, x + quotaDivider, y + Height - 7 * scale);
            }
            float baseline = y + (Height - font.GetHeight(graphics)) / 2;
            foreach (Segment part in segments)
                using (var brush = new SolidBrush(palette.ColorFor(part.Ink)))
                    graphics.DrawString(part.Text, font, brush, new PointF(x + part.X, baseline), StringFormat.GenericTypographic);
        }

        internal static GraphicsPath Rounded(RectangleF rect, float radius)
        {
            float d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class DetailPopup : Form
    {
        private const int ContextHeight = 152;
        private UsageMetrics reading;
        private Palette palette;
        private QuotaSnapshot quota;
        private BarSettings settings;
        private float scale = 1;
        private readonly Timer motion = new Timer { Interval = 15 };
        private readonly Stopwatch clock = new Stopwatch();
        private Rectangle restingBounds;
        private double startOpacity;
        private int startShift, motionOffset;
        private bool allowAnimation;
        internal bool IsClosing { get; private set; }
        internal DetailSection Section { get; private set; }

        internal static Size PanelSize(DetailSection section, float scale)
        {
            int height = section == DetailSection.Quota ? 164 : 142 + (section == DetailSection.Tokens ? ContextHeight : 0);
            return new Size((int)Math.Ceiling(306 * scale), (int)Math.Ceiling(height * scale));
        }

        internal DetailPopup()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            DoubleBuffered = true;
            Deactivate += (sender, args) => Dismiss();
            KeyDown += (sender, args) => { if (args.KeyCode == Keys.Escape) Collapse(allowAnimation); };
            motion.Tick += (sender, args) => AnimateFrame();
        }

        internal void Present(DetailSection section, UsageMetrics metrics, bool dark, Rectangle anchor, float dpi,
            QuotaSnapshot quota = null, BarSettings settings = null)
        {
            Section = section;
            reading = metrics;
            this.quota = quota;
            this.settings = settings;
            palette = Palette.For(dark);
            BackColor = palette.Fill;
            scale = dpi;
            Text = section == DetailSection.Session ? "会话统计" : section == DetailSection.Quota ? "账号额度" : "Token 用量";
            AccessibleName = Text;
            Size size = PanelSize(section, scale);
            int width = size.Width, height = size.Height;
            int gap = (int)Math.Ceiling(8 * scale);
            Rectangle screen = Screen.FromRectangle(anchor).WorkingArea;
            int x = Math.Max(screen.Left, Math.Min(anchor.Left, screen.Right - width));
            int y = anchor.Top - height - gap;
            bool below = y < screen.Top;
            if (below) y = Math.Min(anchor.Bottom + gap, screen.Bottom - height);
            motionOffset = (int)Math.Round((below ? -6 : 6) * scale);
            Rectangle bounds = new Rectangle(x, Math.Max(y, screen.Top), width, height);
            if (restingBounds != bounds || Region == null)
            {
                int shift = motion.Enabled ? Bounds.Top - restingBounds.Top : 0;
                bool resize = bounds.Size != restingBounds.Size || Region == null;
                restingBounds = bounds;
                bounds.Offset(0, shift);
                Bounds = bounds;
                if (resize)
                    using (var path = Capsule.Rounded(new RectangleF(0, 0, ClientSize.Width, ClientSize.Height), 12 * scale))
                        Region = new Region(path);
            }
            Invalidate();
        }

        internal void Expand(bool animated)
        {
            motion.Stop();
            bool wasVisible = Visible;
            allowAnimation = animated;
            IsClosing = false;
            if (animated)
            {
                startOpacity = wasVisible ? Math.Min(0.82, Opacity) : 0.08;
                startShift = wasVisible ? motionOffset / 2 : motionOffset;
                Opacity = startOpacity;
                Location = new Point(restingBounds.Left, restingBounds.Top + startShift);
                clock.Restart();
                motion.Start();
            }
            else
            {
                Opacity = 1;
                Bounds = restingBounds;
            }
            if (!wasVisible) Show();
            Activate();
        }

        internal void Collapse(bool animated)
        {
            if (!Visible || IsClosing) return;
            if (!animated) { Dismiss(); return; }
            IsClosing = true;
            startOpacity = Opacity;
            startShift = Top - restingBounds.Top;
            clock.Restart();
            motion.Start();
        }

        internal void Dismiss()
        {
            motion.Stop();
            IsClosing = false;
            if (Visible) Hide();
            Opacity = 1;
        }

        private void AnimateFrame()
        {
            double progress = Math.Min(1, clock.Elapsed.TotalMilliseconds / (IsClosing ? 115 : 165));
            double eased = 1 - Math.Pow(1 - progress, 3);
            int goal = IsClosing ? motionOffset : 0;
            double targetOpacity = IsClosing ? 0 : 1;
            Opacity = startOpacity + (targetOpacity - startOpacity) * eased;
            int shift = (int)Math.Round(startShift + (goal - startShift) * eased);
            Location = new Point(restingBounds.Left, restingBounds.Top + shift);
            if (progress < 1) return;
            motion.Stop();
            if (IsClosing) Dismiss();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) motion.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (reading == null) return;
            Draw(e.Graphics, Section, reading, palette, scale, ClientSize, quota, settings);
        }

        internal static void Draw(Graphics g, DetailSection section, UsageMetrics reading, Palette palette, float scale, Size size,
            QuotaSnapshot quota = null, BarSettings settings = null)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            bool session = section == DetailSection.Session;
            bool quotaSection = section == DetailSection.Quota;
            string title = session ? "会话统计" : quotaSection ? "账号额度" : "Token 用量";
            string value = session ? (reading.Rounds > 0 ? reading.RoundLabel : "—") : quotaSection ? "剩余额度" : UsageMetrics.Exact(reading.Total);
            var rows = session
                ? new[] {
                    Row("轮次总耗时", reading.Running ? "进行中" : UsageMetrics.Seconds(reading.TurnMs)),
                    Row("工具耗时（已记录 " + reading.TimedTools + " 项）", UsageMetrics.Seconds(reading.TimedTools > 0 ? reading.ToolMs : (long?)null)),
                    Row("首 Token（本轮 TTFT）", UsageMetrics.Seconds(reading.FirstTokenMs)),
                    Row("轮次平均输出", reading.Speed.HasValue ? reading.SpeedLabel + " tok/s" : "—")
                }
                : new[] {
                    Row("缓存命中", reading.HitLabel),
                    Row("未缓存输入", UsageMetrics.Exact(reading.HasCache ? reading.Input - reading.Cached : null)),
                    Row("缓存读取", UsageMetrics.Exact(reading.HasCache ? reading.Cached : null)),
                    Row("输出", UsageMetrics.Exact(reading.Output))
                };
            if (quotaSection)
            {
                var details = new List<KeyValuePair<string, string>>();
                if (settings == null || settings.ShowFiveHour) AddWindow(details, "五小时", quota == null ? null : quota.FiveHour);
                if (settings == null || settings.ShowWeek) AddWindow(details, "每周", quota == null ? null : quota.Week);
                rows = details.ToArray();
            }
            using (var frame = Capsule.Rounded(new RectangleF(0.5f, 0.5f, size.Width - 1, size.Height - 1), 12 * scale))
            using (var outline = new Pen(palette.Edge, scale))
            using (var fill = new SolidBrush(palette.Fill))
            using (var text = new SolidBrush(palette.Text))
            using (var muted = new SolidBrush(palette.Muted))
            using (var accent = new SolidBrush(session ? palette.Blue : palette.Green))
            using (var font = new Font("Segoe UI", 12 * scale, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var heading = new Font("Segoe UI", 13 * scale, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var right = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Far })
            {
                g.FillPath(fill, frame);
                g.DrawPath(outline, frame);
                float left = 15 * scale, edge = size.Width - left;
                float offset = 0;
                if (section == DetailSection.Tokens)
                {
                    DrawContext(g, reading.Context, palette, scale, left, edge, font, heading, right, text, muted);
                    offset = ContextHeight * scale;
                    g.DrawLine(outline, left, offset, edge, offset);
                    title = "会话累计";
                }
                g.FillEllipse(accent, left, offset + 20 * scale, 5 * scale, 5 * scale);
                g.DrawString(title, heading, text, new PointF(left + 13 * scale, offset + 14 * scale), StringFormat.GenericTypographic);
                g.DrawString(value, heading, text, new PointF(edge, offset + 14 * scale), right);
                g.DrawLine(outline, left, offset + 40 * scale, edge, offset + 40 * scale);
                float y = offset + 50 * scale;
                foreach (var row in rows)
                {
                    g.DrawString(row.Key, font, muted, new PointF(left, y), StringFormat.GenericTypographic);
                    g.DrawString(row.Value, font, text, new PointF(edge, y), right);
                    y += 22 * scale;
                }
                if (quotaSection)
                {
                    string note = quota == null ? "等待 Codex 返回账号额度" : quota.Status;
                    g.DrawString(note, font, muted, new PointF(left, 141 * scale), StringFormat.GenericTypographic);
                }
            }
        }

        private static void DrawContext(Graphics g, ContextUsage context, Palette palette, float scale, float left, float edge,
            Font font, Font heading, StringFormat right, Brush text, Brush muted)
        {
            string percent = context == null ? "—" : context.PercentLabel;
            string window = context == null ? "—" : context.WindowLabel;
            g.DrawString("上下文已用  " + percent, heading, text, new PointF(left, 15 * scale), StringFormat.GenericTypographic);
            g.DrawString(window, heading, text, new PointF(edge, 15 * scale), right);

            var track = new RectangleF(left, 40 * scale, edge - left, 6 * scale);
            using (var trackBrush = new SolidBrush(palette.Edge))
            using (var uncached = new SolidBrush(palette.Muted))
            using (var cached = new SolidBrush(palette.Dot))
            using (var output = new SolidBrush(palette.Blue))
            using (var frame = Capsule.Rounded(track, 3 * scale))
            {
                g.FillPath(trackBrush, frame);
                if (context != null && context.Percent > 0)
                {
                    GraphicsState drawing = g.Save();
                    g.SetClip(frame, CombineMode.Intersect);
                    float width = track.Width * (float)(context.Percent.Value / 100);
                    if (context.HasCompleteSplit)
                    {
                        float first = width * (float)(context.Uncached.Value / (double)context.Used.Value);
                        float second = width * (float)(context.Cached.Value / (double)context.Used.Value);
                        g.FillRectangle(uncached, track.X, track.Y, first, track.Height);
                        g.FillRectangle(cached, track.X + first, track.Y, second, track.Height);
                        g.FillRectangle(output, track.X + first + second, track.Y, Math.Max(0, width - first - second), track.Height);
                    }
                    else g.FillRectangle(output, track.X, track.Y, width, track.Height);
                    g.Restore(drawing);
                }

                var rows = new[] {
                    Row("未缓存输入", UsageMetrics.Exact(context == null ? null : context.Uncached)),
                    Row("已缓存输入", UsageMetrics.Exact(context == null ? null : context.ValidCached)),
                    Row("本次输出", UsageMetrics.Exact(context == null ? null : context.Output))
                };
                var colors = new[] { uncached, cached, output };
                for (int i = 0; i < rows.Length; i++)
                {
                    float y = (59 + 20 * i) * scale;
                    g.FillRectangle(colors[i], left, y + 4 * scale, 7 * scale, 7 * scale);
                    g.DrawString(rows[i].Key, font, muted, new PointF(left + 13 * scale, y), StringFormat.GenericTypographic);
                    g.DrawString(rows[i].Value, font, text, new PointF(edge, y), right);
                }
            }
            string note = context == null || !context.Used.HasValue ? "等待 Codex 上报上下文"
                : "最近一次模型上报" + (context.ReportedAt.HasValue ? " · " + context.ReportedAt.Value.ToLocalTime().ToString("HH:mm:ss") : "");
            g.DrawString(note, font, muted, new PointF(left, 126 * scale), StringFormat.GenericTypographic);

        }

        private static void AddWindow(List<KeyValuePair<string, string>> rows, string name, QuotaWindow value)
        {
            rows.Add(Row(name + "剩余", value == null ? "—" : value.Remaining));
            rows.Add(Row(name + "重置", value == null ? "—" : value.ResetLabel));
        }

        private static KeyValuePair<string, string> Row(string label, string value)
        {
            return new KeyValuePair<string, string>(label, value);
        }
    }
}
