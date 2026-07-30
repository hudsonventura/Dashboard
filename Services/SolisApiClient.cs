using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dashboard.Models;

namespace Dashboard.Services;

/// <summary>Cliente da SolisCloud com a assinatura HMAC-SHA1 exigida pela API.</summary>
public sealed class SolisApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly SolisSettings _settings;

    public SolisApiClient(SolisSettings settings)
    {
        _settings = settings;
        _http = new HttpClient
        {
            BaseAddress = new Uri(settings.ApiUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public async Task<InverterDetail> GetInverterDetailAsync(string sn, CancellationToken cancellationToken = default)
    {
        var body = JsonSerializer.Serialize(new { sn });
        var detail = await PostAsync<InverterDetail>("/v1/api/inverterDetail", body, cancellationToken);
        return detail ?? throw new InvalidOperationException("Resposta vazia em inverterDetail.");
    }

    public async Task<string> ResolveInverterSnAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(_settings.InverterSn))
            return _settings.InverterSn.Trim();

        var body = JsonSerializer.Serialize(new { pageNo = 1, pageSize = 50 });
        using var document = await PostRawAsync("/v1/api/inverterList", body, cancellationToken);
        var root = document.RootElement;

        if (!root.TryGetProperty("data", out var data))
            throw new InvalidOperationException("A resposta de inverterList não contém dados.");

        var records = data.TryGetProperty("page", out var page) && page.TryGetProperty("records", out var pageRecords)
            ? pageRecords
            : data.TryGetProperty("records", out var directRecords)
                ? directRecords
                : data;

        var inverters = records.ValueKind == JsonValueKind.Array
            ? JsonSerializer.Deserialize<List<InverterListItem>>(records.GetRawText(), JsonOptions) ?? []
            : [];

        if (inverters.Count == 0)
            throw new InvalidOperationException("Nenhum inversor encontrado na conta SolisCloud.");

        var preferred = inverters.FirstOrDefault(item =>
            string.Equals(item.ProductModel, _settings.InverterModel, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.Model, _settings.InverterModel, StringComparison.OrdinalIgnoreCase) ||
            (item.ProductModel?.Contains(_settings.InverterModel, StringComparison.OrdinalIgnoreCase) ?? false));
        var selected = preferred ?? inverters[0];

        return !string.IsNullOrWhiteSpace(selected.Sn)
            ? selected.Sn
            : throw new InvalidOperationException("Inversor encontrado sem número de série.");
    }

    private async Task<T?> PostAsync<T>(string path, string body, CancellationToken cancellationToken)
    {
        using var document = await PostRawAsync(path, body, cancellationToken);
        var response = JsonSerializer.Deserialize<SolisApiResponse<T>>(document.RootElement.GetRawText(), JsonOptions)
            ?? throw new InvalidOperationException($"Falha ao desserializar a resposta de {path}.");

        if (!response.Success && response.Code != "0")
            throw new InvalidOperationException($"Solis API {path}: [{response.Code}] {response.Message}");

        return response.Data;
    }

    private async Task<JsonDocument> PostRawAsync(string path, string body, CancellationToken cancellationToken)
    {
        var date = SolisAuthHelper.GetGmtDate();
        var contentMd5 = SolisAuthHelper.ComputeContentMd5(body);
        var authorization = SolisAuthHelper.ComputeAuthorization(
            _settings.KeyId.Trim(),
            _settings.KeySecret.Trim(),
            contentMd5,
            date,
            path);

        using var request = new HttpRequestMessage(HttpMethod.Post, path.TrimStart('/'));
        request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
        request.Content.Headers.TryAddWithoutValidation("Content-Type", SolisAuthHelper.ContentTypeHeader);
        request.Content.Headers.TryAddWithoutValidation("Content-MD5", contentMd5);
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        request.Headers.TryAddWithoutValidation("Date", date);

        using var response = await _http.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"HTTP {(int)response.StatusCode} em {path}: {raw}");

        return JsonDocument.Parse(raw);
    }

    public void Dispose() => _http.Dispose();
}
