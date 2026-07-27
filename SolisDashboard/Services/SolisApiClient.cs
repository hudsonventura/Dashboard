using System.Text;
using System.Text.Json;
using SolisDashboard.Configuration;
using SolisDashboard.Models;

namespace SolisDashboard.Services;

public sealed class SolisApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly SolisOptions _options;

    public SolisApiClient(SolisOptions options)
    {
        _options = options;
        _http = new HttpClient
        {
            BaseAddress = new Uri(options.ApiUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public async Task<InverterDetail> GetInverterDetailAsync(string sn, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new { sn });
        var response = await PostAsync<InverterDetail>("/v1/api/inverterDetail", body, ct);
        return response ?? throw new InvalidOperationException("Resposta vazia em inverterDetail.");
    }

    public async Task<List<InverterDayPoint>> GetInverterDayAsync(string sn, DateOnly day, int timeZone, CancellationToken ct = default)
    {
        var payload = new
        {
            sn,
            money = _options.Currency,
            time = day.ToString("yyyy-MM-dd"),
            timeZone = timeZone.ToString()
        };

        var body = JsonSerializer.Serialize(payload);
        var response = await PostAsync<List<InverterDayPoint>>("/v1/api/inverterDay", body, ct);
        return response ?? [];
    }

    public async Task<List<InverterListItem>> GetInverterListAsync(CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new { pageNo = 1, pageSize = 50 });
        using var doc = await PostRawAsync("/v1/api/inverterList", body, ct);
        var root = doc.RootElement;

        if (!root.TryGetProperty("data", out var data))
            return [];

        if (data.TryGetProperty("page", out var page) &&
            page.TryGetProperty("records", out var pageRecords) &&
            pageRecords.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<InverterListItem>>(pageRecords.GetRawText(), JsonOptions) ?? [];
        }

        if (data.TryGetProperty("records", out var records) && records.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<InverterListItem>>(records.GetRawText(), JsonOptions) ?? [];
        }

        if (data.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<InverterListItem>>(data.GetRawText(), JsonOptions) ?? [];
        }

        return [];
    }

    public async Task<string> ResolveInverterSnAsync(CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(_options.InverterSn))
            return _options.InverterSn.Trim();

        var list = await GetInverterListAsync(ct);
        if (list.Count == 0)
            throw new InvalidOperationException("Nenhum inversor encontrado na conta SolisCloud.");

        var preferred = list.FirstOrDefault(i =>
            string.Equals(i.ProductModel, _options.InverterModel, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(i.Model, _options.InverterModel, StringComparison.OrdinalIgnoreCase) ||
            (i.ProductModel?.Contains(_options.InverterModel, StringComparison.OrdinalIgnoreCase) ?? false));

        var selected = preferred ?? list[0];
        if (string.IsNullOrWhiteSpace(selected.Sn))
            throw new InvalidOperationException("Inversor encontrado sem SN.");

        return selected.Sn;
    }

    private async Task<T?> PostAsync<T>(string path, string body, CancellationToken ct)
    {
        using var doc = await PostRawAsync(path, body, ct);
        var envelope = JsonSerializer.Deserialize<SolisApiResponse<T>>(doc.RootElement.GetRawText(), JsonOptions);

        if (envelope is null)
            throw new InvalidOperationException($"Falha ao desserializar resposta de {path}.");

        if (!envelope.Success && envelope.Code != "0")
            throw new InvalidOperationException($"Solis API {path}: [{envelope.Code}] {envelope.Message}");

        return envelope.Data;
    }

    private async Task<JsonDocument> PostRawAsync(string path, string body, CancellationToken ct)
    {
        var keyId = _options.KeyId.Trim();
        var keySecret = _options.KeySecret.Trim();
        var date = SolisAuthHelper.GetGmtDate();
        var contentMd5 = SolisAuthHelper.ComputeContentMd5(body);
        var authorization = SolisAuthHelper.ComputeAuthorization(
            keyId,
            keySecret,
            contentMd5,
            date,
            path);

        var bodyBytes = Encoding.UTF8.GetBytes(body);
        using var request = new HttpRequestMessage(HttpMethod.Post, path.TrimStart('/'));
        request.Content = new ByteArrayContent(bodyBytes);

        // Content-MD5 e Content-Type DEVEM ir nos headers do Content.
        // Se Content-MD5 for colocado em request.Headers, o HttpClient descarta e a Solis retorna "wrong sign".
        request.Content.Headers.TryAddWithoutValidation("Content-Type", SolisAuthHelper.ContentTypeHeader);
        request.Content.Headers.TryAddWithoutValidation("Content-MD5", contentMd5);

        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        request.Headers.TryAddWithoutValidation("Date", date);

        using var response = await _http.SendAsync(request, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"HTTP {(int)response.StatusCode} em {path}: {raw}");

        return JsonDocument.Parse(raw);
    }

    public void Dispose() => _http.Dispose();
}
