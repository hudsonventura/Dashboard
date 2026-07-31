using System.Text.Json.Serialization;

namespace Dashboard.Models;

/// <summary>Envelope retornado pelos endpoints da API SolisCloud.</summary>
public sealed class SolisApiResponse<T>
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("msg")]
    public string? Message { get; set; }

    [JsonPropertyName("data")]
    public T? Data { get; set; }
}

public sealed class InverterListItem
{
    [JsonPropertyName("sn")]
    public string? Sn { get; set; }

    [JsonPropertyName("productModel")]
    public string? ProductModel { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }
}

public sealed class InverterDetail
{
    [JsonPropertyName("sn")]
    public string? Sn { get; set; }

    [JsonPropertyName("productModel")]
    public string? ProductModel { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("timeStr")]
    public string? TimeStr { get; set; }

    [JsonPropertyName("eToday")]
    public double? EToday { get; set; }

    [JsonPropertyName("eTodayStr")]
    public string? ETodayStr { get; set; }

    [JsonPropertyName("pac")]
    public double? Pac { get; set; }

    [JsonPropertyName("pacStr")]
    public string? PacStr { get; set; }

    [JsonPropertyName("iAc1")]
    public double? IAc1 { get; set; }

    [JsonPropertyName("iAc1Str")]
    public string? IAc1Str { get; set; }

    [JsonPropertyName("uPv1")]
    public double? UPv1 { get; set; }

    [JsonPropertyName("uPv1Str")]
    public string? UPv1Str { get; set; }

    [JsonPropertyName("iPv1")]
    public double? IPv1 { get; set; }

    [JsonPropertyName("iPv1Str")]
    public string? IPv1Str { get; set; }

    [JsonPropertyName("pow1")]
    public double? Pow1 { get; set; }

    [JsonPropertyName("pow1Str")]
    public string? Pow1Str { get; set; }

    [JsonPropertyName("uPv2")]
    public double? UPv2 { get; set; }

    [JsonPropertyName("uPv2Str")]
    public string? UPv2Str { get; set; }

    [JsonPropertyName("iPv2")]
    public double? IPv2 { get; set; }

    [JsonPropertyName("iPv2Str")]
    public string? IPv2Str { get; set; }

    [JsonPropertyName("pow2")]
    public double? Pow2 { get; set; }

    [JsonPropertyName("pow2Str")]
    public string? Pow2Str { get; set; }

    [JsonPropertyName("inverterTemperature")]
    public double? InverterTemperature { get; set; }

    [JsonPropertyName("inverterTemperatureUnit")]
    public string? InverterTemperatureUnit { get; set; }

    [JsonPropertyName("temp")]
    public double? IgbtTemperatureLimit { get; set; }

    [JsonPropertyName("tempName")]
    public string? TempName { get; set; }

    [JsonPropertyName("state")]
    public int? State { get; set; }
}

public sealed class InverterDayPoint
{
    [JsonPropertyName("timeStr")]
    public string? TimeStr { get; set; }

    [JsonPropertyName("pac")]
    public double? Pac { get; set; }
}
