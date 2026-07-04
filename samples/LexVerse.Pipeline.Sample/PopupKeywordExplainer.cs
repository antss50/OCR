using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using LexVerse.Core.Translation;

namespace LexVerse.Pipeline.Sample;

public sealed class PopupKeywordExplainer
{
    private const int MaxSummaryLength = 420;
    private static readonly Uri EnglishWikipediaRoot = new("https://en.wikipedia.org/");
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex SentenceRegex = new(@"[^.!?]+[.!?]", RegexOptions.Compiled);

    private static readonly IReadOnlyDictionary<string, string> FallbackSummaries =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Theodosius II"] = "Theodosius II là hoàng đế Đông La Mã/Byzantine đầu thế kỷ V. Trong đoạn này, ông được nhắc tới vì tuyên bố chị gái Aelia Pulcheria là Augusta.",
            ["Aelia Pulcheria"] = "Aelia Pulcheria là chị gái của Theodosius II và là một nhân vật hoàng gia Byzantine có ảnh hưởng lớn trong triều đình.",
            ["Augusta"] = "Augusta là tước hiệu hoàng gia dành cho phụ nữ trong bối cảnh La Mã/Byzantine, thường gắn với địa vị chính trị rất cao.",
            ["Philadelphia"] = "Philadelphia là thành phố ở Pennsylvania, Mỹ. Trong lịch sử Mỹ, nơi này gắn với nhiều sự kiện lập quốc quan trọng.",
            ["Continental Congress"] = "Continental Congress là cơ quan đại diện các thuộc địa Mỹ trong thời Cách mạng Mỹ, trước khi Hoa Kỳ có chính phủ liên bang.",
            ["British Empire"] = "British Empire là Đế quốc Anh, hệ thống lãnh thổ và quyền lực của Anh trên nhiều khu vực trong thời cận đại.",
            ["West Germany"] = "West Germany là Tây Đức, nhà nước Đức phía Tây trong thời Chiến tranh Lạnh trước khi Đức thống nhất năm 1990.",
            ["The Miracle"] = "Cụm này có thể là một tên gọi hoặc tiêu đề. Cần thêm ngữ cảnh của câu kế bên để biết chính xác đang nói tới sự kiện, tác phẩm hay biệt danh nào."
        };

    private readonly ITextTranslator _translator;
    private readonly HttpClient _httpClient;
    private readonly Dictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);

    public PopupKeywordExplainer(ITextTranslator translator, HttpClient? httpClient = null)
    {
        _translator = translator;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(6) };

        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "LexVersePipelineSample/0.1 (keyword lookup)");
        }
    }

    public async Task<string> ExplainAsync(
        string term,
        string context,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        term = term.Trim();
        targetLanguage = NormalizeTargetLanguage(targetLanguage);
        var cacheKey = $"{targetLanguage}|{term}|{BuildContextHint(term, context)}";

        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        try
        {
            var title = await FindWikipediaTitleAsync(term, context, cancellationToken);
            if (string.IsNullOrWhiteSpace(title))
            {
                return Cache(cacheKey, GetFallbackSummary(term));
            }

            var summary = await ReadWikipediaSummaryAsync(title, cancellationToken);
            if (string.IsNullOrWhiteSpace(summary))
            {
                return Cache(cacheKey, GetFallbackSummary(term));
            }

            var shortSummary = ShortenSummary(summary);
            var localizedSummary = await LocalizeSummaryAsync(
                shortSummary,
                targetLanguage,
                cancellationToken);

            return Cache(cacheKey, localizedSummary);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Cache(cacheKey, GetFallbackSummary(term));
        }
    }

    private async Task<string?> FindWikipediaTitleAsync(
        string term,
        string context,
        CancellationToken cancellationToken)
    {
        var query = BuildSearchQuery(term, context);
        var path = "w/api.php?action=query&list=search&srlimit=1&format=json&srsearch="
            + Uri.EscapeDataString(query);

        using var response = await _httpClient.GetAsync(
            new Uri(EnglishWikipediaRoot, path),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("query", out var queryElement) ||
            !queryElement.TryGetProperty("search", out var searchElement) ||
            searchElement.ValueKind != JsonValueKind.Array ||
            searchElement.GetArrayLength() == 0)
        {
            return null;
        }

        return searchElement[0].TryGetProperty("title", out var titleElement)
            ? titleElement.GetString()
            : null;
    }

    private async Task<string?> ReadWikipediaSummaryAsync(
        string title,
        CancellationToken cancellationToken)
    {
        var page = Uri.EscapeDataString(title.Replace(' ', '_'));
        using var response = await _httpClient.GetAsync(
            new Uri(EnglishWikipediaRoot, $"api/rest_v1/page/summary/{page}"),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        return document.RootElement.TryGetProperty("extract", out var extractElement)
            ? extractElement.GetString()
            : null;
    }

    private async Task<string> LocalizeSummaryAsync(
        string summary,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(targetLanguage) ||
            targetLanguage.Equals("en", StringComparison.OrdinalIgnoreCase))
        {
            return summary;
        }

        try
        {
            var result = await _translator.TranslateAsync(
                summary,
                targetLanguage,
                "en",
                cancellationToken);

            return WebUtility.HtmlDecode(result.TranslatedText).Trim();
        }
        catch
        {
            return targetLanguage.Equals("vi", StringComparison.OrdinalIgnoreCase)
                ? $"Tìm thấy thông tin, nhưng chưa dịch được tóm tắt: {summary}"
                : summary;
        }
    }

    private static string BuildSearchQuery(string term, string context)
    {
        if (term.Equals("President", StringComparison.OrdinalIgnoreCase) &&
            context.Contains("Peru", StringComparison.OrdinalIgnoreCase))
        {
            return "President of Peru";
        }

        if (term.Equals("The Miracle", StringComparison.OrdinalIgnoreCase) &&
            context.Contains("West Germany", StringComparison.OrdinalIgnoreCase))
        {
            return "Miracle of Bern";
        }

        if (term.Equals("Augusta", StringComparison.OrdinalIgnoreCase) &&
            context.Contains("Byzantine", StringComparison.OrdinalIgnoreCase))
        {
            return "Augusta title";
        }

        return BuildContextHint(term, context) is { Length: > 0 } hint
            ? $"{term} {hint}"
            : term;
    }

    private static string BuildContextHint(string term, string context)
    {
        if (context.Contains("Byzantine", StringComparison.OrdinalIgnoreCase) &&
            !term.Contains("Byzantine", StringComparison.OrdinalIgnoreCase))
        {
            return "Byzantine";
        }

        if (context.Contains("Continental Congress", StringComparison.OrdinalIgnoreCase) &&
            !term.Contains("Continental Congress", StringComparison.OrdinalIgnoreCase))
        {
            return "American Revolution";
        }

        if (context.Contains("West Germany", StringComparison.OrdinalIgnoreCase) &&
            !term.Contains("West Germany", StringComparison.OrdinalIgnoreCase))
        {
            return "West Germany";
        }

        return string.Empty;
    }

    private static string ShortenSummary(string summary)
    {
        summary = WhitespaceRegex.Replace(summary, " ").Trim();
        var sentences = SentenceRegex.Matches(summary)
            .Select(match => match.Value.Trim())
            .Where(sentence => sentence.Length > 0)
            .Take(2)
            .ToArray();

        var shortSummary = sentences.Length > 0
            ? string.Join(" ", sentences)
            : summary;

        if (shortSummary.Length <= MaxSummaryLength)
        {
            return shortSummary;
        }

        var cutAt = shortSummary.LastIndexOf(' ', MaxSummaryLength);
        return string.Concat(shortSummary.AsSpan(0, cutAt > 120 ? cutAt : MaxSummaryLength), "...");
    }

    private static string NormalizeTargetLanguage(string targetLanguage)
    {
        targetLanguage = targetLanguage.Trim();
        var separator = targetLanguage.IndexOf('-');
        return separator <= 0 ? targetLanguage : targetLanguage[..separator];
    }

    private static string GetFallbackSummary(string term)
    {
        return FallbackSummaries.TryGetValue(term, out var fallback)
            ? fallback
            : $"Chưa tìm thấy nguồn tóm tắt đủ rõ cho \"{term}\". Hãy thử chọn cụm dài hơn hoặc thêm ngữ cảnh.";
    }

    private string Cache(string cacheKey, string summary)
    {
        _cache[cacheKey] = summary;
        return summary;
    }
}
