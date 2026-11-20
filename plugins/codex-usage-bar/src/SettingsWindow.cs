using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CodexUsageBar
{
    // App mark from the v3 design: electric-blue rounded square with three white bars.
    internal static class AppIcon
    {
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);

        internal static Icon Create(int size)
        {
            using (var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var square = Capsule.Rounded(new RectangleF(0.5f, 0.5f, size - 1f, size - 1f), size * 0.22f))
                    using (var blue = new SolidBrush(Color.FromArgb(13, 69, 240)))
                        g.FillPath(blue, square);
                    float bar = size * 0.12f, gap = size * 0.07f, floor = size * 0.75f, x = (size - (3 * bar + 2 * gap)) / 2;
                    using (var white = new SolidBrush(Color.White))
                        foreach (float height in new[] { 0.22f, 0.34f, 0.48f })
                        {
                            using (var column = Capsule.Rounded(new RectangleF(x, floor - height * size, bar, height * size), bar * 0.35f))
                                g.FillPath(white, column);
                            x += bar + gap;
                        }
                }
                IntPtr handle = bitmap.GetHicon();
                try { return (Icon)Icon.FromHandle(handle).Clone(); }
                finally { DestroyIcon(handle); }
            }
        }

        internal static bool DarkMode()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return key != null && Equals(key.GetValue("AppsUseLightTheme"), 0);
            }
            catch (SecurityException) { return false; }
        }
    }

    internal static class UiAssets
    {
        private static readonly Assembly Host = typeof(UiAssets).Assembly;

        internal static void Register()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                string name = new AssemblyName(args.Name).Name;
                if (name != "Microsoft.Web.WebView2.Core" && name != "Microsoft.Web.WebView2.WinForms") return null;
                return Assembly.Load(Read(name + ".dll"));
            };
        }

        internal static byte[] Read(string resource)
        {
            using (Stream source = Host.GetManifestResourceStream(resource))
            using (var bytes = new MemoryStream())
            {
                if (source == null) throw new IOException("缺少界面资源 " + resource);
                source.CopyTo(bytes);
                return bytes.ToArray();
            }
        }

        private static Task<CoreWebView2Environment> environment;
        private static bool loaderSet;

        // WebView2 allows SetLoaderDllFolderPath only once per process, before any other environment call,
        // so every settings window shares one loader path and one environment. A failed creation is retried.
        internal static Task<CoreWebView2Environment> WebEnvironment(string dataFolder)
        {
            if (environment == null || environment.IsFaulted || environment.IsCanceled)
            {
                if (!loaderSet) { SetLoader(dataFolder); loaderSet = true; }
                environment = CoreWebView2Environment.CreateAsync(null, Path.Combine(dataFolder, "webview-profile"));
            }
            return environment;
        }

        private static void SetLoader(string dataFolder)
        {
            string folder = Path.Combine(dataFolder, "webview-sdk-1.0.3405.78");
            string path = Path.Combine(folder, "WebView2Loader.dll");
            byte[] expected = Read("WebView2Loader.dll");
            Directory.CreateDirectory(folder);
            if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(expected))
            {
                string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllBytes(temp, expected);
                    if (File.Exists(path)) File.Replace(temp, path, null);
                    else File.Move(temp, path);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            CoreWebView2Environment.SetLoaderDllFolderPath(folder);
        }
    }

    internal sealed class SettingsWindow : Form
    {
        private const string SettingsPage = "https://codex-usage-bar.invalid/settings";
        private readonly SettingsStore store;
        private readonly StartupRegistration startup;
        private readonly Action<BarSettings> applied;
        private readonly Action refresh;
        private readonly string dataFolder;
        private readonly WebView2 web = new WebView2 { Dock = DockStyle.Fill };
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 8192 };
        private readonly Label state = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Text = "正在加载设置…" };
        private readonly Timer pageReady = new Timer { Interval = 7000 };
        private BarSettings settings;
        private QuotaSnapshot quota;
        private string quotaStatus, usageJson, failure;
        private object usage;
        private bool ready, started, usingFallback;

        internal SettingsWindow(BarSettings settings, SettingsStore store, StartupRegistration startup,
            Action<BarSettings> applied, Action refresh, string dataFolder)
        {
            this.settings = settings;
            this.store = store;
            this.startup = startup;
            this.applied = applied;
            this.refresh = refresh;
            this.dataFolder = dataFolder;
            Text = "Codex 用量栏 控制中心";
            Icon = AppIcon.Create(32);
            StartPosition = FormStartPosition.CenterScreen;
            // Window color of the v3 palette: matches the title bar and sidebar, and the resize strip
            // around the page, so nothing flashes white while WebView2 starts.
            BackColor = AppIcon.DarkMode() ? Color.FromArgb(26, 25, 23) : Color.FromArgb(248, 244, 238);
            state.BackColor = BackColor;
            state.ForeColor = AppIcon.DarkMode() ? Color.FromArgb(163, 159, 152) : Color.FromArgb(107, 111, 118);
            web.DefaultBackgroundColor = BackColor;
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            // Same proportions as the v3 mockup (932×705), about 1.33× its size; smaller screens get a smaller window.
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            float dpi = DeviceDpi / 96f;
            double fit = Math.Min(1.33, Math.Min(area.Width * 0.9 / dpi / DesignWidth, area.Height * 0.9 / dpi / DesignHeight));
            // Outer size: with the page-drawn title bar the whole window is client area.
            Size = new Size((int)(DesignWidth * Math.Max(0.85, fit)), (int)(DesignHeight * Math.Max(0.85, fit)));
            MinimumSize = new Size(760, 560);
            Controls.Add(web);
            Controls.Add(state);
            state.BringToFront();
            pageReady.Tick += (sender, args) =>
            {
                pageReady.Stop();
                if (!ready && !IsDisposed) Fail("页面 " + pageReady.Interval / 1000 + " 秒内未就绪" + (pageError != null ? "：" + pageError : ""));
            };
        }

        private string pageError;

        // Records why the React page could not be used, in the fallback window and in settings-ui.log.
        private void Fail(string reason)
        {
            failure = reason;
            Log("fallback: " + reason);
            if (!IsDisposed) ShowFallback();
        }

        private void Log(string line)
        {
            try
            {
                File.AppendAllText(Path.Combine(dataFolder, "settings-ui.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " v" + AppVersion + " " + line + Environment.NewLine, Encoding.UTF8);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static string Describe(Exception error)
        {
            error = error is AggregateException ? error.GetBaseException() : error;
            return error.GetType().Name + " 0x" + error.HResult.ToString("X8") + " " + error.Message;
        }

        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref Margins margins);
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr window);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [StructLayout(LayoutKind.Sequential)] private struct Margins { public int Left, Right, Top, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct NcCalcSize { public NativeRect Proposed, Old, OldClient; public IntPtr Position; }

        // The page draws the v3 title bar; the window keeps its caption styles so snapping,
        // the taskbar, Alt+Space, the shadow and Windows 11 rounded corners keep working.
        private bool customFrame = true;
        private const int WM_NCCALCSIZE = 0x83, WM_NCHITTEST = 0x84, WM_NCLBUTTONDOWN = 0xA1, HTCLIENT = 1, HTCAPTION = 2;

        protected override void OnHandleCreated(EventArgs args)
        {
            base.OnHandleCreated(args);
            bool dark = AppIcon.DarkMode();
            int immersive = dark ? 1 : 0, round = 2;
            DwmSetWindowAttribute(Handle, 20, ref immersive, 4); // DWMWA_USE_IMMERSIVE_DARK_MODE
            DwmSetWindowAttribute(Handle, 33, ref round, 4);     // DWMWA_WINDOW_CORNER_PREFERENCE: round
            var margins = new Margins { Top = 1 };               // keeps the DWM shadow without a caption
            DwmExtendFrameIntoClientArea(Handle, ref margins);
            SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0027); // SWP_FRAMECHANGED | NOMOVE | NOSIZE | NOZORDER
        }

        private int Scaled(int value)
        {
            double dpi = IsHandleCreated ? (double)GetDpiForWindow(Handle) : DeviceDpi; // never force the handle from the constructor
            return (int)Math.Round(value * dpi / 96.0);
        }

        // WinForms re-adds the native caption and borders when it restores a maximized window, so the
        // window would grow on every round trip; remember the normal bounds and put them back.
        private Rectangle normalBounds;
        private FormWindowState lastState = FormWindowState.Normal;

        private void TrackBounds()
        {
            if (!customFrame || !IsHandleCreated) return;
            if (WindowState == FormWindowState.Normal && lastState != FormWindowState.Normal && !normalBounds.IsEmpty)
            {
                Rectangle restore = normalBounds;
                BeginInvoke((Action)(() => { if (WindowState == FormWindowState.Normal && Bounds != restore) Bounds = restore; }));
            }
            else if (WindowState == FormWindowState.Normal) normalBounds = Bounds;
            lastState = WindowState;
        }

        protected override void OnMove(EventArgs args)
        {
            base.OnMove(args);
            if (WindowState == FormWindowState.Normal && lastState == FormWindowState.Normal) TrackBounds();
        }

        protected override void OnResize(EventArgs args)
        {
            base.OnResize(args);
            TrackBounds();
            // A thin strip around the page stays with the form, so the edges can still resize the window.
            int edge = customFrame && WindowState == FormWindowState.Normal ? Scaled(5) : 0;
            if (Padding.All != edge) Padding = new Padding(edge);
            if (ready && !usingFallback) Send(new { type = "window", maximized = WindowState == FormWindowState.Maximized });
        }

        // The page is written in the units of the v3 mockup (a 932×705 window) and shown at one fixed
        // zoom. The zoom never follows the window size: rescaling on every resize made the UI twitch.
        private const double DesignWidth = 932, DesignHeight = 705, PageZoom = 1.25;

        private void UpdateZoom()
        {
            if (web.CoreWebView2 != null && Math.Abs(web.ZoomFactor - PageZoom) > 0.001) web.ZoomFactor = PageZoom;
        }

        protected override void WndProc(ref Message m)
        {
            if (customFrame && m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
            {
                // Client area = whole window. A maximized window overhangs the screen by its frame, so inset it.
                if (IsZoomed(Handle))
                {
                    var size = (NcCalcSize)Marshal.PtrToStructure(m.LParam, typeof(NcCalcSize));
                    uint dpi = GetDpiForWindow(Handle);
                    int frame = GetSystemMetricsForDpi(32, dpi) + GetSystemMetricsForDpi(92, dpi); // SIZEFRAME + PADDEDBORDER
                    size.Proposed.Left += frame; size.Proposed.Top += frame; size.Proposed.Right -= frame; size.Proposed.Bottom -= frame;
                    Marshal.StructureToPtr(size, m.LParam, false);
                }
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
            if (customFrame && m.Msg == WM_NCHITTEST && (int)m.Result == HTCLIENT && WindowState == FormWindowState.Normal)
                m.Result = (IntPtr)EdgeHit(m.LParam);
        }

        // Without a native caption the client area is the whole window; otherwise WinForms adds caption
        // and borders again when restoring from maximized, and the window grows on every round trip.
        protected override Size SizeFromClientSize(Size clientSize)
        {
            return customFrame ? clientSize : base.SizeFromClientSize(clientSize);
        }

        private int EdgeHit(IntPtr position)
        {
            int x = unchecked((short)(long)position), y = unchecked((short)((long)position >> 16));
            Point p = PointToClient(new Point(x, y));
            int edge = Scaled(6);
            bool left = p.X < edge, right = p.X >= ClientSize.Width - edge, top = p.Y < edge, bottom = p.Y >= ClientSize.Height - edge;
            if (top) return left ? 13 : right ? 14 : 12;       // HTTOPLEFT, HTTOPRIGHT, HTTOP
            if (bottom) return left ? 16 : right ? 17 : 15;    // HTBOTTOMLEFT, HTBOTTOMRIGHT, HTBOTTOM
            return left ? 10 : right ? 11 : HTCLIENT;          // HTLEFT, HTRIGHT
        }

        private void WindowCommand(string action)
        {
            switch (action)
            {
                case "drag":
                    if (WindowState == FormWindowState.Maximized) break;
                    ReleaseCapture();
                    SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
                    break;
                case "minimize": WindowState = FormWindowState.Minimized; break;
                case "maximize": WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized; break;
                case "close": Close(); break;
            }
        }

        // The native frame comes back for the WinForms fallback, which has no title bar of its own.
        private void NativeFrame()
        {
            if (!customFrame) return;
            customFrame = false;
            Padding = new Padding(0);
            if (IsHandleCreated) SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0027);
        }

        protected override async void OnShown(EventArgs args)
        {
            base.OnShown(args);
            if (started) return;
            started = true;
            try
            {
                var environment = await Within(UiAssets.WebEnvironment(dataFolder));
                if (IsDisposed) return;
                state.Text = "正在启动 React 设置页…";
                await Within(web.EnsureCoreWebView2Async(environment));
                if (IsDisposed) return;
                var core = web.CoreWebView2;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.AreBrowserAcceleratorKeysEnabled = false;
                core.Settings.AreHostObjectsAllowed = false;
                core.Settings.IsZoomControlEnabled = false; // Ctrl+wheel must not fight the fitted zoom
                UpdateZoom();
                core.NavigationStarting += (sender, nav) => { if (nav.Uri != SettingsPage) nav.Cancel = true; };
                core.NewWindowRequested += (sender, popup) => popup.Handled = true;
                core.WebMessageReceived += Receive;
                core.NavigationCompleted += (sender, nav) =>
                {
                    if (!nav.IsSuccess) Log("navigation failed: " + nav.WebErrorStatus);
                };
                core.ProcessFailed += (sender, crash) =>
                {
                    Log("webview process failed: " + crash.ProcessFailedKind + " " + crash.Reason);
                    if (!usingFallback && !IsDisposed) Fail("WebView2 进程退出（" + crash.ProcessFailedKind + "）");
                };
                byte[] page = UiAssets.Read("settings.html");
                Log("webview " + environment.BrowserVersionString + " ready, navigating (" + page.Length / 1024 + " KB)");
                core.AddWebResourceRequestedFilter(SettingsPage, CoreWebView2WebResourceContext.Document);
                core.WebResourceRequested += (sender, request) =>
                {
                    if (request.Request.Uri == SettingsPage)
                        request.Response = environment.CreateWebResourceResponse(new MemoryStream(page, false), 200, "OK",
                            "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store");
                };
                pageReady.Start();
                core.Navigate(SettingsPage);
                state.Visible = false;
                web.BringToFront();
            }
            catch (Exception error)
            {
                Fail("WebView2 初始化失败：" + Describe(error));
            }
        }

        private static async Task<T> Within<T>(Task<T> task)
        {
            if (await Task.WhenAny(task, Task.Delay(10000)) != task)
                throw new TimeoutException("Windows WebView2 启动超时。");
            return await task;
        }

        private static async Task Within(Task task)
        {
            if (await Task.WhenAny(task, Task.Delay(10000)) != task)
                throw new TimeoutException("Windows WebView2 界面初始化超时。");
            await task;
        }

        private void Receive(object sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            if (args.Source != SettingsPage) return;
            var message = UsageLog.Parse(json, args.WebMessageAsJson);
            switch (UsageLog.Get(message, "type") as string)
            {
                case "window":
                    if (!usingFallback) WindowCommand(UsageLog.Get(message, "action") as string);
                    break;
                case "page-error":
                    pageError = UsageLog.Get(message, "message") as string;
                    Log("page error: " + pageError);
                    break;
                case "ready":
                    Log(usingFallback ? "page ready after fallback" : "page ready");
                    if (usingFallback) break;
                    pageReady.Stop();
                    ready = true;
                    PostSettings("");
                    SetQuota(quota, quotaStatus);
                    usageJson = null;
                    SendUsage();
                    break;
                case "apply":
                    if (ready) Apply(UsageLog.Get(message, "settings") as Dictionary<string, object>);
                    break;
                case "refresh":
                    if (ready && settings.ShowsQuota) refresh();
                    break;
            }
        }

        private void PostSettings(string message)
        {
            bool auto = false, available = true;
            try { auto = startup.Enabled(); }
            catch (Exception error)
            {
                if (!(error is UnauthorizedAccessException) && !(error is SecurityException) && !(error is IOException)) throw;
                available = false;
            }
            Send(new { type = "settings", notice = message, startupAvailable = available, version = AppVersion,
                frame = customFrame, maximized = WindowState == FormWindowState.Maximized, settings = new {
                Alignment = (int)settings.Alignment, settings.OffsetX, settings.OffsetY, settings.ShowFiveHour,
                settings.ShowWeek, Startup = auto, Language = settings.Language ?? ""
            } });
        }

        internal const string AppVersion = "0.6.2";

        // Latest session usage for the live preview. Sent only when it changes.
        internal void SetUsage(Reading latest, string status, DateTime at)
        {
            UsageMetrics m = latest == null ? null : latest.Metrics;
            usage = new {
                type = "usage", status = status, thread = latest == null ? null : latest.Thread,
                model = latest == null ? null : latest.Model, effort = latest == null ? null : latest.Effort,
                at = latest == null ? null : at.ToLocalTime().ToString("HH:mm:ss"),
                metrics = m == null || !m.Total.HasValue ? null : new {
                    pinned = m.Pinned, running = m.Running, rounds = m.Rounds, steps = m.Steps, total = m.Total,
                    input = m.Input, cached = m.Cached, output = m.Output, speed = m.Speed,
                    turnMs = m.TurnMs, firstTokenMs = m.FirstTokenMs
                }
            };
            if (ready) SendUsage();
        }

        private void SendUsage()
        {
            if (usage == null || IsDisposed || web.CoreWebView2 == null) return;
            string text = json.Serialize(usage);
            if (text == usageJson) return;
            usageJson = text;
            web.CoreWebView2.PostWebMessageAsJson(text);
        }

        internal void SetQuota(QuotaSnapshot snapshot, string status)
        {
            quota = snapshot;
            quotaStatus = status;
            if (ready) Send(new { type = "quota", status = status, quota = snapshot == null ? null :
                new { fiveHour = Window(snapshot.FiveHour), week = Window(snapshot.Week) } });
        }

        private static object Window(QuotaWindow window)
        {
            return window == null ? null : new { remaining = window.Remaining, resets = window.ResetLabel };
        }

        private void Apply(Dictionary<string, object> value)
        {
            if (value == null) return;
            var next = new BarSettings {
                Alignment = (BarAlignment)Int(value, "Alignment"), OffsetX = Int(value, "OffsetX"), OffsetY = Int(value, "OffsetY"),
                ShowFiveHour = Flag(value, "ShowFiveHour"), ShowWeek = Flag(value, "ShowWeek"),
                Language = UsageLog.Get(value, "Language") as string
            };
            string notice = Save(next, Flag(value, "Startup"), true);
            if (ReferenceEquals(settings, next)) PostSettings(notice);
            else Send(new { type = "error", notice = notice });
        }

        private string Save(BarSettings next, bool autostart, bool changeStartup)
        {
            try { store.Save(next); }
            catch (Exception error)
            {
                if (!(error is UnauthorizedAccessException) && !(error is IOException)) throw;
                return "无法保存设置。请检查本地目录权限。";
            }
            settings = next;
            applied(next);
            string notice = "设置已保存。切回 Codex 查看。";
            try { if (changeStartup) startup.Set(autostart); }
            catch (ArgumentException) { notice = "自启未保存。请把程序移到较短的路径。"; }
            catch (Exception error)
            {
                if (!(error is UnauthorizedAccessException) && !(error is SecurityException) && !(error is IOException)) throw;
                notice = "其他设置已保存，Windows 自启更新失败。";
            }
            return notice;
        }

        private static bool Flag(Dictionary<string, object> value, string key) { return Equals(UsageLog.Get(value, key), true); }
        private static int Int(Dictionary<string, object> value, string key)
        {
            object item = UsageLog.Get(value, key);
            return item is int ? (int)item : 0;
        }

        private void Send(object message)
        {
            if (!IsDisposed && web.CoreWebView2 != null) web.CoreWebView2.PostWebMessageAsJson(json.Serialize(message));
        }

        private void ShowFallback()
        {
            if (usingFallback) return;
            usingFallback = true;
            ready = false;
            pageReady.Stop();
            NativeFrame();
            state.Visible = web.Visible = false;
            var fields = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), FlowDirection = FlowDirection.TopDown,
                WrapContents = false, AutoScroll = true, Font = SystemFonts.MessageBoxFont };
            var message = new Label { AutoSize = true, MaximumSize = new Size(560, 0),
                Text = "WebView2 暂不可用，已打开备用设置。" + (failure != null ? "\r\n原因：" + failure : "") };
            fields.Controls.Add(message);
            var align = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160, AccessibleName = "对齐方式" };
            align.Items.AddRange(new object[] { "靠右", "居中", "靠左" });
            align.SelectedIndex = (int)settings.Alignment;
            var x = new NumericUpDown { Minimum = -240, Maximum = 240, Value = settings.OffsetX };
            var y = new NumericUpDown { Minimum = -32, Maximum = 24, Value = settings.OffsetY };
            fields.Controls.Add(new Label { AutoSize = true, Text = "对齐方式" });
            fields.Controls.Add(align);
            fields.Controls.Add(new Label { AutoSize = true, Text = "水平偏移（px）" });
            fields.Controls.Add(x);
            fields.Controls.Add(new Label { AutoSize = true, Text = "垂直偏移（px）" });
            fields.Controls.Add(y);
            var five = new CheckBox { Text = "显示五小时额度", AutoSize = true, Checked = settings.ShowFiveHour };
            var week = new CheckBox { Text = "显示每周额度", AutoSize = true, Checked = settings.ShowWeek };
            var automatic = new CheckBox { Text = "登录 Windows 时启动", AutoSize = true };
            try { automatic.Checked = startup.Enabled(); }
            catch (Exception error)
            {
                if (!(error is UnauthorizedAccessException) && !(error is SecurityException) && !(error is IOException)) throw;
                automatic.Enabled = false;
            }
            fields.Controls.Add(five);
            fields.Controls.Add(week);
            fields.Controls.Add(automatic);
            var save = new Button { Text = "应用设置", AutoSize = true };
            save.Click += (sender, args) =>
            {
                var next = new BarSettings { Alignment = (BarAlignment)align.SelectedIndex, OffsetX = (int)x.Value, OffsetY = (int)y.Value,
                    ShowFiveHour = five.Checked, ShowWeek = week.Checked, Language = settings.Language };
                message.Text = Save(next, automatic.Checked, automatic.Enabled);
            };
            fields.Controls.Add(save);
            Controls.Add(fields);
            fields.BringToFront();
            AcceptButton = save;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) pageReady.Dispose();
            base.Dispose(disposing);
        }
    }
}
