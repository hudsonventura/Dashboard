using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SolisDashboard.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        KeyDown += OnWindowKeyDown;
        AddHandler(PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed &&
            WindowState == WindowState.FullScreen)
        {
            WindowState = WindowState.Maximized;
            e.Handled = true;
        }
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
