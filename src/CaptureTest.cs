using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace GamePulse
{
    // Explicit integration harness: real ETW frames from an animated WPF window.
    // Never feeds fixture values to the application's dashboard or user records.
    public static class CaptureTest
    {
        public static int Run(string output)
        {
            var app=new Application();var window=new Window{Title="GamePulse 帧采集验证",Width=500,Height=320,Background=Brushes.Black};
            var square=new Rectangle{Width=90,Height=90,Fill=Brushes.Turquoise,RenderTransformOrigin=new Point(.5,.5)};var rotate=new RotateTransform();square.RenderTransform=rotate;window.Content=square;int result=1;
            window.Loaded+=async(s,e)=>
            {
                using(var capture=new CaptureService())try
                {
                    rotate.BeginAnimation(RotateTransform.AngleProperty,new DoubleAnimation(0,360,TimeSpan.FromSeconds(2)){RepeatBehavior=RepeatBehavior.Forever});
                    capture.Start(Process.GetCurrentProcess().Id,"GamePulse capture test");await Task.Delay(2500);capture.BeginRecord();await Task.Delay(10000);
                    var frames=capture.EndRecord();var stats=FrameStats.Calculate(frames);Store.Write(output,new{Test="Real ETW frames from animated WPF window",Status=capture.Status,Stats=stats,Processes=GameProcess.List().Count,Admin=Commands.IsAdministrator});result=stats.Count>30?0:2;
                }catch(Exception ex){File.WriteAllText(output,ex.ToString());}finally{window.Close();}
            };
            app.Run(window);return result;
        }
    }
}
