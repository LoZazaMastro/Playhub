import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';

const source = fs.readFileSync(new URL('../src/DailyHistory.tsx', import.meta.url), 'utf8');

test('history interludes participate in horizontal controller navigation', () => {
  assert.match(source, /const selector='\.ph-history-card,\.ph-history-quote'/);
  assert.match(source, /closest\(selector\)/);
  assert.match(source, /className="ph-history-quote"[^>]*focusable=\{true\}/);
});

test('every dated game heading can render its cover beside right aligned metadata', () => {
  assert.match(source, /const headingMedia=\(item:Story\)/);
  assert.match(source, /\{headingMedia\(story\)\}/);
  assert.match(source, /const headingContent=<><div className="ph-history-date"/);
  assert.match(source, /ph-history-head ph-history-day-card/);
  assert.match(source, /\.ph-history-sub\{[^}]*text-align:right/);
});
