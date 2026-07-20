using LexVerse.Application.Account;
using LexVerse.Application.Product;
using LexVerse.Core.Product;

namespace LexVerse.Application.Commerce;

/// <summary>Serializes account and purchase operations while keeping backend authority intact.</summary>
public sealed class CommerceCoordinator : IDisposable
{
    private readonly IAccountSessionProvider _accounts;
    private readonly IProductCatalogProvider _catalogs;
    private readonly ICommerceBackend _commerce;
    private readonly IExternalUriLauncher _launcher;
    private readonly IFeatureAccessService _featureAccess;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;

    public CommerceCoordinator(
        IAccountSessionProvider accounts,
        IProductCatalogProvider catalogs,
        ICommerceBackend commerce,
        IExternalUriLauncher launcher,
        IFeatureAccessService featureAccess)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(catalogs);
        ArgumentNullException.ThrowIfNull(commerce);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(featureAccess);
        _accounts = accounts;
        _catalogs = catalogs;
        _commerce = commerce;
        _launcher = launcher;
        _featureAccess = featureAccess;
        State = new CommerceState(
            accounts.IsConfigured && commerce.IsConfigured
                ? CommerceStatus.Loading
                : CommerceStatus.Unconfigured);
    }

    public CommerceState State { get; private set; }

    public event EventHandler<CommerceState>? StateChanged;

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(async token =>
        {
            var catalog = await _catalogs.GetCatalogAsync(token);
            var session = await _accounts.GetSessionAsync(token);
            if (session is null)
            {
                SetState(new CommerceState(CommerceStatus.SignedOut, Catalog: catalog));
                return;
            }

            SetState(new CommerceState(CommerceStatus.Ready, session, catalog));
        }, cancellationToken);

    public Task SignInAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(async token =>
        {
            var session = await _accounts.SignInAsync(token);
            var catalog = await _catalogs.GetCatalogAsync(token);
            await TryRefreshFeatureAccessAsync(token);
            SetState(new CommerceState(CommerceStatus.Ready, session, catalog));
        }, cancellationToken);

    public Task SignOutAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(async token =>
        {
            try
            {
                await _accounts.SignOutAsync(token);
            }
            finally
            {
                _featureAccess.Invalidate();
            }

            SetState(State with { Status = CommerceStatus.SignedOut, Account = null });
        }, cancellationToken);

    public Task StartCheckoutAsync(string offerId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(async token =>
        {
            var current = State;
            if (current.Account is null || current.Catalog?.FindOffer(offerId) is not { IsPublic: true })
            {
                throw new InvalidOperationException("A current public offer and signed-in account are required.");
            }

            var checkout = await _commerce.CreateCheckoutAsync(offerId, token);
            _launcher.Open(checkout.CheckoutUri);
            SetState(current with { Status = CommerceStatus.CheckoutOpened });
        }, cancellationToken);

    public Task RestorePurchasesAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(async token =>
        {
            if (State.Account is null)
            {
                throw new InvalidOperationException("Sign in before restoring purchases.");
            }

            await _commerce.RestorePurchasesAsync(token);
            await TryRefreshFeatureAccessAsync(token);
            var catalog = await _catalogs.GetCatalogAsync(token);
            SetState(State with { Status = CommerceStatus.Ready, Catalog = catalog });
        }, cancellationToken);

    private async Task TryRefreshFeatureAccessAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _featureAccess.RefreshAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            _featureAccess.Invalidate();
        }
    }

    private async Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_accounts.IsConfigured || !_commerce.IsConfigured)
        {
            SetState(new CommerceState(CommerceStatus.Unconfigured));
            return;
        }

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetime.Token);
        cancellationToken = lifetime.Token;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            SetState(State with { Status = CommerceStatus.Loading });
            await operation(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            SetState(State with { Status = CommerceStatus.Error });
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void SetState(CommerceState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
    }
}
