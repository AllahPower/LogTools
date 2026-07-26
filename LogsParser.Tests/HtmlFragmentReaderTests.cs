using LogsParser.Parsing;
using Xunit;

namespace LogsParser.Tests;

public class HtmlFragmentReaderTests
{
    [Theory]
    [InlineData("""<div class="app__hidden" data-title="Значение строки 1">x</div>""")]
    [InlineData("""<div data-title="Значение строки 1" class="app__hidden">x</div>""")]
    [InlineData("""<div data-title='Значение строки 1' class='app__hidden'>x</div>""")]
    public void Reads_attribute_regardless_of_attribute_order(string html)
    {
        Assert.Equal("Значение строки 1", HtmlFragmentReader.ExtractAttributeValue(html, "data-title"));
    }

    [Fact]
    public void Does_not_confuse_prefixed_attribute_with_bare_one()
    {
        const string html = """<a data-title="скрытое" title="последний">1.2.3.4</a>""";

        Assert.Equal("последний", HtmlFragmentReader.ExtractAttributeValue(html, "title"));
        Assert.Equal("скрытое", HtmlFragmentReader.ExtractAttributeValue(html, "data-title"));
    }

    [Fact]
    public void Reads_attribute_only_from_the_opening_tag()
    {
        const string html = """<div class="app__hidden">nested <span data-title="inner">x</span></div>""";

        Assert.Null(HtmlFragmentReader.ExtractAttributeValue(html, "data-title"));
    }

    [Fact]
    public void Decodes_attribute_entities()
    {
        const string html = """<div data-title="Деньги &amp; банк">x</div>""";

        Assert.Equal("Деньги & банк", HtmlFragmentReader.ExtractAttributeValue(html, "data-title"));
    }

    [Fact]
    public void Returns_null_for_missing_attribute()
    {
        Assert.Null(HtmlFragmentReader.ExtractAttributeValue("""<div class="x">y</div>""", "data-title"));
    }

    [Fact]
    public void Matches_class_listed_among_others()
    {
        const string html = """<div class="btn app__hidden extra">payload</div>""";

        Assert.Single(HtmlFragmentReader.ExtractElementsByClass(html, "app__hidden"));
    }

    [Fact]
    public void Does_not_match_hyphenated_superstring_of_class()
    {
        const string html = """<div class="app__hidden-set">payload</div>""";

        Assert.Empty(HtmlFragmentReader.ExtractElementsByClass(html, "app__hidden"));
    }

    [Fact]
    public void Multiline_normalization_keeps_line_breaks()
    {
        const string html = "1.БоДжек   Редкость\n   Описание: unic_id: -1\n\n\n2.ТВ Робот";

        var text = HtmlFragmentReader.NormalizeMultilineText(html);

        Assert.Equal("1.БоДжек Редкость\nОписание: unic_id: -1\n\n2.ТВ Робот", text);
    }

    [Fact]
    public void Single_line_normalization_still_collapses_line_breaks()
    {
        const string html = "первая\nвторая";

        Assert.Equal("первая вторая", HtmlFragmentReader.NormalizeText(html));
    }
}
