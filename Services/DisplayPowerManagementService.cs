using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Dashboard.Models;

namespace Dashboard.Services;

/// <summary>
/// Serviço responsável por gerenciar o estado de energia do display (Standby / Ligado) no Linux.
/// </summary>
public class DisplayPowerManagementService
{
    private DateTime? _lastActionHour;

    public void ProcessSchedule(DisplayPowerSettings settings, DateTime now)
    {
        if (!settings.Enabled || !RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return;
        }

        // Verifica apenas na virada/inicio de uma hora se ainda não executamos nesta hora específica
        if (_lastActionHour.HasValue &&
            _lastActionHour.Value.Year == now.Year &&
            _lastActionHour.Value.Month == now.Month &&
            _lastActionHour.Value.Day == now.Day &&
            _lastActionHour.Value.Hour == now.Hour)
        {
            return;
        }

        int currentHour = now.Hour;

        // Horário de Standby: das StandbyStartHour (ex: 23h) até StandbyEndHour (ex: 6h)
        // Horas ativas para Standby: 23, 0, 1, 2, 3, 4, 5
        bool isStandbyWindow = IsInStandbyWindow(currentHour, settings.StandbyStartHour, settings.StandbyEndHour);

        if (isStandbyWindow)
        {
            SetDisplayPower(turnOn: false);
            _lastActionHour = now;
        }
        else if (currentHour == settings.StandbyEndHour)
        {
            SetDisplayPower(turnOn: true);
            _lastActionHour = now;
        }
    }

    public static bool IsInStandbyWindow(int currentHour, int startHour, int endHour)
    {
        if (startHour > endHour)
        {
            // Janela que atravessa a meia-noite (ex: 23h às 6h -> horas 23, 0, 1, 2, 3, 4, 5)
            return currentHour >= startHour || currentHour < endHour;
        }
        else
        {
            // Janela no mesmo dia (ex: 1h às 5h)
            return currentHour >= startHour && currentHour < endHour;
        }
    }

    private void SetDisplayPower(bool turnOn)
    {
        try
        {
            if (turnOn)
            {
                // Envia comando para religar o display via X11 DPMS
                ExecuteCommand("xset", "dpms force on");
            }
            else
            {
                // Envia comando para colocar o display em standby / desligar tela via X11 DPMS
                ExecuteCommand("xset", "dpms force off");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DisplayPowerManagementService] Erro ao alterar estado da tela: {ex.Message}");
        }
    }

    private static void ExecuteCommand(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        process?.WaitForExit(3000);
    }
}
