import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const source = readFileSync(new URL('../src/DashboardPage.tsx', import.meta.url), 'utf8');

test('window rail predicts expanded geometry without a delayed snap or card cap', () => {
  assert.match(source, /alignExpandingWindow\(item, container\)/);
  assert.match(source, /const expanded = measure\(\)/);
  assert.doesNotMatch(source, /alignFocusedCard|ph-edge-clipped|windows\.slice\(0, 12\)/);
});

test('directional focus is not delayed by the hover animation', () => {
  assert.doesNotMatch(source, /gridFocusMoveState|previousMove/);
  assert.match(source, /target\.focus\?\.\(\{ preventScroll: true \}\)/);
});

test('app picker and process list have bounded scrolling and no picker cancel button', () => {
  assert.match(source, /ph-app-library \.ph-grid \{ min-height:0; overflow-y:auto/);
  assert.match(source, /ph-process-panel \{ height:100%; min-height: 0/);
  // The process panel must stay a flexible box: a fixed list height overflowed
  // the system stack and clipped the bottom of "Processi attivi".
  assert.match(source, /ph-system-page \.ph-system-lower \{ flex:1 1 auto; min-height:0/);
  assert.match(source, /ph-process-list \{ flex:1 1 auto; height:auto; min-height:0/);
  assert.doesNotMatch(source, /ph-process-list \{ flex:none/);
  assert.doesNotMatch(source, /onPress=\{closeLibrary\}/);
});
