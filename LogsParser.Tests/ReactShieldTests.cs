using System.Security.Cryptography;
using LogsParser.Abstractions;
using LogsParser.Exceptions;
using LogsParser.Models;
using Xunit;

namespace LogsParser.Tests;

public class ReactShieldTests
{
    /// <summary>
    /// Reproduces the markup the service actually serves: an obfuscated array whose payload
    /// strings are plain hex while every identifier around them is \xNN-escaped, the three
    /// toNumbers assignments at indices 7/8/9, and slowAES.decrypt(c,2,a,b) — mode 2 is CBC.
    /// </summary>
    private static string BuildChallenge(string keyHex, string ivHex, string cipherHex) =>
        $$"""
        <!DOCTYPE html>
        <html>
        <script src="/vddosw3data.js"></script>
        <body>
        <div w3-include-html="/5s.html"></div>
        <noscript><h1 style="text-align:center;color:red;"><strong>Please turn JavaScript on and reload the page.</strong></h1></noscript>
        <script>
        w3IncludeHTML(true);
        </script>
        <script type="text/javascript" src="/aes.min.js" ></script><script>
        var _0x6c57=["\x70\x75\x73\x68","\x72\x65\x70\x6C\x61\x63\x65","\x6C\x65\x6E\x67\x74\x68","\x63\x6F\x6E\x73\x74\x72\x75\x63\x74\x6F\x72","","\x30","\x74\x6F\x4C\x6F\x77\x65\x72\x43\x61\x73\x65","{{keyHex}}","{{ivHex}}","{{cipherHex}}","\x63\x6F\x6F\x6B\x69\x65","\x52\x33\x41\x43\x54\x4C\x42\x3D","\x64\x65\x63\x72\x79\x70\x74","\x3B\x20\x65\x78\x70\x69\x72\x65\x73\x3D\x54\x68\x75\x2C\x20\x33\x31\x2D\x44\x65\x63\x2D\x33\x37\x20\x32\x33\x3A\x35\x35\x3A\x35\x35\x20\x47\x4D\x54\x3B\x20\x70\x61\x74\x68\x3D\x2F"];function toNumbers(_0x7fdax2){var _0x7fdax3=[];_0x7fdax2[_0x6c57[1]](/(..)/g,function(_0x7fdax2){_0x7fdax3[_0x6c57[0]](parseInt(_0x7fdax2,16))});return _0x7fdax3}var a=toNumbers(_0x6c57[7]),b=toNumbers(_0x6c57[8]),c=toNumbers(_0x6c57[9]);document[_0x6c57[10]]= _0x6c57[11]+ toHex(slowAES[_0x6c57[12]](c,2,a,b))+ _0x6c57[13]
        setTimeout("location.href='https://arizonarp.logsparser.info:443/admins';",5000);</script>
        </body>
        </html>
        """;

    private static (string KeyHex, string IvHex, string CipherHex, string ExpectedToken) BuildPayload()
    {
        var key = Convert.FromHexString("000102030405060708090a0b0c0d0e0f");
        var iv = Convert.FromHexString("101112131415161718191a1b1c1d1e1f");
        var plaintext = Convert.FromHexString("202122232425262728292a2b2c2d2e2f");

        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        aes.Key = key;
        aes.IV = iv;
        using var encryptor = aes.CreateEncryptor();
        var cipher = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);

        return (
            Convert.ToHexString(key).ToLowerInvariant(),
            Convert.ToHexString(iv).ToLowerInvariant(),
            Convert.ToHexString(cipher).ToLowerInvariant(),
            Convert.ToHexString(plaintext).ToLowerInvariant());
    }

    [Fact]
    public async Task React_challenge_is_solved_and_request_is_retried()
    {
        var (keyHex, ivHex, cipherHex, expectedToken) = BuildPayload();
        var challengeHtml = BuildChallenge(keyHex, ivHex, cipherHex);

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
    public async Task Escaped_identifier_strings_are_not_mistaken_for_payload()
    {
        // Every string here is an \xNN-escaped identifier. Stripping the markers instead of
        // decoding them would turn each into a 32-character hex string and feed the solver
        // three candidates that are not payload at all.
        const string challengeHtml =
            """
            <html><body>
            Please turn JavaScript on and reload the page.
            <script src="/vddosw3data.js"></script>
            <script>
            var names=["\x61\x62\x63\x64\x65\x66\x67\x68\x69\x6A\x6B\x6C\x6D\x6E\x6F\x70","\x71\x72\x73\x74\x75\x76\x77\x78\x79\x7A\x41\x42\x43\x44\x45\x46","\x47\x48\x49\x4A\x4B\x4C\x4D\x4E\x4F\x50\x51\x52\x53\x54\x55\x56"];
            </script>
            </body></html>
            """;

        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Ok(
            challengeHtml, ("server", "nginx"), ("cache-control", "no-cache")));
        using var dataSource = TestDataSource.Create(handler, credentials: null);

        await Assert.ThrowsAsync<ReactShieldBypassException>(
            () => dataSource.GetContentAsync(new ParserRequest("/admins")));
    }

    [Fact]
    public async Task Endlessly_reissued_challenge_gives_up_instead_of_looping()
    {
        var (keyHex, ivHex, cipherHex, _) = BuildPayload();
        var challengeHtml = BuildChallenge(keyHex, ivHex, cipherHex);

        // A solved token cannot be validated before use, so a service that keeps rejecting it
        // must terminate the loop rather than spin forever.
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Ok(
            challengeHtml, ("server", "nginx"), ("cache-control", "no-cache")));
        using var dataSource = TestDataSource.Create(handler, credentials: null);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await Assert.ThrowsAsync<ReactShieldBypassException>(
            () => dataSource.GetContentAsync(new ParserRequest("/admins"), timeout.Token));
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
