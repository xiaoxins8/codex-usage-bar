using System;
using System.Drawing;
using System.IO;
using System.Security;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace CodexUsageBar
{
    public enum BarAlignment { Right, Center, Left }

    public sealed class BarSettings
    {
        public BarAlignment Alignment { get; set; }
        public int OffsetX { get; set; }
        public int OffsetY { get; set; }
        public bool ShowFiveHour { get; set; }
        public bool ShowWeek { get; set; }
        // Settings window language: "" follows Windows, otherwise zh-CN, zh-TW or en.
        public string Language { get; set; }
        internal bool ShowsQuota { get { return ShowFiveHour || ShowWeek; } }

        public BarSettings()
        {
            Alignment = BarAlignment.Right;
        }

        internal void Normalize()
        {
            if (!Enum.IsDefined(typeof(BarAlignment), Alignment)) Alignment = BarAlignment.Right;
            OffsetX = Clamp(OffsetX, -240, 240);
            OffsetY = Clamp(OffsetY, -32, 24);
            if (Language != "zh-CN" && Language != "zh-TW" && Language != "en") Language = "";
        }

        internal Rectangle Place(Placement placement, Size size)
        {
            int left = placement.Left;
            int right = placement.Right - size.Width;
            int anchor = Alignment == BarAlignment.Center ? left + (right - left) / 2
                : Alignment == BarAlignment.Left ? left : right;
            int x = Clamp(anchor + (int)Math.Round(OffsetX * placement.Scale), left, right);
            int y = placement.Model.Top + (placement.Model.Height - size.Height) / 2
                + (int)Math.Round(OffsetY * placement.Scale);
            if (!placement.WindowBounds.IsEmpty)
                y = Clamp(y, placement.WindowBounds.Top + 4, placement.WindowBounds.Bottom - size.Height - 4);
            return new Rectangle(x, y, size.Width, size.Height);
        }

        private static int Clamp(int value, int min, int max) { return Math.Max(min, Math.Min(max, value)); }
    }

    internal sealed class SettingsStore
    {
        private readonly string path;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 8192 };

        internal SettingsStore(string path) { this.path = path; }

        internal BarSettings Load()
        {
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length > json.MaxJsonLength) return new BarSettings();
                var saved = json.Deserialize<BarSettings>(File.ReadAllText(path, Encoding.UTF8));
                if (saved == null) return new BarSettings();
                saved.Normalize();
                return saved;
            }
            catch (IOException) { return new BarSettings(); }
            catch (UnauthorizedAccessException) { return new BarSettings(); }
            catch (ArgumentException) { return new BarSettings(); }
            catch (InvalidOperationException) { return new BarSettings(); }
        }

        internal void Save(BarSettings settings)
        {
            settings.Normalize();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, json.Serialize(settings), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }

    internal sealed class StartupRegistration
    {
        private const string Entry = "CodexUsageBar";
        private readonly string keyPath;
        internal string Command { get; private set; }

        internal StartupRegistration(string executable, string keyPath = null)
        {
            this.keyPath = keyPath ?? @"Software\Microsoft\Windows\CurrentVersion\Run";
            Command = "\"" + executable + "\" --background";
        }

        internal bool Enabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(keyPath))
                return key != null && key.GetValue(Entry) is string;
        }

        internal void Set(bool enable)
        {
            using (RegistryKey current = Registry.CurrentUser.OpenSubKey(keyPath))
            {
                string existing = current == null ? null : current.GetValue(Entry) as string;
                if (!enable && existing == null) return;
                if (enable && String.Equals(existing, Command, StringComparison.OrdinalIgnoreCase)) return;
            }
            if (enable)
            {
                if (Command.Length > 260) throw new ArgumentException("自启路径超过 Windows 长度限制。");
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(keyPath))
                {
                    if (key == null) throw new SecurityException("无法打开当前用户的启动项。");
                    key.SetValue(Entry, Command, RegistryValueKind.String);
                }
            }
            else
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(keyPath, true))
                    if (key != null) key.DeleteValue(Entry, false);
            }
        }
    }
}
