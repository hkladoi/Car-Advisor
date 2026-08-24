using System.Security.Cryptography;
using System.Text;

namespace VietnamCarPlatform.Api.Features.PartnerApi;

public static class PartnerApiKeyMaterial
{
    private const string PrefixMarker = "vcp_v1_";
    private const int StoredPrefixLength = 17;

    public static (string Token, string Prefix, string Hash) Generate()
    {
        var prefixEntropy = Base64Url(RandomNumberGenerator.GetBytes(8));
        var prefix = PrefixMarker + prefixEntropy[..10];
        var secret = Base64Url(RandomNumberGenerator.GetBytes(32));
        var token = $"{prefix}.{secret}";
        return (token, prefix, Hash(token));
    }

    public static bool TryGetPrefix(string? token, out string prefix)
    {
        prefix = string.Empty;
        if (string.IsNullOrWhiteSpace(token)
            || token.Length != 61
            || !token.StartsWith(PrefixMarker, StringComparison.Ordinal)
            || token[StoredPrefixLength] != '.')
        {
            return false;
        }

        prefix = token[..StoredPrefixLength];
        return prefix[PrefixMarker.Length..].All(IsBase64Url)
            && token[(StoredPrefixLength + 1)..].All(IsBase64Url);
    }

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    public static bool FixedTimeEquals(string expectedHash, string token)
    {
        if (expectedHash.Length != 64)
        {
            return false;
        }

        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expectedHash),
                Convert.FromHexString(Hash(token)));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsBase64Url(char value) =>
        char.IsAsciiLetterOrDigit(value) || value is '-' or '_';

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
