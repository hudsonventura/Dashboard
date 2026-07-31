using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Dashboard.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Listen for key and mouse events anywhere in the window
        AddHandler(KeyDownEvent, OnGlobalKeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnGlobalPointerPressed, RoutingStrategies.Tunnel);
    }

    private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl + Enter: Toggle Fullscreen
        if (e.Key == Key.Enter && (e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            ToggleFullScreen();
            e.Handled = true;
        }
        // ESC: Exit Fullscreen
        else if (e.Key == Key.Escape)
        {
            if (WindowState == WindowState.FullScreen)
            {
                ExitFullScreen();
                e.Handled = true;
            }
        }
    }

    private void OnGlobalPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Right Click with Mouse: Exit Fullscreen
        var properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsRightButtonPressed)
        {
            if (WindowState == WindowState.FullScreen)
            {
                ExitFullScreen();
                e.Handled = true;
            }
        }
    }

    private void ToggleFullScreen()
    {
        if (WindowState == WindowState.FullScreen)
        {
            ExitFullScreen();
        }
        else
        {
            EnterFullScreen();
        }
    }

    private void EnterFullScreen()
    {
        WindowState = WindowState.FullScreen;
    }

    private void ExitFullScreen()
    {
        WindowState = WindowState.Normal;
    }
}