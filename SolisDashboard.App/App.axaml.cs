using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SolisDashboard.App.ViewModels;
using SolisDashboard.App.Views;

namespace SolisDashboard.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Cria a janela antes do ViewModel pesado para o compositor Linux exibir algo cedo.
            var window = new MainWindow();
            desktop.MainWindow = window;
            var vm = new MainViewModel();
            window.DataContext = vm;
            desktop.Exit += (_, _) => vm.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
