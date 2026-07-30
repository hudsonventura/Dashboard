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

    [JsonPropertyName("inverterTemperature")]
    public double? InverterTemperature { get; set; }

    [JsonPropertyName("inverterTemperatureUnit")]
    public string? InverterTemperatureUnit { get; set; }

    [JsonPropertyName("state")]
    public int? State { get; set; }
}
