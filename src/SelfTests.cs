using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace GamePulse
{
    public static class SelfTests
    {
        public static int Run(string report)
        {
            var checks=new List<string>();string originalRoot=Store.Root;
            try
            {
                Action<bool,string> check=(condition,name)=>{if(!condition)throw new Exception("FAIL: "+name);checks.Add("PASS: "+name);};
                var stats=FrameStats.Calculate(Enumerable.Repeat(10.0,99).Concat(new[]{100.0}));
                check(Math.Abs(stats.Average-100000.0/1090)<.0001,"FPS is reciprocal of mean frame time");
                check(stats.Low1==10&&stats.P99==10&&stats.Stutters==1,"1% low, nearest-rank P99 and spike threshold");
                check(FrameStats.Calculate(new double[0]).Count==0,"Empty capture stays unavailable");
                check(FrameStats.Calculate(new[]{Double.NaN,0,-1,Double.PositiveInfinity,5.0}).Count==1,"Invalid frame values rejected");
                var parser=new CsvFrameParser{ExpectedPid=42};parser.Read("Application,ProcessID,SwapChainAddress,FrameTime");
                check(parser.Read("\"game,client.exe\",42,0xA,4.5")==4.5,"Quoted process names and decimal frame data");
                check(!parser.Read("game.exe,99,0xA,4.5").HasValue,"Reject unrelated process");
                check(!parser.Read("game.exe,42,0xB,4.5").HasValue,"Reject extra swapchains to avoid doubled FPS");
                check(!parser.Read("game.exe,42,0xA,NaN").HasValue&&!parser.Read("bad row").HasValue,"Malformed CSV is ignored");
                var legacy=new CsvFrameParser{ExpectedPid=1};legacy.Read("Application,ProcessID,MsBetweenPresents");check(legacy.Read("game.exe,1,16.6")==16.6,"Legacy PresentMon column support");
                var previous=CultureInfo.CurrentCulture;System.Threading.Thread.CurrentThread.CurrentCulture=new CultureInfo("fr-FR");check(parser.Read("game.exe,42,0xA,6.25")==6.25,"CSV parser is culture-independent");System.Threading.Thread.CurrentThread.CurrentCulture=previous;
                var plans=PowerService.Parse("电源方案 GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (平衡) *\nPower Scheme GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c (High performance)");check(plans.Count==2&&plans[0].Name=="平衡","Localized power plan parsing");
                check(SensorService.ValidTarget("1.1.1.1")&&SensorService.ValidTarget("example.com")&&!SensorService.ValidTarget("https://example.com/a")&&!SensorService.ValidTarget("x & cmd"),"Only IP / host names accepted for ping");
                check(Store.Csv("=cmd").StartsWith("\"'="),"Spreadsheet formula injection neutralized");
                Store.Root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report)),"test-data-"+Guid.NewGuid().ToString("N"));
                var settings=new Settings{OriginalPowerPlan="keep-original",PingTarget="example.com"};Store.Save(settings);settings.LastAppliedPlan="changed";Store.Save(settings);check(Store.LoadSettings().OriginalPowerPlan=="keep-original","Atomic settings persistence preserves original plan");
                var session=new Session{Id="test",Game="=test",Started="2026-01-01",Frames=new List<double>{10,12},Samples=new List<Sample>{new Sample{Cpu=25,Seconds=1}}};Store.Save(session);check(Store.LoadSessions().Single().Frames.Count==2,"Session round trip");
                string csv=Path.Combine(Store.Root,"export.csv");Store.Export(session,csv);check(File.Exists(Path.Combine(Store.Root,"export-frames.csv"))&&File.ReadAllText(csv).Contains("\"'=test\""),"Paired metric and per-frame export");
                File.WriteAllText(Path.Combine(Store.Records,"corrupt.json"),"bad data");check(Store.LoadSessions().Count==1,"Corrupt record does not break history");
                check(File.Exists(CaptureService.Collector()),"Embedded collector hash verified");
                checks.Add("All "+checks.Count+" checks passed.");File.WriteAllLines(report,checks);return 0;
            }
            catch(Exception e){checks.Add(e.ToString());File.WriteAllLines(report,checks);return 1;}
            finally{Store.Root=originalRoot;}
        }
    }
}
