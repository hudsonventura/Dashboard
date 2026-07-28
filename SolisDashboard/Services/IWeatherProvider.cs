using SolisDashboard.Models;

namespace SolisDashboard.Services;

public interface IWeatherProvider : IDisposable
{
    string DisplayName { get; }
    Task<WeatherSnapshot> GetSnapshotAsync(CancellationToken ct = default);
}
