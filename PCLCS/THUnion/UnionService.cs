using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace PCLCS.THUnion;

/// <summary>THUnion public endpoints and parsing. Remote HTML is data, never launcher XAML.</summary>
public static class UnionService
{
    public const string MapUrl = "http://map.thunion.club/";
    public const string DynmapUrl = "http://www.lvss.xyz:14010/";
    public const string WikiBase = "http://www.lvss.xyz:14014/index.php?title=";
    private static readonly HttpClient Client = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        UseCookies = false
    }) { Timeout = TimeSpan.FromSeconds(12) };
    private static readonly SemaphoreSlim AvatarSlots = new(4);

    public static string WikiUrl(string title) => WikiBase + Uri.EscapeDataString(title);

    public static string PageUrl(int page) => page switch
    {
        0 or 1 => MapUrl,
        2 => WikiUrl("三周目工业设施导引"),
        3 => WikiUrl("首页"),
        4 => WikiUrl("服务器列表"),
        5 => WikiUrl("首页") + "#" + Uri.EscapeDataString("二周目"),
        6 => WikiUrl("首页") + "#" + Uri.EscapeDataString("一周目（已毕业）"),
        7 => WikiUrl("部门规章制度"),
        _ => throw new ArgumentOutOfRangeException(nameof(page))
    };

    public static bool IsWebUrl(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    public static async Task<OnlineSnapshot> GetOnlineAsync(CancellationToken token)
    {
        string url = DynmapUrl + "up/world/world/" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var bytes = await GetBytesAsync(url, "application/json", DynmapUrl, 4 * 1024 * 1024, token).ConfigureAwait(false);
        return ParseOnline(Encoding.UTF8.GetString(bytes));
    }

    public static async Task<IReadOnlyList<WikiTable>> GetTablesAsync(int page, CancellationToken token)
    {
        if (page != 2 && page != 4) throw new ArgumentOutOfRangeException(nameof(page));
        string url = PageUrl(page);
        var bytes = await GetBytesAsync(url, "text/html", WikiUrl("首页"), 4 * 1024 * 1024, token).ConfigureAwait(false);
        return ParseTables(Encoding.UTF8.GetString(bytes), url);
    }

    public static async Task<byte[]> GetAvatarAsync(string name, CancellationToken token)
    {
        await AvatarSlots.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await GetBytesAsync(DynmapUrl + "tiles/faces/32x32/" + Uri.EscapeDataString(name) + ".png",
                "image/png", DynmapUrl, 256 * 1024, token).ConfigureAwait(false);
        }
        finally { AvatarSlots.Release(); }
    }

    private static async Task<byte[]> GetBytesAsync(string url, string accept, string referer, int limit, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd(accept);
        request.Headers.Referrer = new Uri(referer);
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 PCL-Union/2.99");
        request.Headers.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9");
        request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("服务器响应过大。");
        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var output = new MemoryStream();
        byte[] buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false)) > 0)
        {
            if (output.Length + count > limit) throw new InvalidDataException("服务器响应过大。");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    public static OnlineSnapshot ParseOnline(string json)
    {
        var root = JObject.Parse(json);
        if (root["currentcount"]?.Type != JTokenType.Integer || (long)root["currentcount"]! < 0 ||
            (long)root["currentcount"]! > int.MaxValue || root["players"] is not JArray players)
            throw new InvalidDataException("在线列表响应缺少有效的 currentcount 或 players。");
        var result = new OnlineSnapshot { CurrentCount = (int)root["currentcount"]! };
        foreach (var player in players)
        {
            string name = (string?)player["name"] ?? "";
            string world = (string?)player["world"] ?? "";
            if (string.IsNullOrWhiteSpace(name) || name.Length > 256 || string.IsNullOrWhiteSpace(world))
                throw new InvalidDataException("在线玩家名称或维度无效。");
            result.Players.Add(new OnlinePlayer
            {
                Name = name,
                Dimension = world switch { "world" => "主世界", "DIM-1" => "下界", "DIM1" => "末地", _ => world },
                X = Number(player, "x"), Y = Number(player, "y"), Z = Number(player, "z"),
                Health = Number(player, "health"), Armor = Number(player, "armor")
            });
        }
        return result;
    }

    private static double Number(JToken item, string key)
    {
        var value = item[key];
        if (value == null || (value.Type != JTokenType.Integer && value.Type != JTokenType.Float))
            throw new InvalidDataException("玩家数据缺少数值字段：" + key);
        double number = (double)value;
        if (double.IsNaN(number) || double.IsInfinity(number)) throw new InvalidDataException("玩家数值无效：" + key);
        return number;
    }

    public static string FormatNumber(double number) => number.ToString("0.##", CultureInfo.InvariantCulture);

    public static IReadOnlyList<WikiTable> ParseTables(string html, string sourceUrl)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        // Exclude MediaWiki navigation, TOC and nested tables. HAP also handles omitted closing tags.
        var content = document.DocumentNode.SelectSingleNode("//*[@id='mw-content-text']") ?? document.DocumentNode;
        var tables = content.SelectNodes(".//table[contains(concat(' ', normalize-space(@class), ' '), ' wikitable ')]");
        if (tables == null) throw new InvalidDataException("Wiki 响应中未找到数据表格。");
        var result = new List<WikiTable>();
        foreach (var table in tables)
        {
            if (table.Ancestors("table").Any()) continue;
            var parsed = new WikiTable { Caption = CleanText(table.SelectSingleNode("./caption")) };
            var rows = table.SelectNodes("./tbody/tr | ./tr | ./thead/tr | ./tfoot/tr");
            if (rows == null) continue;
            int rowIndex = 0;
            var occupied = new HashSet<(int Row, int Column)>();
            foreach (var row in rows)
            {
                var cells = row.SelectNodes("./th | ./td");
                if (cells == null) continue;
                int column = 0;
                foreach (var cell in cells)
                {
                    while (occupied.Contains((rowIndex, column))) column++;
                    int colspan = Math.Max(1, Math.Min(32, cell.GetAttributeValue("colspan", 1)));
                    int rowspan = Math.Max(1, Math.Min(512, cell.GetAttributeValue("rowspan", 1)));
                    var value = new WikiCell
                    {
                        Row = rowIndex, Column = column, RowSpan = rowspan, ColumnSpan = colspan,
                        IsHeader = cell.Name == "th", Text = CleanText(cell)
                    };
                    foreach (var link in cell.SelectNodes(".//a[@href]") ?? Enumerable.Empty<HtmlNode>())
                    {
                        if (Uri.TryCreate(new Uri(sourceUrl), link.GetAttributeValue("href", ""), out var uri) && IsWebUrl(uri.AbsoluteUri))
                            value.Links.Add(new WikiLink { Text = CleanText(link), Url = uri.AbsoluteUri });
                    }
                    parsed.Cells.Add(value);
                    for (int r = rowIndex; r < rowIndex + rowspan; r++)
                        for (int c = column; c < column + colspan; c++) occupied.Add((r, c));
                    column += colspan;
                    parsed.ColumnCount = Math.Max(parsed.ColumnCount, column);
                    parsed.RowCount = Math.Max(parsed.RowCount, rowIndex + rowspan);
                }
                rowIndex++;
            }
            if (parsed.Cells.Count > 0) result.Add(parsed);
        }
        if (result.Count == 0) throw new InvalidDataException("Wiki 表格为空或格式无效。");
        return result;
    }

    private static string CleanText(HtmlNode? node)
    {
        if (node == null) return "";
        var clone = node.CloneNode(true);
        foreach (var script in clone.SelectNodes(".//script | .//style") ?? Enumerable.Empty<HtmlNode>()) script.Remove();
        foreach (var br in clone.SelectNodes(".//br") ?? Enumerable.Empty<HtmlNode>())
            br.ParentNode.ReplaceChild(HtmlNode.CreateNode("\n"), br);
        return Regex.Replace(HtmlEntity.DeEntitize(clone.InnerText).Replace('\u00a0', ' '), @"[\t ]+", " ").Trim();
    }
}

public sealed class OnlineSnapshot
{
    public int CurrentCount { get; set; }
    public List<OnlinePlayer> Players { get; } = new();
}

public sealed class OnlinePlayer
{
    public string Name { get; set; } = "";
    public string Dimension { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double Health { get; set; }
    public double Armor { get; set; }
    public string Coordinates => $"{UnionService.FormatNumber(X)}, {UnionService.FormatNumber(Y)}, {UnionService.FormatNumber(Z)}";
}

public sealed class WikiTable
{
    public string Caption { get; set; } = "";
    public int RowCount { get; set; }
    public int ColumnCount { get; set; }
    public List<WikiCell> Cells { get; } = new();
}

public sealed class WikiCell
{
    public int Row { get; set; }
    public int Column { get; set; }
    public int RowSpan { get; set; }
    public int ColumnSpan { get; set; }
    public bool IsHeader { get; set; }
    public string Text { get; set; } = "";
    public List<WikiLink> Links { get; } = new();
}

public sealed class WikiLink
{
    public string Text { get; set; } = "";
    public string Url { get; set; } = "";
}
