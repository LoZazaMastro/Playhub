import test from 'node:test';
import assert from 'node:assert/strict';
import { refreshFiber, currentRoot, walkFibers, removeOwnedTab, mapRenderedTabs } from '../src/react19Refresh.mjs';
import { insertQamTab } from '../src/steamHostCore.mjs';

import { fixture } from './fixtures/react-model.mjs';

test('il vecchio force dell’antenato non raggiunge il ramo memo', () => {
  const f = fixture();
  f.target.memoizedProps = { ...f.props, __stamp: 1 };
  f.ReactDOM.flushSync(f.enqueue);
  assert.deepEqual(f.counts(), { renders: 0, commits: 0 });
});
test('lane senza invalidazione: render eseguito ma risultato scartato', () => {
  const f = fixture();
  f.ReactDOM.flushSync(() => {
    f.enqueue(); f.target.lanes |= 2;
    for (let n = f.target.return; n; n = n.return) n.childLanes |= 2;
  });
  assert.deepEqual(f.counts(), { renders: 1, commits: 0 });
});
test('refresh schedula la root, attraversa otto hop e conserva le props ricevute', () => {
  const f = fixture(); let received;
  f.target.type = props => { received = props; };
  const result = refreshFiber(f.target, f.ReactDOM, '19.1.1');
  assert.equal(result.scheduled, true);
  assert.equal(result.ancestorHops, 8);
  assert.deepEqual(f.counts(), { renders: 1, commits: 1 });
  assert.equal(received, f.props);
  assert.equal(f.target.memoizedProps, f.props);
  assert.deepEqual(Object.keys(received), ['native']);
});
test('nessuna scrittura privata su versione React diversa', () => {
  const f = fixture();
  assert.equal(refreshFiber(f.target, f.ReactDOM, '19.2.0').reason, 'unsupported-react-version');
  assert.deepEqual(f.counts(), { renders: 0, commits: 0 });
  assert.equal(f.target.memoizedProps, f.props);
});
test('nessun dispatch casuale se manca una classe', () => {
  const f = fixture(); f.parent.tag = 0;
  f.parent.memoizedState = { queue: { dispatch() { throw new Error('non deve essere chiamato'); } } };
  assert.equal(refreshFiber(f.target, f.ReactDOM, '19.1.1').reason, 'class-updater-unavailable');
});
test('il ramo nascosto non viene forzato', () => {
  const f = fixture(); f.target.return.tag = 22; f.target.return.memoizedState = {};
  assert.equal(refreshFiber(f.target, f.ReactDOM, '19.1.1').reason, 'hidden-or-suspended');
  assert.deepEqual(f.counts(), { renders: 0, commits: 0 });
});
test('una fiber non piu’ appartenente alla current root viene rifiutata', () => {
  const f = fixture(); f.root.child = null;
  assert.equal(refreshFiber(f.target, f.ReactDOM, '19.1.1').reason, 'stale-fiber');
});
test('flushSync assente non provoca mutazioni', () => {
  const f = fixture();
  assert.equal(refreshFiber(f.target, {}, '19.1.1').reason, 'flushSync-unavailable');
  assert.equal(f.target.memoizedProps, f.props);
});
test('le props artificiali vengono rimosse anche se flushSync fallisce', () => {
  const f = fixture();
  const result = refreshFiber(f.target, { flushSync(fn) { fn(); throw new Error('fixture'); } }, '19.1.1');
  assert.equal(result.reason, 'refresh-error');
  assert.equal(f.target.memoizedProps, f.props);
});
test('currentRoot usa il ramo committed e la visita ignora i cicli', () => {
  const f = fixture();
  assert.equal(currentRoot(f.target.alternate), f.root);
  const n = {}; n.child = n;
  let visits = 0; walkFibers(n, () => visits++);
  assert.equal(visits, 1);
});
test('la rimozione per identita’ preserva una scheda altrui con chiave uguale', () => {
  const ours = { key: 0x50484B }, foreign = { key: 0x50484B };
  const tabs = [foreign, ours, { key: 5 }];
  assert.equal(removeOwnedTab(tabs, ours), 1);
  assert.equal(tabs[0], foreign);
});
test('clone delle tabs congelate, ordine e identita’ del pannello invariati', () => {
  const React = { isValidElement: x => x?.element === true, cloneElement: (x, patch) => ({ ...x, props: { ...x.props, ...patch } }) };
  const native = Object.freeze([0, 3, 4, 5, 7, 6].map(key => ({ key })));
  const tab = { key: 0x50484B, panel: {} };
  const leaf = Object.freeze({ element: true, key: 'stable', props: Object.freeze({ tabs: native }) });
  const output = { element: true, props: { children: [leaf] } };
  const mapped = mapRenderedTabs(output, tabs => { const result = tabs.slice(); insertQamTab(result, tab, 5, true); return result; }, React);
  assert.deepEqual(mapped.props.children[0].props.tabs.map(x => x.key), [0, 3, 4, 5, 0x50484B, 7, 6]);
  assert.deepEqual(native.map(x => x.key), [0, 3, 4, 5, 7, 6]);
  assert.equal(mapped.props.children[0].key, 'stable');
  assert.equal(mapped.props.children[0].props.tabs[4], tab);
});
