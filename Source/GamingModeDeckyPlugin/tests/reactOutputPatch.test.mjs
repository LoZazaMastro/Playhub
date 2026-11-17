import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import test from 'node:test';
import vm from 'node:vm';

const require = createRequire(new URL('../package.json', import.meta.url));
const ts = require('typescript');
const sourcePath = process.env.OUTPUT_PATCH_SOURCE ?? new URL('../src/reactOutputPatch.ts', import.meta.url);
const source = readFileSync(sourcePath, 'utf8');
const element = Symbol.for('react.test.element');
function createElement(type, config = {}, ...children) {
  const { key = null, ...props } = config;
  if (children.length) props.children = children.length === 1 ? children[0] : children;
  return { $$typeof: element, type, key, props };
}
const React = {
  createElement,
  cloneElement(node, config, ...children) { return createElement(node.type, { ...node.props, key: node.key, ...config }, ...children); },
  isValidElement(node) { return !!node && node.$$typeof === element; },
  forwardRef(render) { return { $$typeof: Symbol.for('react.forward_ref'), render }; },
  memo(type, compare) { return { $$typeof: Symbol.for('react.memo'), type, compare }; },
};
function render(node, receiver) {
  const type = node.type;
  if (typeof type === 'string') return node;
  if (type.$$typeof === Symbol.for('react.memo')) return render({ ...node, type: type.type }, receiver);
  if (type.$$typeof === Symbol.for('react.forward_ref')) return type.render(node.props, node.props.ref);
  if (type.prototype?.render) return new type(node.props).render();
  return type.call(receiver, node.props);
}
const exports = {};
vm.runInNewContext(ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText,
  { exports, Symbol, WeakMap, require(name) { assert.equal(name, './decky'); return { SP_REACT: React }; } });
const createOutputPatch = exports.createOutputPatch;
const propsFor = appid => Object.freeze({ overview: Object.freeze({ appid }), bShowStreamingSelector: true });
// Actual Steam's primary action consumes overview, then returns a container without it.
function nativeAction(props) { const { overview, bShowStreamingSelector, ...rest } = props; return createElement('div', { ...rest, className: 'PlayButtonContainer', children: 'native Play' }); }
class NativeClass {
  constructor(props) { this.props = props; }
  render() { return nativeAction(this.props); }
}
NativeClass.prototype.isReactComponent = {};
const shapeCases = [
  ['function', nativeAction], ['class', NativeClass],
  ['forwardRef', React.forwardRef((props, ref) => createElement('div', { className: 'PlayButtonContainer', ref }))],
  ['memo function', React.memo(nativeAction, (a, b) => a === b)],
  ['memo forwardRef', React.memo(React.forwardRef(props => nativeAction(props)), (a, b) => a === b)],
];
for (const [shape, type] of shapeCases) test(`${shape}: current source props survive consumed overview, reused wrapper and two AppIDs`, () => {
  const calls = [];
  const patch = createOutputPatch([node => node.props.bShowStreamingSelector === true], (tree, props) => {
    calls.push({ tree, props }); return { kind: 'store-install', appId: props?.overview?.appid, native: tree };
  }, error => { throw error; });
  const a = propsFor(3000000011), b = propsFor(3000000022);
  const first = patch.apply(createElement(type, a)), second = patch.apply(createElement(type, b));
  assert.equal(first.type, second.type, 'cache contains type wrappers, not per-app snapshots');
  const one = render(first), two = render(second), again = render(first);
  assert.equal(one.appId, a.overview.appid);
  assert.equal(two.appId, b.overview.appid);
  assert.equal(again.appId, a.overview.appid);
  assert.equal(one.native.props.overview, undefined, 'the render tree cannot supply identity');
  assert.equal(calls.length, 3);
  assert.equal(calls[0].props.overview, a.overview);
  assert.equal(calls[1].props.overview, b.overview);
  assert.equal(a.overview.appid, 3000000011);
  patch.stop();
  assert.equal(render(first).props.className, 'PlayButtonContainer');
  assert.equal(calls.length, 3, 'stopped wrappers never apply transforms');
});

test('multi-stage route uses final action props, never route AppID or prior render', () => {
  const patch = createOutputPatch([() => true, node => node.props.bShowStreamingSelector === true],
    (tree, props) => ({ native: tree, appId: props?.overview?.appid }), assert.fail);
  const Route = props => createElement(NativeClass, props.action);
  const routeA = patch.apply(createElement(Route, { overview: { appid: 7 }, action: propsFor(3000000033) }));
  const routeB = patch.apply(createElement(Route, { overview: { appid: 8 }, action: propsFor(3000000044) }));
  assert.equal(render(render(routeA)).appId, 3000000033);
  assert.equal(render(render(routeB)).appId, 3000000044);
});

test('forwardRef retains exact ref, keys and original props without mutation', () => {
  const ref = { current: null }, original = Object.freeze({ ...propsFor(3000000055), ref });
  const patch = createOutputPatch([() => true], (tree, props) => ({ tree, props }), assert.fail);
  const input = createElement(shapeCases[2][1], { ...original, key: 'owned' });
  const wrapped = patch.apply(input), result = render(wrapped);
  assert.equal(wrapped.key, 'owned');
  assert.equal(result.props.overview, original.overview);
  assert.equal(result.tree.props.ref, ref);
  assert.equal(input.type, shapeCases[2][1]);
});

test('legacy one-argument transforms, native stop and unknown lazy types are unchanged', () => {
  let count = 0;
  const patch = createOutputPatch([() => true], tree => { ++count; return { legacy: tree }; }, assert.fail);
  const type = props => createElement('button', { children: props.label });
  const wrapped = patch.apply(createElement(type, { label: 'Play' }));
  assert.equal(render(wrapped).legacy.props.children, 'Play');
  const lazy = { $$typeof: Symbol.for('react.lazy'), _init() { assert.fail('unknown lazy execution'); } };
  const unknown = createElement(lazy); assert.equal(patch.apply(unknown), unknown);
  patch.stop(); assert.equal(render(wrapped).props.children, 'Play'); assert.equal(count, 1);
});

test('render errors keep native error semantics and transform errors preserve native output', () => {
  const thrown = new Error('native error'), Native = () => { throw thrown; };
  const patch = createOutputPatch([() => true], () => { throw new Error('transform error'); }, () => {});
  assert.throws(() => render(patch.apply(createElement(Native))), error => error === thrown);
  const output = render(patch.apply(createElement(nativeAction, propsFor(3000000066))));
  assert.equal(output.props.className, 'PlayButtonContainer');
});
