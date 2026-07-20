namespace LexVerse.Application.Privacy;

public interface IPrivacyPreferencesService
{
    PrivacyPreferences Current { get; }

    event EventHandler<PrivacyPreferences>? Changed;

    Task UpdateAsync(
        PrivacyPreferences preferences,
        CancellationToken cancellationToken = default);
}
