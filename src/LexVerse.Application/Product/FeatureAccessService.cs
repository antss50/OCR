using LexVerse.Core.Product;

namespace LexVerse.Application.Product;

/// <summary>
/// Maintains a short-lived verified entitlement snapshot for all feature modules.
/// A refresh failure clears the snapshot and denies access until a later retry.
/// Offline fallback belongs in an entitlement provider that verifies a signed cache.
/// </summary>
public sealed class FeatureAccessService : IFeatureAccessService, IDisposable
{
    public static readonly TimeSpan DefaultRefreshInterval = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan DefaultFailureRetryInterval = TimeSpan.FromSeconds(15);

    private readonly IEntitlementProvider _provider;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _refreshInterval;
    private readonly TimeSpan _failureRetryInterval;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _cacheSync = new();
    private CacheState _cache = new(null, DateTimeOffset.MinValue);
    private int _generation;
    private bool _disposed;

    public FeatureAccessService(
        IEntitlementProvider provider,
        TimeProvider? timeProvider = null,
        TimeSpan? refreshInterval = null,
        TimeSpan? failureRetryInterval = null)
    {
        ArgumentNullException.ThrowIfNull(provider);

        _provider = provider;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _refreshInterval = ValidateInterval(
            refreshInterval ?? DefaultRefreshInterval,
            nameof(refreshInterval));
        _failureRetryInterval = ValidateInterval(
            failureRetryInterval ?? DefaultFailureRetryInterval,
            nameof(failureRetryInterval));
    }

    public async ValueTask<FeatureAccessDecision> GetAccessAsync(
        FeatureKey feature,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(feature);

        EntitlementSnapshot? snapshot;
        try
        {
            snapshot = await GetCurrentSnapshotAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            snapshot = null;
        }

        return snapshot is null
            ? FeatureAccessDecision.Denied(feature, FeatureAccessDenialReason.EntitlementsUnavailable)
            : snapshot.Evaluate(feature, _timeProvider.GetUtcNow());
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await RefreshCoreAsync(force: true, cancellationToken);
    }

    public void Invalidate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_cacheSync)
        {
            _generation++;
            Volatile.Write(ref _cache, new CacheState(null, DateTimeOffset.MinValue));
        }
    }

    private async Task<EntitlementSnapshot?> GetCurrentSnapshotAsync(CancellationToken cancellationToken)
    {
        var utcNow = _timeProvider.GetUtcNow();
        var cache = Volatile.Read(ref _cache);
        if (utcNow < cache.RefreshAfterUtc)
        {
            return cache.Snapshot;
        }

        return await RefreshCoreAsync(force: false, cancellationToken);
    }

    private async Task<EntitlementSnapshot?> RefreshCoreAsync(
        bool force,
        CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            var utcNow = _timeProvider.GetUtcNow();
            var cache = Volatile.Read(ref _cache);
            if (!force && utcNow < cache.RefreshAfterUtc)
            {
                return cache.Snapshot;
            }

            try
            {
                var generation = Volatile.Read(ref _generation);
                var refreshed = await _provider.GetEntitlementsAsync(cancellationToken);
                var snapshot = refreshed ?? throw new InvalidOperationException(
                    "The entitlement provider returned no verified snapshot.");
                lock (_cacheSync)
                {
                    if (generation != _generation)
                    {
                        return null;
                    }

                    Volatile.Write(
                        ref _cache,
                        new CacheState(snapshot, _timeProvider.GetUtcNow().Add(_refreshInterval)));
                    return snapshot;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                lock (_cacheSync)
                {
                    Volatile.Write(
                        ref _cache,
                        new CacheState(null, _timeProvider.GetUtcNow().Add(_failureRetryInterval)));
                }
                throw;
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private static TimeSpan ValidateInterval(TimeSpan value, string parameterName)
    {
        if (value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Refresh intervals must be positive.");
        }

        return value;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _refreshGate.Dispose();
    }

    private sealed record CacheState(
        EntitlementSnapshot? Snapshot,
        DateTimeOffset RefreshAfterUtc);
}
