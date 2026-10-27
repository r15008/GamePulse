using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace GamePulse
{
    public static class Wmi
    {
        public static List<Dictionary<string, object>> Read(string query, string scope = @"root\cimv2")
        {
            var rows = new List<Dictionary<string, object>>();
            using (var searcher = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(query), new EnumerationOptions { Timeout = TimeSpan.FromSeconds(3), ReturnImmediately = true }))
            using (var results = searcher.Get())
                foreach (ManagementObject obj in results)
                    using (obj) { var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase); foreach (PropertyData p in obj.Properties) row[p.Name] = p.Value; rows.Add(row); }
            return rows;
        }
        public static string S(Dictionary<string, object> row, string key) { return row.ContainsKey(key) && row[key] != null ? Convert.ToString(row[key], CultureInfo.InvariantCulture).Trim() : "未提供"; }
        public static double D(Dictionary<string, object> row, string key) { double n; return Double.TryParse(S(row, key), NumberStyles.Float, CultureInfo.InvariantCulture, out n) ? n : 0; }
    }
    public class HardwareInfo
    {
        public string Model = "正在检测…", OS = "正在检测…", Cpu = "正在检测…", Gpu = "正在检测…", Ram = "正在检测…";
        public Dictionary<string, string> Details = new Dictionary<string, string>();
        public List<string> Errors = new List<string>();
        void Query(string label, string q, Func<Dictionary<string, object>, string> format, string scope = @"root\cimv2")
        {
            try { var rows = Wmi.Read(q, scope); Details[label] = rows.Count == 0 ? "未提供" : String.Join("\n", rows.Select(format)); }
            catch (Exception e) { Details[label] = "无法读取"; Errors.Add(label + "：" + e.Message); }
        }
        public static HardwareInfo Detect()
        {
            var h = new HardwareInfo();
            h.Query("电脑型号", "SELECT Manufacturer,Model FROM Win32_ComputerSystem", r => Wmi.S(r, "Manufacturer") + " " + Wmi.S(r, "Model")); h.Model = h.Details["电脑型号"];
            h.Query("操作系统", "SELECT Caption,Version,BuildNumber,OSArchitecture FROM Win32_OperatingSystem", r => Wmi.S(r,"Caption") + " · " + Wmi.S(r,"OSArchitecture") + " · Build " + Wmi.S(r,"BuildNumber")); h.OS = h.Details["操作系统"];
            h.Query("处理器", "SELECT Name,NumberOfCores,NumberOfLogicalProcessors FROM Win32_Processor", r => Wmi.S(r,"Name") + "\n" + Wmi.S(r,"NumberOfCores") + " 核 / " + Wmi.S(r,"NumberOfLogicalProcessors") + " 线程"); h.Cpu = h.Details["处理器"];
            h.Query("主板", "SELECT Manufacturer,Product FROM Win32_BaseBoard", r => Wmi.S(r,"Manufacturer") + " " + Wmi.S(r,"Product"));
            h.Query("内存", "SELECT Manufacturer,Capacity,ConfiguredClockSpeed,SMBIOSMemoryType,DeviceLocator FROM Win32_PhysicalMemory", r => Wmi.S(r,"DeviceLocator") + " · " + (Wmi.D(r,"Capacity") / 1073741824).ToString("0.#") + " GB · " + (Wmi.D(r,"SMBIOSMemoryType") == 34 ? "DDR5" : Wmi.D(r,"SMBIOSMemoryType") == 26 ? "DDR4" : "RAM") + " · 配置速率 " + Wmi.S(r,"ConfiguredClockSpeed") + " MT/s · " + Wmi.S(r,"Manufacturer")); h.Ram = h.Details["内存"];
            h.Query("显卡", "SELECT Name,DriverVersion,CurrentHorizontalResolution,CurrentVerticalResolution,CurrentRefreshRate FROM Win32_VideoController", r => Wmi.S(r,"Name") + " · 驱动 " + Wmi.S(r,"DriverVersion")); h.Gpu = h.Details["显卡"];
            h.Query("显示器", "SELECT UserFriendlyName FROM WmiMonitorID", r => r["UserFriendlyName"] is ushort[] ? new String(((ushort[])r["UserFriendlyName"]).Where(x => x != 0).Select(x => (char)x).ToArray()) : "未提供", @"root\wmi");
            h.Query("显示模式", "SELECT Name,CurrentHorizontalResolution,CurrentVerticalResolution,CurrentRefreshRate FROM Win32_VideoController", r => Wmi.D(r,"CurrentHorizontalResolution") > 0 ? Wmi.S(r,"Name") + " · " + Wmi.S(r,"CurrentHorizontalResolution") + " × " + Wmi.S(r,"CurrentVerticalResolution") + " · " + Wmi.S(r,"CurrentRefreshRate") + " Hz" : Wmi.S(r,"Name") + " · 未连接显示器");
            h.Query("声卡", "SELECT Name FROM Win32_SoundDevice", r => Wmi.S(r,"Name"));
            h.Query("网卡", "SELECT Name FROM Win32_NetworkAdapter WHERE PhysicalAdapter=True", r => Wmi.S(r,"Name"));
            h.Query("固件", "SELECT SMBIOSBIOSVersion,Manufacturer FROM Win32_BIOS", r => Wmi.S(r,"Manufacturer") + " " + Wmi.S(r,"SMBIOSBIOSVersion"));
            try { var memory = Wmi.Read("SELECT Capacity FROM Win32_PhysicalMemory"); h.Ram="总计 "+(memory.Sum(x=>Wmi.D(x,"Capacity"))/1073741824).ToString("0.#")+" GB · "+memory.Count+" 条\n"+h.Ram;h.Details["内存"]=h.Ram; } catch{}
            var gpuNames=h.Gpu.Split('\n'); h.Gpu=gpuNames.FirstOrDefault(x=>x.IndexOf("NVIDIA",StringComparison.OrdinalIgnoreCase)>=0)??gpuNames.FirstOrDefault(x=>x.IndexOf("Radeon",StringComparison.OrdinalIgnoreCase)>=0)??h.Gpu;
            return h;
        }
    }
    public class SensorService : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] struct FT { public uint Low, High; public ulong Value { get { return ((ulong)High << 32) | Low; } } }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] class MEMORY { public uint Length = (uint)Marshal.SizeOf(typeof(MEMORY)); public uint Load; public ulong TotalPhys, AvailPhys, TotalPage, AvailPage, TotalVirtual, AvailVirtual, Extended; }
        [StructLayout(LayoutKind.Sequential)] struct Util { public uint Gpu, Memory; }
        [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out FT idle, out FT kernel, out FT user);
        [DllImport("kernel32.dll", CharSet=CharSet.Auto)] static extern bool GlobalMemoryStatusEx([In,Out] MEMORY memory);
        [DllImport("kernel32.dll")] public static extern ulong GetTickCount64();
        [DllImport("nvml.dll", CallingConvention=CallingConvention.Cdecl)] static extern int nvmlInit_v2();
        [DllImport("nvml.dll", CallingConvention=CallingConvention.Cdecl)] static extern int nvmlShutdown();
        [DllImport("nvml.dll", CallingConvention=CallingConvention.Cdecl)] static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);
        [DllImport("nvml.dll", CallingConvention=CallingConvention.Cdecl)] static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out Util util);
        [DllImport("nvml.dll", CallingConvention=CallingConvention.Cdecl)] static extern int nvmlDeviceGetTemperature(IntPtr device, uint sensor, out uint value);
        [DllImport("nvml.dll", CallingConvention=CallingConvention.Cdecl)] static extern int nvmlDeviceGetPowerUsage(IntPtr device, out uint value);
        [DllImport("nvml.dll", CallingConvention=CallingConvention.Cdecl)] static extern int nvmlDeviceGetName(IntPtr device, System.Text.StringBuilder name, uint length);
        ulong idleLast, totalLast; bool primed, nvInitialized; IntPtr gpu; int ticks; string lhmNamespace;
        public string GpuSource = "Windows GPU 引擎";
        public string TemperatureSource = "等待温度传感器";
        public SensorService()
        {
            try { if (nvmlInit_v2() == 0) { nvInitialized = true; if (nvmlDeviceGetHandleByIndex_v2(0, out gpu) == 0) { var b = new System.Text.StringBuilder(128); if (nvmlDeviceGetName(gpu,b,128)==0) GpuSource = b.ToString(); } } } catch { gpu = IntPtr.Zero; }
        }
        public static string Gateway()
        {
            try { foreach (var n in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)) foreach (var g in n.GetIPProperties().GatewayAddresses) if (g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)) return g.Address.ToString(); } catch { }
            return "";
        }
        public static bool ValidTarget(string value) { return value.Length <= 253 && Uri.CheckHostName(value) != UriHostNameType.Unknown && !value.Any(Char.IsWhiteSpace); }
        public Sample Read(string target)
        {
            var s = new Sample(); FT idle, kernel, user;
            if (GetSystemTimes(out idle, out kernel, out user)) { ulong total = kernel.Value + user.Value; if (primed && total > totalLast) s.Cpu = Math.Max(0, Math.Min(100, 100.0 * (1 - (double)(idle.Value - idleLast) / (total - totalLast)))); idleLast = idle.Value; totalLast = total; primed = true; }
            var mem = new MEMORY(); if (GlobalMemoryStatusEx(mem)) s.Ram = 100.0 * (mem.TotalPhys - mem.AvailPhys) / mem.TotalPhys;
            if (gpu != IntPtr.Zero) try { Util u; uint t; if (nvmlDeviceGetUtilizationRates(gpu,out u)==0) s.Gpu=u.Gpu; if(nvmlDeviceGetTemperature(gpu,0,out t)==0) s.GpuTemp=t; if(nvmlDeviceGetPowerUsage(gpu,out t)==0) s.GpuPower=t/1000.0; } catch { }
            if (!s.Gpu.HasValue) try { var engines = Wmi.Read("SELECT Name,UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine"); var groups = engines.Where(x=>Wmi.S(x,"Name").EndsWith("engtype_3D",StringComparison.OrdinalIgnoreCase)).GroupBy(x=>{ string n=Wmi.S(x,"Name"); int i=n.IndexOf("luid_",StringComparison.Ordinal); return i>=0?n.Substring(i):n; }); if(groups.Any()) s.Gpu=Math.Min(100,groups.Max(g=>g.Sum(x=>Wmi.D(x,"UtilizationPercentage")))); } catch { }
            if (lhmNamespace == null && ticks % 30 == 0) { foreach(var ns in new[]{@"root\LibreHardwareMonitor",@"root\OpenHardwareMonitor"}) try { if(Wmi.Read("SELECT Name FROM Sensor",ns).Count>0) { lhmNamespace=ns; break; } } catch{} }
            ticks++;
            if (lhmNamespace != null) try
            {
                var sensors=Wmi.Read("SELECT Name,Identifier,SensorType,Value FROM Sensor WHERE SensorType='Temperature'",lhmNamespace);
                var cpus=sensors.Where(x=>Wmi.S(x,"Identifier").IndexOf("cpu",StringComparison.OrdinalIgnoreCase)>=0).Select(x=>Wmi.D(x,"Value")).Where(x=>x>0&&x<150).ToArray();
                if(cpus.Length>0) s.CpuTemp=cpus.Max();
                if (!s.GpuTemp.HasValue) { var gs=sensors.Where(x=>Wmi.S(x,"Identifier").IndexOf("gpu",StringComparison.OrdinalIgnoreCase)>=0 && Wmi.S(x,"Name").IndexOf("core",StringComparison.OrdinalIgnoreCase)>=0).Select(x=>Wmi.D(x,"Value")).Where(x=>x>0&&x<150).ToArray(); if(gs.Length>0)s.GpuTemp=gs.Max(); }
                TemperatureSource=s.CpuTemp.HasValue?"CPU：LibreHardwareMonitor / OpenHardwareMonitor（最高可用 CPU 传感器）":"CPU 温度传感器未提供";
            } catch { lhmNamespace=null; }
            if (s.GpuTemp.HasValue && lhmNamespace==null) TemperatureSource="GPU：NVIDIA NVML · CPU：需连接温度传感器";
            if(ValidTarget(target)) try { using(var ping=new Ping()){ var r=ping.Send(target,700); if(r.Status==IPStatus.Success) s.Ping=r.RoundtripTime; } }catch{}
            return s;
        }
        public void Dispose() { if(nvInitialized)try{nvmlShutdown();}catch{} }
    }
}
