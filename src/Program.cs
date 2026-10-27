using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

[assembly: AssemblyTitle("GamePulse · 竞技性能控制中心")]
[assembly: AssemblyDescription("Windows 游戏性能监控与调校工具接入 · 作者 d2STR0Y")]
[assembly: AssemblyCompany("d2STR0Y")]
[assembly: AssemblyProduct("GamePulse")]
[assembly: AssemblyCopyright("Copyright © 2026 d2STR0Y")]
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]

namespace GamePulse
{
    public static class Program
    {
        [STAThread] public static int Main(string[] args)
        {
            if(args.Length>1&&args[0]=="--self-test")return SelfTests.Run(args[1]);
            if(args.Length>1&&args[0]=="--capture-test")return CaptureTest.Run(args[1]);
            if(args.Length>1&&args[0]=="--diagnose")
            {
                try { using(var sensors=new SensorService()){var hw=HardwareInfo.Detect();sensors.Read("");Thread.Sleep(1100);var sample=sensors.Read("");Store.Write(args[1],new{Hardware=hw,Sample=sample,GpuSource=sensors.GpuSource,Collector=CaptureService.Collector()});return hw.Errors.Count==0?0:2;} } catch(Exception ex){File.WriteAllText(args[1],ex.ToString());return 1;}
            }
            bool render=args.Length>1&&args[0]=="--render-check";
            var app=new Application();
            app.DispatcherUnhandledException+=(s,e)=>{MessageBox.Show("操作未完成："+e.Exception.Message,"GamePulse",MessageBoxButton.OK,MessageBoxImage.Warning);e.Handled=true;};
            var window=new MainWindow(render);
            if(render)window.Loaded+=async(s,e)=>
            {
                Directory.CreateDirectory(args[1]);
                for(int i=0;i<6;i++){window.Navigate(i);await Task.Delay(150);window.UpdateLayout();var visual=(FrameworkElement)window.Content;var bmp=new RenderTargetBitmap((int)visual.ActualWidth,(int)visual.ActualHeight,96,96,PixelFormats.Pbgra32);bmp.Render(visual);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));using(var stream=File.Create(Path.Combine(args[1],"page-"+i+".png")))png.Save(stream);}
                window.Close();
            };
            return app.Run(window);
        }
    }
}
