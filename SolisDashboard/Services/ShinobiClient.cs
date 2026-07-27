using System.Text;
using SolisDashboard.Configuration;

namespace SolisDashboard.Services;

/// <summary>
/// Cliente Shinobi para snapshots JPEG.
/// Docs: https://docs.shinobi.video/api/get-streams
/// </summary>
public sealed class ShinobiClient : IDisposable
{
    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47];

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

    public async Task<ShinobiJpegResult> GetJpegAsync(string monitorId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(monitorId))
            return ShinobiJpegResult.Fail("MonitorId vazio.");

        var url = BuildJpegUrl(monitorId);
        try
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            var contentType = response.Content.Headers.ContentType?.MediaType;
            var status = (int)response.StatusCode;
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                return ShinobiJpegResult.Fail(
                    $"HTTP {status} em {url}. Corpo: {Preview(bytes)}",
                    url, contentType, status);
            }

            if (bytes.Length < 16)
            {
                return ShinobiJpegResult.Fail(
                    $"Resposta muito pequena ({bytes.Length} bytes). Snapshot pode estar desabilitado no monitor. URL: {url}",
                    url, contentType, status);
            }

            if (LooksLikeJpeg(bytes) || LooksLikePng(bytes))
                return ShinobiJpegResult.Ok(bytes, url, contentType, status);

            // Shinobi às vezes devolve JSON/HTML com 200.
            return ShinobiJpegResult.Fail(
                $"Resposta não é imagem JPEG/PNG ({bytes.Length} bytes, content-type={contentType ?? "?"}). " +
                $"Verifique ApiKey/GroupKey/MonitorId e se o Snapshot está ativo. URL: {url}. Corpo: {Preview(bytes)}",
                url, contentType, status);
        }
        catch (Exception ex)
        {
            return ShinobiJpegResult.Fail($"Falha ao baixar {url}: {ex.Message}", url);
        }
    }

    private static bool LooksLikeJpeg(ReadOnlySpan<byte> data) =>
        data.Length >= 3 && data[0] == JpegMagic[0] && data[1] == JpegMagic[1] && data[2] == JpegMagic[2];

    private static bool LooksLikePng(ReadOnlySpan<byte> data) =>
        data.Length >= 4 &&
        data[0] == PngMagic[0] && data[1] == PngMagic[1] &&
        data[2] == PngMagic[2] && data[3] == PngMagic[3];

    private static string Preview(byte[] bytes)
    {
        if (bytes.Length == 0)
            return "(vazio)";

        var take = Math.Min(bytes.Length, 180);
        var text = Encoding.UTF8.GetString(bytes, 0, take)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

        if (string.IsNullOrWhiteSpace(text) || text.Any(c => char.IsControl(c) && c != '\t'))
            return $"binário [{BitConverter.ToString(bytes, 0, Math.Min(8, bytes.Length))}]";

        return text.Length < bytes.Length ? text + "…" : text;
    }

    public void Dispose() => _http.Dispose();
}
