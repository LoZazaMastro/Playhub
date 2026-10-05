import test from 'node:test';
import assert from 'node:assert/strict';
import {
  applyRoutePatches, createRouteBuilder, injectRoutes, insertQamTab,
  registerTabKey, removeQamTab, routeTypeFromList, OUR_ROUTES,
} from '../src/steamHostCore.mjs';

const React = { createElement: (type, props, ...children) => ({ type, props: { ...props, children } }) };
const Route = function Route() {};
const steamList = () => [
  { type: Route, props: { path: '/library/home', children: {} } },
  { type: Route, props: { path: '/settings', children: {} } },
];

test('our routes are appended once and reuse their slot across renders', () => {
  const build = createRouteBuilder(React);
  const routes = new Map([['/playhub/dashboard', { component: () => null, props: { exact: true } }]]);
  const list = steamList();
  const before = list.length;
  injectRoutes(list, build(Route, routes, undefined));
  injectRoutes(list, build(Route, routes, undefined));
  injectRoutes(list, build(Route, routes, undefined));
  assert.equal(list.length, before + 1, 'routes were duplicated on re-render');
  assert.ok(list[list.length - 1][OUR_ROUTES]);
  assert.equal(list[0].props.path, '/library/home', "Steam's own routes must keep their position");
});

test('route elements keep their identity while the route set is unchanged', () => {
  const build = createRouteBuilder(React);
  const routes = new Map([['/playhub/store', { component: () => null, props: {} }]]);
  const first = build(Route, routes, undefined);
  assert.equal(build(Route, routes, undefined), first);
  routes.set('/playhub/dashboard', { component: () => null, props: {} });
  assert.notEqual(build(Route, routes, undefined), first);
});

test('a route patch runs once per rendered route', () => {
  const list = steamList();
  let calls = 0;
  const patches = new Map([['/library/home', new Set([(props) => { calls++; return { children: { patched: true, ...props.children } }; }])]]);
  applyRoutePatches(list, patches);
  applyRoutePatches(list, patches);
  assert.equal(calls, 1);
  assert.equal(list[0].props.children.patched, true);
  assert.equal(list[1].props.children.patched, undefined);
});

test('a failing patch is contained and reported', () => {
  const list = steamList();
  const seen = [];
  const patches = new Map([['/settings', new Set([() => { throw new Error('boom'); }])]]);
  applyRoutePatches(list, patches, (path, error) => seen.push([path, String(error)]));
  assert.equal(seen.length, 1);
  assert.equal(seen[0][0], '/settings');
});

test('the Steam route type is taken from the list being extended', () => {
  assert.equal(routeTypeFromList(steamList()), Route);
  assert.equal(routeTypeFromList([]), undefined);
});

test('the QAM tab is inserted after Performance and never twice', () => {
  const tabs = [{ key: 0 }, { key: 5 }, { key: 999 }];
  const tab = { key: 0x50484B };
  assert.equal(insertQamTab(tabs, tab, 5, true), 'inserted');
  assert.deepEqual(tabs.map((entry) => entry.key), [0, 5, 0x50484B, 999]);
  assert.equal(insertQamTab(tabs, tab, 5, false), 'present');
  assert.equal(tabs.length, 4);
  assert.equal(tab.initialVisibility, false);
});

test('visibility goes through Steam’s own setter when the rendered tab has one', () => {
  let asked;
  const tabs = [{ key: 0x50484B, qAMVisibilitySetter: (value) => { asked = value; } }];
  insertQamTab(tabs, { key: 0x50484B }, 5, true);
  assert.equal(asked, true);
});

test('uninstall removes only our tab', () => {
  const tabs = [{ key: 0 }, { key: 0x50484B }, { key: 999 }];
  assert.equal(removeQamTab(tabs, 0x50484B), 1);
  assert.deepEqual(tabs.map((entry) => entry.key), [0, 999]);
});

test('our key is registered in Steam’s tab enum without overwriting an existing one', () => {
  const tabEnum = { Notifications: 0, Settings: 4, 4: 'Settings' };
  assert.equal(registerTabKey(tabEnum, 0x50484B, 'Playhub'), true);
  assert.equal(tabEnum[0x50484B], 'Playhub');
  assert.equal(tabEnum.Playhub, 0x50484B);
  assert.equal(registerTabKey({ ...tabEnum, [0x50484B]: 'Other' }, 0x50484B, 'Playhub'), false);
});
