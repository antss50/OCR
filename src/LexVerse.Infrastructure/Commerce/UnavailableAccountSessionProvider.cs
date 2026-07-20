using LexVerse.Application.Account;

namespace LexVerse.Infrastructure.Commerce;

public sealed class UnavailableAccountSessionProvider : IAccountSessionProvider
{
    public bool IsConfigured => false;

    public Task<AccountSession?> GetSessionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<AccountSession?>(null);

    public Task<AccountSession> SignInAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<AccountSession>(new InvalidOperationException("Account sign-in is not configured."));

    public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<string>(new InvalidOperationException("Account sign-in is not configured."));
}
