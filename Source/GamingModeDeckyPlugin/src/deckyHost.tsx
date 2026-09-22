import { DFL, SP_REACT as React } from "./decky";
import { createScopedDeckyState } from "./deckyHostState";
import {
  failDeckyHost, getDeckyHostSnapshot, mountDeckyHost, subscribeDeckyHost,
  type DeckyHostLease,
} from "./deckyHostRuntime";
export { initDeckyHost, setDeckyHostEnabled, setDeckyNativeHidden, getDeckyHostSnapshot, subscribeDeckyHost } from "./deckyHostRuntime";
export type { DeckyHostSnapshot } from "./deckyHostRuntime";

class HostBoundary extends React.Component<React.PropsWithChildren<{ onError(): void }>, { failed: boolean }> {
  state = { failed: false };
  static getDerivedStateFromError() { return { failed: true }; }
  componentDidCatch() { this.props.onError(); }
  render() { return this.state.failed ? null : this.props.children; }
}

function Commit({ onCommit }: { onCommit(): void }) {
  React.useLayoutEffect(onCommit, [onCommit]);
  return null;
}

/** Mount only in Playhub's standalone QAM entry, outside .ph-controls/setting CSS. */
export function DeckyHost({ active }: { active: boolean }) {
  const Focusable = (DFL as any).Focusable;
  const snapshot = React.useSyncExternalStore(subscribeDeckyHost, getDeckyHostSnapshot);
  const element = React.useRef<HTMLDivElement>(null);
  const [panel, setPanel] = React.useState<React.ReactNode>(null);
  const leaseRef = React.useRef<DeckyHostLease | null>(null);
  const activeRef = React.useRef(active);
  activeRef.current = active;
  const onCommit = React.useCallback(() => {
    leaseRef.current?.setActive(activeRef.current);
    leaseRef.current?.setReady();
  }, []);
  React.useEffect(() => {
    if (!snapshot.enabled || !snapshot.available || !element.current) return;
    const host = element.current;
    // Reject inherited settings selectors instead of trying to restyle native Decky.
    if (host.closest(".ph-controls, .ph-control-settings, .qsRedesign")) {
      failDeckyHost("settings_css_ancestor");
      return;
    }
    let alive = true;
    let scoped: ReturnType<typeof createScopedDeckyState> | undefined;
    let timer: number | undefined;
    const onError = () => { if (alive) failDeckyHost("native_root_error"); };
    try {
      const lease = mountDeckyHost({ element: host,
        createContent(root) {
          const nativeRoot = root as React.ReactElement<{ deckyState: any; children?: React.ReactNode }>;
          if (!React.isValidElement(root) || !nativeRoot.props?.deckyState || !nativeRoot.props.children) {
            throw new Error("unsupported_native_root");
          }
          scoped = createScopedDeckyState(nativeRoot.props.deckyState);
          scoped.setVisible(false);
          return <HostBoundary onError={onError}>
            {React.cloneElement(nativeRoot, { deckyState: scoped.state })}
            <Commit onCommit={onCommit} />
          </HostBoundary>;
        },
        isHealthy: () => alive && host.isConnected && !host.closest(".ph-controls, .ph-control-settings, .qsRedesign"),
        onVisibility: (visible) => scoped?.setVisible(visible),
        onRelease(reason) {
          if (alive && reason !== "host_unmounted" && reason !== "owner_changed") failDeckyHost(reason);
        },
      });
      if (!lease) { scoped?.dispose(); failDeckyHost("native_adapter_unavailable"); return; }
      leaseRef.current = lease;
      setPanel(lease.panel);
      timer = window.setInterval(() => { if (!lease.renew()) onError(); }, 500);
    } catch {
      scoped?.dispose();
      failDeckyHost("unsupported_native_root");
    }
    return () => {
      alive = false;
      if (timer !== undefined) window.clearInterval(timer);
      leaseRef.current?.release();
      leaseRef.current = null;
      scoped?.dispose();
      setPanel(null);
    };
  }, [snapshot.enabled, snapshot.available, snapshot.ownerRevision, onCommit]);
  React.useLayoutEffect(() => { leaseRef.current?.setActive(active); }, [active]);
  return <Focusable ref={element} data-playhub-decky-host="true" aria-hidden={!active}
    {...(!active ? { inert: "" } : {})} focusable={false} childFocusDisabled={!active}
    style={{ width: "100%", minWidth: 0, display: active ? undefined : "none" }}>
    {snapshot.enabled && snapshot.available ? panel : null}
  </Focusable>;
}
