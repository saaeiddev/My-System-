using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MySystem.Core;
using MySystem.Gaming;
using MySystem.Infrastructure;
namespace MySystem.App.Views;
public sealed class OverlayWindow:Window
{
 readonly AppSettings settings;readonly TextBlock text=new(){FontFamily=new FontFamily("Consolas"),FontSize=15,Foreground=new SolidColorBrush(Windows.UI.Color.FromArgb(255,220,245,240)),Margin=new(16)};
 public OverlayWindow(AppSettings s){settings=s;Title="My System Overlay";Content=new Border{Background=new SolidColorBrush(Windows.UI.Color.FromArgb(255,17,23,25)),Child=text};var p=(OverlappedPresenter)AppWindow.Presenter;p.IsAlwaysOnTop=true;p.IsResizable=false;p.IsMaximizable=false;p.IsMinimizable=false;p.SetBorderAndTitleBar(false,false);var hwnd=WinRT.Interop.WindowNative.GetWindowHandle(this);SetWindowLongPtr(hwnd,-20,GetWindowLongPtr(hwnd,-20)|(nint)0x08000080);ApplySettings();}
 public void ApplySettings(){((FrameworkElement)Content).Opacity=settings.OverlayOpacity;AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(settings.OverlayX,settings.OverlayY,520,settings.OverlayPreset=="Expanded"?132:66));}
 public void Update(TelemetrySnapshot s,GamingSnapshot g){string M(string id)=>s.Find(id) is {Value:double v,Availability:MetricAvailability.Available}?v.ToString("0"):"N/A";text.Text=$"FPS {g.Fps?.ToString("0")??"N/A"} | CPU {M("cpu.usage")}% | GPU {M("gpu.usage")}% | RAM {M("memory.usage")}%";if(settings.OverlayPreset=="Expanded")text.Text+=$"\nGPU {M("gpu.temperature")} °C | CPU {M("cpu.temperature")} °C\n1% low {g.OnePercentLow?.ToString("0")??"N/A"} | {g.FrameTimeMs?.ToString("0.0")??"N/A"} ms\nMy System • Ctrl+Alt+M to hide";}
 [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern nint GetWindowLongPtr(nint hwnd,int index);
 [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]static extern nint SetWindowLongPtr(nint hwnd,int index,nint value);
}
