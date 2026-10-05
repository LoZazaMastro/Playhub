// Standalone adapter. Steam's own React and the verified UI components are
// borrowed; Decky is never initialized and no loader global is read.
import { DFL } from './steamUi';

const host = (window as any).__PLAYHUB_HOST__;
if (!host?.React) throw new Error('Playhub standalone UI is not ready.');
export const SP_REACT = host.React;
export const SP_JSX = host.jsx;
export { DFL };
export const routerHook = host.routerHook;
export const toaster = host.toaster;
export const definePlugin = (factory: unknown) => factory;
