using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Hl7.Fhir.Model;

namespace GenericPopulation;

/// <summary>Tester hele HTTP-fristen og kansellering med strømmede, syntetiske svar uten nettverk.</summary>
internal static class FhirTransportSelfTests
{
    public static IEnumerable<(string, Func<SysTask>)> Cases()
    {
        foreach (var postSearch in new[] { false, true })
        {
            var name = postSearch ? "FHIR POST search" : "FHIR GET";
            yield return ($"{name}: frist avbryter forsinkede headere", () => Deadline(postSearch, true));
            yield return ($"{name}: frist avbryter pågående strømming uten Content-Length", () => Deadline(postSearch, false));
            yield return ($"{name}: operasjonskansellering avbryter pågående strømming", () => CallerCancellation(postSearch));
            yield return ($"{name}: komplett strømmet svar leses og frigjøres", () => Success(postSearch));
        }
    }

    private static async SysTask Deadline(bool postSearch, bool delayHeaders)
    {
        using var handler = new StreamingHandler(postSearch, delayHeaders, stallBody: true);
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(300) };
        using var caller = new CancellationTokenSource();
        await ExpectCancellation(Read(postSearch, client, caller.Token));
        Check(!caller.IsCancellationRequested, "call deadline must not cancel the enclosing operation");
        Check(handler.Calls == 1, "one request only");
        if (!delayHeaders)
            Check(handler.Body is { ReadStarted: true, Disposed: true }, "body read started and response stream disposed");
    }

    private static async SysTask CallerCancellation(bool postSearch)
    {
        using var handler = new StreamingHandler(postSearch, delayHeaders: false, stallBody: true);
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var caller = new CancellationTokenSource();
        var reading = Read(postSearch, client, caller.Token);
        await handler.BodyReading.Task.WaitAsync(TimeSpan.FromSeconds(3));
        caller.Cancel();
        await ExpectCancellation(reading);
        Check(handler.Body is { Disposed: true }, "caller cancellation disposes the body stream");
    }

    private static async SysTask Success(bool postSearch)
    {
        using var handler = new StreamingHandler(postSearch, delayHeaders: false, stallBody: false);
        using var client = new HttpClient(handler);
        var patient = await Read(postSearch, client, default);
        Check(patient.Id == "synthetic-patient", "expected patient returned");
        Check(handler.Body is { Disposed: true }, "successful response stream disposed");
    }

    private static Task<Patient> Read(bool postSearch, HttpClient client, CancellationToken ct) => postSearch
        ? new HttpFhirDataSource(client, new FhirSourceOptions
            {
                Id = "stream-test", BaseUrl = "http://127.0.0.1/fhir/", SearchMethod = "POST",
                PatientLookup = new() { Interaction = "search" }
            }).ReadPatientAsync("synthetic-identifier", ct)
        : new HttpFhirDataSource(client, new Uri("http://127.0.0.1/fhir/"), new LocalDemoAuthorizer())
            .ReadPatientAsync("synthetic-patient", ct);

    private static async SysTask ExpectCancellation(SysTask action)
    {
        try { await action.WaitAsync(TimeSpan.FromSeconds(3)); }
        catch (OperationCanceledException) { return; }
        throw new InvalidOperationException("Expected cancellation before a complete response");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class StreamingHandler(bool postSearch, bool delayHeaders, bool stallBody) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public ResponseStream? Body { get; private set; }
        public TaskCompletionSource BodyReading { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Check(request.Method == (postSearch ? HttpMethod.Post : HttpMethod.Get), "configured method");
            if (delayHeaders) await SysTask.Delay(Timeout.InfiniteTimeSpan, ct);
            const string patient = "{\"resourceType\":\"Patient\",\"id\":\"synthetic-patient\"}";
            var json = postSearch ? "{\"resourceType\":\"Bundle\",\"type\":\"searchset\",\"total\":1,\"entry\":[{\"resource\":" + patient + "}]}" : patient;
            Body = new ResponseStream(Encoding.UTF8.GetBytes(json), stallBody, BodyReading);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(Body) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/fhir+json");
            Check(response.Content.Headers.ContentLength is null, "streamed response has no Content-Length");
            return response;
        }
    }

    // Første byte leveres straks; neste lesing stanser inntil fristen eller operasjonen kanselleres.
    private sealed class ResponseStream(byte[] body, bool stall, TaskCompletionSource reading) : Stream
    {
        private int position;
        public bool ReadStarted { get; private set; }
        public bool Disposed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => position; set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            ReadStarted = true;
            if (stall && position > 0)
            {
                reading.TrySetResult();
                await SysTask.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            var length = Math.Min(buffer.Length, Math.Min(body.Length - position, 1));
            body.AsMemory(position, length).CopyTo(buffer);
            position += length;
            return length;
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
