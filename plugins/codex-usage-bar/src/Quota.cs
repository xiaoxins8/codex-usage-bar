using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexUsageBar
{
    internal sealed class QuotaWindow
    {
        internal double Used;
        internal long Duration;
        internal DateTimeOffset? Reset;
        internal string Remaining { get { return Math.Max(0, 100 - Used).ToString("0.#", CultureInfo.InvariantCulture) + "%"; } }
        internal string ResetLabel { get { return Reset.HasValue ? Reset.Value.ToLocalTime().ToString("MM-dd HH:mm") : "—"; } }
    }

    internal sealed class QuotaSnapshot
    {
        internal QuotaWindow FiveHour, Week;
        internal DateTimeOffset Observed;
        internal bool FromLog;

        internal string Status
        {
            get { return "更新于 " + Observed.ToLocalTime().ToString("HH:mm:ss") + (FromLog ? " · 会话日志" : " · Codex"); }
        }

        internal static QuotaSnapshot ParseAccount(Dictionary<string, object> result, DateTimeOffset now)
        {
            var byId = UsageLog.Get(result, "rateLimitsByLimitId") as Dictionary<string, object>;
            var bucket = UsageLog.Get(byId, "codex") as Dictionary<string, object>
                ?? UsageLog.Get(result, "rateLimits") as Dictionary<string, object>;
            return Parse(bucket, now, false);
        }

        internal static QuotaSnapshot Parse(Dictionary<string, object> bucket, DateTimeOffset when, bool fromLog)
        {
            if (bucket == null) return null;
            string id = UsageLog.Get(bucket, fromLog ? "limit_id" : "limitId") as string;
            if (!String.IsNullOrEmpty(id) && !String.Equals(id, "codex", StringComparison.OrdinalIgnoreCase)) return null;
            var windows = new[] { "primary", "secondary" }
                .Select(key => ReadWindow(UsageLog.Get(bucket, key) as Dictionary<string, object>, fromLog))
                .Where(window => window != null).ToArray();
            var fiveHour = windows.FirstOrDefault(window => window.Duration == 300);
            var week = windows.FirstOrDefault(window => window.Duration == 10080);
            if (fiveHour == null && week == null) return null;
            return new QuotaSnapshot { FiveHour = fiveHour, Week = week, Observed = when, FromLog = fromLog };
        }

        private static QuotaWindow ReadWindow(Dictionary<string, object> value, bool fromLog)
        {
            double used, duration;
            if (!Number(UsageLog.Get(value, fromLog ? "used_percent" : "usedPercent"), out used)
                || !Number(UsageLog.Get(value, fromLog ? "window_minutes" : "windowDurationMins"), out duration)
                || used < 0 || duration <= 0 || duration > 10080 || duration != Math.Truncate(duration)) return null;
            double epoch;
            DateTimeOffset? reset = null;
            if (Number(UsageLog.Get(value, fromLog ? "resets_at" : "resetsAt"), out epoch)
                && epoch > 0 && epoch < 253402300799)
                reset = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(epoch);
            return new QuotaWindow { Used = used, Duration = (long)duration, Reset = reset };
        }

        private static bool Number(object value, out double number)
        {
            number = 0;
            if (!(value is int) && !(value is long) && !(value is double) && !(value is decimal)) return false;
            number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return !Double.IsInfinity(number) && !Double.IsNaN(number);
        }
    }

    internal static class QuotaClient
    {
        internal static QuotaSnapshot Read(string home, CancellationToken cancel, string executable = null)
        {
            string cli = executable ?? FindCli();
            if (cli == null) throw new InvalidOperationException("找不到 Codex CLI。");
            var start = new ProcessStartInfo(cli, "app-server --listen stdio://") {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                RedirectStandardInput = true, RedirectStandardError = true, WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
            };
            start.EnvironmentVariables["CODEX_HOME"] = home;
            using (Process server = Process.Start(start))
            {
                if (server == null) throw new InvalidOperationException("无法启动 Codex App Server。");
                try
                {
                    var json = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 };
                    var elapsed = Stopwatch.StartNew();
                    server.BeginErrorReadLine();
                    server.StandardInput.AutoFlush = true;
                    server.StandardInput.WriteLine("{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"codex-usage-bar\",\"title\":\"Codex Usage Bar\",\"version\":\"" + SettingsWindow.AppVersion + "\"}}}");
                    ReadResult(server, json, 1, elapsed, cancel);
                    server.StandardInput.WriteLine("{\"method\":\"initialized\"}");
                    server.StandardInput.WriteLine("{\"id\":2,\"method\":\"account/rateLimits/read\",\"params\":{}}");
                    return QuotaSnapshot.ParseAccount(ReadResult(server, json, 2, elapsed, cancel), DateTimeOffset.UtcNow);
                }
                finally
                {
                    try { server.StandardInput.Close(); }
                    catch (InvalidOperationException) { }
                    catch (IOException) { }
                    try
                    {
                        if (!server.WaitForExit(350)) server.Kill();
                    }
                    catch (InvalidOperationException) { }
                    catch (System.ComponentModel.Win32Exception) { }
                }
            }
        }

        private static Dictionary<string, object> ReadResult(Process server, JavaScriptSerializer json, int id,
            Stopwatch elapsed, CancellationToken cancel)
        {
            while (elapsed.ElapsedMilliseconds < 14000)
            {
                Task<string> read = server.StandardOutput.ReadLineAsync();
                int remaining = Math.Max(1, 14000 - (int)elapsed.ElapsedMilliseconds);
                if (!read.Wait(remaining, cancel)) break;
                if (read.Result == null) throw new IOException("Codex App Server 已关闭。");
                var message = UsageLog.Parse(json, read.Result);
                if (!Equals(UsageLog.Get(message, "id"), id)) continue;
                var result = UsageLog.Get(message, "result") as Dictionary<string, object>;
                if (result == null) throw new InvalidOperationException("Codex 没有返回额度。");
                return result;
            }
            throw new TimeoutException("Codex 额度查询超时。");
        }

        private static string FindCli()
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
            try
            {
                string bundled = Directory.EnumerateDirectories(root, "OpenAI.Codex_*_x64__*")
                    .Select(dir => new { Path = Path.Combine(dir, "app", "resources", "codex.exe"), Version = PackageVersion(dir) })
                    .OrderByDescending(item => item.Version).Select(item => item.Path).FirstOrDefault(File.Exists);
                if (bundled != null) return bundled;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            // Older standalone installations can expose the official executable on PATH.
            foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                string directory = folder.Trim().Trim('"');
                if (directory.Length < 3 || !Char.IsLetter(directory[0]) || directory[1] != ':'
                    || (directory[2] != '\\' && directory[2] != '/')) continue;
                string candidate = Path.Combine(directory, "codex.exe");
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        private static Version PackageVersion(string path)
        {
            string[] parts = Path.GetFileName(path).Split('_');
            Version result;
            return parts.Length > 1 && Version.TryParse(parts[1], out result) ? result : new Version();
        }
    }
}
