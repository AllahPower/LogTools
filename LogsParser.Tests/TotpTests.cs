using LogsParser.Exceptions;
using LogsParser.Net;
using Xunit;

namespace LogsParser.Tests;

public class TotpTests
{
    // ASCII "12345678901234567890" encoded as Base32 — the canonical RFC 6238 (HMAC-SHA1) test key.
    private const string RfcSecret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    [InlineData(20000000000L, "353130")]
    public void Generates_rfc6238_vectors(long unixTimeSeconds, string expected)
    {
        // The library truncates to 6 digits (mod 1_000_000), i.e. the last 6 of the RFC 8-digit codes.
        Assert.Equal(expected, LogsParserAuthenticator.GenerateTotp(RfcSecret, unixTimeSeconds));
    }

    [Fact]
    public void Generates_six_digit_code_for_current_time()
    {
        Assert.Matches(@"^\d{6}$", LogsParserAuthenticator.GenerateTotp(RfcSecret));
    }

    [Fact]
    public void Empty_secret_throws()
    {
        Assert.Throws<TwoFactorAuthenticationException>(() => LogsParserAuthenticator.GenerateTotp("", 59));
    }

    [Fact]
    public void Invalid_base32_throws()
    {
        Assert.Throws<TwoFactorAuthenticationException>(() => LogsParserAuthenticator.GenerateTotp("!!!!", 59));
    }
}
