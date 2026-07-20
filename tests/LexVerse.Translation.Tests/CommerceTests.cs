using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using LexVerse.Application.Account;
using LexVerse.Application.Commerce;
using LexVerse.Application.Product;
using LexVerse.Core.Product;
using LexVerse.Infrastructure.Commerce;
using Xunit;

namespace LexVerse.Translation.Tests;

public sealed class CommerceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Coordinator_LoadsPublicCatalogWhileSignedOutAndRequiresSignInForCheckout()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var catalog = CreateCatalog();
        var accounts = new StubAccounts(session: null);
        var backend = new StubCommerceBackend();
        var launcher = new RecordingLauncher();
        var access = new RecordingAccessService();
        using var coordinator = new CommerceCoordinator(
            accounts, new FixedCatalogProvider(catalog), backend, launcher, access);

        await coordinator.InitializeAsync(cancellationToken);

        Assert.Equal(CommerceStatus.SignedOut, coordinator.State.Status);
        Assert.Same(catalog, coordinator.State.Catalog);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.StartCheckoutAsync("pro-monthly", cancellationToken));
        Assert.Equal(0, backend.CheckoutCount);
    }

    [Fact]
    public async Task Coordinator_CheckoutUsesKnownOfferAndSignOutInvalidatesPaidAccess()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var accounts = new StubAccounts(new AccountSession("customer-1", "Reader", Now));
        var backend = new StubCommerceBackend();
        var launcher = new RecordingLauncher();
        var access = new RecordingAccessService();
        using var coordinator = new CommerceCoordinator(
            accounts, new FixedCatalogProvider(CreateCatalog()), backend, launcher, access);
        await coordinator.InitializeAsync(cancellationToken);

        await coordinator.StartCheckoutAsync("pro-monthly", cancellationToken);
        await coordinator.SignOutAsync(cancellationToken);

        Assert.Equal("pro-monthly", backend.LastOfferId);
        Assert.Equal(new Uri("https://checkout.example.test/session"), launcher.LastUri);
        Assert.Equal(1, access.InvalidateCount);
        Assert.Equal(CommerceStatus.SignedOut, coordinator.State.Status);
    }

    [Fact]
    public async Task RemoteCatalog_MapsRemotePlansPricesAndPeriods()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        const string json = """
            {
              "revision":"catalog-42",
              "publishedAtUtc":"2026-07-20T12:00:00Z",
              "plans":[{
                "id":"pro","displayName":"LexVerse Pro",
                "features":["translation.region","translation.full-screen"],"isPublic":true
              }],
              "offers":[{
                "id":"pro-monthly","planId":"pro","amount":99000,"currencyCode":"VND",
                "billingPeriod":{"count":1,"unit":"Month"},
                "trialPeriod":{"count":7,"unit":"Day"},"isPublic":true
              }]
            }
            """;
        using var client = new HttpClient(new StaticHandler(json));
        var provider = new RemoteProductCatalogProvider(
            client, new Uri("https://api.example.test/v1/catalog"));

        var catalog = await provider.GetCatalogAsync(cancellationToken);

        var offer = Assert.Single(catalog.Offers);
        Assert.Equal(99_000m, offer.Price.Amount);
        Assert.Equal("VND", offer.Price.CurrencyCode);
        Assert.Equal(SubscriptionPeriodUnit.Month, offer.BillingPeriod?.Unit);
        Assert.Equal(2, catalog.FindPlan("pro")?.Features.Count);
    }

    [Fact]
    public async Task OAuthPkce_UsesS256LoopbackAndPersistsOnlyDpapiCiphertext()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = Path.Combine(Path.GetTempPath(), $"lexverse-oauth-{Guid.NewGuid():N}");
        var storePath = Path.Combine(directory, "session.dat");
        var handler = new OAuthHandler();
        using var client = new HttpClient(handler);
        var launcher = new CallbackLauncher();
        using var provider = new OAuthPkceAccountSessionProvider(
            client,
            new OAuthNativeAppOptions(
                new Uri("https://identity.example.test/authorize"),
                new Uri("https://identity.example.test/token"),
                new Uri("https://api.example.test/v1/account"),
                "lexverse-desktop",
                ["openid", "offline_access", "lexverse.api"]),
            new WindowsDpapiAccountStore(storePath),
            launcher,
            new FixedTimeProvider(Now));

        var session = await provider.SignInAsync(cancellationToken);

        Assert.Equal("customer-1", session.SubjectId);
        Assert.Equal("S256", launcher.Parameters["code_challenge_method"]);
        Assert.Equal(launcher.Parameters["code_challenge"], PkceChallenge(handler.CodeVerifier));
        Assert.DoesNotContain("client_secret", handler.TokenForm.Keys);
        Assert.Equal("authorization_code", handler.TokenForm["grant_type"]);
        var storedBytes = await File.ReadAllBytesAsync(storePath, cancellationToken);
        Assert.DoesNotContain("access-token-secret", Encoding.UTF8.GetString(storedBytes));
        Assert.Equal("access-token-secret", await provider.GetAccessTokenAsync(cancellationToken));

        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public async Task RemoteCommerce_SendsBearerAndIdempotencyButNotClientPrice()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var handler = new CommerceHandler();
        using var client = new HttpClient(handler);
        var backend = new RemoteCommerceBackend(
            client,
            new Uri("https://api.example.test/v1/checkout"),
            new Uri("https://api.example.test/v1/restore"),
            new StubAccounts(new AccountSession("customer-1", "Reader", Now)));

        var checkout = await backend.CreateCheckoutAsync("pro-monthly", cancellationToken);
        await backend.RestorePurchasesAsync(cancellationToken);

        Assert.Equal(new Uri("https://checkout.example.test/session"), checkout.CheckoutUri);
        Assert.Equal("Bearer", handler.CheckoutAuthorization?.Scheme);
        Assert.Equal("token", handler.CheckoutAuthorization?.Parameter);
        Assert.False(string.IsNullOrWhiteSpace(handler.IdempotencyKey));
        Assert.Contains("\"offerId\":\"pro-monthly\"", handler.CheckoutBody, StringComparison.Ordinal);
        Assert.DoesNotContain("amount", handler.CheckoutBody, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task OAuthPkce_DisposeCancelsPendingBrowserSignIn()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = Path.Combine(Path.GetTempPath(), $"lexverse-oauth-{Guid.NewGuid():N}");
        var launcher = new NoCallbackLauncher();
        using var client = new HttpClient(new StaticHandler("{}"));
        var provider = new OAuthPkceAccountSessionProvider(
            client,
            new OAuthNativeAppOptions(
                new Uri("https://identity.example.test/authorize"),
                new Uri("https://identity.example.test/token"),
                new Uri("https://api.example.test/v1/account"),
                "lexverse-desktop",
                ["openid"]),
            new WindowsDpapiAccountStore(Path.Combine(directory, "session.dat")),
            launcher);
        var pending = provider.SignInAsync(cancellationToken);
        await launcher.Opened.Task.WaitAsync(cancellationToken);

        provider.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string PkceChallenge(string verifier) => Base64Url(
        SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static ProductCatalog CreateCatalog()
    {
        var plan = new ProductPlan(
            "pro", "LexVerse Pro", [ProductFeatures.RegionTranslation, ProductFeatures.FullScreenTranslation]);
        var offer = new ProductOffer(
            "pro-monthly", plan.Id, new Money(99_000m, "VND"),
            new SubscriptionPeriod(1, SubscriptionPeriodUnit.Month));
        return new ProductCatalog("catalog-1", Now, [plan], [offer]);
    }

    private sealed class StubAccounts(AccountSession? session) : IAccountSessionProvider
    {
        public bool IsConfigured => true;
        public Task<AccountSession?> GetSessionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(session);
        public Task<AccountSession> SignInAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(session ?? new AccountSession("customer-1", "Reader", Now));
        public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult("token");
    }

    private sealed class StubCommerceBackend : ICommerceBackend
    {
        public bool IsConfigured => true;
        public int CheckoutCount { get; private set; }
        public string? LastOfferId { get; private set; }
        public Task<CheckoutSession> CreateCheckoutAsync(string offerId, CancellationToken cancellationToken = default)
        {
            CheckoutCount++;
            LastOfferId = offerId;
            return Task.FromResult(new CheckoutSession(
                new Uri("https://checkout.example.test/session"), Now.AddMinutes(10)));
        }
        public Task RestorePurchasesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingLauncher : IExternalUriLauncher
    {
        public Uri? LastUri { get; private set; }
        public void Open(Uri uri) => LastUri = uri;
    }

    private sealed class RecordingAccessService : IFeatureAccessService
    {
        public int InvalidateCount { get; private set; }
        public ValueTask<FeatureAccessDecision> GetAccessAsync(
            FeatureKey feature, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(FeatureAccessDecision.Denied(feature, FeatureAccessDenialReason.NotEntitled));
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Invalidate() => InvalidateCount++;
    }

    private sealed class FixedCatalogProvider(ProductCatalog catalog) : IProductCatalogProvider
    {
        public Task<ProductCatalog> GetCatalogAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(catalog);
    }

    private sealed class StaticHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
    }

    private sealed class CallbackLauncher : IExternalUriLauncher
    {
        public Dictionary<string, string> Parameters { get; private set; } = [];

        public void Open(Uri uri)
        {
            Parameters = ParseQuery(uri.Query);
            var callback = new UriBuilder(Parameters["redirect_uri"])
            {
                Query = $"code=test-code&state={Uri.EscapeDataString(Parameters["state"])}"
            }.Uri;
            _ = Task.Run(async () =>
            {
                using var loopbackClient = new HttpClient();
                using var response = await loopbackClient.GetAsync(callback);
                _ = await response.Content.ReadAsStringAsync();
            });
        }
    }

    private sealed class NoCallbackLauncher : IExternalUriLauncher
    {
        public TaskCompletionSource Opened { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Open(Uri uri) => Opened.TrySetResult();
    }

    private sealed class OAuthHandler : HttpMessageHandler
    {
        public Dictionary<string, string> TokenForm { get; private set; } = [];
        public string CodeVerifier => TokenForm["code_verifier"];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath == "/token")
            {
                var form = await request.Content!.ReadAsStringAsync(cancellationToken);
                TokenForm = ParseQuery(form);
                return Json("""
                    {"access_token":"access-token-secret","refresh_token":"refresh-token-secret","token_type":"Bearer","expires_in":3600,"scope":"openid lexverse.api"}
                    """);
            }

            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("access-token-secret", request.Headers.Authorization?.Parameter);
            return Json("""{"subjectId":"customer-1","displayName":"Reader"}""");
        }

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class CommerceHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public System.Net.Http.Headers.AuthenticationHeaderValue? CheckoutAuthorization { get; private set; }
        public string? IdempotencyKey { get; private set; }
        public string CheckoutBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            if (request.RequestUri?.AbsolutePath == "/v1/checkout")
            {
                CheckoutAuthorization = request.Headers.Authorization;
                IdempotencyKey = request.Headers.GetValues("Idempotency-Key").Single();
                CheckoutBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                return Json("""
                    {"checkoutUri":"https://checkout.example.test/session","expiresAtUtc":"2026-07-20T12:10:00Z"}
                    """);
            }

            return Json("{}");
        }

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static Dictionary<string, string> ParseQuery(string query) =>
        query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(
                pair => Uri.UnescapeDataString(pair[0].Replace('+', ' ')),
                pair => Uri.UnescapeDataString(pair.Length > 1 ? pair[1].Replace('+', ' ') : string.Empty),
                StringComparer.Ordinal);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
