namespace LexVerse.Application.Account;

public interface IAccountSessionProvider
{
    bool IsConfigured { get; }

    Task<AccountSession?> GetSessionAsync(CancellationToken cancellationToken = default);

    Task<AccountSession> SignInAsync(CancellationToken cancellationToken = default);

    Task SignOutAsync(CancellationToken cancellationToken = default);

    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}
