using System.IO.Compression;
using System.Net;
using System.Reflection;
using Playhub.Services;

int passed = 0;
var service = new DeckyInstallerService();
var httpField = typeof(DeckyInstallerService).GetField("_http", BindingFlags.Instance | BindingFlags.NonPublic)!;
((HttpClient)httpField.GetValue(service)!).Dispose();
async Task Expect(string method, object[] args, Type exception, string message)
{
    try
    {
        await (Task<string>)typeof(DeckyInstallerService).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, args)!;
        throw new Exception("Installer returned success for " + method);
    }
    catch (Exception error) when (error.GetType() == exception && error.Message == message)
    { passed++; Console.WriteLine("PASS " + method + " routes " + exception.Name + " to repair"); }
}

// Only invalid inputs are submitted: every call must fail before extraction/start.
await Expect("InstallFromBytesAsync", [new byte[100], "fixture", false], typeof(InvalidDataException), "Il download di Decky sembra incompleto. Riprova.");
await Expect("InstallFromBytesAsync", [new byte[600_000], "fixture", false], typeof(InvalidDataException), "Il download di Decky sembra incompleto. Riprova.");
foreach (string entry in new[] { "missing-loader.bin", "../outside.bin" })
{
    using var buffer = new MemoryStream();
    using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
    using (var payload = zip.CreateEntry(entry, CompressionLevel.NoCompression).Open())
        payload.Write(new byte[600_000]);
    await Expect("InstallFromBytesAsync", [buffer.ToArray(), "fixture", false], typeof(InvalidDataException), "Il download di Decky sembra incompleto. Riprova.");
}
foreach (bool networkError in new[] { false, true })
{
    using var client = new HttpClient(new FailedDownload(networkError));
    httpField.SetValue(service, client);
    await Expect("InstallFromUrlAsync", ["https://fixture.invalid/decky.zip", "fixture", false], typeof(InvalidOperationException), "Non riesco a scaricare Decky. Controlla la connessione e riprova.");
}
Console.WriteLine($"Actual Decky installer repair checks: {passed} passed. No successful install, restart, extraction or input executed.");

sealed class FailedDownload(bool failTransport) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        failTransport ? Task.FromException<HttpResponseMessage>(new HttpRequestException("Isolated transport failure.")) :
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
}
