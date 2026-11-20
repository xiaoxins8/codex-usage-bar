using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using System.Windows.Forms;

namespace CodexUsageBar
{
    internal sealed class Placement
    {
        internal IntPtr Window;
        internal Rectangle Model, WindowBounds;
        internal int Left, Right;
        internal float Scale;
        internal string ThreadId;
        internal string[] PageTitles = new string[0], SelectedTitles, HeaderTitles;
        internal string[] PromptFingerprints;
        internal bool MenuOpen;

        internal Placement At(Rectangle bounds)
        {
            if (bounds == WindowBounds) return this;
            if (bounds.Size != WindowBounds.Size) return null;
            var moved = (Placement)MemberwiseClone();
            int x = bounds.Left - WindowBounds.Left, y = bounds.Top - WindowBounds.Top;
            moved.Model.Offset(x, y);
            moved.WindowBounds = bounds;
            moved.Left += x;
            moved.Right += x;
            return moved;
        }
    }

    internal sealed class Control
    {
        internal ControlType Type;
        internal string Name, Help, Value, Id;
        internal string PromptFingerprint;
        internal Rectangle Bounds;
        internal bool Selected, Expanded;

        internal bool IsButton
        {
            get { return Type == ControlType.Button || Type == ControlType.ComboBox || Type == ControlType.SplitButton; }
        }
    }

    internal static class Composer
    {
        private static readonly Regex ModelName = new Regex(
            @"^(?:GPT\s*[-‑–]?\s*\d|\d+\.\d+\s+(?:Sol|Astra|Terra|Luna)\b|O[134](?:[-\s]|$)|Claude\b|DeepSeek\b|Gemini\b|Qwen\b|Kimi\b|GLM[-\s]|Codex\b)",
            RegexOptions.IgnoreCase);
        private static readonly Regex ModelLabel = new Regex(
            @"^(?:默认|預設|自動|自动|标准|標準|扩展|擴展|低|中|高|极高|極高|超高|选择(?:模型|推理强度|推理力度)|選擇(?:模型|推理強度|推理程度)|Auto|Default|Standard|Extended|Low|Medium|High|Extra high|Select (?:model|effort)|Model and reasoning(?: effort)?)$",
            RegexOptions.IgnoreCase);
        private static readonly Regex ContextLabel = new Regex(
            @"^(?:Context (?:window )?usage|上下文(?:用量|使用情况)|情境使用量|コンテキスト使用状況)\s*[:：]",
            RegexOptions.IgnoreCase);

        internal static Placement Inspect(IntPtr window, Rectangle bounds, float scale)
        {
            var controls = new List<Control>();
            AutomationElement root = AutomationElement.FromHandle(window);
            string windowTitle = ThreadStore.Normalize(root.Current.Name);
            var condition = new OrCondition(new[] {
                ControlType.Button, ControlType.SplitButton, ControlType.ComboBox, ControlType.Hyperlink,
                ControlType.Edit, ControlType.Document, ControlType.Header, ControlType.HeaderItem,
                ControlType.TabItem, ControlType.ListItem, ControlType.Menu, ControlType.Text,
                ControlType.Image, ControlType.ProgressBar, ControlType.DataItem, ControlType.Separator
            }.Select(type => (Condition)new PropertyCondition(AutomationElement.ControlTypeProperty, type)).ToArray());
            var request = new CacheRequest { AutomationElementMode = AutomationElementMode.None };
            foreach (AutomationProperty property in new[] {
                AutomationElement.BoundingRectangleProperty, AutomationElement.NameProperty,
                AutomationElement.AutomationIdProperty, AutomationElement.ControlTypeProperty,
                AutomationElement.HelpTextProperty, AutomationElement.IsOffscreenProperty,
                ValuePattern.ValueProperty, SelectionItemPattern.IsSelectedProperty,
                ExpandCollapsePattern.ExpandCollapseStateProperty
            }) request.Add(property);
            using (request.Activate())
            {
                foreach (AutomationElement element in root.FindAll(TreeScope.Descendants, condition))
                {
                    var current = element.Cached;
                    if (current.IsOffscreen || current.BoundingRectangle.IsEmpty) continue;
                    System.Windows.Rect box = current.BoundingRectangle;
                    if (box.Width < 1 || box.Height < 1) continue;
                    ControlType type = current.ControlType;
                    Rectangle screen = Rectangle.FromLTRB((int)box.Left, (int)box.Top, (int)Math.Ceiling(box.Right), (int)Math.Ceiling(box.Bottom));
                    if ((type == ControlType.Text || type == ControlType.DataItem) && box.Top > bounds.Top + 140 * scale)
                    {
                        if (bounds.Contains(screen))
                        {
                            string fingerprint = ThreadStore.Fingerprint(current.Name);
                            if (fingerprint != null) controls.Add(new Control { Type = type, Bounds = screen, Name = "", PromptFingerprint = fingerprint });
                        }
                        continue;
                    }
                    controls.Add(new Control {
                        Type = type,
                        Bounds = screen,
                        Name = ThreadStore.Normalize(current.Name),
                        Id = current.AutomationId,
                        Help = ThreadStore.Normalize(current.HelpText),
                        // Inspect document/link addresses only. Editable text can contain an unsent prompt.
                        Value = type == ControlType.Document || type == ControlType.Hyperlink
                            ? element.GetCachedPropertyValue(ValuePattern.ValueProperty, true) as string : null,
                        Selected = Equals(element.GetCachedPropertyValue(SelectionItemPattern.IsSelectedProperty, true), true),
                        Expanded = Equals(element.GetCachedPropertyValue(ExpandCollapsePattern.ExpandCollapseStateProperty, true),
                            ExpandCollapseState.Expanded)
                    });
                }
            }
            var placement = Locate(window, bounds, scale, controls);
            // The app's own web document carries the open thread's title (document.title),
            // independent of sidebar state or UI language. Embedded MCP/web documents are https.
            if (placement != null)
                placement.PageTitles = controls.Where(c => c.Type == ControlType.Document && c.Name.Length > 0
                        && c.Value != null && c.Value.StartsWith("app://", StringComparison.OrdinalIgnoreCase)
                        && c.Name != "Codex" && c.Name != "ChatGPT")
                    .Select(c => c.Name).Distinct().ToArray();
            if (placement != null && windowTitle.Length > 0)
            {
                string title = Regex.Replace(windowTitle, @"\s+[-–—|]\s+(?:ChatGPT|Codex)$", "", RegexOptions.IgnoreCase);
                if (title != "Codex" && title != "ChatGPT")
                    placement.HeaderTitles = placement.HeaderTitles.Concat(new[] { title }).Distinct().ToArray();
            }
            return placement;
        }

        internal static Placement Locate(IntPtr window, Rectangle bounds, float scale, List<Control> controls)
        {
            var buttons = controls.Where(c => c.IsButton && c.Bounds.Height >= 15 * scale && c.Bounds.Height <= 65 * scale
                && c.Bounds.Width >= 20 * scale && bounds.Contains(c.Bounds)).ToArray();
            var edits = controls.Where(c => c.Type == ControlType.Edit && c.Bounds.Width >= 160 * scale).ToArray();
            var knownContexts = controls.Where(c => c.Name != null && ContextLabel.IsMatch(c.Name)).ToArray();
            var anchors = buttons.Where(c => c.Bounds.Top > bounds.Top + 0.32 * bounds.Height
                && (IsModel(c) || c.Bounds.Width >= 48 * scale && knownContexts.Any(context =>
                    context.Bounds.Right <= c.Bounds.Left && c.Bounds.Left - context.Bounds.Right <= 42 * scale
                    && Math.Abs(Middle(c.Bounds) - Middle(context.Bounds)) <= 11 * scale)))
                .OrderByDescending(c => c.Bounds.Top).ThenByDescending(c => c.Bounds.Right).ToArray();
            foreach (Control anchor in anchors)
            {
                Rectangle box = anchor.Bounds;
                Control editor = edits.Where(e => e.Bounds.Left <= box.Left && e.Bounds.Right >= box.Left
                    && e.Bounds.Bottom <= box.Top + 9 * scale && box.Top - e.Bounds.Bottom < 145 * scale)
                    .OrderByDescending(e => e.Bounds.Bottom).FirstOrDefault();
                var row = buttons.Where(c => c != anchor && Math.Abs(Middle(c.Bounds) - Middle(box)) <= 11 * scale).ToArray();
                Control left = row.Where(c => c.Bounds.Right <= box.Left + 2 * scale)
                    .OrderByDescending(c => c.Bounds.Right).FirstOrDefault();
                bool right = row.Any(c => c.Bounds.Left >= box.Right - 2 * scale && c.Bounds.Left < box.Right + 180 * scale);
                if (left == null && editor == null) continue;
                if (!right && editor == null) continue;
                int gap = (int)Math.Ceiling(9 * scale);
                Control context = knownContexts.Where(c => c.Bounds.Left < box.Left && c.Bounds.Left >= box.Left - 120 * scale
                    && Math.Abs(Middle(c.Bounds) - Middle(box)) <= 11 * scale)
                    .OrderBy(c => c.Bounds.Left).FirstOrDefault();
                // Reserve the context donut even when its accessible node is missing.
                int end = context == null ? box.Left - (int)Math.Ceiling(46 * scale) : context.Bounds.Left - gap;
                int start = editor == null ? bounds.Left : editor.Bounds.Left;
                foreach (Control neighbor in controls.Where(c => c != anchor && (c.IsButton || c.Type == ControlType.Hyperlink)
                    && c.Bounds.Width >= 12 * scale && c.Bounds.Left < end
                    && Math.Abs(Middle(c.Bounds) - Middle(box)) <= 11 * scale))
                    start = Math.Max(start, neighbor.Bounds.Right);
                start += gap;
                if (end - start < 48 * scale) continue;
                var titles = HeaderTitles(controls, bounds, start, box.Right, scale);
                Rectangle strip = Rectangle.FromLTRB(start, box.Top, end, box.Bottom);
                int contentRight = editor != null ? editor.Bounds.Right : Math.Min(bounds.Right, box.Right + (int)(80 * scale));
                return new Placement {
                    Window = window, Model = box, WindowBounds = bounds, Left = start, Right = end, Scale = scale,
                    ThreadId = RouteId(controls, box), HeaderTitles = titles,
                    SelectedTitles = controls.Where(c => c.Selected && c.Name.Length > 0)
                        .Select(c => c.Name).Distinct().ToArray(),
                    PromptFingerprints = controls.Where(c => c.PromptFingerprint != null && c.Bounds.Bottom < box.Top
                        && c.Bounds.Left >= start - 40 * scale && c.Bounds.Right >= contentRight - 176 * scale
                        && c.Bounds.Right <= contentRight + 24 * scale)
                        .OrderBy(c => c.Bounds.Top).Take(22).Select(c => c.PromptFingerprint).Distinct().ToArray(),
                    MenuOpen = anchor.Expanded || controls.Any(c => c.Type == ControlType.Menu && c.Bounds.Width >= 25 * scale
                        && c.Bounds.IntersectsWith(strip))
                };
            }
            return null;
        }

        private static bool IsModel(Control control)
        {
            return ModelName.IsMatch(control.Name) || ModelLabel.IsMatch(control.Name) || ModelLabel.IsMatch(control.Help)
                || String.Equals(control.Id, "model-picker", StringComparison.OrdinalIgnoreCase);
        }

        private static string RouteId(IEnumerable<Control> controls, Rectangle model)
        {
            var ids = controls.Where(c => c.Type == ControlType.Document && c.Bounds.Contains(model))
                .Select(c => ThreadStore.ExtractId(c.Value, false)).Where(id => id != null).Distinct().ToArray();
            if (ids.Length == 1) return ids[0];
            ids = controls.Where(c => c.Selected && c.Type == ControlType.Hyperlink)
                .Select(c => ThreadStore.ExtractId(c.Value, false)).Where(id => id != null).Distinct().ToArray();
            return ids.Length == 1 ? ids[0] : null;
        }

        private static string[] HeaderTitles(List<Control> controls, Rectangle window, int composerLeft, int composerRight, float scale)
        {
            // Codex thread headers can be wider than, and to the left of, the centered composer.
            // The sidebar splitter marks where the thread area begins. Without one the sidebar is
            // collapsed and the title sits at the far left, next to the navigation rail.
            var splitter = controls.Where(c => c.Type == ControlType.Separator && c.Bounds.Height > window.Height / 2
                && c.Bounds.Left < composerLeft).OrderByDescending(c => c.Bounds.Left).FirstOrDefault();
            int sidebarLeft = splitter != null ? splitter.Bounds.Left : window.Left;
            int floor = Math.Min(composerLeft - (int)(12 * scale), sidebarLeft);
            var top = controls.Where(c => c.Bounds.Left >= floor && c.Bounds.Left < composerRight
                && c.Bounds.Top >= window.Top && c.Bounds.Top < window.Top + 110 * scale
                && c.Name.Length > 0 && c.Name.Length <= 200
                && (c.Type == ControlType.Text || c.IsButton || c.Type == ControlType.Header || c.Type == ControlType.HeaderItem))
                .Select(c => c.Name);
            return top.Distinct().ToArray();
        }

        private static int Middle(Rectangle rectangle) { return rectangle.Top + rectangle.Height / 2; }

        internal static string ResolveThread(Placement placement, List<ThreadEntry> entries)
        {
            if (placement.ThreadId != null) return placement.ThreadId;
            var fingerprints = placement.PromptFingerprints ?? new string[0];
            foreach (string[] titles in new[] { placement.PageTitles, placement.SelectedTitles, placement.HeaderTitles })
            {
                if (titles == null || titles.Length == 0) continue;
                var named = entries.Where(row => !String.IsNullOrEmpty(row.Name)
                    && titles.Contains(row.Name, StringComparer.Ordinal))
                    .GroupBy(row => row.Id, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToList();
                if (named.Count == 1) return named[0].Id;
                // Different titles matched different threads: these hints disagree, try the next source.
                if (named.Count == 0 || named.Select(row => row.Name).Distinct().Count() > 1) continue;
                string pick = PickNamesake(named, fingerprints);
                if (pick != null) return pick;
            }
            var prompts = entries.Where(row => (row.PromptFingerprint != null && fingerprints.Contains(row.PromptFingerprint))
                    || (row.RolloutFingerprint != null && fingerprints.Contains(row.RolloutFingerprint)))
                .Select(row => row.Id).Distinct().Take(2).ToArray();
            return prompts.Length == 1 ? prompts[0] : null;
        }

        // Codex auto-names short chats identically ("回应问候", "Greeting"...), so one visible title
        // can belong to several threads. Narrow by visible first prompt, then live over archived,
        // then the thread Codex wrote to last, which is the one being looked at.
        internal static string PickNamesake(List<ThreadEntry> rows, string[] fingerprints)
        {
            var seen = rows.Where(row => (row.PromptFingerprint != null && fingerprints.Contains(row.PromptFingerprint))
                || (row.RolloutFingerprint != null && fingerprints.Contains(row.RolloutFingerprint))).ToList();
            if (seen.Count == 1) return seen[0].Id;
            if (seen.Count > 1) rows = seen;
            var live = rows.Where(row => !row.Archived).ToList();
            if (live.Count == 1) return live[0].Id;
            if (live.Count > 1) rows = live;
            var latest = rows.OrderByDescending(row => row.Updated).Take(2).ToArray();
            // Without timestamps (old index formats) there is nothing to rank by.
            return latest[0].Updated > 0 && latest[0].Updated != latest[1].Updated ? latest[0].Id : null;
        }
    }

    internal static class Desktop
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect { internal int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        internal struct Point { internal int X, Y; internal Point(int x, int y) { X = x; Y = y; } }

        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern uint GetPixel(IntPtr dc, int x, int y);

        internal static IntPtr CodexWindow()
        {
            IntPtr window = GetForegroundWindow();
            if (window == IntPtr.Zero || IsIconic(window)) return IntPtr.Zero;
            uint pid;
            GetWindowThreadProcessId(window, out pid);
            try
            {
                using (Process process = Process.GetProcessById((int)pid))
                {
                    string name = process.ProcessName;
                    if (String.Equals(name, "ChatGPT", StringComparison.OrdinalIgnoreCase)
                        || String.Equals(name, "Codex", StringComparison.OrdinalIgnoreCase))
                        return window;
                }
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            return IntPtr.Zero;
        }

        internal static bool IsUsable(IntPtr window)
        {
            return window != IntPtr.Zero && IsWindow(window) && IsWindowVisible(window) && !IsIconic(window);
        }

        internal static Rectangle Bounds(IntPtr window)
        {
            Rect rect;
            return GetWindowRect(window, out rect) ? Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom) : Rectangle.Empty;
        }

        internal static float Scale(IntPtr window) { return Math.Max(1, GetDpiForWindow(window) / 96f); }

        internal static void EnsureVisible(IntPtr window, int command)
        {
            if (IsWindowVisible(window)) return;
            ShowWindow(window, command);
            // A background launcher can set SW_HIDE for the process's first ShowWindow call.
            if (!IsWindowVisible(window)) ShowWindow(window, command);
        }

        internal static bool IsDark(int x, int y)
        {
            IntPtr dc = GetDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) return false;
            try
            {
                uint color = GetPixel(dc, x, y);
                if (color == UInt32.MaxValue) return false;
                return (color & 255) * 0.2126 + ((color >> 8) & 255) * 0.7152 + ((color >> 16) & 255) * 0.0722 < 128;
            }
            finally { ReleaseDC(IntPtr.Zero, dc); }
        }
    }
}
