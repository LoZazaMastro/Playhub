using System.ComponentModel;
using System.IO;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Playhub.Shared;

namespace GamingMode.Services;

internal enum EpicProtocolDispatchState { Submitted, Pending }

// This broker runs in the existing login agent, outside the Steam-tracked
// helper. It asks Windows to invoke Epic's normal registered URI handler.
internal sealed class EpicProtocolLaunchBroker
{
    private readonly Func<IPAddress?, string, bool> authorized;
    private readonly Func<EpicProtocolLaunchRequest, EpicInstalledLaunch?> resolve;
    private readonly Func<bool> normalUser;
    private readonly Func<string, EpicProtocolDispatchState> dispatch;
    private readonly Func<long> clock;
    private readonly object gate = new();
    private readonly Dictionary<string, (long At, EpicProtocolLaunchRequest Request, EpicProtocolLaunchResult Result)> requests = new();

    internal EpicProtocolLaunchBroker(Func<IPAddress?, string, bool> authorized,
        Func<EpicProtocolLaunchRequest, EpicInstalledLaunch?> resolve, Func<bool> normalUser,
        Func<string, EpicProtocolDispatchState> dispatch, Func<long> clock)
    { this.authorized = authorized; this.resolve = resolve; this.normalUser = normalUser; this.dispatch = dispatch; this.clock = clock; }

    internal EpicProtocolLaunchResult Submit(IPAddress? address, string token, EpicProtocolLaunchRequest? request)
    {
        if (!authorized(address, token)) return new(false, "unauthorized");
        if (request?.Valid != true) return new(false, "invalid_request");
        lock (gate)
        {
            long now = clock();
            foreach (string old in requests.Where(pair => now < pair.Value.At || now - pair.Value.At > 120000).Select(pair => pair.Key).ToArray()) requests.Remove(old);
            if (requests.TryGetValue(request.RequestId, out var prior))
                return prior.Request == request ? prior.Result : new(false, "request_changed");
            if (requests.Count >= 64) return new(false, "busy");
            EpicProtocolLaunchResult result;
            try
            {
                if (!normalUser()) result = new(false, "user_session_unavailable");
                else if (resolve(request) is not { } plan || !request.Matches(plan)) result = new(false, "metadata_changed");
                else
                {
                    // Re-resolve immediately before the OS request. The server
                    // supplies the URI; client fields cannot change the handler.
                    if (resolve(request) is not { } current || current != plan || !normalUser()) result = new(false, "metadata_changed");
                    else
                    {
                        var state = dispatch(current.LaunchUri);
                        // Accepted/pending is not a game-start proof. The
                        // wrapper still waits for its real payload lifetime.
                        result = new(true, state == EpicProtocolDispatchState.Submitted ? "submitted" : "pending");
                    }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
                or InvalidOperationException or Win32Exception or JsonException or System.Security.SecurityException
                or COMException)
            { result = new(false, "dispatch_failed"); }
            // Cache both success and uncertain failure: same-request retries
            // cannot launch Epic twice after a lost or unsuccessful HTTP reply.
            requests.Add(request.RequestId, (now, request, result));
            return result;
        }
    }

    internal static void Map(WebApplication app, XboxShellBroker auth, EpicProtocolLaunchBroker? supplied = null)
    {
        var broker = supplied ?? new EpicProtocolLaunchBroker(auth.IsAuthorized,
            request => EpicLaunchCatalog.ResolveInstalled(request.Namespace, request.CatalogItemId, request.AppName, request.GameExecutable),
            WindowsEpicProtocolDispatcher.NormalUser, WindowsEpicProtocolDispatcher.Dispatch, () => Environment.TickCount64);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        app.MapPost("/session/epic/launch", async (HttpContext context) =>
        {
            string token = context.Request.Headers["X-Playhub-Shell-Token"].ToString();
            if (!auth.IsAuthorized(context.Connection.RemoteIpAddress, token)) return Results.StatusCode(401);
            if (context.Request.ContentLength is not > 0 or > 40000) return Results.BadRequest(new { ok = false, code = "invalid_request" });
            try
            {
                var request = await context.Request.ReadFromJsonAsync<EpicProtocolLaunchRequest>(json, context.RequestAborted);
                return Results.Json(broker.Submit(context.Connection.RemoteIpAddress, token, request));
            }
            catch (Exception error) when (error is JsonException or BadHttpRequestException or OperationCanceledException or InvalidOperationException)
            { return Results.BadRequest(new { ok = false, code = "invalid_request" }); }
        });
    }
}

internal static class WindowsEpicProtocolDispatcher
{
    private static readonly EpicProtocolDispatchWorker Worker = new();

    internal static bool NormalUser()
    {
        if (!OperatingSystem.IsWindows() || Process.GetCurrentProcess().SessionId == 0) return false;
        using var identity = WindowsIdentity.GetCurrent();
        if (identity.User is null || identity.IsSystem || identity.IsAnonymous) return false;
        return GetTokenInformation(identity.AccessToken.DangerousGetHandle(), 20, out int elevated, sizeof(int), out int length)
            && length == sizeof(int) && elevated == 0;
    }

    internal static EpicProtocolDispatchState Dispatch(string uri)
    {
        Exception? failed = null;
        Thread? thread = null;
        if (!Worker.TryStart(() =>
        {
            bool initialized = false;
            try
            {
                Marshal.ThrowExceptionForHR(CoInitializeEx(0, 2 | 4)); // STA + disable OLE1 DDE.
                initialized = true;
                if (!NormalUser()) throw new InvalidOperationException("Normal interactive user session required.");
                var info = new ShellExecuteInfo { Size = Marshal.SizeOf<ShellExecuteInfo>(), Mask = 0x100 | 0x400,
                    Verb = "open", File = uri, Show = 1 };
                // SEE_MASK_NOASYNC: this STA does not outlive the request.
                // No NOCLOSEPROCESS: returned launch handles are not ownership.
                if (!ShellExecuteEx(ref info)) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            catch (Exception error) { failed = error; }
            finally { if (initialized) CoUninitialize(); }
        }, action =>
        {
            thread = new Thread(() => action()) { IsBackground = true, Name = "PlayhubEpicProtocol" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        })) throw new InvalidOperationException("An Epic registered-handler request is already in progress.");
        // A slow Windows handler may remain in progress. Keep that one OS call
        // and let the wrapper observe startup, rather than repeat or substitute
        // it after a client timeout. The agent's request thread stays bounded.
        if (!thread!.Join(8000)) return EpicProtocolDispatchState.Pending;
        if (failed is not null) throw new InvalidOperationException("Epic registered handler failed.", failed);
        return EpicProtocolDispatchState.Submitted;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        internal int Size; internal uint Mask; internal nint Window;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? Verb;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? File;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? Parameters;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? Directory;
        internal int Show; internal nint Instance; internal nint IdList;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? Class;
        internal nint ClassKey; internal uint HotKey; internal nint Icon; internal nint Process;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShellExecuteEx(ref ShellExecuteInfo info);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetTokenInformation(nint token, int type, out int information, int size, out int returned);
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
}

// Held until the actual OS worker finishes, including after an HTTP caller
// receives Pending. Other requests cannot create a second in-flight worker.
internal sealed class EpicProtocolDispatchWorker
{
    private int busy;
    internal bool TryStart(Action work, Action<Action> start)
    {
        if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) return false;
        try
        {
            start(() => { try { work(); } finally { Interlocked.Exchange(ref busy, 0); } });
            return true;
        }
        catch { Interlocked.Exchange(ref busy, 0); throw; }
    }
}
