using Avalonia.Controls;
using Avalonia.Input;

namespace SolisDashboard.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        KeyDown += OnWindowKeyDown;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && WindowState == WindowState.FullScreen)
        {
            WindowState = WindowState.Maximized;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter &&
            e.KeyModifiers.HasFlag(KeyModifiers.Control) &&
            WindowState != WindowState.FullScreen)
        {
            WindowState = WindowState.FullScreen;
            e.Handled = true;
        }
    }
}
