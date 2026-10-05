import { SP_REACT as React } from './decky';
const { cloneElement, createElement, forwardRef, isValidElement, memo } = React;

type Transform = (tree: any) => any;
type Step = (node: any) => boolean;

/** Wrap rendered OUTPUT, without modifying Steam's elements, props or shared types.
 * Functions, classes, memo and forwardRef need different invocation contracts.
 * Each original type has one wrapper per step; rendered output is never cached.
 * Unknown/lazy types are left native rather than guessed/invoked during discovery.
 */
export const createOutputPatch = (steps: Step[], transform: Transform, onError: (error: unknown) => void) => {
  const caches = steps.map(() => new WeakMap<object, any>());
  let active = true;
  const report = (error: unknown) => { try { onError(error); } catch { /* Diagnostics are optional. */ } };
  const finish = (tree: any): any => {
    if (!active) return tree;
    try { return transform(tree); } catch (error) { report(error); return tree; }
  };
  const copyStatics = (source: any, target: any) => {
    for (const key of ['defaultProps', 'propTypes', 'contextType', 'contextTypes']) {
      try {
        const descriptor = Object.getOwnPropertyDescriptor(source, key);
        if (descriptor) Object.defineProperty(target, key, descriptor);
      } catch { /* Nonessential compatibility metadata. */ }
    }
    return target;
  };
  const typeFor = (original: any, stage: number): any => {
    if (!original || (typeof original !== 'function' && typeof original !== 'object')) return original;
    const cache = caches[stage];
    if (cache.has(original)) return cache.get(original);
    const output = (tree: any) => active ? (stage + 1 < steps.length ? walk(tree, stage + 1) : finish(tree)) : tree;
    let wrapped: any;
    if (typeof original === 'function') {
      if (original.prototype?.isReactComponent || typeof original.prototype?.render === 'function') {
        wrapped = class extends original {
          render() { return output(super.render()); }
        };
      } else {
        wrapped = function (this: any, ...args: any[]) { return output(original.apply(this, args)); };
      }
      copyStatics(original, wrapped);
    } else if (original.$$typeof === Symbol.for('react.memo')) {
      const inner = typeFor(original.type, stage);
      wrapped = inner === original.type ? original : memo(inner, original.compare);
    } else if (original.$$typeof === Symbol.for('react.forward_ref') && typeof original.render === 'function') {
      wrapped = forwardRef((props, ref) => output(original.render(props, ref)));
    } else {
      return original;
    }
    // Recording wrappers as well prevents nesting when a route hook sees its own output.
    cache.set(original, wrapped);
    cache.set(wrapped, wrapped);
    return wrapped;
  };
  const replaceType = (node: any, type: any): any => {
    if (node.type === type) return node;
    const props: any = { ...node.props, key: node.key };
    // React 19 stores ref in props. Do not read element.ref's deprecated getter.
    const legacyRef = Object.getOwnPropertyDescriptor(node, 'ref');
    if (!('ref' in props) && legacyRef && 'value' in legacyRef) props.ref = legacyRef.value;
    return createElement(type, props);
  };
  const walk = (tree: any, stage: number, depth = 0): any => {
    if (!active || depth > 32) return tree;
    try {
      if (Array.isArray(tree)) {
        const result = tree.map(node => walk(node, stage, depth + 1));
        return result.some((node, index) => node !== tree[index]) ? result : tree;
      }
      if (!isValidElement<any>(tree)) return tree;
      if (steps[stage](tree)) {
        if (typeof tree.type !== 'string' && caches[stage].has(tree.type) && caches[stage].get(tree.type) === tree.type) return tree;
        const wrapped = typeFor(tree.type, stage);
        if (wrapped !== tree.type) return replaceType(tree, wrapped);
      }
      const children = tree.props.children;
      if (children === undefined) return tree;
      const result = walk(children, stage, depth + 1);
      return result === children ? tree : cloneElement(tree, {}, result);
    } catch (error) { report(error); return tree; }
  };
  return { apply: (tree: any) => walk(tree, 0), stop: () => { active = false; } };
};
