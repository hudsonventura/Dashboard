using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Dashboard.Services;

internal static class SolisAuthHelper
{
    // A Solis exige charset no header HTTP, mas não na string assinada.
    internal const string ContentTypeHeader = "application/json;charset=UTF-8";
    private const string ContentTypeForSignature = "application/json";

    internal static string GetGmtDate() =>
        DateTime.UtcNow.ToString("ddd, dd MMM yyyy HH:mm:ss 'GMT'", CultureInfo.InvariantCulture);

    internal static string ComputeContentMd5(string body) =>
        Convert.ToBase64String(MD5.HashData(Encoding.UTF8.GetBytes(body)));

    internal static string ComputeAuthorization(
        string keyId,
        string keySecret,
        string contentMd5,
        string date,
        string path)
    {
        var stringToSign = string.Join('\n',
            "POST",
            contentMd5,
            ContentTypeForSignature,
            date,
            path);

        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(keySecret));
        var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
        return $"API {keyId}:{signature}";
    }
}
