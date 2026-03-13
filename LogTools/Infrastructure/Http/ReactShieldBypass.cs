using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using LogsParser.Diagnostics;
using LogsParser.Exceptions;

namespace LogsParser.Net;

internal static partial class ReactShieldBypass
{
    private static ILogger Logger => LogsParserLogging.CreateLogger(nameof(ReactShieldBypass));

    public static async Task<bool> IsReactChallengeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode != System.Net.HttpStatusCode.OK)
        {
            return false;
        }

        var hasExpectedHeaders =
            response.Headers.TryGetValues("server", out var serverValues) &&
            serverValues.Contains("nginx", StringComparer.OrdinalIgnoreCase) &&
            response.Headers.TryGetValues("cache-control", out var cacheValues) &&
            cacheValues.Contains("no-cache", StringComparer.OrdinalIgnoreCase);

        if (!hasExpectedHeaders)
        {
            return false;
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return content.Contains("/vddosw3data.js", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("Please turn JavaScript on and reload the page.", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<string> SolveAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (TrySolveIndexedArrayChallenge(content, out var indexedToken))
            {
                Logger.LogInformation("React challenge bypass completed successfully.");
                return indexedToken;
            }

            var values = QuotedHexRegex().Matches(content)
                .Select(static match => NormalizeHex(match.Groups["value"].Value))
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (values.Length < 3)
            {
                Logger.LogError("React challenge payload is incomplete. ValuesCount: {ValuesCount}", values.Length);
                throw new ReactShieldBypassException("React challenge payload is incomplete.");
            }

            foreach (var candidate in EnumerateCandidates(values))
            {
                if (!TryDecrypt(candidate.KeyHex, candidate.IvHex, candidate.EncryptedHex, out var token))
                {
                    continue;
                }

                Logger.LogInformation("React challenge bypass completed successfully.");
                return token;
            }

            Logger.LogError("No valid React challenge payload candidates were found. ValuesCount: {ValuesCount}", values.Length);
            throw new ReactShieldBypassException("Failed to derive the React challenge token from the response payload.");
        }
        catch (LogsParserException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Failed to bypass React challenge.");
            throw new ReactShieldBypassException("Failed to bypass React challenge.", exception);
        }
    }

    private static bool TrySolveIndexedArrayChallenge(string content, out string token)
    {
        token = string.Empty;

        var arrayMatch = ObfuscatedArrayRegex().Match(content);
        if (!arrayMatch.Success)
        {
            return false;
        }

        var values = QuotedStringRegex().Matches(arrayMatch.Groups["values"].Value)
            .Select(static match => DecodeJavascriptStringLiteral(match.Groups["value"].Value))
            .ToArray();

        if (values.Length == 0)
        {
            return false;
        }

        var variableMatches = ToNumbersAssignmentRegex().Matches(content);
        if (variableMatches.Count < 3)
        {
            return false;
        }

        string? keyHex = null;
        string? ivHex = null;
        string? encryptedHex = null;

        foreach (Match match in variableMatches)
        {
            var variableName = match.Groups["name"].Value;
            if (!int.TryParse(match.Groups["index"].Value, out var index) || index < 0 || index >= values.Length)
            {
                continue;
            }

            var value = NormalizeHex(values[index]);
            if (!IsHex(value))
            {
                continue;
            }

            switch (variableName)
            {
                case "a":
                    keyHex = value;
                    break;
                case "b":
                    ivHex = value;
                    break;
                case "c":
                    encryptedHex = value;
                    break;
            }
        }

        return !string.IsNullOrWhiteSpace(keyHex) &&
               !string.IsNullOrWhiteSpace(ivHex) &&
               !string.IsNullOrWhiteSpace(encryptedHex) &&
               TryDecrypt(keyHex, ivHex, encryptedHex, out token);
    }

    private static IEnumerable<(string KeyHex, string IvHex, string EncryptedHex)> EnumerateCandidates(string[] values)
    {
        var keyCandidates = values.Where(static value => IsHex(value) && value.Length is 32 or 48 or 64).ToArray();
        var ivCandidates = values.Where(static value => IsHex(value) && value.Length == 32).ToArray();
        var encryptedCandidates = values.Where(static value => IsHex(value) && value.Length >= 32 && value.Length % 32 == 0).ToArray();

        foreach (var keyHex in keyCandidates)
        {
            foreach (var ivHex in ivCandidates)
            {
                foreach (var encryptedHex in encryptedCandidates)
                {
                    yield return (keyHex, ivHex, encryptedHex);
                }
            }
        }
    }

    private static bool TryDecrypt(string keyHex, string ivHex, string encryptedHex, out string token)
    {
        token = string.Empty;

        try
        {
            var key = Convert.FromHexString(keyHex);
            var iv = Convert.FromHexString(ivHex);
            var encrypted = Convert.FromHexString(encryptedHex);

            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;
            aes.Key = key;
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            var decrypted = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
            token = Convert.ToHexString(decrypted).ToLowerInvariant();
            return IsHex(token) && token.Length >= 32;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string NormalizeHex(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Contains(@"\x", StringComparison.Ordinal)
            ? value.Replace(@"\x", string.Empty, StringComparison.Ordinal)
            : value;
    }

    private static bool IsHex(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length % 2 != 0)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!Uri.IsHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    private static string DecodeJavascriptStringLiteral(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return HexEscapeRegex().Replace(
            value,
            static match => Convert.ToChar(Convert.ToInt32(match.Groups["hex"].Value, 16)).ToString());
    }

    [GeneratedRegex("""var\s+_0x[a-f0-9]+\s*=\s*\[(?<values>.*?)\];""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ObfuscatedArrayRegex();

    [GeneratedRegex("""["'](?<value>(?:\\.|[^"'\\])*)["']""", RegexOptions.Singleline)]
    private static partial Regex QuotedStringRegex();

    [GeneratedRegex("""(?<name>[abc])\s*=\s*toNumbers\(_0x[a-f0-9]+\[(?<index>\d+)\]\)""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ToNumbersAssignmentRegex();

    [GeneratedRegex(@"\\x(?<hex>[0-9a-fA-F]{2})", RegexOptions.Singleline)]
    private static partial Regex HexEscapeRegex();

    [GeneratedRegex("""["'](?<value>.*?)["']""", RegexOptions.Singleline)]
    private static partial Regex QuotedHexRegex();
}
