using System.Collections.Concurrent;

namespace LexVerse.Core.Translation;

public sealed class InMemoryTranslationCache : ITranslationCache
{
    private readonly ConcurrentDictionary<TranslationCacheKey, string> _cache = new();

    public bool TryGet(TranslationCacheKey key, out string translatedText)
    {
        return _cache.TryGetValue(key, out translatedText!);
    }

    public void Set(TranslationCacheKey key, string translatedText)
    {
        ArgumentNullException.ThrowIfNull(translatedText);

        _cache[key] = translatedText;
    }
}
