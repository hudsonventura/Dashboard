using SolisDashboard.Configuration;

namespace SolisDashboard.Services;

/// <summary>
/// Cliente Shinobi para snapshots JPEG.
/// Docs: https://docs.shinobi.video/api/get-streams
/// </summary>
public sealed class ShinobiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly ShinobiOptions _options;

    public ShinobiClient(ShinobiOptions options)
    {
        _options = options;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
    }

    public bool IsConfigured =>
        _options.Enabled &&
        !string.IsNullOrWhiteSpace(_options.BaseUrl) &&
        !string.IsNullOrWhiteSpace(_options.ApiKey) &&
        !string.IsNullOrWhiteSpace(_options.GroupKey) &&
        (!string.IsNullOrWhiteSpace(_options.Primary.MonitorId) ||
         !string.IsNullOrWhiteSpace(_options.Secondary.MonitorId));

    public string BuildJpegUrl(string monitorId)
    {
        var baseUrl = _options.BaseUrl.TrimEnd('/');
        var apiKey = Uri.EscapeDataString(_options.ApiKey.Trim());
        var groupKey = Uri.EscapeDataString(_options.GroupKey.Trim());
        var id = Uri.EscapeDataString(monitorId.Trim());
        // http://host/[API KEY]/jpeg/[GROUP KEY]/[MONITOR ID]/s.jpg
        return $"{baseUrl}/{apiKey}/jpeg/{groupKey}/{id}/s.jpg";
    }

    public async Task<byte[]?> GetJpegAsync(string monitorId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(monitorId))
            return null;

        var url = BuildJpegUrl(monitorId);
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    public void Dispose() => _http.Dispose();
}
