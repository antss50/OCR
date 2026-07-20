using System.Net.Http.Headers;
using LexVerse.Core.Product;

namespace LexVerse.Infrastructure.Product;

public sealed class RemoteSignedEntitlementProvider : IEntitlementProvider
{
    public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(8);

    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;
    private readonly string? _subjectId;
    private readonly IEntitlementRequestContextProvider? _contextProvider;
    private readonly EntitlementEnvelopeVerifier _verifier;
    private readonly SignedEntitlementFileCache _cache;
    private readonly TimeSpan _requestTimeout;

    public RemoteSignedEntitlementProvider(
        HttpClient httpClient,
        Uri endpoint,
        string subjectId,
        EntitlementEnvelopeVerifier verifier,
        SignedEntitlementFileCache cache,
        TimeSpan? requestTimeout = null,
        bool allowInsecureLocalhost = false)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        ArgumentNullException.ThrowIfNull(verifier);
        ArgumentNullException.ThrowIfNull(cache);
        if (!string.IsNullOrEmpty(endpoint.UserInfo) ||
            (endpoint.Scheme != Uri.UriSchemeHttps &&
            !(allowInsecureLocalhost && endpoint.Scheme == Uri.UriSchemeHttp && endpoint.IsLoopback))
            )
        {
            throw new ArgumentException("The entitlement endpoint must use HTTPS.", nameof(endpoint));
        }

        _httpClient = httpClient;
        _endpoint = endpoint;
        _subjectId = subjectId.Trim();
        _verifier = verifier;
        _cache = cache;
        _requestTimeout = requestTimeout ?? DefaultRequestTimeout;
        if (_requestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        }
    }

    public RemoteSignedEntitlementProvider(
        HttpClient httpClient,
        Uri endpoint,
        IEntitlementRequestContextProvider contextProvider,
        EntitlementEnvelopeVerifier verifier,
        SignedEntitlementFileCache cache,
        TimeSpan? requestTimeout = null)
        : this(
            httpClient,
            endpoint,
            subjectId: "dynamic-subject",
            verifier,
            cache,
            requestTimeout)
    {
        ArgumentNullException.ThrowIfNull(contextProvider);
        _subjectId = null;
        _contextProvider = contextProvider;
    }

    public async Task<EntitlementSnapshot> GetEntitlementsAsync(
        CancellationToken cancellationToken = default)
    {
        var context = _contextProvider is null
            ? new EntitlementRequestContext(_subjectId!, AccessToken: null)
            : await _contextProvider.GetContextAsync(cancellationToken);
        Exception? remoteFailure = null;
        try
        {
            var envelope = await FetchEnvelopeAsync(context.AccessToken, cancellationToken);
            var snapshot = _verifier.Verify(envelope, context.SubjectId);
            try
            {
                await _cache.WriteAsync(envelope, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A verified online response remains usable even if offline persistence is unavailable.
            }

            return snapshot;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            remoteFailure = exception;
        }

        try
        {
            var cachedEnvelope = await _cache.ReadAsync(cancellationToken);
            if (cachedEnvelope is not null)
            {
                return _verifier.Verify(cachedEnvelope, context.SubjectId);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // The caller only receives a generic failure; cached claims are never trusted on error.
        }

        throw remoteFailure is null
            ? new EntitlementProviderUnavailableException()
            : new EntitlementProviderUnavailableException(remoteFailure);
    }

    private async Task<byte[]> FetchEnvelopeAsync(string? accessToken, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_requestTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, _endpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is > EntitlementEnvelopeVerifier.MaximumEnvelopeBytes)
        {
            throw new EntitlementVerificationException("The remote entitlement envelope is too large.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, timeout.Token);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > EntitlementEnvelopeVerifier.MaximumEnvelopeBytes)
            {
                throw new EntitlementVerificationException("The remote entitlement envelope is too large.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
