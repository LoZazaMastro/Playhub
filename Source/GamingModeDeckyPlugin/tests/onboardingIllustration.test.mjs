import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';
import ts from 'typescript';
import { readFileSync } from 'node:fs';

const source = fs.readFileSync(new URL('../src/OnboardingIllustration.tsx', import.meta.url), 'utf8');
const exports = {};
const Fragment = Symbol('Fragment');
const React = {
  Fragment,
  createElement: (type, props, ...children) => typeof type === 'function'
    ? type({ ...props, children }) : ({ type, props: props ?? {}, children }),
};
vm.runInNewContext(ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.React },
}).outputText, { exports, require: name => {
  if (name === './decky') return { SP_REACT: React };
  // I plugin Playhub si riconoscono dal loro logo, non da un'icona generica.
  if (name === './PlayhubIcon') return { PlayhubIcon: () => ({ type: 'svg', props: { 'data-playhub-logo': true }, children: [] }) };
  assert.equal(name, 'react-icons/tb');
  return new Proxy({}, { get: (_target, key) => key === '__esModule' ? true : `icon:${String(key)}` });
} });
const views = ['intro', 'playhub', 'decky', 'decky-off', 'store', 'customize', 'audio', 'performance', 'graphics', 'controller'];
function nodes(value, found = []) {
  if (Array.isArray(value)) value.forEach(item => nodes(item, found));
  else if (value && typeof value === 'object') { found.push(value); nodes(value.children, found); }
  return found;
}

test('each view renders only its distinct decorative motif, keyed for mount restart', () => {
  const fingerprints = new Set();
  for (const view of views) {
    const tree = exports.OnboardingIllustration({ view });
    assert.equal(tree.props['aria-hidden'], 'true');
    assert.equal(tree.props['data-onboarding-illustration'], view);
    const stage = nodes(tree).find(node => node.props.className === 'ph-oi-stage');
    assert.equal(stage.props.key, view);
    const content = nodes(stage);
    assert.ok(content.filter(node => String(node.type).startsWith('icon:')).length >= 1);
    assert.ok(content.every(node => !['button', 'a', 'input'].includes(node.type)));
    assert.ok(content.every(node => node.props.tabIndex === undefined));
    assert.ok(content.every(node => !(node.children ?? []).some(child => typeof child === 'string')));
    fingerprints.add(JSON.stringify(stage));
  }
  assert.equal(fingerprints.size, 10);
});

test('no JS clocks, effects, observers, runtime IO or layout measurement', () => {
  assert.doesNotMatch(source, /useEffect|useLayoutEffect|useState|useRef|setInterval|setTimeout|requestAnimationFrame|ResizeObserver|getBoundingClientRect|fetch\(/);
  assert.doesNotMatch(source, /\bwill-change\s*:|\bfilter\s*:|\btransition\s*:/);
});

test('stable responsive region is unframed and inert', () => {
  const root = source.match(/\.ph-oi\{([^}]+)\}/)[1];
  // The region is sized by the caller now; the defaults stay the historical ones.
  assert.match(root, /max-width:var\(--ph-oi-max,320px\)/);
  assert.match(root, /height:var\(--ph-oi-height,130px\)/);
  assert.match(root, /overflow:hidden/);
  assert.match(root, /pointer-events:none/);
  assert.doesNotMatch(root, /background|border|padding/);
  assert.match(source, /@container\(max-width:219px\)/);
});

test('all keyframes animate only transform and opacity', () => {
  const blocks = [...source.matchAll(/@keyframes\s+(\w+)\{((?:[^{}]|\{[^{}]*\})*)\}/g)];
  assert.ok(blocks.length >= 24);
  for (const [, name, body] of blocks) {
    const properties = [...body.matchAll(/(?:\{|;)([\w-]+):/g)].map(match => match[1]);
    assert.ok(properties.length > 0, name);
    assert.ok(properties.every(prop => ['transform', 'opacity'].includes(prop)), name);
  }
});

/** The Store page keeps its own download animation; the guide must not reuse it. */
test('store gathers three sources into one catalogue, distinct from the Store page motif', () => {
  const tree = exports.OnboardingIllustration({ view: 'store' });
  const classes = nodes(tree).map(node => node.props.className ?? '');
  assert.equal(classes.filter(name => name.includes('ph-oi-src ')).length, 3, 'Playhub, Decky Store and GitHub');
  assert.ok(nodes(tree).some(node => node.props?.['data-playhub-logo']), 'i plugin Playhub portano il logo Playhub');
  assert.equal(classes.filter(name => name.includes('ph-oi-route ')).length, 3);
  assert.ok(classes.some(name => name.includes('ph-oi-collector')));
  assert.ok(classes.some(name => name.includes('ph-oi-feed')));
  const storeCss = source.slice(source.indexOf('.ph-oi-store-frame{'), source.indexOf('.ph-oi-tabs-line{'));
  assert.doesNotMatch(storeCss, /nth-of-type/, 'positional selectors stacked the composition');
  for (const suffix of ['src-a', 'src-b', 'src-c', 'route-a', 'route-b', 'route-c',
    'store-item-a', 'store-item-b', 'store-item-c']) assert.match(source, new RegExp(`ph-oi-${suffix}\\{`));
  assert.equal(classes.filter(name => name.includes('ph-oi-download')).length, 0, 'never the Store page animation');
  assert.doesNotMatch(source, /phOiSave|ph-oi-store-stage|ph-oi-shelf|ph-oi-saved/);
  const store = readFileSync(new URL('../src/PluginStoreIntro.tsx', import.meta.url), 'utf8');
  for (const motif of ['phOiCollect', 'phOiRoute', 'phOiSource', 'phOiSweep']) assert.ok(!store.includes(motif), motif);
  assert.doesNotMatch(source, /launcher|ButtonItem|overlap|wrapperGap/);
});

/** The closing slide shows the Playhub wordmark itself, not a motif. */
test('the closing slide uses the wordmark instead of an illustration', () => {
  const coach = readFileSync(new URL('../src/PlayhubOnboarding.tsx', import.meta.url), 'utf8');
  assert.match(coach, /import playhubWordmark from "\.\.\/assets\/playhub-wordmark-large\.png"/);
  assert.match(coach, /\{final \? <img className="ph-onboarding-wordmark" src=\{playhubWordmark\}/);
  assert.doesNotMatch(source, /ph-oi-ring|ph-oi-play|ph-oi-orbit|ph-oi-launch/);
});

test('the caller can scale the region without breaking the narrow-container shrink', () => {
  const scaled = exports.OnboardingIllustration({ view: 'store', scale: 1.6, height: 208 });
  assert.equal(scaled.props.style['--ph-oi-scale'], 1.6);
  assert.equal(scaled.props.style['--ph-oi-height'], '208px');
  assert.equal(scaled.props.style['--ph-oi-max'], '100%');
  assert.equal(exports.OnboardingIllustration({ view: 'intro' }).props.style['--ph-oi-height'], undefined);
  assert.match(source, /transform:scale\(calc\(var\(--ph-oi-scale,1\) \* var\(--ph-oi-shrink,1\)\)\)/);
  assert.match(source, /@container\(max-width:219px\)\{\.ph-oi-stage\{--ph-oi-shrink:\.6\}\}/);
});

test('reduced motion explicitly freezes every motif in a visible static composition', () => {
  const reduced = source.slice(source.indexOf('@media(prefers-reduced-motion:reduce)'));
  assert.match(reduced, /animation:none!important/);
  for (const name of ['panel', 'window', 'plugin', 'src', 'store-item', 'tab']) {
    assert.match(reduced, new RegExp(`ph-oi-${name}\\{opacity:`));
  }
  assert.match(source, /--lift:-25px/);
  assert.match(source, /--lift:25px/);
  for (const name of ['wave', 'load', 'render-plane', 'input-key']) {
    assert.match(reduced, new RegExp(`ph-oi-${name}\\{opacity:`));
  }
});

test('every added tab has multiple coordinated semantic moving parts', () => {
  const motifs = {
    audio: ['monitor', 'video-light', 'slider'],
    performance: ['speaker', 'slider', 'wave'],
    graphics: ['render-plane', 'render-image', 'scan'],
    controller: ['pad-body', 'input-orbit', 'input-stick', 'input-key', 'input-link'],
  };
  for (const [view, classes] of Object.entries(motifs)) {
    const content = nodes(exports.OnboardingIllustration({ view }));
    for (const name of classes) assert.ok(content.some(node => node.props.className === `ph-oi-${name}`), `${view}:${name}`);
  }
  for (const animation of ['phOiFader', 'phOiWave', 'phOiWork', 'phOiProcess', 'phOiLayers', 'phOiScan', 'phOiStick', 'phOiButtonPress']) {
    assert.match(source, new RegExp(`@keyframes ${animation}\\{`));
  }
});
