using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using MySystem.Core;
using Windows.Foundation;
namespace MySystem.App.Views;
public sealed class HistoryChart : Grid
{
 readonly Canvas plot=new(); readonly TextBlock caption=new(){FontSize=12,Margin=new(8)};
 IReadOnlyList<Metric> points=Array.Empty<Metric>();
 public HistoryChart(){Height=220;MinWidth=180;Background=new SolidColorBrush(Windows.UI.Color.FromArgb(16,80,150,140));Children.Add(plot);Children.Add(caption);SizeChanged+=(_,_)=>Draw();PointerMoved+=(_,e)=>{if(points.Count==0)return;int i=Math.Clamp((int)(e.GetCurrentPoint(this).Position.X/Math.Max(1,ActualWidth)*(points.Count-1)),0,points.Count-1);var p=points[i];caption.Text=$"{p.Timestamp.ToLocalTime():HH:mm:ss} • {p.Value?.ToString("0.##")??"N/A"} {p.Unit}";};Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this,"History chart. Hover to inspect timestamp and value.");}
 public void SetPoints(IReadOnlyList<Metric> values){points=values;Draw();}
 void Draw(){plot.Children.Clear();if(points.Count==0){caption.Text="Waiting for real measurements";return;}double w=Math.Max(1,ActualWidth),h=Math.Max(1,ActualHeight-45),max=Math.Max(1,points.Where(p=>p.Value.HasValue).Select(p=>p.Value!.Value).DefaultIfEmpty(1).Max());if(points[0].Unit=="%")max=100;for(int n=1;n<=4;n++)plot.Children.Add(new Line{X1=0,X2=w,Y1=35+h*n/4,Y2=35+h*n/4,Stroke=new SolidColorBrush(Windows.UI.Color.FromArgb(25,150,180,180)),StrokeThickness=1});Polyline? line=null;for(int i=0;i<points.Count;i++){var p=points[i];if(p.Value is null||p.Availability!=MetricAvailability.Available){line=null;continue;}if(line is null){line=new(){Stroke=new SolidColorBrush(Windows.UI.Color.FromArgb(255,65,219,193)),StrokeThickness=2};plot.Children.Add(line);}line.Points.Add(new Point(i*w/Math.Max(1,points.Count-1),35+h-Math.Clamp(p.Value.Value/max,0,1)*h));}caption.Text=$"{points.First().Timestamp.ToLocalTime():HH:mm:ss} – {points.Last().Timestamp.ToLocalTime():HH:mm:ss} • scale 0–{max:0.##} {points[0].Unit}";}
}
