using Xunit;

namespace LogsParser.Tests;

public class LogsHtmlParserTests
{
    // Mirrors the real logs table: row 1 carries an eye-toggled row value in the action cell,
    // row 2 has both participants, row 3 has a target only, row 4 has no hidden markup at all.
    private const string LogsHtml = """
        <html><body>
        <p>Показано с <strong>1</strong> по <strong>4</strong> из <strong>96069</strong></p>
        <table class="table table-hover">
        <thead><tr><th>Дата</th><th>Действие</th><th>Данные (I, II)</th><th>IP адрес</th></tr></thead>
        <tbody>
        <tr>
        <td>2026-07-26 14:18:45</td>
        <td>Игрок <a href="#"><strong>Ivan_Kuznetsov</strong></a> арендует объявление №4851. Готовый сет: <div class="btn btn-sm btn-primary js_entry_format_button app-content-entry-format__button"><i class="bi bi-eye"></i></div><div class="app__hidden" data-title="Значение строки 1">1.БоДжек Редкость 6(9553)
        Описание: unic_id: -1 | Заточка: 1

        2.ТВ Робот(8583)
        Описание: unic_id: -1 | Заточка: 0</div></td>
        <td><code><strong>I:</strong></code> <code>100,000,000</code> / <code>20,000,000</code> / <code>1,000</code>
        <div class="btn btn-tiny btn-primary js_entry_format_button app-content-entry-format__button"><i class="bi bi-eye"></i></div><div class="app__hidden" data-title="Дополнительная информация"><ul>
        <li>ID аккаунта: <code>1000001</code></li>
        <li>VC$: <code>0</code></li>
        <li>Доп. счёт №1: <code>-1</code></li>
        <li>Доп. счёт №2: <code>-1</code></li>
        <li>Доп. счёт №3: <code>-1</code></li>
        <li>Доп. счёт №4: <code>-1</code></li>
        <li>Доп. счёт №5: <code>-1</code></li>
        <li>Доп. счёт №6: <code>-1</code></li>
        <li>Депозит: <code>5,000</code></li>
        <li>Уровень администрирования: <code>0</code></li>
        </ul></div><br></td>
        <td><div class="table-ip"><strong><code>I:</code></strong> <a href="#" title="последний">203.0.113.10</a> <a href="#" title="регистрационный">198.51.100.7</a></div></td>
        </tr>
        <tr>
        <td>2026-07-26 14:17:02</td>
        <td>Игрок передаёт предмет</td>
        <td><code><strong>I:</strong></code> <code>200,000,000</code> / <code>30,000,000</code> / <code>0</code>
        <div class="btn btn-tiny btn-primary js_entry_format_button app-content-entry-format__button"><i class="bi bi-eye"></i></div><div class="app__hidden" data-title="Дополнительная информация"><ul>
        <li>ID аккаунта: <code>1000002</code></li>
        <li>VC$: <code>0</code></li>
        <li>Доп. счёт №1: <code>-1</code></li>
        <li>Доп. счёт №2: <code>-1</code></li>
        <li>Доп. счёт №3: <code>-1</code></li>
        <li>Доп. счёт №4: <code>-1</code></li>
        <li>Доп. счёт №5: <code>-1</code></li>
        <li>Доп. счёт №6: <code>-1</code></li>
        <li>Депозит: <code>50,000,000</code></li>
        <li>Уровень администрирования: <code>0</code></li>
        </ul></div><br>
        <code><strong>II:</strong></code> <code>300,000,000</code> / <code>40,000,000</code> / <code>197</code>
        <div class="btn btn-tiny btn-primary js_entry_format_button app-content-entry-format__button"><i class="bi bi-eye"></i></div><div class="app__hidden" data-title="Дополнительная информация"><ul>
        <li>ID аккаунта: <code>1000003</code></li>
        <li>VC$: <code>0</code></li>
        <li>Доп. счёт №1: <code>-1</code></li>
        <li>Доп. счёт №2: <code>-1</code></li>
        <li>Доп. счёт №3: <code>-1</code></li>
        <li>Доп. счёт №4: <code>-1</code></li>
        <li>Доп. счёт №5: <code>-1</code></li>
        <li>Доп. счёт №6: <code>-1</code></li>
        <li>Депозит: <code>6,000</code></li>
        <li>Уровень администрирования: <code>0</code></li>
        </ul></div><br></td>
        <td></td>
        </tr>
        <tr>
        <td>2026-07-26 14:16:00</td>
        <td>Игрок получает предмет</td>
        <td><code><strong>II:</strong></code> <code>10</code> / <code>20</code> / <code>30</code>
        <div class="btn btn-tiny btn-primary js_entry_format_button app-content-entry-format__button"><i class="bi bi-eye"></i></div><div class="app__hidden" data-title="Дополнительная информация"><ul>
        <li>ID аккаунта: <code>999111</code></li>
        <li>VC$: <code>0</code></li>
        <li>Доп. счёт №1: <code>-1</code></li>
        <li>Доп. счёт №2: <code>-1</code></li>
        <li>Доп. счёт №3: <code>-1</code></li>
        <li>Доп. счёт №4: <code>-1</code></li>
        <li>Доп. счёт №5: <code>-1</code></li>
        <li>Доп. счёт №6: <code>-1</code></li>
        <li>Депозит: <code>7</code></li>
        <li>Уровень администрирования: <code>0</code></li>
        </ul></div><br></td>
        <td></td>
        </tr>
        <tr>
        <td>2026-07-26 14:15:00</td>
        <td>Игрок подключается к серверу</td>
        <td></td>
        <td></td>
        </tr>
        </tbody>
        </table>
        </body></html>
        """;

    [Fact]
    public void Parses_every_row_of_the_table()
    {
        var page = LogsHtmlParser.ParseLogs(LogsHtml);

        Assert.Equal(4, page.Entries.Count);
        Assert.Equal(new DateTime(2026, 7, 26, 14, 18, 45), page.Entries[0].Timestamp);
    }

    [Fact]
    public void Parses_meta_info_wrapped_in_tags()
    {
        var page = LogsHtmlParser.ParseLogs(LogsHtml);

        var meta = page.MetaInfo;
        Assert.NotNull(meta);
        Assert.Equal(1, meta.Start);
        Assert.Equal(4, meta.End);
        Assert.Equal(96069, meta.Total);
    }

    [Fact]
    public void Extracts_revealed_value_with_its_label()
    {
        var page = LogsHtmlParser.ParseLogs(LogsHtml);

        var value = Assert.Single(page.Entries[0].RevealedValues);
        Assert.Equal("Значение строки 1", value.Label);
        Assert.StartsWith("1.БоДжек Редкость 6(9553)", value.Text);
    }

    [Fact]
    public void Revealed_value_keeps_the_line_structure_of_the_set()
    {
        var page = LogsHtmlParser.ParseLogs(LogsHtml);

        var value = Assert.Single(page.Entries[0].RevealedValues);
        Assert.Contains("\nОписание: unic_id: -1 | Заточка: 1", value.Text);
        Assert.Contains("\n\n2.ТВ Робот(8583)", value.Text);
    }

    [Fact]
    public void Text_holds_only_what_the_page_shows()
    {
        var page = LogsHtmlParser.ParseLogs(LogsHtml);

        var entry = page.Entries[0];
        Assert.Equal("Игрок Ivan_Kuznetsov арендует объявление №4851. Готовый сет:", entry.Text);
        Assert.DoesNotContain("БоДжек", entry.Text);
    }

    [Fact]
    public void Html_still_carries_the_hidden_markup()
    {
        var page = LogsHtmlParser.ParseLogs(LogsHtml);

        Assert.Contains("app__hidden", page.Entries[0].Html);
        Assert.Contains("БоДжек", page.Entries[0].Html);
    }

    [Fact]
    public void Rows_without_hidden_markup_expose_an_empty_collection()
    {
        var page = LogsHtmlParser.ParseLogs(LogsHtml);

        Assert.Empty(page.Entries[3].RevealedValues);
        Assert.Equal("Игрок подключается к серверу", page.Entries[3].Text);
    }

    [Fact]
    public void Participant_info_blocks_do_not_leak_across_the_data_cell()
    {
        var page = LogsHtmlParser.ParseLogs(LogsHtml);

        var entry = page.Entries[0];
        Assert.NotNull(entry.Sender);
        Assert.Equal(1000001L, entry.Sender.AdditionalInfo?.AccountId);
        Assert.Null(entry.Target);
    }

    [Fact]
    public void Assigns_each_info_block_to_its_own_participant()
    {
        var page = LogsHtmlParser.ParseLogs(LogsHtml);

        var entry = page.Entries[1];
        Assert.Equal(200000000L, entry.Sender?.Money);
        Assert.Equal(1000002L, entry.Sender?.AdditionalInfo?.AccountId);
        Assert.Equal(300000000L, entry.Target?.Money);
        Assert.Equal(1000003L, entry.Target?.AdditionalInfo?.AccountId);
    }

    [Fact]
    public void Target_only_row_does_not_hand_its_info_to_the_sender()
    {
        var page = LogsHtmlParser.ParseLogs(LogsHtml);

        var entry = page.Entries[2];
        Assert.Null(entry.Sender);
        Assert.Equal(999111L, entry.Target?.AdditionalInfo?.AccountId);
        Assert.Equal(10L, entry.Target?.Money);
    }

    [Fact]
    public void Parses_last_and_registration_ip()
    {
        var page = LogsHtmlParser.ParseLogs(LogsHtml);

        Assert.Equal("203.0.113.10", page.Entries[0].Sender?.LastIp);
        Assert.Equal("198.51.100.7", page.Entries[0].Sender?.RegistrationIp);
    }

    [Fact]
    public void Missing_tbody_degrades_to_an_empty_page()
    {
        var page = LogsHtmlParser.ParseLogs("<html><body><p>Показано с 1 по 4 из 96069</p></body></html>");

        Assert.Empty(page.Entries);
        Assert.Equal(96069, page.MetaInfo?.Total);
    }

    [Fact]
    public void Empty_html_throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => LogsHtmlParser.ParseLogs("   "));
    }
}
