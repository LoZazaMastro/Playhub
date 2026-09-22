import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import ts from 'typescript';

const fieldControls = new Set(['Field', 'SliderField', 'ToggleField', 'DropdownItem', 'ButtonItem']);
for (const file of ['quickSettings/index.tsx', 'index.tsx', 'PlayhubQamToggle.tsx', 'ControlCenter.tsx']) {
  test(`QAM fields explicitly disable native separators: ${file}`, () => {
    const source = ts.createSourceFile(file,
      readFileSync(new URL(`../src/${file}`, import.meta.url), 'utf8'),
      ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
    let fields = 0;
    const visit = node => {
      if ((ts.isJsxOpeningElement(node) || ts.isJsxSelfClosingElement(node))
          && fieldControls.has(node.tagName.getText(source))) {
        fields++;
        const attributes = node.attributes.properties;
        const separators = attributes.filter(attribute => ts.isJsxAttribute(attribute)
          && attribute.name.getText(source) === 'bottomSeparator');
        const line = source.getLineAndCharacterOfPosition(node.getStart(source)).line + 1;
        assert.equal(separators.length, 1, `${file}:${line}: requires one bottomSeparator prop`);
        const value = separators[0].initializer;
        assert.ok(value && ts.isStringLiteral(value) && value.text === 'none',
          `${file}:${line}: bottomSeparator must be the literal none`);
        assert.ok(!attributes.slice(attributes.indexOf(separators[0]) + 1).some(ts.isJsxSpreadAttribute),
          `${file}:${line}: trailing spread could override bottomSeparator`);
      }
      ts.forEachChild(node, visit);
    };
    visit(source);
    assert.ok(fields > 0, `${file}: test must inspect at least one native field`);
  });
}
