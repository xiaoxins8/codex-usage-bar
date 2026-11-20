using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace CodexUsageBar
{
    internal sealed class UsageMetrics
    {
        private static readonly CultureInfo NumberFormat = CultureInfo.InvariantCulture;
        internal string Status;
        internal bool Pinned, Running;
        internal int Rounds, Steps, TimedTools;
        internal long? Total, Input, Cached, Output, TurnMs, FirstTokenMs;
        internal long ToolMs;
        internal double? Speed;
        internal QuotaSnapshot LoggedQuota;
        internal ContextUsage Context;

        internal bool HasCache { get { return Input.HasValue && Cached.HasValue && Cached <= Input; } }
        internal string RoundLabel { get { return Rounds + " 轮 " + Steps + " 步"; } }
        internal string HitLabel { get { return Input > 0 && HasCache ? (100.0 * Cached.Value / Input.Value).ToString("0.#", NumberFormat) + "%" : "—"; } }
        internal string SpeedLabel { get { return Speed.HasValue ? "≈" + Speed.Value.ToString("0.#", NumberFormat) : "—"; } }

        internal static string Compact(long value)
        {
            if (value >= 1000000000) return (value / 1e9).ToString("0.#", NumberFormat) + "B";
            if (value >= 1000000) return (value / 1e6).ToString("0.#", NumberFormat) + "M";
            if (value >= 1000) return (value / 1e3).ToString("0.#", NumberFormat) + "K";
            return value.ToString(NumberFormat);
        }

        internal static string Exact(long? value)
        {
            return value.HasValue ? value.Value.ToString("N0", NumberFormat) + " tok" : "—";
        }

        internal static string Seconds(long? milliseconds)
        {
            return milliseconds.HasValue ? (milliseconds.Value / 1000.0).ToString("0.##", NumberFormat) + " 秒" : "—";
        }
    }

    internal sealed class ContextUsage
    {
        internal long? Used, Window, Input, Cached, Output;
        internal DateTimeOffset? ReportedAt;

        internal long? CappedUsed { get { return Used.HasValue && Window.HasValue ? Math.Min(Used.Value, Window.Value) : Used; } }
        internal double? Percent { get { return Used.HasValue && Window > 0 ? 100.0 * CappedUsed.Value / Window.Value : (double?)null; } }
        internal long? ValidCached { get { return Input.HasValue && Cached <= Input ? Cached : null; } }
        internal long? Uncached { get { return Input - ValidCached; } }
        internal bool HasCompleteSplit
        {
            get { return Used > 0 && ValidCached.HasValue && Output.HasValue && Input <= Used && Used - Input == Output; }
        }

        internal string PercentLabel
        {
            get { return Percent.HasValue ? Percent.Value.ToString("0.#", CultureInfo.InvariantCulture) + "%" : "—"; }
        }

        internal string WindowLabel
        {
            get { return (Used.HasValue ? "约 " : "") + CompactOrDash(CappedUsed) + " / " + CompactOrDash(Window); }
        }

        private static string CompactOrDash(long? count)
        {
            return count.HasValue ? UsageMetrics.Compact(count.Value) : "—";
        }
    }

    internal sealed class ThreadEntry
    {
        internal string Id, Name, Path, PromptFingerprint, RolloutFingerprint, Model, Effort;
        internal bool Archived;
        internal long Updated, TokensUsed;
    }

    internal sealed class ThreadStore
    {
        private const string GuidText = @"[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}";
        private static readonly Regex IdPattern = new Regex(@"\A" + GuidText + @"\z", RegexOptions.IgnoreCase);
        private static readonly Regex RoutePattern = new Regex(
            @"(?:^|[:/#])(?:local|threads?)/(" + GuidText + @")(?=$|[/?#])", RegexOptions.IgnoreCase);
        private readonly string home;
        private readonly Dictionary<string, string> recentFingerprints = new Dictionary<string, string>();

        internal ThreadStore(string home)
        {
            this.home = Path.GetFullPath(home);
        }

        internal static string ExtractId(string text, bool allowBare)
        {
            text = (text ?? "").Trim();
            if (allowBare && IdPattern.IsMatch(text)) return text.ToLowerInvariant();
            Match route = RoutePattern.Match(text);
            return route.Success ? route.Groups[1].Value.ToLowerInvariant() : null;
        }

        internal static string Normalize(string text)
        {
            return Regex.Replace((text ?? "").Trim(), @"\s+", " ");
        }

        internal static string Fingerprint(string text)
        {
            if (String.IsNullOrWhiteSpace(text) || text.Length > 320) return null;
            string normalized = Normalize(text);
            if (normalized.Length < 4) return null;
            using (var hash = SHA256.Create())
                return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(normalized)));
        }

        internal List<ThreadEntry> ReadIndex()
        {
            var rows = new List<ThreadEntry>();
            if (!Directory.Exists(home)) return rows;
            string db = Directory.EnumerateFiles(home, "state_*.sqlite")
                .Where(p => Regex.IsMatch(Path.GetFileName(p), @"^state_\d+\.sqlite$"))
                .OrderByDescending(p => VersionNumber(p)).FirstOrDefault();
            if (db != null) rows.AddRange(ReadDatabase(db));

            // Older builds record user-assigned names in the session index as well.
            string index = Path.Combine(home, "session_index.jsonl");
            if (File.Exists(index))
            {
                var entries = rows.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
                var databaseNames = new HashSet<string>(rows.Where(r => r.Name.Length > 0).Select(r => r.Id));
                var json = new JavaScriptSerializer();
                using (var file = new FileStream(index, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(file, Encoding.UTF8))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Length > 65536) continue;
                        Dictionary<string, object> item = UsageLog.Parse(json, line);
                        string id = ExtractId(UsageLog.Get(item, "id") as string, true);
                        string title = Normalize(UsageLog.Get(item, "thread_name") as string);
                        if (id == null || title.Length == 0) continue;
                        ThreadEntry row;
                        if (!entries.TryGetValue(id, out row))
                        {
                            row = new ThreadEntry { Id = id };
                            entries.Add(id, row);
                            rows.Add(row);
                        }
                        // A database name wins over the session index. Last index entry wins otherwise.
                        if (!databaseNames.Contains(id)) row.Name = title;
                    }
                }
            }
            return rows;
        }

        internal void ReadRecentRollouts(List<ThreadEntry> index)
        {
            var existing = index.ToDictionary(row => row.Id, StringComparer.OrdinalIgnoreCase);
            var recent = new List<string>();
            for (int day = 0; day < 3; day++)
            {
                DateTime date = DateTime.Now.Date.AddDays(-day);
                string folder = Path.Combine(home, "sessions", date.ToString("yyyy"), date.ToString("MM"), date.ToString("dd"));
                if (Directory.Exists(folder)) recent.AddRange(Directory.EnumerateFiles(folder, "rollout-*.jsonl"));
            }
            var json = new JavaScriptSerializer { MaxJsonLength = 128 * 1024 };
            foreach (string candidate in recent.OrderByDescending(File.GetLastWriteTimeUtc).Take(32))
            {
                string thread = RolloutThread(Path.GetFileName(candidate));
                if (thread == null) continue;
                ThreadEntry row;
                existing.TryGetValue(thread, out row);
                string fingerprint;
                if (!recentFingerprints.TryGetValue(thread, out fingerprint))
                {
                    fingerprint = FirstPrompt(candidate, json);
                    if (fingerprint != null)
                    {
                        if (recentFingerprints.Count >= 128) recentFingerprints.Clear();
                        recentFingerprints[thread] = fingerprint;
                    }
                }
                if (fingerprint == null) continue;
                if (row == null)
                {
                    row = new ThreadEntry { Id = thread, Path = candidate };
                    existing.Add(thread, row);
                    index.Add(row);
                }
                row.RolloutFingerprint = fingerprint;
            }
        }

        private static string FirstPrompt(string path, JavaScriptSerializer json)
        {
            try
            {
                using (var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(source, Encoding.UTF8))
                {
                    var text = new char[96 * 1024];
                    int size = reader.ReadBlock(text, 0, text.Length);
                    foreach (string line in new String(text, 0, size).Split('\n').Take(32))
                    {
                        string prefix = line.Substring(0, Math.Min(line.Length, 270));
                        if (!prefix.Contains("\"user_message\"") && !prefix.Contains("\"UserMessage\"")) continue;
                        var envelope = UsageLog.Parse(json, line);
                        if (!Equals(UsageLog.Get(envelope, "type"), "event_msg")) continue;
                        var payload = UsageLog.Get(envelope, "payload") as Dictionary<string, object>;
                        if (Equals(UsageLog.Get(payload, "type"), "user_message"))
                            return Fingerprint(UsageLog.Get(payload, "message") as string);
                        var item = UsageLog.Get(payload, "item") as Dictionary<string, object>;
                        if (!Equals(UsageLog.Get(item, "type"), "UserMessage")) continue;
                        var content = UsageLog.Get(item, "content") as object[];
                        if (content != null)
                        {
                            var textParts = content.OfType<Dictionary<string, object>>()
                                .Select(part => UsageLog.Get(part, "text") as string).Where(partText => partText != null);
                            return Fingerprint(String.Join(" ", textParts));
                        }
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return null;
        }

        private static int VersionNumber(string path)
        {
            int number;
            return Int32.TryParse(Path.GetFileNameWithoutExtension(path).Substring(6), out number) ? number : 0;
        }

        internal string FindLog(string id, List<ThreadEntry> index)
        {
            if (id == null || !IdPattern.IsMatch(id)) return null;
            ThreadEntry row = index.FirstOrDefault(entry => entry.Id == id);
            string known = row != null ? SessionFile(row.Path, id) : null;
            if (known != null) return known;
            foreach (string directory in Roots())
            {
                if (!Directory.Exists(directory)) continue;
                // A resumed chat continues in "<id>_<segment>.jsonl"; the newest segment is the live one.
                string found = Directory.EnumerateFiles(directory, "rollout-*-" + id + "*.jsonl", SearchOption.AllDirectories)
                    .Select(path => SessionFile(path, id)).Where(path => path != null)
                    .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                if (found != null) return found;
            }
            return null;
        }

        // Codex stores some rollout paths with the Win32 extended-length prefix, which .NET Framework path APIs reject.
        internal static string PlainPath(string path)
        {
            if (String.IsNullOrEmpty(path)) return path;
            if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return @"\\" + path.Substring(8);
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path.Substring(4);
            return path;
        }

        // rollout-<time>-<thread>.jsonl, or rollout-<time>-<thread>_<segment>.jsonl after a resume.
        private static readonly Regex RolloutName = new Regex(
            @"-(" + GuidText + @")(?:_" + GuidText + @")?\.jsonl\z", RegexOptions.IgnoreCase);

        internal static string RolloutThread(string fileName)
        {
            Match name = RolloutName.Match(fileName ?? "");
            return name.Success ? name.Groups[1].Value.ToLowerInvariant() : null;
        }

        private IEnumerable<string> Roots()
        {
            yield return Path.Combine(home, "sessions");
            yield return Path.Combine(home, "archived_sessions");
        }

        // Returns the normalized path when it is this thread's rollout inside CODEX_HOME, otherwise null.
        private string SessionFile(string path, string id)
        {
            path = PlainPath(path);
            if (String.IsNullOrEmpty(path) || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || !Path.IsPathRooted(path)) return null;
            string full;
            try { full = Path.GetFullPath(path); }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
            catch (PathTooLongException) { return null; }
            return String.Equals(RolloutThread(Path.GetFileName(full)), id, StringComparison.OrdinalIgnoreCase)
                && Roots().Any(root => full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                && File.Exists(full) ? full : null;
        }

        private static List<ThreadEntry> ReadDatabase(string path)
        {
            var rows = new List<ThreadEntry>();
            IntPtr db, statement = IntPtr.Zero;
            int opened = Sqlite.sqlite3_open_v2(Utf8(path), out db, 1, IntPtr.Zero);
            try
            {
                if (opened != 0) return rows;
                Sqlite.sqlite3_busy_timeout(db, 100);
                // Newest schema first. Unnamed threads show their title in the header, and
                // archived/updated let identically auto-named threads (e.g. greetings) be told apart.
                string[] queries = {
                    "SELECT id, COALESCE(NULLIF(name,''), substr(title,1,400)), rollout_path, substr(first_user_message,1,321), archived, updated_at_ms, tokens_used, model, reasoning_effort FROM threads",
                    "SELECT id, name, rollout_path, substr(first_user_message,1,321), 0, 0, 0, NULL, NULL FROM threads",
                    "SELECT id, title, rollout_path, NULL, 0, 0, 0, NULL, NULL FROM threads"
                };
                bool prepared = false;
                foreach (string query in queries)
                {
                    if (Sqlite.sqlite3_prepare_v2(db, Utf8(query), -1, out statement, IntPtr.Zero) == 0) { prepared = true; break; }
                    if (statement != IntPtr.Zero) Sqlite.sqlite3_finalize(statement);
                    statement = IntPtr.Zero;
                }
                if (!prepared) return rows;
                while (Sqlite.sqlite3_step(statement) == 100)
                {
                    string id = ExtractId(Sqlite.Text(statement, 0), true);
                    if (id == null) continue;
                    rows.Add(new ThreadEntry { Id = id, Name = Normalize(Sqlite.Text(statement, 1)), Path = Sqlite.Text(statement, 2),
                        PromptFingerprint = Fingerprint(Sqlite.Text(statement, 3)),
                        Archived = Sqlite.sqlite3_column_int64(statement, 4) != 0, Updated = Sqlite.sqlite3_column_int64(statement, 5),
                        TokensUsed = Sqlite.sqlite3_column_int64(statement, 6),
                        Model = Sqlite.Text(statement, 7), Effort = Sqlite.Text(statement, 8) });
                }
                return rows;
            }
            finally
            {
                if (statement != IntPtr.Zero) Sqlite.sqlite3_finalize(statement);
                if (db != IntPtr.Zero) Sqlite.sqlite3_close(db);
            }
        }

        private static byte[] Utf8(string text) { return Encoding.UTF8.GetBytes(text + "\0"); }
    }

    internal static class Sqlite
    {
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_open_v2(byte[] filename, out IntPtr database, int flags, IntPtr vfs);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_close(IntPtr database);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_busy_timeout(IntPtr database, int milliseconds);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int size, out IntPtr statement, IntPtr tail);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_step(IntPtr statement);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_finalize(IntPtr statement);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sqlite3_column_text(IntPtr statement, int column);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_column_bytes(IntPtr statement, int column);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern long sqlite3_column_int64(IntPtr statement, int column);

        internal static string Text(IntPtr statement, int column)
        {
            IntPtr pointer = sqlite3_column_text(statement, column);
            if (pointer == IntPtr.Zero) return null;
            byte[] data = new byte[sqlite3_column_bytes(statement, column)];
            Marshal.Copy(pointer, data, 0, data.Length);
            return Encoding.UTF8.GetString(data);
        }
    }

    internal sealed class UsageLog
    {
        private const int MaxEventBytes = 128 * 1024;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = MaxEventBytes };
        private readonly byte[] chunk = new byte[64 * 1024];
        private readonly byte[] line = new byte[MaxEventBytes];
        private FileStream stream;
        private string path;
        private DateTime createdAt;
        private int lineLength;
        private bool skipLine;
        private int turns, steps;
        private long? input, cached, output, total, baseline, turnOutput;
        private long? turnMs, firstTokenMs;
        private long toolMs;
        private readonly HashSet<string> timedTools = new HashSet<string>();
        private QuotaSnapshot loggedQuota;
        private ContextUsage context;
        private static readonly HashSet<string> ToolTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "CommandExecution", "McpToolCall", "DynamicToolCall", "WebSearch", "FileChange", "Extension", "CollabAgentToolCall"
        };
        private DateTimeOffset? started, lastReport;
        private string turnId;
        private bool inTurn;

        internal void Read(string nextPath)
        {
            if (String.IsNullOrEmpty(nextPath)) { Reset(); return; }
            var info = new FileInfo(nextPath);
            if (!info.Exists) { Reset(); return; }
            if (stream == null || path != nextPath || info.Length < stream.Position || info.CreationTimeUtc != createdAt)
            {
                Reset();
                stream = new FileStream(nextPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                path = nextPath;
                createdAt = info.CreationTimeUtc;
            }
            // Preserve partial events between polls and cap the work per refresh for large logs.
            int remaining = (int)Math.Min(8L * 1024 * 1024, info.Length - stream.Position);
            int read;
            while (remaining > 0 && (read = stream.Read(chunk, 0, Math.Min(chunk.Length, remaining))) != 0)
            {
                remaining -= read;
                int position = 0;
                while (position < read)
                {
                    int end = Array.IndexOf(chunk, (byte)'\n', position, read - position);
                    int count = (end < 0 ? read : end) - position;
                    if (!skipLine && lineLength + count <= line.Length)
                    {
                        Buffer.BlockCopy(chunk, position, line, lineLength, count);
                        lineLength += count;
                    }
                    else skipLine = true;
                    if (end < 0) break;
                    if (!skipLine) ReadEvent();
                    skipLine = false;
                    lineLength = 0;
                    position = end + 1;
                }
            }
        }

        internal void Reset()
        {
            if (stream != null) stream.Dispose();
            stream = null;
            path = null;
            lineLength = turns = steps = 0;
            skipLine = inTurn = false;
            input = cached = output = total = baseline = turnOutput = null;
            started = lastReport = null;
            loggedQuota = null;
            context = null;
            turnMs = firstTokenMs = null;
            toolMs = 0;
            timedTools.Clear();
            turnId = null;
        }

        private void ReadEvent()
        {
            // Check the envelope before decoding or parsing a JSON line.
            int prefix = Math.Min(256, lineLength);
            string header = Encoding.ASCII.GetString(line, 0, prefix);
            if (!header.Contains("\"event_msg\"")) return;
            if (header.Contains("\"item_completed\"") && !ToolTypes.Any(type => header.Contains("\"" + type + "\""))) return;
            if (!header.Contains("\"token_count\"") && !header.Contains("\"task_started\"")
                && !header.Contains("\"task_complete\"") && !header.Contains("\"task_completed\"")
                && !header.Contains("\"turn_started\"") && !header.Contains("\"turn_completed\"")
                && !header.Contains("\"turn_aborted\"") && !header.Contains("\"item_completed\"")) return;
            Dictionary<string, object> item = Parse(json, Encoding.UTF8.GetString(line, 0, lineLength).TrimStart('\uFEFF'));
            if (Get(item, "type") as string != "event_msg") return;
            var payload = Get(item, "payload") as Dictionary<string, object>;
            string kind = Get(payload, "type") as string;
            string id = Get(payload, "turn_id") as string;
            DateTimeOffset time;
            DateTimeOffset? timestamp = DateTimeOffset.TryParse(Get(item, "timestamp") as string, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out time) ? time : (DateTimeOffset?)null;
            switch (kind)
            {
                case "task_started":
                case "turn_started":
                    if (id != null && id == turnId) return;
                    turns++;
                    steps = 0;
                    baseline = output ?? 0;
                    turnOutput = null;
                    started = timestamp;
                    lastReport = null;
                    turnMs = firstTokenMs = null;
                    toolMs = 0;
                    timedTools.Clear();
                    turnId = id;
                    inTurn = true;
                    return;
                case "task_complete":
                case "task_completed":
                case "turn_completed":
                    if (inTurn && (id == null || id == turnId))
                    {
                        turnMs = Count(payload, "duration_ms");
                        firstTokenMs = Count(payload, "time_to_first_token_ms");
                        inTurn = false;
                    }
                    return;
                case "turn_aborted":
                    if (id == null || id == turnId) inTurn = false;
                    return;
                case "item_completed":
                    if (inTurn && id == turnId)
                    {
                        var tool = Get(payload, "item") as Dictionary<string, object>;
                        string itemId = Get(tool, "id") as string;
                        long? from = Count(payload, "started_at_ms"), to = Count(payload, "completed_at_ms");
                        if (itemId != null && ToolTypes.Contains(Get(tool, "type") as string)
                            && from.HasValue && to >= from && timedTools.Add(itemId))
                            toolMs += to.Value - from.Value;
                    }
                    return;
                case "token_count":
                    if (timestamp.HasValue)
                    {
                        var rate = QuotaSnapshot.Parse(Get(payload, "rate_limits") as Dictionary<string, object>, timestamp.Value, true);
                        if (rate != null) loggedQuota = rate;
                    }
                    var info = Get(payload, "info") as Dictionary<string, object>;
                    var last = Get(info, "last_token_usage") as Dictionary<string, object>;
                    if (last != null)
                    {
                        long? window = Count(info, "model_context_window");
                        context = new ContextUsage {
                            Used = Count(last, "total_tokens"), Window = window > 0 ? window : null,
                            Input = Count(last, "input_tokens"), Cached = Count(last, "cached_input_tokens"),
                            Output = Count(last, "output_tokens"), ReportedAt = timestamp
                        };
                    }
                    var counts = Get(info, "total_token_usage") as Dictionary<string, object>;
                    long? newTotal = Count(counts, "total_tokens");
                    if (!newTotal.HasValue) return;
                    long? newInput = Count(counts, "input_tokens");
                    long? newCached = Count(counts, "cached_input_tokens");
                    long? newOutput = Count(counts, "output_tokens");
                    if (total == newTotal && input == newInput && cached == newCached && output == newOutput) return;
                    if (inTurn)
                    {
                        steps++;
                        if ((total.HasValue && newTotal < total) || (output.HasValue && newOutput < output)) baseline = null;
                        turnOutput = newOutput;
                        lastReport = timestamp;
                    }
                    input = newInput;
                    cached = newCached;
                    output = newOutput;
                    total = newTotal;
                    return;
            }
        }

        internal UsageMetrics Metrics(bool pinned)
        {
            double? speed = null;
            if (steps > 0 && started.HasValue && lastReport.HasValue && baseline.HasValue && turnOutput >= baseline)
            {
                double seconds = turnMs.HasValue ? turnMs.Value / 1000.0 : (lastReport.Value - started.Value).TotalSeconds;
                if (seconds > 0) speed = (turnOutput.Value - baseline.Value) / seconds;
            }
            return new UsageMetrics {
                Status = total.HasValue ? null : "等待用量", Pinned = pinned, Running = inTurn,
                Rounds = turns, Steps = steps, Total = total, Input = input, Cached = cached, Output = output,
                TurnMs = turnMs, FirstTokenMs = firstTokenMs, TimedTools = timedTools.Count, ToolMs = toolMs, Speed = speed,
                LoggedQuota = loggedQuota, Context = context
            };
        }

        internal static object Get(Dictionary<string, object> obj, string key)
        {
            object value;
            return obj != null && obj.TryGetValue(key, out value) ? value : null;
        }

        internal static Dictionary<string, object> Parse(JavaScriptSerializer parser, string text)
        {
            try { return parser.DeserializeObject(text) as Dictionary<string, object>; }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        private static long? Count(Dictionary<string, object> obj, string key)
        {
            object value = Get(obj, key);
            if (value is int || value is long)
            {
                long number = Convert.ToInt64(value);
                if (number >= 0) return number;
            }
            return null;
        }
    }
}
