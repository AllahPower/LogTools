using LogsParser.Exceptions;
using LogsParser.Net;
using Xunit;

namespace LogsParser.Tests;

public class CsrfTests
{
    [Fact]
    public void Extracts_token_from_meta_with_double_quotes()
    {
        var html = "<html><head><meta name=\"csrf-token\" content=\"ABC123\"></head></html>";
        Assert.Equal("ABC123", LogsParserAuthenticator.ExtractCsrfToken(html));
    }

    [Fact]
    public void Extracts_token_with_single_quotes()
    {
        var html = "<meta name='csrf-token' content='XYZ'>";
        Assert.Equal("XYZ", LogsParserAuthenticator.ExtractCsrfToken(html));
    }

    [Fact]
    public void Missing_token_throws()
    {
        Assert.Throws<CsrfTokenNotFoundException>(() => LogsParserAuthenticator.ExtractCsrfToken("<html></html>"));
    }
}
