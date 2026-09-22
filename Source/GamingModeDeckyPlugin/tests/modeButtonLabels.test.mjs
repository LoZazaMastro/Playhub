import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';
import ts from 'typescript';

const source = readFileSync(new URL('../src/index.tsx', import.meta.url), 'utf8');
const ast = ts.createSourceFile('index.tsx', source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
const helper = ast.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === 'ModeButtonLabel');
const copy = ast.statements.find(node => ts.isVariableStatement(node) && node.declarationList.declarations.some(item => item.name.getText(ast) === 'strings'));
let actions;
const visit = node => {
  if (ts.isJsxElement(node) && node.openingElement.attributes.properties.some(prop =>
    prop.name?.text === 'className' && prop.initializer?.text === 'ph-mode-actions')) actions = node;
  ts.forEachChild(node, visit);
};
visit(ast);
assert.ok(helper && copy && actions);
function execute(code, scope = {}) {
  const exports = {};
  vm.runInNewContext(ts.transpileModule(code, { compilerOptions: {
    module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.React,
  } }).outputText, { exports, ...scope });
  return exports;
}
const { strings } = execute(`${copy.getText(ast)}\nexport { strings };`);
const locales = ['en', 'it', 'es', 'fr', 'de', 'pt', 'uk', 'zh', 'ja', 'ko', 'hi', 'ru'];
for (const locale of locales) test(`${locale}: actual mode buttons retain literal two-line product names`, () => {
  assert.ok(strings[locale]);
  const React = { createElement: (type, props, ...children) => typeof type === 'function'
    ? type(props) : { type, props: props ?? {}, children: children.flat() } };
  const { tree } = execute(`${helper.getText(ast)}\nexport const tree=(${actions.getText(ast)});`, {
    React, Focusable: 'div', DialogButton: 'button', busy: false, local: strings[locale],
  });
  const labels = tree.children.map(button => button.children.find(child => child.props?.className === 'ph-mode-action-label'));
  assert.deepEqual(Array.from(labels[0].children, child => child.children.join('')), ['Gaming', 'Mode']);
  assert.deepEqual(Array.from(labels[1].children, child => child.children.join('')), ['Desktop', 'Mode']);
  const text = node => typeof node === 'object' ? node.children.map(text).join('') : String(node);
  assert.equal(text(tree.children[0].children[0]), '\uE7FC');
  assert.equal(text(tree.children[1].children[0]), '\uE765\uE962');
});

test('mode label calls cannot revive locale-dependent splitting and retain two fixed rows', () => {
  assert.equal(Object.keys(strings).length, 12);
  assert.doesNotMatch(helper.getText(ast), /local\.|text\.slice|indexOf|endsWith/);
  assert.doesNotMatch(actions.getText(ast), /ModeButtonLabel text=/);
  assert.match(source, /grid-template-rows:repeat\(2,1\.2em\)/);
  assert.match(source, /font-family:'Segoe Fluent Icons','Segoe MDL2 Assets'/);
});
