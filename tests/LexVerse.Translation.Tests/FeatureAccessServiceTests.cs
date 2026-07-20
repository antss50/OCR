using LexVerse.Application.Product;
using LexVerse.Core.Product;
using Xunit;

namespace LexVerse.Translation.Tests;

public sealed class FeatureAccessServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAccess_CachesVerifiedSnapshotUntilRefreshInterval()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new ManualTimeProvider(Now);
        var provider = new StubEntitlementProvider(CreateSnapshot(ProductFeatures.PopupTranslation));
        using var service = new FeatureAccessService(
            provider,
            clock,
            refreshInterval: TimeSpan.FromMinutes(5));

        var first = await service.GetAccessAsync(ProductFeatures.PopupTranslation, cancellationToken);
        clock.Advance(TimeSpan.FromMinutes(4));
        var second = await service.GetAccessAsync(ProductFeatures.PopupTranslation, cancellationToken);

        Assert.True(first.IsAllowed);
        Assert.True(second.IsAllowed);
        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public async Task GetAccess_RefreshesAfterIntervalAndUsesNewRevision()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new ManualTimeProvider(Now);
        var provider = new SequenceEntitlementProvider(
            CreateSnapshot(ProductFeatures.PopupTranslation),
            CreateSnapshot(ProductFeatures.RegionTranslation));
        using var service = new FeatureAccessService(
            provider,
            clock,
            refreshInterval: TimeSpan.FromMinutes(5));

        Assert.True((await service.GetAccessAsync(ProductFeatures.PopupTranslation, cancellationToken)).IsAllowed);
        clock.Advance(TimeSpan.FromMinutes(5));

        var refreshedDecision = await service.GetAccessAsync(ProductFeatures.PopupTranslation, cancellationToken);

        Assert.False(refreshedDecision.IsAllowed);
        Assert.Equal(FeatureAccessDenialReason.NotEntitled, refreshedDecision.DenialReason);
        Assert.Equal(2, provider.CallCount);
    }

    [Fact]
    public async Task GetAccess_FailsClosedAndThrottlesRetriesWhenProviderFails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new ManualTimeProvider(Now);
        var provider = new ThrowingEntitlementProvider();
        using var service = new FeatureAccessService(
            provider,
            clock,
            failureRetryInterval: TimeSpan.FromSeconds(15));

        var first = await service.GetAccessAsync(ProductFeatures.DocumentMode, cancellationToken);
        var second = await service.GetAccessAsync(ProductFeatures.DocumentMode, cancellationToken);

        Assert.Equal(FeatureAccessDenialReason.EntitlementsUnavailable, first.DenialReason);
        Assert.Equal(FeatureAccessDenialReason.EntitlementsUnavailable, second.DenialReason);
        Assert.Equal(1, provider.CallCount);

        clock.Advance(TimeSpan.FromSeconds(15));
        _ = await service.GetAccessAsync(ProductFeatures.DocumentMode, cancellationToken);
        Assert.Equal(2, provider.CallCount);
    }

    [Fact]
    public async Task Refresh_PropagatesProviderFailureForRecoveryUx()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var service = new FeatureAccessService(new ThrowingEntitlementProvider());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RefreshAsync(cancellationToken));
    }

    [Fact]
    public async Task Invalidate_RemovesCachedPaidGrantImmediately()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var provider = new SequenceEntitlementProvider(
            CreateSnapshot(ProductFeatures.RegionTranslation),
            CreateSnapshot());
        using var service = new FeatureAccessService(provider);
        Assert.True((await service.GetAccessAsync(
            ProductFeatures.RegionTranslation, cancellationToken)).IsAllowed);

        service.Invalidate();
        var afterSignOut = await service.GetAccessAsync(
            ProductFeatures.RegionTranslation, cancellationToken);

        Assert.False(afterSignOut.IsAllowed);
        Assert.Equal(2, provider.CallCount);
    }

    [Fact]
    public async Task Invalidate_PreventsInFlightRefreshFromRestoringSignedOutGrant()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var provider = new DeferredEntitlementProvider();
        using var service = new FeatureAccessService(provider);
        var pendingAccess = service.GetAccessAsync(
            ProductFeatures.RegionTranslation, cancellationToken).AsTask();
        await provider.Started.Task.WaitAsync(cancellationToken);

        service.Invalidate();
        provider.Complete(CreateSnapshot(ProductFeatures.RegionTranslation));
        var decision = await pendingAccess;

        Assert.False(decision.IsAllowed);
        Assert.Equal(FeatureAccessDenialReason.EntitlementsUnavailable, decision.DenialReason);
    }

    [Fact]
    public async Task FeatureGate_ExecutesAllowedUseCase()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var gate = new FeatureGate(new FixedAccessService(FeatureAccessDecision.Allowed(
            ProductFeatures.RegionTranslation,
            Now.AddDays(1))));
        var invoked = false;

        await gate.ExecuteAsync(
            ProductFeatures.RegionTranslation,
            _ =>
            {
                invoked = true;
                return Task.CompletedTask;
            },
            cancellationToken);

        Assert.True(invoked);
    }

    [Fact]
    public async Task FeatureGate_BlocksDeniedUseCaseWithoutInvokingIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var decision = FeatureAccessDecision.Denied(
            ProductFeatures.FullScreenTranslation,
            FeatureAccessDenialReason.Expired);
        var gate = new FeatureGate(new FixedAccessService(decision));
        var invoked = false;

        var exception = await Assert.ThrowsAsync<FeatureAccessDeniedException>(() => gate.ExecuteAsync(
            ProductFeatures.FullScreenTranslation,
            _ =>
            {
                invoked = true;
                return Task.CompletedTask;
            },
            cancellationToken));

        Assert.False(invoked);
        Assert.Same(decision, exception.Decision);
    }

    private static EntitlementSnapshot CreateSnapshot(params FeatureKey[] features) =>
        new(
            "customer-1",
            Guid.NewGuid().ToString("N"),
            Now,
            features.Select(feature => new FeatureEntitlement(feature)));

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }

    private sealed class StubEntitlementProvider(EntitlementSnapshot snapshot) : IEntitlementProvider
    {
        public int CallCount { get; private set; }

        public Task<EntitlementSnapshot> GetEntitlementsAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(snapshot);
        }
    }

    private sealed class SequenceEntitlementProvider(params EntitlementSnapshot[] snapshots) : IEntitlementProvider
    {
        private int _index;

        public int CallCount { get; private set; }

        public Task<EntitlementSnapshot> GetEntitlementsAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            var snapshot = snapshots[Math.Min(_index, snapshots.Length - 1)];
            _index++;
            return Task.FromResult(snapshot);
        }
    }

    private sealed class ThrowingEntitlementProvider : IEntitlementProvider
    {
        public int CallCount { get; private set; }

        public Task<EntitlementSnapshot> GetEntitlementsAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromException<EntitlementSnapshot>(new InvalidOperationException("unavailable"));
        }
    }

    private sealed class DeferredEntitlementProvider : IEntitlementProvider
    {
        private readonly TaskCompletionSource<EntitlementSnapshot> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<EntitlementSnapshot> GetEntitlementsAsync(
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            return _completion.Task.WaitAsync(cancellationToken);
        }

        public void Complete(EntitlementSnapshot snapshot) => _completion.TrySetResult(snapshot);
    }

    private sealed class FixedAccessService(FeatureAccessDecision decision) : IFeatureAccessService
    {
        public ValueTask<FeatureAccessDecision> GetAccessAsync(
            FeatureKey feature,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(decision);

        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Invalidate() { }
    }
}
