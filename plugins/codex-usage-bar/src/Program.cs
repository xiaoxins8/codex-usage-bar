using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Forms;

namespace CodexUsageBar
{
    internal static class Program
    {
        private const string StopEvent = @"Local\CodexUsageBar.Stop";
        private const string SettingsEvent = @"Local\CodexUsageBar.Settings";
        [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Contains("--stop"))
            {
                Signal(StopEvent);
                return;
            }
            bool background = args.Contains("--background");
            bool first;
            using (var mutex = new Mutex(true, @"Local\CodexUsageBar.Instance", out first))
            {
                if (!first)
                {
                    if (!background && !Signal(SettingsEvent))
                        MessageBox.Show("请先从系统托盘退出正在运行的旧版，再打开新版。", "Codex 用量栏",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                using (var stop = new EventWaitHandle(false, EventResetMode.AutoReset, StopEvent))
                using (var showSettings = new EventWaitHandle(false, EventResetMode.AutoReset, SettingsEvent))
                {
                    SetProcessDpiAwarenessContext(new IntPtr(-4));
                    Application.EnableVisualStyles();
                    UiAssets.Register();
                    string home = Environment.GetEnvironmentVariable("CODEX_HOME");
                    if (String.IsNullOrWhiteSpace(home))
                        home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
                    if (!background) showSettings.Set();
                    string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexUsageBar");
                    Application.Run(new UsageApp(home, stop, showSettings, new SettingsStore(Path.Combine(appData, "settings.json")), appData,
                        new StartupRegistration(Application.ExecutablePath)));
                }
            }
        }

        private static bool Signal(string name)
        {
            try
            {
                EventWaitHandle existing;
                if (!EventWaitHandle.TryOpenExisting(name, out existing)) return false;
                using (existing) return existing.Set();
            }
            catch (UnauthorizedAccessException) { return false; }
        }
    }

    internal sealed class Reading
    {
        internal UsageMetrics Metrics;
        internal string Status, Thread, Model, Effort;
    }

    internal sealed class Observer
    {
        private readonly ThreadStore store;
        private readonly UsageLog log = new UsageLog();
        private List<ThreadEntry> index = new List<ThreadEntry>();
        private DateTime indexRead, recentRead, lastLookup;
        private string lastId, lastPath;

        internal Observer(string home) { store = new ThreadStore(home); }

        internal Reading Read(Placement placement, string pin)
        {
            try
            {
                DateTime now = DateTime.UtcNow;
                if ((now - indexRead).TotalSeconds >= (lastId == null ? 1.4 : 4))
                {
                    index = store.ReadIndex();
                    indexRead = now;
                }
                string id = pin ?? Composer.ResolveThread(placement, index);
                if (id == null && placement.PromptFingerprints != null && placement.PromptFingerprints.Length > 0
                    && (now - recentRead).TotalSeconds >= 1.4)
                {
                    store.ReadRecentRollouts(index);
                    recentRead = now;
                    id = Composer.ResolveThread(placement, index);
                }
                if (id == null)
                {
                    log.Reset();
                    lastId = lastPath = null;
                    return new Reading { Metrics = new UsageMetrics { Status = "识别会话…" },
                        Status = "正在识别当前会话，可从托盘手动固定" };
                }
                // Re-check every few seconds: resuming a chat moves its log to a new "<id>_<segment>.jsonl" file.
                if (id != lastId || (now - lastLookup).TotalSeconds >= 3 || (lastPath != null && !File.Exists(lastPath)))
                {
                    lastPath = store.FindLog(id, index);
                    lastId = id;
                    lastLookup = now;
                }
                log.Read(lastPath);
                ThreadEntry entry = index.FirstOrDefault(row => String.Equals(row.Id, id, StringComparison.OrdinalIgnoreCase));
                UsageMetrics metrics = log.Metrics(pin != null);
                // Codex compresses idle chats (rollout-*.jsonl.zst) until the next message; the database keeps the exact total.
                if (lastPath == null && !metrics.Total.HasValue && entry != null && entry.TokensUsed > 0)
                    metrics = new UsageMetrics { Total = entry.TokensUsed, Pinned = pin != null };
                return new Reading {
                    Metrics = metrics,
                    Status = pin != null ? "已固定会话" : "跟随当前会话",
                    Thread = entry != null && entry.Name.Length > 0 ? entry.Name : null,
                    Model = entry != null ? entry.Model : null,
                    Effort = entry != null ? entry.Effort : null
                };
            }
            catch (UnauthorizedAccessException) { return new Reading { Status = "本机会话日志暂不可读" }; }
            catch (IOException) { return new Reading { Status = "正在等待本机会话日志" }; }
            // An unusual log path must not fault the read loop; the capsule would fall back to "识别会话…".
            catch (ArgumentException) { return new Reading { Status = "本机会话日志路径无法读取" }; }
            catch (NotSupportedException) { return new Reading { Status = "本机会话日志路径无法读取" }; }
        }
    }

    internal sealed class UsageApp : ApplicationContext
    {
        private readonly Observer observer;
        private readonly Layer overlay = new Layer();
        private readonly NotifyIcon tray;
        private readonly ToolStripMenuItem status;
        private readonly ToolStripMenuItem follow;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 100 };
        private readonly EventWaitHandle stop;
        private readonly EventWaitHandle showSettings;
        private readonly SettingsStore settingsStore;
        private readonly StartupRegistration startup;
        private readonly string home, appData;
        private BarSettings settings;
        private SettingsWindow settingsWindow;
        // Last session reading that carried usage. The settings window previews it while Codex is in the background.
        private Reading lastUsage;
        private DateTime lastUsageAt;
        private Task<Reading> pending;
        private Task<Placement> layoutTask;
        private Task<QuotaSnapshot> quotaTask;
        private CancellationTokenSource quotaCancel;
        private Reading reading;
        private Placement layout;
        private IntPtr activeWindow, scanWindow, cachedForeground, cachedCodex;
        private Rectangle scanBounds;
        private DateTime nextLayout, nextRead, lastLayout, nextQuota, lastQuotaStart;
        private QuotaSnapshot accountQuota, shownQuota;
        private string quotaNotice = "等待首次刷新", shownQuotaStatus;
        private string pin;
        private int generation, observedGeneration;
        private bool disposed;

        internal UsageApp(string home, EventWaitHandle stop, EventWaitHandle showSettings, SettingsStore settingsStore, string appData,
            StartupRegistration startup)
        {
            observer = new Observer(home);
            this.home = home;
            this.appData = appData;
            this.stop = stop;
            this.showSettings = showSettings;
            this.settingsStore = settingsStore;
            this.startup = startup;
            settings = settingsStore.Load();
            status = new ToolStripMenuItem("等待 Codex 前台窗口") { Enabled = false };
            follow = new ToolStripMenuItem("跟随当前会话", null, (sender, args) => SetPin(null)) { Checked = true };
            var menu = new ContextMenuStrip();
            menu.Items.Add("用量栏设置…", null, (sender, args) => OpenSettings());
            menu.Items.Add(status);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(follow);
            menu.Items.Add("固定会话…", null, (sender, args) => PinDialog());
            menu.Items.Add("退出", null, (sender, args) => ExitThread());
            tray = new NotifyIcon { Text = "Codex 用量栏", Icon = AppIcon.Create(32), ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += (sender, args) => OpenSettings();
            timer.Tick += Tick;
            timer.Start();
        }

        private void Tick(object sender, EventArgs args)
        {
            if (stop.WaitOne(0)) { ExitThread(); return; }
            if (showSettings.WaitOne(0)) OpenSettings();
            IntPtr foreground = Desktop.GetForegroundWindow();
            IntPtr detailTarget = overlay.DetailTarget(foreground);
            if (foreground != cachedForeground)
            {
                cachedForeground = foreground;
                cachedCodex = detailTarget != IntPtr.Zero ? detailTarget : Desktop.CodexWindow();
            }
            IntPtr window = detailTarget != IntPtr.Zero ? detailTarget : cachedCodex;
            if (!Desktop.IsUsable(window)) window = IntPtr.Zero;
            Rectangle bounds = Desktop.Bounds(window);
            DateTime now = DateTime.UtcNow;
            if (activeWindow != window)
            {
                activeWindow = window;
                generation++;
                reading = null;
                nextLayout = nextRead = DateTime.MinValue;
            }
            bool scanValid = AdvanceLayout(window, bounds, now);
            if (pending != null && pending.IsCompleted)
            {
                if (pending.Status == TaskStatus.RanToCompletion && observedGeneration == generation)
                {
                    reading = pending.Result;
                    status.Text = reading.Status;
                    if (reading.Metrics != null && reading.Metrics.Total.HasValue) { lastUsage = reading; lastUsageAt = now; }
                    if (settingsWindow != null) settingsWindow.SetUsage(lastUsage, reading.Status, lastUsageAt);
                }
                else if (pending.IsFaulted)
                {
                    // Observe worker exceptions and let the next scan retry.
                    var failure = pending.Exception.GetBaseException();
                    status.Text = "本地用量读取暂不可用 · " + failure.GetType().Name;
                }
                pending = null;
            }
            Placement visible = scanValid ? layout.At(bounds) : null;
            if (pending == null && visible != null && !visible.MenuOpen && now >= nextRead
                && ((now - lastLayout).TotalSeconds < 3 || pin != null))
            {
                string pinned = pin;
                observedGeneration = generation;
                nextRead = now.AddMilliseconds(300);
                pending = Task.Factory.StartNew(() => observer.Read(visible, pinned),
                    CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default);
            }
            UpdateQuota(now);
            if (visible == null || visible.MenuOpen) overlay.Clear();
            else
            {
                UsageMetrics metrics = reading == null ? null : reading.Metrics;
                if (metrics == null || (pin == null && (now - lastLayout).TotalSeconds > 3)) metrics = Identifying;
                overlay.Display(visible, metrics, settings, shownQuota);
            }
        }

        private static readonly UsageMetrics Identifying = new UsageMetrics { Status = "识别会话…" };

        private bool AdvanceLayout(IntPtr window, Rectangle bounds, DateTime now)
        {
            if (layoutTask != null && layoutTask.IsCompleted)
            {
                if (layoutTask.Status == TaskStatus.RanToCompletion && layoutTask.Result != null
                    && scanWindow == window && scanBounds.Size == bounds.Size)
                {
                    var confirmed = layoutTask.Result.At(bounds);
                    if (layout == null || layout.Window != window || ChangedThreadHints(layout, confirmed))
                    {
                        generation++;
                        reading = null;
                        nextRead = DateTime.MinValue;
                    }
                    layout = confirmed;
                    lastLayout = now;
                }
                else if (layoutTask.IsFaulted)
                {
                    var failure = layoutTask.Exception.GetBaseException();
                    status.Text = "等待 Codex 界面重试 · " + failure.GetType().Name;
                }
                layoutTask = null;
            }
            if (window != IntPtr.Zero && layoutTask == null && (now >= nextLayout || scanWindow != window || scanBounds.Size != bounds.Size))
            {
                scanWindow = window;
                scanBounds = bounds;
                nextLayout = now.AddMilliseconds(650);
                layoutTask = Task.Factory.StartNew(() => Composer.Inspect(window, bounds, Desktop.Scale(window)),
                    CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default);
            }
            // Temporary UIA gaps keep the last geometry on the same Codex window.
            return RetainLayout(layout, window, lastLayout, now);
        }

        internal static bool RetainLayout(Placement confirmed, IntPtr window, DateTime lastConfirmed, DateTime now)
        {
            return confirmed != null && window != IntPtr.Zero && confirmed.Window == window && (now - lastConfirmed).TotalSeconds < 18;
        }

        private static bool ChangedThreadHints(Placement previous, Placement next)
        {
            if (previous.ThreadId != next.ThreadId) return true;
            if (next.ThreadId != null) return false;
            if (previous.PageTitles.Length > 0 && next.PageTitles.Length > 0
                && !previous.PageTitles.Intersect(next.PageTitles).Any()) return true;
            if (previous.SelectedTitles.Length > 0 && next.SelectedTitles.Length > 0)
                return !previous.SelectedTitles.Intersect(next.SelectedTitles).Any();
            if (previous.HeaderTitles.Length > 0 && next.HeaderTitles.Length > 0
                && !previous.HeaderTitles.Intersect(next.HeaderTitles).Any()) return true;
            return previous.PromptFingerprints.Length > 0 && next.PromptFingerprints.Length > 0
                && !previous.PromptFingerprints.Intersect(next.PromptFingerprints).Any();
        }

        private void UpdateQuota(DateTime now)
        {
            if (quotaTask != null && quotaTask.IsCompleted)
            {
                if (quotaTask.Status == TaskStatus.RanToCompletion && settings.ShowsQuota && !quotaCancel.IsCancellationRequested)
                {
                    accountQuota = quotaTask.Result;
                    quotaNotice = accountQuota == null ? "当前账号未返回对应额度" : "";
                }
                else if (quotaTask.IsFaulted)
                {
                    var failure = quotaTask.Exception.GetBaseException();
                    quotaNotice = failure is TimeoutException ? "额度查询超时" : "暂时无法读取账号额度";
                }
                quotaTask = null;
                quotaCancel.Dispose();
                quotaCancel = null;
            }
            if (settings.ShowsQuota && quotaTask == null && now >= nextQuota)
            {
                nextQuota = now.AddSeconds(60);
                lastQuotaStart = now;
                quotaCancel = new CancellationTokenSource();
                CancellationToken cancel = quotaCancel.Token;
                quotaTask = Task.Factory.StartNew(() => QuotaClient.Read(home, cancel), cancel,
                    TaskCreationOptions.None, TaskScheduler.Default);
            }
            QuotaSnapshot snapshot = settings.ShowsQuota ? accountQuota : null;
            var logged = reading != null && reading.Metrics != null ? reading.Metrics.LoggedQuota : null;
            if (settings.ShowsQuota && logged != null && DateTimeOffset.UtcNow - logged.Observed < TimeSpan.FromMinutes(3)
                && (snapshot == null || snapshot.Observed < logged.Observed)) snapshot = logged;
            string summary = snapshot == null ? quotaNotice : snapshot.Status;
            if (snapshot != null && !String.IsNullOrEmpty(quotaNotice) && !snapshot.FromLog) summary += " · " + quotaNotice;
            if (!ReferenceEquals(snapshot, shownQuota) || summary != shownQuotaStatus)
            {
                shownQuota = snapshot;
                shownQuotaStatus = summary;
                if (settingsWindow != null) settingsWindow.SetQuota(shownQuota, shownQuotaStatus);
            }
        }

        private void ApplySettings(BarSettings value)
        {
            bool previous = settings.ShowsQuota;
            settings = value;
            if (previous && !settings.ShowsQuota)
            {
                if (quotaCancel != null) quotaCancel.Cancel();
                accountQuota = null;
                quotaNotice = "等待首次刷新";
            }
            else if (!previous && settings.ShowsQuota) nextQuota = DateTime.MinValue;
        }

        private void OpenSettings()
        {
            if (settingsWindow == null)
            {
                settingsWindow = new SettingsWindow(settings, settingsStore, startup, ApplySettings,
                    () => nextQuota = lastQuotaStart.AddSeconds(10), appData);
                settingsWindow.FormClosed += (sender, args) => settingsWindow = null;
                settingsWindow.SetQuota(shownQuota, shownQuotaStatus);
                settingsWindow.SetUsage(lastUsage, status.Text, lastUsageAt);
                settingsWindow.Show();
            }
            if (settingsWindow.WindowState == FormWindowState.Minimized)
                settingsWindow.WindowState = FormWindowState.Normal;
            Desktop.EnsureVisible(settingsWindow.Handle, 5); // SW_SHOW
            settingsWindow.Activate();
        }

        private void SetPin(string value)
        {
            pin = value;
            generation++;
            follow.Checked = value == null;
            reading = null;
            nextRead = DateTime.MinValue;
            overlay.Clear();
        }

        private void PinDialog()
        {
            using (var dialog = new Form { Text = "固定 Codex 会话", StartPosition = FormStartPosition.CenterScreen,
                FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
                TopMost = true, ClientSize = new Size(420, 124), Font = SystemFonts.MessageBoxFont })
            {
                var label = new Label { Text = "从 Codex 复制会话链接，或粘贴会话 ID。", AutoSize = true, Location = new Point(12, 13) };
                var input = new TextBox { Text = pin ?? "", Location = new Point(12, 39), Width = 396, MaxLength = 1000 };
                var save = new Button { Text = "固定", Location = new Point(331, 78), Size = new Size(77, 30) };
                save.Click += (sender, args) =>
                {
                    string value = ThreadStore.ExtractId(input.Text, true);
                    if (value == null) { label.Text = "请输入有效的 Codex 会话链接或 ID。"; return; }
                    SetPin(value);
                    dialog.Close();
                };
                dialog.Controls.AddRange(new System.Windows.Forms.Control[] { label, input, save });
                dialog.AcceptButton = save;
                dialog.ShowDialog();
            }
        }

        protected override void ExitThreadCore()
        {
            timer.Stop();
            tray.Visible = false;
            base.ExitThreadCore();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                disposed = true;
                timer.Dispose();
                if (quotaCancel != null) quotaCancel.Cancel();
                ContextMenuStrip menu = tray.ContextMenuStrip;
                tray.Dispose();
                menu.Dispose();
                if (settingsWindow != null) settingsWindow.Dispose();
                overlay.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class Layer : NativeWindow, IDisposable
    {
        private Rectangle lastBounds;
        private UsageMetrics lastMetrics;
        private Capsule lastCapsule;
        private DetailPopup popup;
        private BarSettings lastSettings;
        private Placement lastPlacement;
        private DetailSection? lastSelection;
        private QuotaSnapshot lastQuota;
        private IntPtr target;
        private bool lastDark;
        private bool visible;
        private Font font;
        private float lastScale;

        internal Layer()
        {
            CreateHandle(new CreateParams {
                Caption = "Codex 用量栏",
                Style = unchecked((int)0x80000000), // WS_POPUP
                ExStyle = 0x00080000 | 0x08000000 | 0x00000080 | 0x00000008
                // Layered, no activation, tool window, topmost.
            });
        }

        internal void Display(Placement placement, UsageMetrics metrics, BarSettings settings, QuotaSnapshot quota = null)
        {
            int available = placement.Right - placement.Left;
            int y = placement.Model.Top + placement.Model.Height / 2;
            if (available < 48 * placement.Scale)
            {
                Clear();
                return;
            }
            if (font == null || placement.Scale != lastScale)
            {
                if (font != null) font.Dispose();
                font = new Font("Segoe UI", 12 * placement.Scale, FontStyle.Regular, GraphicsUnit.Pixel);
                lastScale = placement.Scale;
                lastMetrics = null;
            }
            bool dark = Desktop.IsDark(placement.Right + 2, y);
            if (visible && !Desktop.IsWindowVisible(Handle)) visible = false;
            if (visible && ReferenceEquals(metrics, lastMetrics) && ReferenceEquals(settings, lastSettings)
                && ReferenceEquals(lastQuota, quota) && ReferenceEquals(lastPlacement, placement) && lastDark == dark) return;
            using (var canvas = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(canvas))
            {
                var capsule = Capsule.Fit(g, font, metrics, available, placement.Scale, settings, quota);
                if (capsule != null)
                {
                    if (target != placement.Window && popup != null) popup.Dismiss();
                    if (popup != null && popup.Visible && !capsule.CanShow(popup.Section)) popup.Dismiss();
                    target = placement.Window;
                    lastPlacement = placement;
                    lastSettings = settings;
                    lastQuota = quota;
                    Rectangle bounds = settings.Place(placement, new Size(capsule.Width, capsule.Height));
                    Paint(bounds, capsule, metrics, dark);
                    if (popup != null && popup.Visible && visible) Present(popup.Section);
                    return;
                }
            }
            Clear();
        }

        private void Paint(Rectangle bounds, Capsule capsule, UsageMetrics metrics, bool dark)
        {
            using (var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                DetailSection? selected = popup != null && popup.Visible ? popup.Section : (DetailSection?)null;
                capsule.Draw(graphics, font, Palette.For(dark), 0, 0, selected);
                IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
                if (dc == IntPtr.Zero) { Clear(); return; }
                IntPtr image = IntPtr.Zero, previous = IntPtr.Zero;
                try
                {
                    image = bitmap.GetHbitmap(Color.FromArgb(0));
                    previous = SelectObject(dc, image);
                    var position = new Desktop.Point(bounds.Left, bounds.Top);
                    var size = new Desktop.Point(bounds.Width, bounds.Height);
                    var origin = new Desktop.Point(0, 0);
                    var blend = new Blend { Alpha = 255, Format = 1 };
                    if (!UpdateLayeredWindow(Handle, IntPtr.Zero, ref position, ref size, dc, ref origin, 0, ref blend, 2))
                    {
                        Clear();
                        return;
                    }
                    if (!visible) Desktop.EnsureVisible(Handle, 4); // SW_SHOWNOACTIVATE
                    visible = true;
                    lastBounds = bounds;
                    lastCapsule = capsule;
                    lastMetrics = metrics;
                    lastDark = dark;
                    lastSelection = selected;
                }
                finally
                {
                    if (previous != IntPtr.Zero) SelectObject(dc, previous);
                    if (image != IntPtr.Zero) DeleteObject(image);
                    DeleteDC(dc);
                }
            }
        }

        internal void Clear()
        {
            if (visible) Desktop.ShowWindow(Handle, 0);
            visible = false;
            if (popup != null && popup.Visible) popup.Dismiss();
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x84) // WM_NCHITTEST
            {
                bool enabled = visible && lastCapsule != null && lastCapsule.CanOpen;
                message.Result = enabled ? new IntPtr(1) : new IntPtr(-1); // HTCLIENT or HTTRANSPARENT
                return;
            }
            if (message.Msg == 0x21) { message.Result = new IntPtr(3); return; } // MA_NOACTIVATE
            if (message.Msg == 0x20 && visible && lastCapsule != null && lastCapsule.CanOpen)
            {
                Cursor.Current = Cursors.Hand;
                message.Result = new IntPtr(1);
                return;
            }
            if (message.Msg == 0x202 && visible && lastCapsule != null && lastCapsule.CanOpen) // WM_LBUTTONUP
            {
                Open(lastCapsule.SectionAt(unchecked((short)(message.LParam.ToInt64() & 0xffff))));
                return;
            }
            base.WndProc(ref message);
        }

        private void Open(DetailSection section)
        {
            if (popup == null)
            {
                popup = new DetailPopup();
                popup.VisibleChanged += (sender, args) => PaintSelection();
            }
            bool animate = SystemInformation.IsMenuAnimationEnabled;
            if (popup.Visible && popup.Section == section && !popup.IsClosing) { popup.Collapse(animate); return; }
            Present(section);
            popup.Expand(animate);
            PaintSelection();
        }

        private void PaintSelection()
        {
            DetailSection? selected = popup.Visible ? popup.Section : (DetailSection?)null;
            if (visible && lastSelection != selected) Paint(lastBounds, lastCapsule, lastMetrics, lastDark);
        }

        private void Present(DetailSection section)
        {
            int offset = section == DetailSection.Session ? 0 : section == DetailSection.Quota ? lastCapsule.QuotaSplit : lastCapsule.Split;
            int left = lastBounds.Left + Math.Min(offset, lastBounds.Width - 1);
            popup.Present(section, lastMetrics, lastDark, new Rectangle(left, lastBounds.Top, lastBounds.Right - left, lastBounds.Height),
                lastScale, lastQuota, lastSettings);
        }

        internal IntPtr DetailTarget(IntPtr foreground)
        {
            return popup != null && popup.Visible && popup.Handle == foreground ? target : IntPtr.Zero;
        }

        public void Dispose()
        {
            visible = false;
            if (popup != null) popup.Dispose();
            if (font != null) font.Dispose();
            DestroyHandle();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Blend { internal byte Op, Flags, Alpha, Format; }
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr screen,
            ref Desktop.Point pos, ref Desktop.Point size, IntPtr source, ref Desktop.Point origin, int color, ref Blend blend, int flags);
    }
}
