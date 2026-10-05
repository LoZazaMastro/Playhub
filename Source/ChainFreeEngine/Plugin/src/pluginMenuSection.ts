// @ts-nocheck
// Insert into Steam's Properties section, including nested React fragments.
// Return the original tree when no Properties action exists.
export function insertPluginSection(React, tree, entry) {
  const keys = new Set(['playhub-metadata-edit', 'themedeck-change-music',
    'trailerhero-game-settings', 'launch-curtain-game-settings',
    'quick-settings-game-profile', 'playhub-artworks-change-artwork']);
  const collected = new Map();
  let anchor;
  function scan(node, depth = 0) {
    if (!node || depth > 32) return;
    if (Array.isArray(node)) { node.forEach(n => scan(n, depth + 1)); return; }
    if (!React.isValidElement(node)) return;
    if (keys.has(node.key)) { collected.set(node.key, node); return; }
    const handler = node.props?.onSelected ?? node.props?.onClick;
    if (typeof handler === 'function' && /(?:Show)?AppProperties/.test(Function.prototype.toString.call(handler))) anchor = node;
    scan(node.props?.children, depth + 1);
  }
  scan(tree);
  if (!anchor) return tree;
  collected.set(entry.key, entry);
  const ordered = [...keys].filter(key => collected.has(key)).map(key => collected.get(key));
  function visit(node, depth = 0) {
    if (!node || depth > 32) return node;
    if (Array.isArray(node)) return node.flatMap(n => n === anchor ? [...ordered, n] : keys.has(n?.key) ? [] : [visit(n, depth + 1)]);
    if (!React.isValidElement(node)) return node;
    if (keys.has(node.key)) return null;
    if (node === anchor) return [...ordered, node];
    if (node.props?.children === undefined) return node;
    return React.cloneElement(node, { children: visit(node.props.children, depth + 1) });
  }
  return visit(tree);
}
