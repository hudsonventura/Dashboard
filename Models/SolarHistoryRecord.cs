using System;
using System.ComponentModel.DataAnnotations;

namespace Dashboard.Models;

/// <summary>
/// Representa a foto (snapshot) dos dados de geração solar de um determinado dia.
/// Armazena apenas dados estruturados e pontos do gráfico em JSON (sem imagem).
/// </summary>
public class SolarHistoryRecord
{
    [Key]
    public string Date { get; set; } = string.Empty; // Formato: "yyyy-MM-dd"

    public double? EToday { get; set; }
    public string? ETodayStr { get; set; }

    public double? Pac { get; set; }
    public string? PacStr { get; set; }

    public double? IAc1 { get; set; }
    public string? IAc1Str { get; set; }

    public double? UPv1 { get; set; }
    public string? UPv1Str { get; set; }
    public double? IPv1 { get; set; }
    public string? IPv1Str { get; set; }
    public double? Pow1 { get; set; }
    public string? Pow1Str { get; set; }

    public double? UPv2 { get; set; }
    public string? UPv2Str { get; set; }
    public double? IPv2 { get; set; }
    public string? IPv2Str { get; set; }
    public double? Pow2 { get; set; }
    public string? Pow2Str { get; set; }

    public double? InverterTemperature { get; set; }
    public string? InverterTemperatureUnit { get; set; }

    public double? IgbtTemperature { get; set; }

    public int? State { get; set; }
    public string? StateText { get; set; }

    public string? LastUpdatedText { get; set; }

    public string? First6KwText { get; set; }
    public string? Last6KwText { get; set; }

    public string DayPointsJson { get; set; } = "[]";

    public DateTime LastSyncedAt { get; set; }
}
