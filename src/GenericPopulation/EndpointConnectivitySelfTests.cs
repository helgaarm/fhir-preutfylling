using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace GenericPopulation;

internal static class EndpointConnectivitySelfTests
{
    private const string Metadata = """{"resourceType":"CapabilityStatement","fhirVersion":"4.0.1"}""";
    private static FhirSourceOptions Source(string url = "https://example.org/fhir/") => new()
    { Id = "test", Name = "Test", BaseUrl = url, BearerTokenEnvironmentVariable = "TOKEN_MUST_NOT_BE_READ" };

    public static IEnumerable<(string, Func<SysTask>)> Cases() =>
    [
        ("Nettverk: sperreproxy avvises før oppstart med eksterne kilder", () =>
        {
            foreach (var address in new[] { "http://127.0.0.1:9", "http://localhost:9", "http://[::1]:9" })
            {
                var proxy = new WebProxy(address);
                var rejected = false;
                try { NetworkStartupGuard.Validate([Source()], proxy); }
                catch (InvalidOperationException error) { rejected = error.Message == NetworkStartupGuard.BlockedProxyMessage; }
                Check(rejected && proxy.Address == new Uri(address), "blocked without modifying proxy");
            }
            return SysTask.CompletedTask;
        }),
        ("Nettverk: vanlige proxyer, bypass og rene lokale testmiljøer beholdes", () =>
        {
            NetworkStartupGuard.Validate([Source()], new WebProxy("http://proxy.example:8080"));
            NetworkStartupGuard.Validate([Source()], new WebProxy());
            NetworkStartupGuard.Validate([Source()], new WebProxy("http://127.0.0.1:9", false, ["example\\.org"]));
            NetworkStartupGuard.Validate([Source("http://127.0.0.1:5077/demo/fhir/")], new WebProxy("http://127.0.0.1:9"));
            return SysTask.CompletedTask;
        }),
        ("Endepunktstatus: én anonym GET av metadata uten pasient eller nye forsøk", async () =>
        {
            var calls = 0;
            var body = new MetadataStream(Encoding.UTF8.GetBytes(Metadata));
            using var client = new HttpClient(new Handler((request, _) =>
            {
                calls++;
                Check(request.Method == HttpMethod.Get && request.RequestUri == new Uri("https://example.org/fhir/metadata"), "metadata only");
                Check(request.Content is null && request.Headers.Authorization is null && request.Headers.Count() == 1 &&
                    request.Headers.Accept.Single().MediaType == "application/fhir+json", "anonymous request");
                return Task.FromResult(Response(new StreamContent(body)));
            }));
            var result = await new EndpointConnectivity(client).CheckAsync(Source(), default);
            Check(result.State == "ok" && result.HttpStatus == 200 && calls == 1 && body.Disposed, "success and disposal");
            Check(result.CheckedAt <= DateTimeOffset.UtcNow && result.CheckedAt > DateTimeOffset.UtcNow.AddMinutes(-1), "fresh timestamp");
        }),
        ("Endepunktstatus: HTTP 200 alene bekrefter ikke FHIR R4-metadata", async () =>
        {
            foreach (var (json, mediaType, state, label) in new[] {
                (Metadata, "application/json", "ok", "FHIR svarer"),
                ("<html>Login</html>", "text/html", "warning", "Uventet svar"),
                ("{", "application/fhir+json", "warning", "Ugyldig metadata"),
                ("[]", "application/fhir+json", "warning", "Uventet metadata"),
                ("{\"resourceType\":\"OperationOutcome\"}", "application/fhir+json", "warning", "Uventet metadata"),
                ("{\"resourceType\":\"CapabilityStatement\"}", "application/fhir+json", "warning", "FHIR-versjon ikke bekreftet"),
                (Metadata.Replace("4.0.1", "5.0.0"), "application/fhir+json", "warning", "FHIR-versjon ikke bekreftet") })
            {
                using var client = new HttpClient(new Handler((_, _) => Task.FromResult(
                    Response(new StringContent(json), mediaType))));
                var result = await new EndpointConnectivity(client).CheckAsync(Source(), default);
                Check(result.State == state && result.Label == label && result.HttpStatus == 200, label);
            }
        }),
        ("Endepunktstatus: store metadata avvises både med og uten Content-Length", async () =>
        {
            var declared = new UnreadContent();
            using (var client = new HttpClient(new Handler((_, _) => Task.FromResult(Response(declared)))))
            {
                var result = await new EndpointConnectivity(client).CheckAsync(Source(), default);
                Check(result.State == "warning" && result.Label == "Metadata for store" && declared.Disposed, "declared limit");
            }
            var streamed = new MetadataStream(new byte[512 * 1024]);
            using (var client = new HttpClient(new Handler((_, _) => Task.FromResult(Response(new StreamContent(streamed))))))
            {
                var result = await new EndpointConnectivity(client).CheckAsync(Source(), default);
                Check(result.State == "warning" && result.Label == "Metadata for store" && streamed.Disposed &&
                    streamed.BytesRead <= 256 * 1024 + 8192, "bounded streaming and disposal");
            }
        }),
        ("Endepunktstatus: autentisering, ukjent kontroll, redirect og serverfeil skilles", async () =>
        {
            foreach (var (status, state) in new[] { (401, "warning"), (403, "warning"), (404, "warning"),
                (405, "warning"), (301, "warning"), (429, "warning"), (500, "error"), (503, "error") })
            {
                var calls = 0;
                using var client = new HttpClient(new Handler((_, _) =>
                {
                    calls++;
                    return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status));
                }));
                var result = await new EndpointConnectivity(client).CheckAsync(Source(), default);
                Check(result.State == state && result.HttpStatus == status && calls == 1, $"HTTP {status}");
            }
        }),
        ("Endepunktstatus: DNS, TLS og proxyfeil viser aldri rå unntak", async () =>
        {
            foreach (var kind in new[] { HttpRequestError.NameResolutionError, HttpRequestError.SecureConnectionError,
                HttpRequestError.ProxyTunnelError, HttpRequestError.ConnectionError })
            {
                using var client = new HttpClient(new Handler((_, _) => throw new HttpRequestException(kind, "secret-token-and-patient")));
                var result = await new EndpointConnectivity(client).CheckAsync(Source(), default);
                Check(result.State == "error" && result.HttpStatus is null && !result.Detail.Contains("secret"), "safe failure");
            }
        }),
        ("Endepunktstatus: tidsfrist avbryter oppkobling", async () =>
        {
            using var client = new HttpClient(new Handler(async (_, ct) =>
            {
                await SysTask.Delay(Timeout.Infinite, ct);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }));
            var result = await new EndpointConnectivity(client, TimeSpan.FromMilliseconds(30)).CheckAsync(Source(), default);
            Check(result.State == "error" && result.Label == "Tidsavbrudd", "timeout");
        }),
        ("Endepunktstatus: tidsfrist gjelder også strømming av metadata", async () =>
        {
            var body = new MetadataStream(Encoding.UTF8.GetBytes(Metadata), stall: true);
            using var client = new HttpClient(new Handler((_, _) => Task.FromResult(Response(new StreamContent(body)))));
            var result = await new EndpointConnectivity(client, TimeSpan.FromMilliseconds(100)).CheckAsync(Source(), default);
            Check(result.State == "error" && result.Label == "Tidsavbrudd" && body.Disposed && body.BytesRead > 0,
                "deadline disposes incomplete metadata");
        }),
        ("Endepunktstatus: avbrutt brukerforespørsel blir ikke et gammelt resultat", async () =>
        {
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            using var client = new HttpClient(new Handler((_, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }));
            var aborted = false;
            try { await new EndpointConnectivity(client).CheckAsync(Source(), cancelled.Token); }
            catch (OperationCanceledException) { aborted = true; }
            Check(aborted, "caller cancellation propagated");
        })
    ];

    private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    private static HttpResponseMessage Response(HttpContent content, string mediaType = "application/fhir+json")
    {
        content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request, ct);
    }
    private sealed class UnreadContent : HttpContent
    {
        public bool Disposed { get; private set; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new InvalidOperationException("Body must not be read");
        protected override bool TryComputeLength(out long length) { length = 1_000_000_000; return true; }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
    private sealed class MetadataStream(byte[] bytes, bool stall = false) : MemoryStream(bytes)
    {
        public int BytesRead { get; private set; }
        public bool Disposed { get; private set; }
        public override bool CanSeek => false;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (stall && BytesRead > 0) await SysTask.Delay(Timeout.InfiniteTimeSpan, ct);
            var count = await base.ReadAsync(buffer[..Math.Min(buffer.Length, stall ? 1 : buffer.Length)], ct);
            BytesRead += count;
            return count;
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
