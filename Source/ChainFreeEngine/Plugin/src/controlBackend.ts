// The isolated host exposes the original preference RPC contract only.
export async function ensureControlBackend(): Promise<void> {
  await (window as any).__PLAYHUB_HOST__.call('get_panel_preferences');
}
export async function call<Args extends unknown[] = unknown[], Result = unknown>(method: string, ...args: Args): Promise<Result> {
  return (window as any).__PLAYHUB_HOST__.call(method, ...args);
}
