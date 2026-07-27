using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SolisDashboard.Services;

public static class SolisAuthHelper
{
    /// <summary>Header HTTP enviado na requisição.</summary>
    public const string ContentTypeHeader = "application/json;charset=UTF-8";

    /// <summary>Valor usado na assinatura (docs Solis: sem charset).</summary>
    public const string ContentTypeForSign = "application/json";

    public static string GetGmtDate()
    {
        // Formato alinhado ao Python/HA: dia com zero à esquerda (%d).
        return DateTime.UtcNow.ToString("ddd, dd MMM yyyy HH:mm:ss 'GMT'", CultureInfo.InvariantCulture);
    }

    public static string ComputeContentMd5(string body)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(body));
        return Convert.ToBase64String(hash);
    }

    public static string ComputeAuthorization(
        string keyId,
        string keySecret,
        string contentMd5,
        string date,
        string canonicalizedResource)
    {
        var stringToSign = string.Join('\n',
            "POST",
            contentMd5,
            ContentTypeForSign,
            date,
            canonicalizedResource);

        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(keySecret));
        var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
        return $"API {keyId}:{signature}";
    }
}
