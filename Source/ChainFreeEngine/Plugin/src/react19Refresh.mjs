// React 19.1.1 conserva la funzione risolta nel SimpleMemoComponent montato.
// Questa integrazione privata resta deliberatamente vincolata alla build
// verificata: su altre versioni si attende un render naturale, senza indovinare.
export const VERIFIED_REACT_VERSION = '19.1.1';
const SYNC_LANE = 2;
let sequence = 0;

export function walkFibers(root, visit, limit = 50000) {
  if (!root) return;
  const stack = [root];
  const seen = new Set();
  while (stack.length && seen.size < limit) {
    const node = stack.pop();
    if (!node || seen.has(node)) continue;
    seen.add(node);
    visit(node);
    if (node.sibling) stack.push(node.sibling);
    if (node.child) stack.push(node.child);
  }
}

export function currentRoot(value) {
  let node = value?._internalRoot?.current || value?.current || value;
  const seen = new Set();
  while (node?.return && !seen.has(node)) { seen.add(node); node = node.return; }
  return node?.tag === 3 ? (node.stateNode?.current || node) : null;
}

// Non si aggiungono hook alla funzione Steam: cambiarne l'ordine corromperebbe
// lo stato gia' montato. Si usa una vera ForceUpdate di una classe esistente
// per schedulare la root, e la stessa lane per rendere raggiungibile il ramo.
export function refreshFiber(fiber, ReactDOM, reactVersion) {
  const no = reason => ({ scheduled: false, reason });
  if (reactVersion !== VERIFIED_REACT_VERSION) return no('unsupported-react-version');
  if (typeof ReactDOM?.flushSync !== 'function') return no('flushSync-unavailable');
  if (!fiber || ![0, 1, 11, 15].includes(fiber.tag)) return no('unsupported-fiber-tag');
  if (!fiber.memoizedProps || typeof fiber.memoizedProps !== 'object') return no('props-unavailable');
  const root = currentRoot(fiber);
  if (!root || root.stateNode?.tag !== 1) return no('concurrent-root-unavailable');
  let found = false;
  walkFibers(root, node => { if (node === fiber) found = true; });
  if (!found) return no('stale-fiber');
  let ancestor = null;
  let ancestorHops = 0;
  let hops = 0;
  for (let node = fiber; node; node = node.return, hops++) {
    // Un menu nascosto non deve essere aperto come effetto collaterale. Il
    // monitor riprova quando Steam rende visibile il ramo Offscreen/Suspense.
    if ((node.tag === 22 && node.memoizedState !== null) ||
        (node.tag === 13 && node.memoizedState !== null)) return no('hidden-or-suspended');
    if (!ancestor && node.tag === 1 &&
        typeof node.stateNode?.updater?.enqueueForceUpdate === 'function') {
      ancestor = node; ancestorHops = hops;
    }
  }
  if (!ancestor) return no('class-updater-unavailable');
  const oldProps = fiber.memoizedProps;
  const marker = '__playhubRefresh_' + (++sequence);
  const stamped = { ...oldProps, [marker]: sequence };
  let scheduled = false;
  try {
    ReactDOM.flushSync(() => {
      // flushSync assegna SyncLane alla ForceUpdate e React schedula davvero
      // la root. Scrivere soltanto fiber.lanes non farebbe partire alcun lavoro.
      ancestor.stateNode.updater.enqueueForceUpdate(ancestor.stateNode);
      const lanes = (ancestor.lanes || 0) | (ancestor.alternate?.lanes || 0);
      if (!(lanes & SYNC_LANE)) return;
      // Cambiamo SOLO il ricordo delle props, non quelle passate alla funzione.
      // Senza questa invalidazione il render puo' avvenire, ma il suo risultato
      // essere scartato da updateFunctionComponent per didReceiveUpdate=false.
      fiber.memoizedProps = stamped;
      fiber.lanes |= SYNC_LANE;
      if (fiber.alternate) fiber.alternate.lanes |= SYNC_LANE;
      for (let node = fiber.return; node; node = node.return) {
        node.childLanes |= SYNC_LANE;
        if (node.alternate) node.alternate.childLanes |= SYNC_LANE;
      }
      scheduled = true;
    });
    return { scheduled, reason: scheduled ? 'sync-lane-scheduled' : 'sync-lane-not-observed', ancestorHops };
  } catch (error) {
    return { scheduled, reason: 'refresh-error', error: String(error), ancestorHops };
  } finally {
    // Dopo il commit l'alternate puo' conservare il vecchio oggetto; non
    // lasciamo props artificiali nelle copie inattive o dopo un'eccezione.
    for (const node of [fiber, fiber.alternate])
      if (node?.memoizedProps === stamped) node.memoizedProps = oldProps;
  }
}

// Si rimuove per identita', mai soltanto per chiave: una scheda di Decky puo'
// avere la stessa chiave numerica e non appartiene a questo motore.
export function removeOwnedTab(tabs, tab) {
  if (!Array.isArray(tabs)) return 0;
  let removed = 0;
  for (let i = tabs.length - 1; i >= 0; i--)
    if (tabs[i] === tab) { tabs.splice(i, 1); removed++; }
  return removed;
}

// I nuovi array evitano sia props congelate sia cache basate sull'identita'
// dell'array tabs. cloneElement conserva key/ref e non aggiunge UI o wrapper.
export function mapRenderedTabs(output, transform, React, depth = 0) {
  if (depth > 60 || output == null) return output;
  if (Array.isArray(output)) {
    let changed = false;
    const next = output.map(value => {
      const result = mapRenderedTabs(value, transform, React, depth + 1);
      changed ||= result !== value;
      return result;
    });
    return changed ? next : output;
  }
  if (!React.isValidElement(output)) return output;
  const props = output.props;
  const patch = {};
  let changed = false;
  if (Array.isArray(props.tabs)) {
    const tabs = transform(props.tabs);
    if (tabs !== props.tabs) { patch.tabs = tabs; changed = true; }
  }
  if (props.children !== undefined) {
    const children = mapRenderedTabs(props.children, transform, React, depth + 1);
    if (children !== props.children) { patch.children = children; changed = true; }
  }
  return changed ? React.cloneElement(output, patch) : output;
}
