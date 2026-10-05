import { createRequire } from 'node:module';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
const root = path.dirname(fileURLToPath(import.meta.url));
const source = process.env.PLAYHUB_PLUGIN_SOURCE || path.resolve(root, '../../GamingModeDeckyPlugin');
const require = createRequire(path.join(source, 'package.json'));
const plugins = createRequire(require.resolve('@decky/rollup'));
const ts = require('typescript');
const commonjs = plugins('@rollup/plugin-commonjs');
const resolve = plugins('@rollup/plugin-node-resolve').nodeResolve;
const globals = plugins('rollup-plugin-external-globals');
const bootstrap = readFileSync(path.join(root, 'src/standalone-bootstrap.js'), 'utf8');
export default {
  input: path.join(root, 'src/standalone-entry.tsx'),
  external: ['react', 'react/jsx-runtime', 'react-dom'],
  plugins: [
    { name: 'isolated-source',
      resolveId(id, importer) {
        if (id.startsWith('react-icons/')) return require.resolve(id);
        // The verified UI adapter uses Steam's own resolver, @decky/ui.
        if (id === '@decky/ui' || id.startsWith('@decky/ui/')) return require.resolve(id);
        if (/\.(png|jpe?g|webp|svg|gif|avif)$/.test(id)) return '\0omitted-editorial-asset:' + id;
        if (id.startsWith('.') && importer && !importer.startsWith('\0')) {
          const base = path.resolve(path.dirname(importer), id);
          for (const extension of ['', '.ts', '.tsx', '.mjs', '.js', '/index.ts', '/index.tsx']) {
            try { readFileSync(base + extension); return base + extension; } catch {}
          }
        }
      },
      load(id) { if (id.startsWith('\0omitted-editorial-asset:')) return 'export default "OMITTED_EDITORIAL_ASSET";'; },
      transform(code, id) {
        if (/\.tsx?$/.test(id)) return { code: ts.transpileModule(code, {compilerOptions: {
          target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.ESNext,
          jsx: ts.JsxEmit.ReactJSX, esModuleInterop: true,
        }}).outputText, map: null };
      },
    },
    commonjs(), resolve({browser:true}),
    globals({react:'window.__PLAYHUB_HOST__.React', 'react/jsx-runtime':'window.__PLAYHUB_HOST__.jsx', 'react-dom':'window.__PLAYHUB_HOST__.ReactDOM'}),
    { name: 'no-omitted-assets-in-output', generateBundle(_, bundle) {
      for (const item of Object.values(bundle)) if (item.type === 'chunk' && item.code.includes('OMITTED_EDITORIAL_ASSET')) throw new Error('Settings build unexpectedly retained editorial images.');
    }},
  ],
  treeshake: {moduleSideEffects:false},
  output: {
    file: path.join(root,'dist/playhub-standalone.js'), format:'iife', name:'PlayhubStandaloneSettings',
    banner: '(async function(){\n' + bootstrap + '\nawait bootstrapPlayhubStandalone();',
    footer: '\n})().catch(function(error){var h=window.__PLAYHUB_HOST__; if(h)h.event({state:"failed",reason:String(error&&error.message||error)}); console.error("[Playhub standalone] Mount failed",error);});',
  },
};
