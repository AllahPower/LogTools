using System.Security.Cryptography;
using LogsParser.Abstractions;
using LogsParser.Models;
using Xunit;

namespace LogsParser.Tests;

public class ReactShieldTests
{
    [Fact]
    public async Task React_challenge_is_solved_and_request_is_retried()
    {
        // Build a self-consistent AES-CBC (NoPadding) "indexed array" challenge that the bypass can
        // solve: token == hex(plaintext). 16-byte plaintext → 32 hex chars, which passes IsHex/length.
        var key = Convert.FromHexString("000102030405060708090a0b0c0d0e0f");
        var iv = Convert.FromHexString("101112131415161718191a1b1c1d1e1f");
        var plaintext = Convert.FromHexString("202122232425262728292a2b2c2d2e2f");

        byte[] cipher;
        using (var aes = Aes.Create())
        {
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;
            aes.Key = key;
            aes.IV = iv;
            using var encryptor = aes.CreateEncryptor();
            cipher = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
        }

        var keyHex = Convert.ToHexString(key);
        var ivHex = Convert.ToHexString(iv);
        var cipherHex = Convert.ToHexString(cipher);
        var expectedToken = Convert.ToHexString(plaintext).ToLowerInvariant();

        var challengeHtml =
            "<html><head><title>Just a moment...</title></head><body>" +
            "Please turn JavaScript on and reload the page." +
            "<script src=\"/vddosw3data.js\"></script>" +
            "<script>var _0xabc=[\"" + keyHex + "\",\"" + ivHex + "\",\"" + cipherHex + "\"];" +
            "function f(){a=toNumbers(_0xabc[0]);b=toNumbers(_0xabc[1]);c=toNumbers(_0xabc[2]);" +
            "document.cookie=\"R3ACTLB=\"+toHex(slowAES.decrypt(c,2,a,b));location.reload(true);}" +
            "</script></body></html>";

        var calls = 0;
        var cookies = new MemoryCookieStorage();
        var handler = new FakeHttpMessageHandler(_ =>
        {
            calls++;
            return calls == 1
                ? FakeHttpMessageHandler.Ok(challengeHtml, ("server", "nginx"), ("cache-control", "no-cache"))
                : FakeHttpMessageHandler.Ok("<html><body>DATA-OK</body></html>");
        });

        using var dataSource = TestDataSource.Create(handler, credentials: null, cookies: cookies);

        var content = await dataSource.GetContentAsync(new ParserRequest("/admins"));

        Assert.Contains("DATA-OK", content);
        Assert.True(calls >= 2, "challenge should have been retried after solving");
        Assert.Contains(cookies.GetCookies(), c => c.Name == "R3ACTLB" && c.Value == expectedToken);
    }

    [Fact]
    public async Task Normal_page_is_not_treated_as_challenge()
    {
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Ok(
            "<html><body>plain</body></html>", ("server", "nginx"), ("cache-control", "no-cache")));
        using var dataSource = TestDataSource.Create(handler, credentials: null);

        var content = await dataSource.GetContentAsync(new ParserRequest("/admins"));

        Assert.Contains("plain", content);
    }
}
