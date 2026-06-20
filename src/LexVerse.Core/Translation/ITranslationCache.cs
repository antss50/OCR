namespace LexVerse.Core.Translation;

public interface ITranslationCache
{
    bool TryGet(TranslationCacheKey key, out string translatedText);

    void Set(TranslationCacheKey key, string translatedText);
}
