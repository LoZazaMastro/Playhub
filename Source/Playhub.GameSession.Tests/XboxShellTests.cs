using System.Net;
using GamingMode.Services;
using Playhub.GameSession;

internal static class XboxShellTests
{
    internal static void Run(string fixtureDirectory)
    {
        var directory = Path.Combine(fixtureDirectory, "shell-broker");
        var starts = 0;
        var present = false;
        var clock = 0L;
        var broker = new XboxShellBroker(directory, () => present, () => starts++, () => clock);
        var token = File.ReadAllText(Path.Combine(directory, "xbox-shell-token"));
        Check(!broker.RequestShell(IPAddress.Loopback, "invalid") && starts == 0,
            "Broker denies unauthenticated shell startup");
        Check(!broker.RequestShell(IPAddress.Parse("192.0.2.5"), token) && starts == 0,
            "Broker denies remote shell startup even with the valid token");
        Check(broker.RequestShell(IPAddress.Loopback, token) && starts == 1,
            "Authenticated shell startup executes at the broker instead of the Steam child");
        Check(broker.RequestShell(IPAddress.IPv6Loopback, token) && starts == 1,
            "Concurrent or repeated startup requests cannot spawn another Explorer during initialization");
        present = true;
        clock = 20000;
        Check(broker.RequestShell(IPAddress.Loopback, token) && starts == 1,
            "Existing desktop shell causes no process launch");

        File.WriteAllText(Path.Combine(directory, "config.json"), "{\"safety\":{\"apiPort\":49234}}");
        using var handler = new Handler();
        using var client = new HttpClient(handler);
        XboxShellBrokerClient.Ensure(directory, client);
        Check(handler.Method == HttpMethod.Post && handler.Url == "http://127.0.0.1:49234/xbox/shell/ensure" && handler.Token == token,
            "Helper sends the session token to the configured local broker and does not create Explorer");
        handler.Code = HttpStatusCode.Unauthorized;
        var denied = false;
        try { XboxShellBrokerClient.Ensure(directory, client); }
        catch (InvalidOperationException ex) { denied = ex.InnerException is HttpRequestException; }
        Check(denied, "Rejected broker request fails launch before activation");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        Console.WriteLine("PASS " + name);
    }

    private sealed class Handler : HttpMessageHandler
    {
        public HttpStatusCode Code = HttpStatusCode.OK;
        public string Url = "", Token = "";
        public HttpMethod? Method;
        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.AbsoluteUri;
            Method = request.Method;
            Token = request.Headers.GetValues("X-Playhub-Shell-Token").Single();
            return new HttpResponseMessage(Code) { Content = new StringContent("{\"ok\":true}") };
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Send(request, cancellationToken));
    }
}
