using System.Threading;
using System.Threading.Tasks;
using Dashboard.Models;

namespace Dashboard.Services;

public interface IWeatherService
{
    string ProviderName { get; }
    Task<WeatherData> GetWeatherAsync(double latitude, double longitude, string timezone, CancellationToken ct = default);
}
