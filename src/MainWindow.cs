using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace GamePulse
{
    public class Chart : FrameworkElement
    {
        public List<double?> Values = new List<double?>();
        public Brush LineBrush = MainWindow.Brush("#53E3C2");
        public double Maximum = 100;
        public string EmptyText="等待采集数据";
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc); double w=ActualWidth,h=ActualHeight;if(w<10||h<10)return;
            var grid=new Pen(MainWindow.Brush("#243044"),1);
            for(int i=0;i<4;i++)dc.DrawLine(grid,new Point(0,8+i*(h-24)/3),new Point(w,8+i*(h-24)/3));
            var valid=Values.Where(x=>x.HasValue).ToArray();
            if(valid.Length==0) {dc.DrawText(new FormattedText(EmptyText,System.Globalization.CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),12,MainWindow.Brush("#7D8DA7")),new Point(12,h/2-10));return;}
            double max=Maximum>0?Maximum:Math.Max(1,valid.Max(x=>x.Value)*1.15);var pen=new Pen(LineBrush,2); Point? prior=null;
            for(int i=0;i<Values.Count;i++){if(!Values[i].HasValue){prior=null;continue;} var pt=new Point(i*w/Math.Max(1,Values.Count-1),h-14-Math.Max(0,Math.Min(max,Values[i].Value))/max*(h-24)); if(prior.HasValue)dc.DrawLine(pen,prior.Value,pt);prior=pt;}
            dc.DrawText(new FormattedText((Maximum>0?Maximum:max).ToString("0.#"),System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),10,MainWindow.Brush("#7D8DA7")),new Point(w-36,9));
        }
    }
    public class MainWindow : Window
    {
        public static SolidColorBrush Brush(string hex) {var b=new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));b.Freeze();return b;}
        static readonly Brush Accent=Brush("#53E3C2"), Muted=Brush("#93A3BB"), Amber=Brush("#FFC879");
        readonly Settings settings; readonly PowerService power; readonly CaptureService capture=new CaptureService();
        SensorService sensors; HardwareInfo hardware=new HardwareInfo();
        DispatcherTimer timer; bool busy,closing; int page; readonly Stopwatch watch=Stopwatch.StartNew();
        StackPanel content,nav; TextBlock title,subtitle,status,sidebarStatus; readonly Dictionary<string,TextBlock> metrics=new Dictionary<string,TextBlock>();
        TextBlock captureStatus,recordStatus,hardwareNote,uptime,tempSource; Chart cpuChart,gpuChart,frameChart; ComboBox games;
        Button captureButton,recordButton; readonly List<Sample> history=new List<Sample>(); Session recording; double recordStart;
        Window overlay; TextBlock overlayText; bool renderMode;
        public MainWindow(bool renderMode=false)
        {
            this.renderMode=renderMode; settings=Store.LoadSettings();power=new PowerService(settings);
            Title="GamePulse · 竞技性能控制中心";Width=1180;Height=820;MinWidth=980;MinHeight=680;WindowStartupLocation=WindowStartupLocation.CenterScreen;
            Resources.MergedDictionaries.Add((ResourceDictionary)System.Windows.Markup.XamlReader.Load(typeof(MainWindow).Assembly.GetManifestResourceStream("Theme.xaml")));
            FontFamily=new FontFamily("Microsoft YaHei UI");FontSize=13;Background=Brush("#0B101A");Foreground=Brush("#E6EDF8");
            BuildShell();Navigate(0);
            Loaded+=async (s,e)=> { if(renderMode)return; sensors=await Task.Run(()=>new SensorService()); if(closing){sensors.Dispose();return;} timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};timer.Tick+=async(a,b)=>await Tick();timer.Start();await Tick(); await RefreshHardware(); };
            Closing+=(s,e)=> { if(recording!=null) {try{FinishRecord();}catch(Exception ex){MessageBox.Show(this,"测试记录保存失败："+ex.Message,"GamePulse");e.Cancel=true;return;}} closing=true;if(timer!=null)timer.Stop();capture.Dispose();if(!busy&&sensors!=null)sensors.Dispose();if(overlay!=null)overlay.Close(); };
        }
        TextBlock Text(string value,double size=13,Brush color=null) {return new TextBlock{Text=value,FontSize=size,Foreground=color??Brush("#E6EDF8"),TextWrapping=TextWrapping.Wrap};}
        TextBlock Label(string value){var t=Text(value,12,Muted);t.Margin=new Thickness(0,0,0,8);return t;}
        Button Button(string label,Action action,bool primary=false)
        {
            var b=new Button{Content=label};if(primary){b.Background=Accent;b.Foreground=Brush("#081A17");b.FontWeight=FontWeights.SemiBold;}
            b.Click+=(s,e)=>{try{action();}catch(Exception ex){Notice(ex.Message,true);}};return b;
        }
        Button AsyncButton(string label,Func<Task> action,bool primary=false)
        {
            var b=Button(label,()=>{},primary);b.Click+=async(s,e)=>{b.IsEnabled=false;try{await action();}catch(Exception ex){Notice(ex.Message,true);}finally{b.IsEnabled=true;}};return b;
        }
        StackPanel Stack(params UIElement[] children){var p=new StackPanel();foreach(var c in children)p.Children.Add(c);return p;}
        WrapPanel Row(params UIElement[] children){var p=new WrapPanel{Margin=new Thickness(0,4,0,4),VerticalAlignment=VerticalAlignment.Center};foreach(var c in children)p.Children.Add(c);return p;}
        Border Card(UIElement child,Thickness? margin=null) {return new Border{Background=Brush("#121C2B"),BorderBrush=Brush("#253247"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=new Thickness(20),Margin=margin??new Thickness(0,0,0,16),Child=child};}
        void Add(UIElement element){content.Children.Add(element);}
        void Section(string value,string desc=null){var t=Text(value,17);t.FontWeight=FontWeights.SemiBold;t.Margin=new Thickness(0,5,0,12);Add(t);if(desc!=null){var d=Label(desc);Add(d);}}
        void BuildShell()
        {
            var root=new Grid{Background=Brush("#0B101A")};root.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(210)});root.ColumnDefinitions.Add(new ColumnDefinition());Content=root;
            var side=new DockPanel{Background=Brush("#101824"),LastChildFill=true};root.Children.Add(side);
            var brand=Stack(Text("◈  GamePulse",24,Accent),Text("竞技性能控制中心",12,Muted));brand.Margin=new Thickness(22,30,15,30);DockPanel.SetDock(brand,Dock.Top);side.Children.Add(brand);
            var footer=Stack(Text("by d2STR0Y",14,Accent),Text("Instagram  0724stephen",11,Muted),Text("微信  QouC6_11",11,Muted),Text("v0.1.0  /  EARLY ACCESS",10,Muted));sidebarStatus=Text("本地监控 · 数据留在本机",11,Muted);footer.Children.Add(sidebarStatus);footer.Margin=new Thickness(22,20,14,25);DockPanel.SetDock(footer,Dock.Bottom);side.Children.Add(footer);
            nav=new StackPanel{Margin=new Thickness(12,0,12,0)};side.Children.Add(nav);
            string[] names={"◉   实时监控","▤   硬件信息","ϟ   性能方案","◇   超频调校","▥   测试记录","⚙   设置与连接"};
            for(int i=0;i<names.Length;i++){int target=i;var b=Button(names[i],()=>Navigate(target));b.HorizontalContentAlignment=HorizontalAlignment.Left;b.Margin=new Thickness(0,0,0,8);b.Padding=new Thickness(16,14,16,14);nav.Children.Add(b);}
            var main=new DockPanel{Margin=new Thickness(28,24,28,14)};Grid.SetColumn(main,1);root.Children.Add(main);
            var header=new DockPanel();DockPanel.SetDock(header,Dock.Top);header.Margin=new Thickness(0,0,0,20);main.Children.Add(header);
            var badge=Card(Text("●  LIVE",11,Accent),new Thickness(0));badge.Padding=new Thickness(12,7,12,7);badge.VerticalAlignment=VerticalAlignment.Top;DockPanel.SetDock(badge,Dock.Right);header.Children.Add(badge);
            title=Text("",27);title.FontWeight=FontWeights.SemiBold;subtitle=Text("",12,Muted);subtitle.Margin=new Thickness(0,6,0,0);header.Children.Add(Stack(title,subtitle));
            status=Text("就绪 · 监控不会自动更改硬件设置",11,Muted);status.Margin=new Thickness(0,10,0,0);DockPanel.SetDock(status,Dock.Bottom);main.Children.Add(status);
            var scroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};content=new StackPanel{Margin=new Thickness(0,0,10,0)};scroll.Content=content;main.Children.Add(scroll);
        }
        public void Navigate(int target)
        {
            page=target;content.Children.Clear();metrics.Clear();cpuChart=gpuChart=frameChart=null;captureStatus=recordStatus=hardwareNote=uptime=tempSource=null;
            for(int i=0;i<nav.Children.Count;i++){var b=(Button)nav.Children[i];b.Background=i==page?Brush("#173B38"):Brush("#101824");b.Foreground=i==page?Accent:Muted;}
            string[] titles={"掌握每一帧","你的硬件，一目了然","开局前，准备就绪","为你的硬件寻找合适设置","每一次测试，都有记录","连接你的性能数据"};
            string[] descriptions={"真实数据 · 低开销监控 · 为竞技游戏而生","自动检测本机配置，查看驱动、显示模式和运行时间","切换 Windows 电源计划，并保留原方案用于恢复","厂商工具接入 · 兼容能力由硬件、主板和驱动决定","比较帧率、帧时间和资源占用，定位卡顿线索","调整采集目标，连接温度传感器与调校工具"};
            title.Text=titles[page];subtitle.Text=descriptions[page];
            switch(page){case 0:Dashboard();break;case 1:Hardware();break;case 2:Profiles();break;case 3:Tuning();break;case 4:Records();break;case 5:SettingsPage();break;}
            if(history.Count>0)UpdateMetrics(history.Last());
        }
        void Notice(string message,bool error=false){status.Text=message;status.Foreground=error?Amber:Accent;}
        Border Metric(string key,string label,string hint,Brush color=null)
        {
            var number=Text("—",31,color??Accent);number.FontFamily=new FontFamily("Segoe UI");number.FontWeight=FontWeights.SemiBold;number.Margin=new Thickness(0,2,0,9);metrics[key]=number;
            return Card(Stack(Label(label),number,Text(hint,11,Muted)),new Thickness(0,0,12,14));
        }
        void Dashboard()
        {
            games=new ComboBox{Width=330};RefreshGames();
            captureButton=Button(capture.Running?"停止采集":"开始采集",ToggleCapture,true);
            recordButton=Button(recording!=null?"结束并保存":"开始记录",ToggleRecord);
            captureStatus=Text(capture.Status,11,Muted);
            Add(Card(Stack(Label("游戏采集目标"),Row(games,Button("刷新",RefreshGames),captureButton,recordButton),captureStatus)));
            var cards=new UniformGrid{Columns=4};cards.Children.Add(Metric("fps","游戏帧率 / FPS","最近 2 秒 · 主交换链"));cards.Children.Add(Metric("cpu","CPU 占用","全处理器平均",Brush("#96AAFF")));cards.Children.Add(Metric("gpu","GPU 占用","NVIDIA 优先 / 3D 引擎",Brush("#69B8FF")));cards.Children.Add(Metric("ping","网络探测 / ms","到设置中的检测目标",Amber));Add(cards);
            var graphs=new UniformGrid{Columns=2};cpuChart=new Chart{Height=92,LineBrush=Brush("#96AAFF")};gpuChart=new Chart{Height=92,LineBrush=Brush("#69B8FF")};graphs.Children.Add(Card(Stack(Label("CPU 负载趋势 · 0–100%"),cpuChart),new Thickness(0,0,12,14)));graphs.Children.Add(Card(Stack(Label("GPU 负载趋势 · 0–100%"),gpuChart),new Thickness(0,0,12,14)));Add(graphs);
            frameChart=new Chart{Height=102,Maximum=0,EmptyText="启动游戏并开始采集后显示真实帧时间"};
            var frameHeader=new DockPanel();var overlayButton=Button("悬浮监控",ToggleOverlay);DockPanel.SetDock(overlayButton,Dock.Right);frameHeader.Children.Add(overlayButton);frameHeader.Children.Add(Label("帧时间 / ms · 最近 120 次采样"));
            Add(Card(Stack(frameHeader,frameChart)));
            var secondary=new UniformGrid{Columns=4};secondary.Children.Add(Metric("ram","内存使用","物理内存",Muted));secondary.Children.Add(Metric("ct","CPU 温度","需连接温度传感器",Muted));secondary.Children.Add(Metric("gt","GPU 温度","可用传感器实时读取",Muted));secondary.Children.Add(Metric("low","1% LOW","最近 2 秒 · 样本较短",Muted));Add(secondary);
            recordStatus=Text(recording==null?"尚未记录测试。点击“开始记录”，完成后在测试记录中查看分析。":"正在记录…",12,Muted);Add(recordStatus);
        }
        void RefreshGames(){if(games==null)return;var selected=games.SelectedItem as GameProcess;var list=GameProcess.List();games.ItemsSource=list;games.SelectedItem=list.FirstOrDefault(x=>selected!=null&&x.Id==selected.Id)??list.FirstOrDefault();}
        void ToggleCapture()
        {
            if(recording!=null)throw new InvalidOperationException("请先结束并保存当前测试，再切换采集。");
            if(capture.Running){capture.Stop();Notice("已停止游戏采集。");}
            else{var p=games.SelectedItem as GameProcess;if(p==null)throw new InvalidOperationException("请先启动游戏，刷新列表并选择游戏进程。");capture.Start(p.Id,p.Name);Notice("正在连接游戏帧数据；权限不足时可在设置中以管理员身份重启。");}
            captureButton.Content=capture.Running?"停止采集":"开始采集";
        }
        void ToggleRecord()
        {
            if(recording!=null){FinishRecord();return;}
            string plan;try{plan=power.Current();}catch{plan="无法读取";}
            recording=new Session{Id=DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"),Started=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),Game=capture.Running?capture.Name:"系统监控（无游戏帧数据）",PowerPlan=plan,PingTarget=PingTarget()};recordStart=watch.Elapsed.TotalSeconds;capture.BeginRecord();
            if(recordButton!=null)recordButton.Content="结束并保存";Notice("开始记录。最长 30 分钟或 200 万帧；达到任一上限时自动保存。");
        }
        void FinishRecord()
        {
            if(recording==null)return;recording.Duration=watch.Elapsed.TotalSeconds-recordStart;var frames=capture.EndRecord();if(frames!=null)recording.Frames=frames;Store.Save(recording);recording=null;if(recordButton!=null)recordButton.Content="开始记录";Notice("测试已保存，可在“测试记录”中查看、比较和导出。");
        }
        string PingTarget(){return String.IsNullOrWhiteSpace(settings.PingTarget)?SensorService.Gateway():settings.PingTarget;}
        async Task Tick()
        {
            if(busy||closing||sensors==null)return;busy=true;
            try
            {
                string target=PingTarget();var sample=await Task.Run(()=>sensors.Read(target));if(closing)return;sample.Seconds=watch.Elapsed.TotalSeconds;
                var frames=capture.Recent();if(frames.Length>0){sample.Fps=1000/frames.Average();sample.FrameTime=frames.Average();}
                history.Add(sample);if(history.Count>120)history.RemoveAt(0);
                if(recording!=null){var copy=Store.Json().Deserialize<Sample>(Store.Json().Serialize(sample));copy.Seconds=sample.Seconds-recordStart;recording.Samples.Add(copy);if(copy.Seconds>=1800||capture.RecordedCount>=2000000)FinishRecord();}
                UpdateMetrics(sample);
                if(captureStatus!=null)captureStatus.Text=capture.Status+(capture.SwapChain!=null?" · 交换链 "+capture.SwapChain:"");
                if(captureButton!=null)captureButton.Content=capture.Running?"停止采集":"开始采集";
                if(recordStatus!=null)recordStatus.Text=recording==null?"数据记录在本机 · 网络目标："+(target.Length==0?"未连接网络":target):"● 正在记录 "+TimeSpan.FromSeconds(watch.Elapsed.TotalSeconds-recordStart).ToString(@"mm\:ss")+" · "+recording.Game;
                if(uptime!=null)uptime.Text=Uptime();if(tempSource!=null)tempSource.Text=sensors.TemperatureSource;
                if(overlayText!=null)overlayText.Text="GamePulse     FPS "+Format(sample.Fps,"0")+"\nCPU "+Format(sample.Cpu,"0")+"%     GPU "+Format(sample.Gpu,"0")+"%\n帧时间 "+Format(sample.FrameTime,"0.0")+" ms     探测 "+Format(sample.Ping,"0")+" ms";
            }catch(Exception e){if(!closing)Notice("监控暂不可用："+e.Message,true);}finally{busy=false;if(closing&&sensors!=null)sensors.Dispose();}
        }
        static string Format(double? n,string format){return n.HasValue?n.Value.ToString(format):"—";}
        void MetricValue(string key,double? n,string format,string suffix=""){TextBlock t;if(metrics.TryGetValue(key,out t))t.Text=n.HasValue?n.Value.ToString(format)+suffix:"—";}
        void UpdateMetrics(Sample s)
        {
            MetricValue("fps",s.Fps,"0");MetricValue("cpu",s.Cpu,"0","%");MetricValue("gpu",s.Gpu,"0","%");MetricValue("ping",s.Ping,"0");MetricValue("ram",s.Ram,"0","%");MetricValue("ct",s.CpuTemp,"0","°C");MetricValue("gt",s.GpuTemp,"0","°C");var f=capture.Recent();MetricValue("low",f.Length>0?(double?)FrameStats.Calculate(f).Low1:null,"0");
            if(cpuChart!=null){cpuChart.Values=history.Select(x=>x.Cpu).ToList();cpuChart.InvalidateVisual();}if(gpuChart!=null){gpuChart.Values=history.Select(x=>x.Gpu).ToList();gpuChart.InvalidateVisual();}if(frameChart!=null){frameChart.Values=history.Select(x=>x.FrameTime).ToList();frameChart.InvalidateVisual();}
        }
        static string Uptime(){var t=TimeSpan.FromMilliseconds(SensorService.GetTickCount64());return t.Days+" 天 "+t.Hours+" 小时 "+t.Minutes+" 分钟";}
        async Task RefreshHardware(){Notice("正在读取硬件信息…");hardware=await Task.Run(()=>HardwareInfo.Detect());if(closing)return;if(page==1||page==3)Navigate(page);Notice(hardware.Errors.Count==0?"硬件信息已更新。":"部分硬件信息读取失败，可在硬件页查看详情。",hardware.Errors.Count>0);}
        void Hardware()
        {
            var tops=new UniformGrid{Columns=3};uptime=Text(Uptime(),17,Accent);tops.Children.Add(Card(Stack(Label("电脑型号"),Text(hardware.Model,15)),new Thickness(0,0,12,16)));tops.Children.Add(Card(Stack(Label("操作系统"),Text(hardware.OS,15)),new Thickness(0,0,12,16)));tops.Children.Add(Card(Stack(Label("本次运行时间"),uptime),new Thickness(0,0,0,16)));Add(tops);
            Add(Row(AsyncButton("重新检测",RefreshHardware,true),Button("导出硬件信息",()=>{var d=new SaveFileDialog{Filter="文本文件|*.txt",FileName="GamePulse-硬件信息.txt"};if(d.ShowDialog(this)==true){File.WriteAllText(d.FileName,String.Join(Environment.NewLine+Environment.NewLine,hardware.Details.Select(x=>x.Key+Environment.NewLine+x.Value)),System.Text.Encoding.UTF8);Notice("硬件信息已导出。");}})));
            var rows=new StackPanel();foreach(var kv in hardware.Details){var g=new Grid{Margin=new Thickness(0,0,0,17)};g.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(100)});g.ColumnDefinitions.Add(new ColumnDefinition());g.Children.Add(Text(kv.Key,13,Muted));var value=Text(kv.Value);Grid.SetColumn(value,1);g.Children.Add(value);rows.Children.Add(g);}if(hardware.Details.Count==0)rows.Children.Add(Text("正在检测硬件，请稍候…",13,Muted));Add(Card(rows));
            if(hardware.Errors.Count>0)Add(Text(String.Join("\n",hardware.Errors),12,Amber));
        }
        void Profiles()
        {
            Section("Windows 电源方案","选择本机已有方案。首次切换前自动保存原方案；恢复按钮跨重启可用。");
            var plans=new ComboBox{MinWidth=310};var current=Text("读取中…",12,Muted);
            Action refresh=()=>{var list=power.List();string id=power.Current();plans.ItemsSource=list;plans.SelectedItem=list.FirstOrDefault(x=>x.Id==id);current.Text="当前方案："+(list.FirstOrDefault(x=>x.Id==id)??new PowerPlan{Name=id}).Name;};
            try{refresh();}catch(Exception e){current.Text=e.Message;}
            Add(Card(Stack(Label("选择性能方案"),Row(plans,AsyncButton("应用方案",async()=>{var p=plans.SelectedItem as PowerPlan;if(p==null)throw new InvalidOperationException("请选择电源方案。");await Task.Run(()=>power.Apply(p.Id));refresh();Notice("已应用："+p.Name);},true),AsyncButton("恢复原方案",async()=>{await Task.Run(()=>power.Restore());refresh();Notice("原电源计划已恢复。");})),current)));
            Section("游戏前检查");var check=Stack(Text("01  连接电源并确认散热状态",16),Label("笔记本的性能取决于厂商设置、电源状态和散热能力。"),Text("02  选择游戏，开始帧率采集",16),Label("建议在相同地图、画质与帧率上限下记录前后两次测试。"),Text("03  用帧时间判断流畅度",16),Label("更高的平均帧率不一定意味着更少的卡顿，观察 1% Low 和 P99 帧时间。"));Add(Card(check));
            Add(Card(Stack(Text("已验证的 GPU 配置",17),Label("在超频调校页连接 MSI Afterburner，可一键加载你已经测试过的配置槽。"),Row(Button("进入超频调校",()=>Navigate(3))))));
        }
        string DetectTool(params string[] relatives)
        {
            foreach(string root in new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)})foreach(string relative in relatives){string p=Path.Combine(root,relative);if(File.Exists(p))return p;}return null;
        }
        string CpuTool(){return !String.IsNullOrEmpty(settings.CpuTool)?settings.CpuTool:DetectTool(@"AMD\RyzenMaster\bin\AMD Ryzen Master.exe",@"Intel\Intel(R) Extreme Tuning Utility\Client\XtuUiLauncher.exe",@"Intel\Intel(R) Extreme Tuning Utility\Client\XtuUi.exe");}
        string GpuTool(){return !String.IsNullOrEmpty(settings.GpuTool)?settings.GpuTool:DetectTool(@"NVIDIA Corporation\NVIDIA app\CEF\NVIDIA app.exe",@"AMD\CNext\CNext\RadeonSoftware.exe");}
        void LaunchTool(string path){if(String.IsNullOrWhiteSpace(path)||!File.Exists(path))throw new InvalidOperationException("未找到调校工具。请在设置页连接已安装的厂商工具。");Commands.Open(path);Notice("已打开厂商工具，请在工具内完成调校和稳定性检查。");}
        void Tuning()
        {
            Add(Card(Stack(Text("按实际支持能力调校",18,Accent),Text("当前版本通过厂商工具进行自动／手动调校，不直接写入 CPU、GPU 电压或频率。硬件型号只能判断候选能力，具体支持与结果由厂商工具确认。",13,Muted))));
            var two=new UniformGrid{Columns=2};
            two.Children.Add(Card(Stack(Label("CPU / 处理器"),Text(hardware.Cpu.Split('\n')[0],16),Text("AMD Ryzen Master / Intel XTU",12,Muted),Text(CpuTool()!=null?"● 已找到工具 · 硬件支持待工具确认":"○ 尚未连接官方工具",12,CpuTool()!=null?Accent:Amber),Row(Button("打开 CPU 调校",()=>LaunchTool(CpuTool()),true))),new Thickness(0,0,12,16)));
            two.Children.Add(Card(Stack(Label("GPU / 显卡"),Text(hardware.Gpu.Split('\n')[0],16),Text("NVIDIA App / AMD Software",12,Muted),Text(GpuTool()!=null?"● 已找到工具 · 支持自动调校的设备可在工具内开启":"○ 尚未连接官方工具",12,GpuTool()!=null?Accent:Amber),Row(Button("打开 GPU 调校",()=>LaunchTool(GpuTool()),true))),new Thickness(0,0,0,16)));Add(two);
            Section("自动调校与手动调校");Add(Card(Stack(Text("自动：在官方工具内运行自动调校／扫描，完成后返回 GamePulse 记录游戏表现。",14),Label("同型号芯片的体质、散热和主板供电不同，不能仅根据型号给出稳定频率。"),Text("手动：在官方工具内调整支持的频率、功耗和其他参数，然后保存配置。",14),Label("超频可能导致崩溃、数据损坏或硬件损坏，并可能影响保修。测试通过不能保证所有负载长期稳定。"),Text("内存：XMP / EXPO 是否可用由主板与内存决定，通常需要进入 BIOS 并重启。",14))));
            Section("一键加载已验证 GPU 配置","需要 MSI Afterburner 中已经保存的配置槽。此操作不会自动寻找频率，也不会自动判断配置稳定性。");
            var slots=new ComboBox{Width=160,ItemsSource=new[]{"配置槽 1","配置槽 2","配置槽 3","配置槽 4","配置槽 5"},SelectedIndex=Math.Max(0,Math.Min(4,settings.AfterburnerProfile-1))};
            var verified=new CheckBox{Content="此配置已由我完成稳定性测试",IsChecked=settings.ProfileVerified};
            slots.SelectionChanged+=(s,e)=>{settings.ProfileVerified=false;verified.IsChecked=false;settings.AfterburnerProfile=slots.SelectedIndex+1;Store.Save(settings);};
            verified.Checked+=(s,e)=>{settings.ProfileVerified=true;Store.Save(settings);};verified.Unchecked+=(s,e)=>{settings.ProfileVerified=false;Store.Save(settings);};
            Add(Card(Stack(Row(slots,Button("加载已验证配置",()=>{if(verified.IsChecked!=true)throw new InvalidOperationException("请先在 Afterburner 中测试并保存配置，再勾选已验证。");string path=settings.Afterburner??DetectTool(@"MSI Afterburner\MSIAfterburner.exe");if(path==null||!File.Exists(path))throw new InvalidOperationException("请在设置中连接 MSI Afterburner。");Process.Start(new ProcessStartInfo(path,"-Profile"+(slots.SelectedIndex+1)){UseShellExecute=true});Notice("已向 Afterburner 发送配置槽加载请求。请在该工具中核对是否应用成功。");},true)),verified,Label("恢复 GPU 默认设置请使用厂商工具或 Afterburner 的重置按钮。"))));
            Add(Row(Button("连接调校工具",()=>Navigate(5)),Button("开始性能对比",()=>Navigate(0))));
        }
        void Records()
        {
            var sessions=Store.LoadSessions();
            Add(Row(Button("刷新记录",()=>Navigate(4),true),Button("打开记录文件夹",()=>{Directory.CreateDirectory(Store.Records);Commands.Open(Store.Records);})));
            if(sessions.Count==0){Add(Card(Stack(Text("你的第一份性能报告，从下一局开始",20),Label("在实时监控中选择游戏并开始采集，然后点击“开始记录”。结束后会生成帧率统计与卡顿分析。"),Row(Button("开始监控",()=>Navigate(0),true)))));return;}
            var list=new ListBox{MaxHeight=190};foreach(var s in sessions){var fs=FrameStats.Calculate(s.Frames);list.Items.Add(new ListBoxItem{Tag=s,Content=Text(s.Started+"    "+s.Game+"    "+s.Duration.ToString("0")+" 秒    "+(fs.Count>0?fs.Average.ToString("0")+" FPS":"无帧数据"))});}
            var detail=new StackPanel();list.SelectionChanged+=(s,e)=>{var item=list.SelectedItem as ListBoxItem;if(item==null)return;Session selected=(Session)item.Tag;detail.Children.Clear();ShowRecord(detail,selected);};Add(Card(list));Add(detail);list.SelectedIndex=0;
            if(sessions.Count>=2){FrameStats a=FrameStats.Calculate(sessions[1].Frames),b=FrameStats.Calculate(sessions[0].Frames);Section("最近两次对比");if(a.Count>0&&b.Count>0){Add(Card(Stack(Text("平均 FPS  "+a.Average.ToString("0.0")+" → "+b.Average.ToString("0.0")+"   ("+((b.Average/a.Average-1)*100).ToString("+0.0;-0.0;0")+"%)",17,Accent),Text("1% Low  "+a.Low1.ToString("0.0")+" → "+b.Low1.ToString("0.0")+" FPS",14),Label("仅当游戏、场景、画质与帧率上限一致时，对比才有参考意义。"))));}else Add(Label("最近两次测试中存在无帧数据记录，无法比较 FPS。"));}
        }
        void ShowRecord(StackPanel panel,Session session)
        {
            var fs=FrameStats.Calculate(session.Frames);var detail=Stack(Text(session.Game,20),Label(session.Started+" · "+session.Duration.ToString("0")+" 秒 · "+session.Samples.Count+" 次系统采样 · "+fs.Count+" 帧"));
            if(fs.Count>0){detail.Children.Add(Text("平均 FPS  "+fs.Average.ToString("0.0")+"       1% Low  "+fs.Low1.ToString("0.0")+"       P99  "+fs.P99.ToString("0.00")+" ms",18,Accent));detail.Children.Add(Text("帧时间尖峰  "+fs.Stutters+" 次（阈值 > "+fs.Threshold.ToString("0.0")+" ms）",14,Amber));detail.Children.Add(Label("1% Low = 1000 ÷ 最慢 1% 帧的平均帧时间。尖峰阈值取 25 ms 与全程中位数 2 倍中的较大值。"));}
            else detail.Children.Add(Text("此记录没有游戏帧数据，不能据此判断 FPS 或游戏卡顿。",14,Amber));
            var hints=new List<string>();if(session.Samples.Count>0){if(session.Samples.Count(x=>x.Gpu>=95)>session.Samples.Count*.5)hints.Add("GPU 在超过半数采样中达到 95% 以上，可检查分辨率与画质；这只是负载线索。 ");if(session.Samples.Count(x=>x.Cpu>=90)>session.Samples.Count*.3)hints.Add("CPU 在多次采样中负载较高，可检查后台程序；总占用无法排除单线程瓶颈。");if(session.Samples.Any(x=>x.Ram>=90))hints.Add("出现内存使用率 ≥90% 的采样，可能增加换页压力。");if(session.Samples.Any(x=>x.CpuTemp>=90||x.GpuTemp>=85))hints.Add("记录中有较高温度，建议核对该型号的温度规格；不能据此确认发生降频。");}
            if(hints.Count==0)hints.Add("当前采样未发现持续高占用线索。单次尖峰不能直接确定原因，可用相同场景复测。");
            foreach(string hint in hints)detail.Children.Add(Text("• "+hint,13,Muted));
            int replies=session.Samples.Count(x=>x.Ping.HasValue);detail.Children.Add(Label("网络目标："+session.PingTarget+" · ICMP 成功回应 "+replies+" / "+session.Samples.Count+"（不等于游戏服务器延迟或游戏丢包率）"));
            detail.Children.Add(Row(Button("导出 CSV + 逐帧数据",()=>{var d=new SaveFileDialog{Filter="CSV 数据|*.csv",FileName="GamePulse-"+session.Id+".csv"};if(d.ShowDialog(this)==true){Store.Export(session,d.FileName);Notice("已导出系统采样 CSV 和同目录的 -frames.csv 逐帧数据。");}},true)));
            panel.Children.Add(Card(detail));
        }
        void SettingsPage()
        {
            Section("网络探测","默认检测本机网络网关。也可填写你希望检测的 IP 或主机名；ICMP 可能被目标阻止。");var target=new TextBox{Text=settings.PingTarget??"",Width=330};
            Add(Card(Stack(Row(target,Button("保存目标",()=>{string value=target.Text.Trim();if(value.Length>0&&!SensorService.ValidTarget(value))throw new InvalidOperationException("请输入有效的 IP 地址或主机名，不含协议、路径或端口。");if(recording!=null)throw new InvalidOperationException("请先结束记录再更改检测目标。");settings.PingTarget=value;Store.Save(settings);Notice("网络目标已保存："+PingTarget());},true)),Label("留空 = 自动选择默认网关 · 当前目标："+PingTarget()))));
            Section("CPU / AMD GPU 温度传感器");tempSource=Text(sensors==null?"检测中…":sensors.TemperatureSource,13,Accent);
            Add(Card(Stack(tempSource,Text("NVIDIA GPU 温度由已安装驱动读取。CPU 或其他 GPU 的温度可连接支持 WMI 的 LibreHardwareMonitor（例如 v0.9.4）或 OpenHardwareMonitor。以管理员身份运行该工具并保持打开，WMI 提供程序会随工具启动。",13,Muted),Label("兼容版本取决于硬件支持。新版工具可能不含 WMI。传感器会在最多 30 次采样后重新检测；GamePulse 本身不安装内核驱动。"),Row(Button("打开兼容版本说明",()=>Commands.Open("https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/tag/v0.9.4"))))));
            Section("调校工具路径","选择你已安装的官方调校程序。打开工具后，在工具内完成自动扫描或手动参数调整。");
            ToolSetting("CPU 官方工具",CpuTool(),p=>{settings.CpuTool=p;Store.Save(settings);});ToolSetting("GPU 官方工具",GpuTool(),p=>{settings.GpuTool=p;Store.Save(settings);});ToolSetting("MSI Afterburner",settings.Afterburner??DetectTool(@"MSI Afterburner\MSIAfterburner.exe"),p=>{settings.Afterburner=p;settings.ProfileVerified=false;Store.Save(settings);});
            Section("游戏帧率采集");Add(Card(Stack(Text("内置 PresentMon 2.3.0 · Intel 签名组件",15),Label("只采集所选进程的首个有效交换链。无数据时请核对游戏进程和权限；多窗口应用可能需要换目标。悬浮监控适用于窗口／无边框模式。"),Row(Button("以管理员身份重启",()=>{if(recording!=null)throw new InvalidOperationException("请先结束记录。");if(Commands.IsAdministrator){Notice("当前已经是管理员模式。");return;}Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName){UseShellExecute=true,Verb="runas"});Close();})),Label("当前权限："+(Commands.IsAdministrator?"管理员":"普通用户")))));
            Add(Card(Stack(Text("GamePulse  ·  by d2STR0Y",20,Accent),Text("Instagram：0724stephen    微信 ID：QouC6_11",14),Row(Button("Instagram",()=>Commands.Open("https://www.instagram.com/0724stephen/")),Button("GitHub 仓库",()=>Commands.Open("https://github.com/r15008/GamePulse"))))));
            Add(Label("数据存储："+Store.Root+"\n所有记录保存在本机。程序不会自动上传硬件信息或测试记录。\nGamePulse v0.1.0 · Windows 10/11 x64 · .NET Framework 4.8"));
        }
        void ToolSetting(string label,string value,Action<string> save)
        {
            var path=Text(value??"尚未连接",11,Muted);Add(Card(Stack(Text(label,14),path,Row(Button("选择程序",()=>{var d=new OpenFileDialog{Filter="Windows 程序|*.exe",Title="选择 "+label};if(d.ShowDialog(this)==true){save(d.FileName);path.Text=d.FileName;Notice("已保存工具路径。");}}))),new Thickness(0,0,0,10)));
        }
        void ToggleOverlay()
        {
            if(overlay!=null){overlay.Close();return;}overlayText=Text("等待数据…",13,Accent);var close=Button("×",()=>overlay.Close());close.Padding=new Thickness(8,1,8,1);var dock=new DockPanel();DockPanel.SetDock(close,Dock.Right);dock.Children.Add(close);dock.Children.Add(overlayText);
            overlay=new Window{Title="GamePulse 悬浮监控",Width=365,Height=120,Topmost=true,ResizeMode=ResizeMode.NoResize,WindowStyle=WindowStyle.None,AllowsTransparency=true,Background=Brush("#E6111B2B"),Content=new Border{Padding=new Thickness(15),Child=dock},ShowInTaskbar=false};overlay.Resources=Resources;overlay.MouseLeftButtonDown+=(s,e)=>{if(e.LeftButton==MouseButtonState.Pressed)overlay.DragMove();};overlay.Closed+=(s,e)=>{overlay=null;overlayText=null;};overlay.Show();
        }
    }
}
