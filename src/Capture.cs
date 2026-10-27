using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace GamePulse
{
    public class CsvFrameParser
    {
        int timeIndex=-1, pidIndex=-1, chainIndex=-1;
        public int ExpectedPid;
        public string Chain;
        public static string[] Split(string line)
        {
            var cells=new List<string>(); var b=new StringBuilder(); bool quoted=false;
            for(int i=0;i<line.Length;i++) { char c=line[i]; if(c=='"') { if(quoted&&i+1<line.Length&&line[i+1]=='"') {b.Append('"');i++;}else quoted=!quoted; } else if(c==','&&!quoted) { cells.Add(b.ToString());b.Clear(); } else b.Append(c); }
            cells.Add(b.ToString()); return cells.ToArray();
        }
        public double? Read(string line)
        {
            var cells=Split(line);
            if(cells.Contains("Application")&&cells.Contains("ProcessID"))
            {
                pidIndex=Array.IndexOf(cells,"ProcessID"); chainIndex=Array.IndexOf(cells,"SwapChainAddress");
                timeIndex=Array.IndexOf(cells,"FrameTime"); if(timeIndex<0)timeIndex=Array.IndexOf(cells,"CPUFrameTime"); if(timeIndex<0)timeIndex=Array.IndexOf(cells,"MsBetweenPresents"); return null;
            }
            if(timeIndex<0||timeIndex>=cells.Length||pidIndex<0||pidIndex>=cells.Length)return null;
            int pid; double ms; if(!Int32.TryParse(cells[pidIndex],out pid)||pid!=ExpectedPid)return null;
            if(!Double.TryParse(cells[timeIndex],NumberStyles.Float,CultureInfo.InvariantCulture,out ms)||ms<=0||Double.IsNaN(ms)||Double.IsInfinity(ms))return null;
            // One swapchain per capture avoids counting independent UI / video surfaces as game frames.
            if(chainIndex>=0&&chainIndex<cells.Length) { if(Chain==null)Chain=cells[chainIndex]; if(Chain!=cells[chainIndex])return null; }
            return ms;
        }
    }
    public class CaptureService : IDisposable
    {
        readonly object gate=new object(); readonly Queue<Tuple<DateTime,double>> recent=new Queue<Tuple<DateTime,double>>();
        Process process; CsvFrameParser parser; List<double> recorded; bool active;
        public string Status="请选择正在运行的游戏";
        public string Name="未选择游戏";
        public bool Running { get { lock(gate)return active; } }
        public static string Collector()
        {
            const string expected="690533CF5C2591EF571A3037AA4C205C8E75860EF3F56927BF3A98ACF7198485";
            string dir=Path.Combine(Store.Root,"tools","presentmon-2.3.0"); Directory.CreateDirectory(dir); string path=Path.Combine(dir,"PresentMon.exe");
            using(var license=typeof(CaptureService).Assembly.GetManifestResourceStream("ThirdPartyNotices.txt"))using(var reader=new StreamReader(license))File.WriteAllText(Path.Combine(dir,"LICENSE.txt"),reader.ReadToEnd());
            if(File.Exists(path)) { using(var sha=SHA256.Create())using(var stream=File.OpenRead(path)) if(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","")==expected)return path; }
            using(var input=typeof(CaptureService).Assembly.GetManifestResourceStream("PresentMon.exe"))using(var output=File.Create(path))input.CopyTo(output);
            using(var sha=SHA256.Create())using(var stream=File.OpenRead(path))if(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","")!=expected)throw new InvalidOperationException("帧率采集组件校验失败。");
            return path;
        }
        public void Start(int pid,string name)
        {
            Stop(); parser=new CsvFrameParser{ExpectedPid=pid}; Name=name;
            var p=new Process{ StartInfo=new ProcessStartInfo(Collector(),"--process_id "+pid+" --output_stdout --no_console_stats --no_track_input --terminate_on_proc_exit --session_name GamePulse-"+Process.GetCurrentProcess().Id+"-"+Guid.NewGuid().ToString("N")) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true},EnableRaisingEvents=true};
            process=p;
            p.OutputDataReceived+=(sender,e)=> { if(e.Data==null)return; lock(gate) { if(process!=p||!active)return; double? ms=parser.Read(e.Data); if(!ms.HasValue)return; recent.Enqueue(Tuple.Create(DateTime.UtcNow,ms.Value)); while(recent.Count>20000)recent.Dequeue(); if(recorded!=null&&recorded.Count<2000000)recorded.Add(ms.Value); Status="采集中 · "+Name; } };
            p.ErrorDataReceived+=(sender,e)=> { if(!String.IsNullOrWhiteSpace(e.Data))lock(gate)if(process==p)Status="采集提示："+e.Data; };
            p.Exited+=(sender,e)=> { lock(gate) { if(process!=p)return; active=false; Status="采集已结束。若游戏仍在运行，请以管理员身份重启后重试。"; } };
            lock(gate){recent.Clear();active=true;Status="正在等待游戏画面…";}
            try { p.Start(); p.BeginOutputReadLine();p.BeginErrorReadLine(); } catch { lock(gate)active=false;throw; }
        }
        public double[] Recent()
        {
            lock(gate) { DateTime since=DateTime.UtcNow.AddSeconds(-2);while(recent.Count>0&&recent.Peek().Item1<since)recent.Dequeue();return active?recent.Select(x=>x.Item2).ToArray():new double[0]; }
        }
        public string SwapChain { get { lock(gate)return parser==null?null:parser.Chain; } }
        public void BeginRecord() {lock(gate)recorded=new List<double>();}
        public int RecordedCount {get {lock(gate)return recorded==null?0:recorded.Count;}}
        public List<double> EndRecord() {lock(gate){var result=recorded;recorded=null;return result;}}
        public void Stop()
        {
            Process p;lock(gate){active=false;p=process;process=null;recent.Clear();Status="采集已停止";}
            if(p!=null)try{if(!p.HasExited){p.Kill();p.WaitForExit(3000);}p.Dispose();}catch{}
        }
        public void Dispose(){Stop();}
    }
    public class GameProcess
    {
        public int Id; public string Name;
        public override string ToString(){return Name+"  ·  PID "+Id;}
        public static List<GameProcess> List()
        {
            var result=new List<GameProcess>();
            foreach(var p in Process.GetProcesses())using(p)try{string n=p.ProcessName;if(p.Id!=Process.GetCurrentProcess().Id&&(p.MainWindowHandle!=IntPtr.Zero||n.IndexOf("VALORANT",StringComparison.OrdinalIgnoreCase)>=0||n=="cs2"||n=="r5apex"))result.Add(new GameProcess{Id=p.Id,Name=n+".exe"});}catch{}
            // Some Windows desktop / permission combinations do not expose MainWindowHandle.
            // WMI provides a fallback to processes in the same interactive session.
            if(result.Count==0)try{foreach(var r in Wmi.Read("SELECT Name,ProcessId,SessionId FROM Win32_Process")){int id=(int)Wmi.D(r,"ProcessId");string name=Wmi.S(r,"Name");if(id!=Process.GetCurrentProcess().Id&&(int)Wmi.D(r,"SessionId")==Process.GetCurrentProcess().SessionId&&!new[]{"svchost.exe","conhost.exe","RuntimeBroker.exe","dllhost.exe","PresentMon.exe"}.Contains(name))result.Add(new GameProcess{Id=id,Name=name});}}catch{}
            return result.OrderByDescending(x=>x.Name.IndexOf("VALORANT",StringComparison.OrdinalIgnoreCase)>=0).ThenBy(x=>x.Name).ToList();
        }
    }
}
