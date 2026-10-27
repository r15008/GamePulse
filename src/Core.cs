using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace GamePulse
{
    public class Settings
    {
        public string PingTarget { get; set; }
        public string OriginalPowerPlan { get; set; }
        public string LastAppliedPlan { get; set; }
        public string CpuTool { get; set; }
        public string GpuTool { get; set; }
        public string Afterburner { get; set; }
        public int AfterburnerProfile { get; set; }
        public bool ProfileVerified { get; set; }
        public Settings() { PingTarget = ""; AfterburnerProfile = 1; }
    }
    public class Sample
    {
        public double Seconds { get; set; }
        public double? Cpu { get; set; }
        public double? Gpu { get; set; }
        public double? Ram { get; set; }
        public double? CpuTemp { get; set; }
        public double? GpuTemp { get; set; }
        public double? GpuPower { get; set; }
        public double? Ping { get; set; }
        public double? Fps { get; set; }
        public double? FrameTime { get; set; }
    }
    public class Session
    {
        public string Id { get; set; }
        public string Started { get; set; }
        public string Game { get; set; }
        public string PowerPlan { get; set; }
        public string PingTarget { get; set; }
        public double Duration { get; set; }
        public List<Sample> Samples { get; set; }
        public List<double> Frames { get; set; }
        public Session() { Samples = new List<Sample>(); Frames = new List<double>(); }
    }
    public class FrameStats
    {
        public int Count, Stutters;
        public double Average, Low1, P99, Median, Threshold;
        public static FrameStats Calculate(IEnumerable<double> frames)
        {
            double[] a = frames.Where(x => x > 0 && !Double.IsNaN(x) && !Double.IsInfinity(x)).OrderBy(x => x).ToArray();
            FrameStats s = new FrameStats(); s.Count = a.Length;
            if (a.Length == 0) return s;
            s.Average = 1000 / a.Average();
            s.Low1 = 1000 / a.Skip(a.Length - Math.Max(1, (int)Math.Ceiling(a.Length * .01))).Average();
            s.P99 = a[Math.Max(0, (int)Math.Ceiling(a.Length * .99) - 1)];
            s.Median = a[a.Length / 2]; s.Threshold = Math.Max(25, s.Median * 2);
            s.Stutters = a.Count(x => x > s.Threshold);
            return s;
        }
    }
    public static class Store
    {
        public static string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GamePulse");
        public static string Records { get { return Path.Combine(Root, "records"); } }
        public static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024, RecursionLimit = 100 }; }
        public static void Write(string path, object value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp"; File.WriteAllText(temp, Json().Serialize(value), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        public static Settings LoadSettings()
        {
            try { return Json().Deserialize<Settings>(File.ReadAllText(Path.Combine(Root, "settings.json"))) ?? new Settings(); }
            catch { return new Settings(); }
        }
        public static void Save(Settings settings) { Write(Path.Combine(Root, "settings.json"), settings); }
        public static void Save(Session session) { Write(Path.Combine(Records, session.Id + ".json"), session); }
        public static List<Session> LoadSessions()
        {
            List<Session> result = new List<Session>();
            if (!Directory.Exists(Records)) return result;
            foreach (string p in Directory.GetFiles(Records, "*.json").OrderByDescending(x => x).Take(100))
                try { var s = Json().Deserialize<Session>(File.ReadAllText(p)); if (s != null && s.Samples != null && s.Frames != null) result.Add(s); } catch { }
            return result;
        }
        public static string Csv(string s)
        {
            s = s ?? "";
            // Protect spreadsheet viewers from formula injection in process names / imported labels.
            if (s.Length > 0 && "=+-@\t\r".Contains(s[0])) s = "'" + s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        static string N(double? n) { return n.HasValue ? n.Value.ToString("0.###", CultureInfo.InvariantCulture) : ""; }
        public static void Export(Session s, string path)
        {
            var b = new StringBuilder("Game,Started,Seconds,CPU_percent,GPU_percent,RAM_percent,CPU_C,GPU_C,GPU_W,Ping_ms,PingTarget,FPS,Frame_ms\r\n");
            foreach (var m in s.Samples) b.AppendLine(String.Join(",", new[] { Csv(s.Game), Csv(s.Started), N(m.Seconds), N(m.Cpu), N(m.Gpu), N(m.Ram), N(m.CpuTemp), N(m.GpuTemp), N(m.GpuPower), N(m.Ping), Csv(s.PingTarget), N(m.Fps), N(m.FrameTime) }));
            File.WriteAllText(path, b.ToString(), new UTF8Encoding(true));
            var frames = new StringBuilder("Frame,FrameTime_ms\r\n");
            for (int i = 0; i < s.Frames.Count; i++) frames.AppendLine((i + 1) + "," + N(s.Frames[i]));
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + "-frames.csv"), frames.ToString(), new UTF8Encoding(true));
        }
    }
    public class CommandResult { public int Code; public string Output, Error; }
    public static class Commands
    {
        public static CommandResult Run(string file, string args, int timeout)
        {
            using (var p = new Process())
            {
                p.StartInfo = new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                p.Start(); var output = p.StandardOutput.ReadToEndAsync(); var error = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(timeout)) { try { p.Kill(); } catch { } throw new TimeoutException("操作超时，请稍后重试。"); }
                return new CommandResult { Code = p.ExitCode, Output = output.Result, Error = error.Result };
            }
        }
        public static void Open(string path) { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        public static bool IsAdministrator { get { using (var id = System.Security.Principal.WindowsIdentity.GetCurrent()) return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator); } }
    }
    public class PowerPlan
    {
        public string Id, Name;
        public override string ToString() { return Name; }
    }
    public class PowerService
    {
        static readonly Regex GuidPattern = new Regex(@"[a-fA-F0-9]{8}-(?:[a-fA-F0-9]{4}-){3}[a-fA-F0-9]{12}");
        static readonly string Exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "powercfg.exe");
        readonly Settings settings;
        public PowerService(Settings settings) { this.settings = settings; }
        public string Current()
        {
            var r = Commands.Run(Exe, "/getactivescheme", 5000); var m = GuidPattern.Match(r.Output);
            if (r.Code != 0 || !m.Success) throw new InvalidOperationException("无法读取当前电源计划。");
            return m.Value.ToLowerInvariant();
        }
        public List<PowerPlan> List()
        {
            var r = Commands.Run(Exe, "/list", 5000); if (r.Code != 0) throw new InvalidOperationException("无法读取电源计划。");
            return Parse(r.Output);
        }
        public static List<PowerPlan> Parse(string text)
        {
            var list = new List<PowerPlan>();
            foreach (string line in text.Split('\n'))
            {
                Match m = GuidPattern.Match(line); if (!m.Success) continue;
                string name = line.Substring(m.Index + m.Length).Trim().TrimEnd('*').Trim().Trim('(', ')');
                list.Add(new PowerPlan { Id = m.Value.ToLowerInvariant(), Name = name.Length > 0 ? name : m.Value });
            }
            return list;
        }
        public void Apply(string id)
        {
            if (!List().Any(x => x.Id == id)) throw new InvalidOperationException("该电源计划已不存在，请刷新后重试。");
            string current = Current();
            if (String.IsNullOrEmpty(settings.OriginalPowerPlan)) { settings.OriginalPowerPlan = current; Store.Save(settings); }
            Set(id); settings.LastAppliedPlan = id; Store.Save(settings);
        }
        void Set(string id)
        {
            if (!GuidPattern.IsMatch(id) || !Guid.TryParse(id, out newGuid)) throw new InvalidOperationException("电源计划 ID 无效。");
            var r = Commands.Run(Exe, "/setactive " + id, 5000);
            if (r.Code != 0 || Current() != id.ToLowerInvariant()) throw new InvalidOperationException("Windows 未应用电源计划，请检查权限或设备支持情况。");
        }
        Guid newGuid;
        public void Restore()
        {
            if (String.IsNullOrEmpty(settings.OriginalPowerPlan)) throw new InvalidOperationException("暂无需要恢复的电源计划。");
            Set(settings.OriginalPowerPlan); settings.OriginalPowerPlan = null; settings.LastAppliedPlan = null; Store.Save(settings);
        }
    }
}
