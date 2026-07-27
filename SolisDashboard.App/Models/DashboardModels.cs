namespace SolisDashboard.App.Models;

public sealed class GenerationPoint
{
    public string Time { get; init; } = "";
    public double PowerW { get; init; }
}

public sealed class MetricCard
{
    public string Title { get; init; } = "";
    public string Value { get; init; } = "";
    public string Unit { get; init; } = "";
    public string Subtitle { get; init; } = "";
}

public sealed class ForecastDayItem
{
    public string DateLabel { get; init; } = "";
    public string Condition { get; init; } = "";
    public string IconKey { get; init; } = "unknown";
    public string TempRange { get; init; } = "";
    public string Rain { get; init; } = "";
    public string Wind { get; init; } = "";
}
