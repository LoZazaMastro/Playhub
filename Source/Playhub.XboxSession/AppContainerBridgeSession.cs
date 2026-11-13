// Playhub Xbox Session. Distributed under GPL-3.0-only.
using WSGM.PackagedLaunch;

internal enum AppContainerBridgeStage { Transfer, Publish, Load, Initialize, Ready }

// Owns the desktop side of the WSGM bridge handshake. The caller has already verified and
// retained the target process. Keep the returned session alive until that target exits,
// including on failure, because the target may have received part of the transport.
internal sealed class AppContainerBridgeSession : IDisposable
{
    private readonly OverlayBrokerTransport _broker;
    internal AppContainerBridgeStage Stage { get; private set; }
    internal (nint Mapping, nint Request, nint Response) TargetHandles { get; private set; }
    internal uint? InitializationResult { get; private set; }
    internal Exception? Failure { get; private set; }
    internal bool Uncertain { get; private set; }

    private AppContainerBridgeSession(OverlayBrokerTransport broker) => _broker = broker;

    internal static AppContainerBridgeSession Start(int pid, ulong gameId, string bridgePath,
        GameInjector injector, Func<nint, uint, nint> duplicate)
    {
        bridgePath = Path.GetFullPath(bridgePath);
        if (!Path.GetFileName(bridgePath).Equals(AppContainerProof.BridgeFileName, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The expected AppContainer bridge file is required.", nameof(bridgePath));
        using var image = File.OpenRead(bridgePath);
        AppContainerProof.VerifyBridge(image);
        var session = new AppContainerBridgeSession(new OverlayBrokerTransport(pid, gameId, duplicate));
        (nint Mapping, nint Request, nint Response) target = default;
        try
        {
            var handles = session._broker.Handles;
            target.Mapping = duplicate(handles.Mapping, 0x06);
            if (target.Mapping == 0) return session;
            target.Request = duplicate(handles.Request, 0x02);
            if (target.Request == 0) return session;
            target.Response = duplicate(handles.Response, 0x100000);
            if (target.Response == 0) return session;
            session.Stage = AppContainerBridgeStage.Publish;
            if (!injector.SetEnvironment(pid,
                [$"WSGM_UWP_BRIDGE_MAPPING={target.Mapping:X}", $"WSGM_UWP_BRIDGE_REQUEST={target.Request:X}", $"WSGM_UWP_BRIDGE_RESPONSE={target.Response:X}"]))
                return session;
            session.Stage = AppContainerBridgeStage.Load;
            if (!injector.Load(pid, bridgePath)) return session;
            session.Stage = AppContainerBridgeStage.Initialize;
            session.InitializationResult = injector.Call(pid, bridgePath, "InitializeBridge");
            if (session.InitializationResult == 0) session.Stage = AppContainerBridgeStage.Ready;
            return session;
        }
        catch (Exception failure)
        {
            session.Failure = failure;
            session.Uncertain = session.Stage != AppContainerBridgeStage.Transfer;
            return session;
        }
        finally
        {
            session.TargetHandles = target;
            session.Uncertain |= injector.Latched(pid);
        }
    }

    public void Dispose() => _broker.Dispose();
}
