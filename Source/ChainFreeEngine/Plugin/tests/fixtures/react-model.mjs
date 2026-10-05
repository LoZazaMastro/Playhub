import { walkFibers } from '../../src/react19Refresh.mjs';
// Questo e' un modello mirato dei bailout, NON il renderer React reale.
// Le prove d'integrazione sul client Steam restano distinte nel verbale.
export function fixture() {
  const root = { tag: 3, lanes: 0, childLanes: 0, return: null };
  root.stateNode = { tag: 1, current: root };
  const parent = { tag: 1, lanes: 0, childLanes: 0, return: root };
  root.child = parent;
  let last = parent;
  for (let i = 0; i < 7; i++) {
    const node = { tag: 15, lanes: 0, childLanes: 0, return: last };
    last.child = node; last = node;
  }
  const props = { native: true };
  const target = { tag: 15, lanes: 0, childLanes: 0, return: last, pendingProps: props, memoizedProps: props };
  last.child = target;
  walkFibers(root, node => { node.alternate = { ...node, alternate: node }; });
  walkFibers(root, node => { node.alternate.return = node.return?.alternate || null; });
  let scheduled = false;
  let renders = 0;
  let commits = 0;
  const enqueue = () => {
    parent.lanes |= 2;
    parent.alternate.lanes |= 2;
    root.childLanes |= 2;
    scheduled = true;
  };
  parent.stateNode = { updater: { enqueueForceUpdate: enqueue } };
  const ReactDOM = {
    flushSync(fn) {
      fn();
      if (!scheduled) return;
      let reached = true;
      for (let node = target.return; node && node !== parent; node = node.return)
        if (!(node.childLanes & 2)) reached = false;
      if (reached) {
        const a = target.memoizedProps, b = target.pendingProps;
        const changed = Object.keys(a).length !== Object.keys(b).length || Object.keys(a).some(k => a[k] !== b[k]);
        if (changed || (target.lanes & 2)) {
          renders++;
          const output = target.type?.(b);
          if (changed) { commits++; target.onCommit?.(output); target.memoizedProps = b; }
        }
      }
      walkFibers(root, node => { node.lanes = 0; node.childLanes = 0; if (node.alternate) { node.alternate.lanes = 0; node.alternate.childLanes = 0; } });
      scheduled = false;
    },
  };
  return { root, parent, target, props, ReactDOM, counts: () => ({ renders, commits }), enqueue };
}

