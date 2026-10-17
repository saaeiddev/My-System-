using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using MySystem.App.ViewModels;
using MySystem.Infrastructure;

namespace MySystem.App;
public partial class App : Application
{
    private Window? window;
    private ServiceProvider? services;
    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        services = new ServiceCollection().AddSingleton<SettingsStore>().AddSingleton<MainViewModel>().BuildServiceProvider();
        var command = Environment.GetCommandLineArgs();
        int smoke = Array.IndexOf(command, "--smoke-test");
        string? smokeDirectory = smoke >= 0 && smoke + 1 < command.Length ? Path.GetFullPath(command[smoke + 1]) : null;
        window = new MainWindow(services.GetRequiredService<MainViewModel>(), smokeDirectory);
        window.Closed += (_, _) =>
        {
            services.Dispose();
            Exit();
        };
        window.Activate();
    }
}
