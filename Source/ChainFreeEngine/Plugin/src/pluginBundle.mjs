// Loads the plugin's OWN build (dist/index.js, the same file Decky loads) so the
// standalone host shows the real Playhub interface instead of a substitute.
//
// The build is an ES module that expects a loader: Steam-side globals
// (SP_REACT, SP_JSX, SP_REACTDOM, DFL), Decky's private loader-API global, and
// its assets served from the loader's HTTP port. Rather than publishing those
// globals — which would collide with a running Decky — the source is prepared
// once and evaluated with the values passed in as locals.

export const DECKY_LOADER_GLOBAL =
  '__DECKY_SECRET_INTERNALS_DO_NOT_USE_OR_YOU_WILL_BE_FIRED_deckyLoaderAPIInit';
const LOADER_PARAM = '__playhubStandaloneLoaderApi';
const DEFAULT_EXPORT = /export\s*\{\s*([A-Za-z0-9_$]+)\s+as\s+default\s*\}\s*;?/;
const ASSET_URL = /https?:\/\/127\.0\.0\.1:\d+\/plugins\/([A-Za-z0-9 ._-]+)\//g;
const MODULE_KEYWORD = /^[ \t]*(?:import|export)\b/m;

/**
 * Rewrite the built bundle into a function body.
 * @param {string} source the bundle text
 * @param {{assetBase: string}} options assetBase replaces the loader's asset URL, e.g. 'http://127.0.0.1:47993/plugin-assets/'
 */
export function prepareBundle(source, { assetBase }) {
  if (typeof source !== 'string' || source.length < 1024) throw new Error('The Playhub bundle is empty or truncated.');
  if (!assetBase || !/^https?:\/\/(127\.0\.0\.1|localhost)[:/]/.test(assetBase))
    throw new Error('The asset base must be a loopback URL.');
  const base = assetBase.endsWith('/') ? assetBase : assetBase + '/';
  let assets = 0;
  let code = source
    .replace(/^﻿/, '')
    .replace(/\/\/#\s*sourceMappingURL=.*$/gm, '')
    .replace(ASSET_URL, (_match, plugin) => { assets++; return base + plugin + '/'; });
  const exported = code.match(DEFAULT_EXPORT);
  if (!exported) throw new Error('The Playhub bundle has no default export to load.');
  code = code.replace(DEFAULT_EXPORT, `return ${exported[1]};`);
  const globalUses = code.split('window.' + DECKY_LOADER_GLOBAL).length - 1;
  code = code.split('window.' + DECKY_LOADER_GLOBAL).join(LOADER_PARAM);
  if (MODULE_KEYWORD.test(code)) throw new Error('The Playhub bundle still declares module imports or exports.');
  if (code.includes(DECKY_LOADER_GLOBAL)) throw new Error('The Playhub bundle still reads the Decky loader global.');
  return { code, assets, loaderReferences: globalUses, exportName: exported[1] };
}

/** The loader surface the build asks for: exactly what the plugin uses, nothing else. */
export function createLoaderApi({ call, routerHook, toaster, version = 2 }) {
  if (typeof call !== 'function') throw new Error('The standalone loader needs a backend call.');
  return {
    connect(requested, name) {
      if (typeof name !== 'string' || !name) throw new Error('A plugin name is required.');
      return { _version: Math.min(version, Number(requested) || version), call, routerHook, toaster };
    },
  };
}

/**
 * Evaluate the prepared bundle and return the plugin definition
 * ({name, titleView, content, icon, onDismount}).
 */
export function loadPluginBundle(source, { assetBase, globals, loaderApi, evaluate }) {
  const prepared = prepareBundle(source, { assetBase });
  const names = ['SP_REACT', 'SP_JSX', 'SP_REACTDOM', 'DFL'];
  const missing = names.filter((name) => !globals?.[name]);
  if (missing.length) throw new Error('Steam globals unavailable: ' + missing.join(', '));
  const factory = (evaluate || defaultEvaluate)(prepared.code, [...names, LOADER_PARAM]);
  const definePluginResult = factory(...names.map((name) => globals[name]), loaderApi);
  if (typeof definePluginResult !== 'function') throw new Error('The Playhub bundle did not return a plugin factory.');
  const plugin = definePluginResult();
  if (!plugin || typeof plugin !== 'object') throw new Error('The Playhub plugin did not start.');
  return { plugin, prepared };
}

function defaultEvaluate(code, parameters) {
  // eslint-disable-next-line no-new-func
  return new Function(...parameters, '"use strict";\n' + code);
}
