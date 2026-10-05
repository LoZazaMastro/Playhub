import {
  CatalogPlugin,
  InstallArtifact,
  InstalledPlugin,
  compareVersions,
  findInstalledPlugin,
  isSafePluginName,
} from "./pluginStoreCatalog";

export enum DeckyInstallType {
  Install = 0,
  Reinstall = 1,
  Update = 2,
  Downgrade = 3,
  Overwrite = 4,
}

interface DeckyBackend {
  call<Args extends unknown[] = [], Result = void>(route: string, ...args: Args): Promise<Result>;
  addEventListener(event: string, listener: (...args: any[]) => unknown): unknown;
  removeEventListener(event: string, listener: (...args: any[]) => unknown): void;
}

interface DeckyLoader {
  deckyState?: {
    publicState?: () => {
      installedPlugins?: Array<{ name?: string; version?: string }>;
      disabledPlugins?: Array<{ name?: string; version?: string }>;
    };
    eventBus?: EventTarget;
  };
  uninstallPlugin?: (
    this: DeckyLoader,
    name: string,
    title: string,
    buttonText: string,
    description: string,
  ) => void;
  checkPluginUpdates?: () => Promise<unknown>;
}

declare global {
  interface Window {
    DeckyBackend?: DeckyBackend;
    DeckyPluginLoader?: DeckyLoader;
    deckyAuthToken?: string;
  }
}

export interface PluginOperationProgress {
  plugin: string;
  percentage: number;
  message: string;
  active: boolean;
}

let installQueue: Promise<void> = Promise.resolve();

function backend(): DeckyBackend {
  const value = window.DeckyBackend;
  if (!value?.call) throw new Error("Decky backend is not available.");
  return value;
}

export function readInstalledPlugins(): InstalledPlugin[] {
  const state = window.DeckyPluginLoader?.deckyState?.publicState?.();
  const disabled = new Set((state?.disabledPlugins ?? []).map((plugin) => String(plugin.name ?? "")));
  return (state?.installedPlugins ?? [])
    .map((plugin) => ({
      name: String(plugin.name ?? "").trim(),
      version: String(plugin.version ?? "").trim(),
      disabled: disabled.has(String(plugin.name ?? "")),
    }))
    .filter((plugin) => isSafePluginName(plugin.name));
}

export function subscribeInstalledPlugins(listener: () => void): () => void {
  const eventBus = window.DeckyPluginLoader?.deckyState?.eventBus;
  if (!eventBus) return () => {};
  eventBus.addEventListener("update", listener);
  return () => eventBus.removeEventListener("update", listener);
}

export function operationType(plugin: CatalogPlugin, installed: InstalledPlugin[]): DeckyInstallType {
  const current = findInstalledPlugin(plugin, installed);
  if (!current) return DeckyInstallType.Install;
  const comparison = compareVersions(plugin.version, current.version);
  if (comparison > 0) return DeckyInstallType.Update;
  if (comparison < 0) return DeckyInstallType.Downgrade;
  return DeckyInstallType.Reinstall;
}

export function requestDeckyInstall(
  plugin: CatalogPlugin,
  artifact: InstallArtifact,
  installed: InstalledPlugin[],
): Promise<void> {
  if (!isSafePluginName(plugin.name)) return Promise.reject(new Error("Invalid plugin name."));
  const request = {
    url: artifact.url,
    name: plugin.name,
    version: artifact.version || plugin.version || "dev",
    hash: artifact.hash,
    type: operationType({ ...plugin, version: artifact.version || plugin.version }, installed),
  };
  const operation = installQueue.then(async () => {
    await backend().call<[string, string, string, string, DeckyInstallType]>(
      "utilities/install_plugin",
      request.url,
      request.name,
      request.version,
      request.hash,
      request.type,
    );
  });
  installQueue = operation.catch(() => {});
  return operation;
}

export function requestDeckyUninstall(
  installedName: string,
  title: string,
  buttonText: string,
  description: string,
): void {
  const pluginName = installedName.trim();
  if (!isSafePluginName(pluginName)) throw new Error("Invalid plugin name.");
  const loader = window.DeckyPluginLoader;
  if (!loader || typeof loader.uninstallPlugin !== "function") {
    throw new Error("Decky's uninstall dialog is not available.");
  }

  // Decky's modal reads this.deckyState, so the loader must remain the method receiver.
  loader.uninstallPlugin(pluginName, title, buttonText, description);
}

export function subscribePluginProgress(listener: (progress: PluginOperationProgress) => void): () => void {
  const value = window.DeckyBackend;
  if (!value?.addEventListener) return () => {};
  const activePlugins = new Map<string, string>();
  const keyFor = (name: unknown) => String(name ?? "").trim().toLocaleLowerCase("en-US");
  const start = (name: string) => {
    const plugin = String(name ?? "").trim();
    const key = keyFor(plugin);
    if (!key) return;
    activePlugins.set(key, plugin);
    listener({ plugin, percentage: 0, message: "", active: true });
  };
  const info = (percentage: number, message?: string) => {
    if (activePlugins.size !== 1) return;
    const plugin = activePlugins.values().next().value as string | undefined;
    if (!plugin) return;
    listener({
      plugin,
      percentage: Math.max(0, Math.min(100, Number(percentage) || 0)),
      message: String(message ?? ""),
      active: true,
    });
  };
  const finish = (name: string) => {
    const suppliedName = String(name ?? "").trim();
    const key = keyFor(suppliedName);
    const plugin = key
      ? activePlugins.get(key)
      : activePlugins.size === 1 ? activePlugins.values().next().value as string | undefined : undefined;
    if (!plugin) return;
    activePlugins.delete(key || keyFor(plugin));
    listener({ plugin, percentage: 100, message: "", active: false });
    if (!activePlugins.size) {
      window.setTimeout(() => { void window.DeckyPluginLoader?.checkPluginUpdates?.(); }, 700);
    }
  };
  value.addEventListener("loader/plugin_download_start", start);
  value.addEventListener("loader/plugin_download_info", info);
  value.addEventListener("loader/plugin_download_finish", finish);
  return () => {
    value.removeEventListener("loader/plugin_download_start", start);
    value.removeEventListener("loader/plugin_download_info", info);
    value.removeEventListener("loader/plugin_download_finish", finish);
  };
}
