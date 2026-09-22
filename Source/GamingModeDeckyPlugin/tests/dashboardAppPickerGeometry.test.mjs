import assert from 'node:assert/strict';
import { readFileSync, mkdirSync } from 'node:fs';
import { createRequire } from 'node:module';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import test from 'node:test';
import ts from 'typescript';

const source = readFileSync(new URL('../src/DashboardPage.tsx', import.meta.url), 'utf8');
const css = source.split('const STYLE = `')[1].split('`;')[0];
const ast = ts.createSourceFile('DashboardPage.tsx', source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
const sizeFunction = ast.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === 'sizeAppPickerGrid');
const revealFunction = ast.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === 'revealAppPickerTile');
const sizingCode = sizeFunction ? ts.transpileModule(`${revealFunction?.getText(ast) ?? ''}\n${sizeFunction.getText(ast)}`, {
  compilerOptions: { target: ts.ScriptTarget.ES2022 },
}).outputText : '';

const browserOptions = { skip: !process.env.PLAYHUB_BROWSER_QA
  && 'Set PLAYHUB_BROWSER_QA=1 with Playwright available for offline geometry QA' };
async function openBrowser() {
  const { chromium } = createRequire(import.meta.url)('playwright');
  return chromium.launch({ headless: true,
    ...(process.env.PLAYHUB_CHROMIUM_PATH ? { executablePath: process.env.PLAYHUB_CHROMIUM_PATH } : {}) });
}
const outputDirectory = () => {
  const output = process.env.PLAYHUB_QA_OUTPUT ?? join(tmpdir(), 'playhub-picker-geometry');
  mkdirSync(output, { recursive: true });
  return output;
};

test('app picker last-row focus stays inside its scrollport and above the footer', {
  skip: !process.env.PLAYHUB_BROWSER_QA && 'Set PLAYHUB_BROWSER_QA=1 with Playwright available for offline geometry QA',
}, async () => {
  const { chromium } = createRequire(import.meta.url)('playwright');
  const browser = await chromium.launch({ headless: true,
    ...(process.env.PLAYHUB_CHROMIUM_PATH ? { executablePath: process.env.PLAYHUB_CHROMIUM_PATH } : {}) });
  const output = process.env.PLAYHUB_QA_OUTPUT ?? join(tmpdir(), 'playhub-picker-geometry');
  mkdirSync(output, { recursive: true });
  try {
    for (const [width, height] of [[1280, 720], [1920, 1080], [1024, 600]]) {
      const page = await browser.newPage({ viewport: { width, height } });
      await page.setContent(`<style>${css}</style>
        <div class="ph-dashboard"><header class="ph-header"><div class="ph-brand-fallback">playhub</div></header>
        <main class="ph-main"><div class="ph-page ph-app-library">
        <div class="ph-toolbar"><div class="ph-back">Scegli un'app</div></div>
        <div class="ph-app-grid-viewport"><div class="ph-grid" data-ph-focus-grid="true">${Array.from({ length: 37 }, (_, index) =>
          `<div tabindex="0" class="ph-app-tile" data-ph-grid-index="${index}"><img alt="" src="data:image/svg+xml,${encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" width="72" height="72"><rect x="8" y="8" width="56" height="56" rx="10" fill="#7697b8"/></svg>')}"><span class="ph-app-name">Applicazione ${index + 1}</span></div>`).join('')}</div>
        </div></div></main><div data-mock-footer style="position:absolute;bottom:0;height:64px;left:0;right:0;padding:18px 54px">A Seleziona &nbsp; B Indietro</div></div>`);
      if (sizingCode) await page.addScriptTag({ content: `${sizingCode}; sizeAppPickerGrid(document.querySelector('.ph-app-grid-viewport'));` });
      await page.waitForTimeout(300);
      for (const end of ['last', 'first']) {
        const tile = end === 'last' ? page.locator('.ph-app-tile').last() : page.locator('.ph-app-tile').first();
        await tile.evaluate(element => {
          element.focus({ preventScroll: true });
          if (typeof revealAppPickerTile === 'function') revealAppPickerTile(element);
          else element.scrollIntoView({ behavior: 'auto', block: 'nearest' });
        });
        await page.waitForTimeout(200);
        const geometry = await tile.evaluate(element => {
          const grid = element.closest('.ph-grid');
          const rect = element.getBoundingClientRect();
          const clip = grid.getBoundingClientRect();
          const footer = document.querySelector('[data-mock-footer]').getBoundingClientRect();
          const rows = [...grid.children].map(child => child.getBoundingClientRect().top);
          return { tile: rect.toJSON(), clip: clip.toJSON(), footerTop: footer.top,
            scrollTop: grid.scrollTop, scrollHeight: grid.scrollHeight, clientHeight: grid.clientHeight,
            rows: [...new Set(rows.map(top => Math.round(top)))], padding: getComputedStyle(grid).padding,
            partial: [...grid.children].filter(child => {
              const bounds = child.getBoundingClientRect();
              return bounds.bottom > clip.top + 0.5 && bounds.top < clip.bottom - 0.5
                && (bounds.top < clip.top - 0.5 || bounds.bottom > clip.bottom + 0.5);
            }).length };
        });
        await page.screenshot({ path: join(output, `${width}x${height}-${end}.png`) });
        const evidence = JSON.stringify(geometry);
        // Four CSS pixels of focus ring also scale with the tile's transform.
        const ring = 4 * 1.035;
        assert.ok(geometry.tile.bottom + ring <= geometry.clip.bottom + 0.5, `Bottom clipped: ${evidence}`);
        assert.ok(geometry.tile.top - ring >= geometry.clip.top - 0.5, `Top clipped: ${evidence}`);
        assert.ok(geometry.tile.left - ring >= geometry.clip.left - 0.5, `Left clipped: ${evidence}`);
        assert.ok(geometry.tile.right + ring <= geometry.clip.right + 0.5, `Right clipped: ${evidence}`);
        assert.ok(geometry.clip.bottom <= geometry.footerTop, `Footer overlap: ${evidence}`);
        // The scrollport must reach the footer: snapping it down to whole rows
        // left up to a full row of dead space and capped the picker at two rows.
        assert.ok(geometry.footerTop - geometry.clip.bottom <= 8,
          `Scrollport stops short of the footer: ${evidence}`);
      }
      await page.close();
    }
  } finally { await browser.close(); }
});

test('process viewport shows five complete rows initially and after focus scrolling', browserOptions, async () => {
  let onFocus;
  const visit = node => {
    if (ts.isJsxAttribute(node) && node.name.getText(ast) === 'onFocus'
      && node.initializer?.getText(ast).includes('.ph-process-list')) onFocus = node.initializer.expression;
    ts.forEachChild(node, visit);
  };
  visit(ast);
  assert.ok(onFocus, 'Exercise the actual process-row focus handler');
  const focusCode = ts.transpileModule(`window.focusProcessRow = ${onFocus.getText(ast)}`, {
    compilerOptions: { target: ts.ScriptTarget.ES2022 },
  }).outputText;
  const browser = await openBrowser();
  try {
    for (const [width, height] of [[1280, 720], [1920, 1080], [1024, 600]]) {
      const page = await browser.newPage({ viewport: { width, height } });
      await page.setContent(`<style>${css}</style><div class="ph-dashboard">
        <header class="ph-header"><div class="ph-brand-fallback">playhub</div></header>
        <main class="ph-main"><div class="ph-page ph-page-scroll ph-system-page"><div class="ph-system-stack">
        <div class="ph-metrics-rail">${['CPU','GPU','RAM','Rete','Disco'].map(label =>
          `<div class="ph-history-card"><div class="ph-history-label">${label}</div><div class="ph-history-value">24%</div><svg class="ph-history-chart" viewBox="0 0 180 42"><polyline points="0,34 30,20 60,28 90,10 120,22 180,18"/></svg><div class="ph-history-detail">Stato attuale</div></div>`).join('')}</div>
        <div class="ph-system-lower"><div class="ph-tile ph-process-panel">
        <div class="ph-process-heading"><div class="ph-device-list-head" style="padding:0">Processi attivi</div>
        <div class="ph-restart-decky">Riavvia Decky</div></div><div class="ph-process-list">${Array.from({length:18},(_,i)=>
          `<div tabindex="0" class="ph-process-row"><span class="ph-process-name">Processo ${i+1}</span><span class="ph-process-stat">0.3%</span><span class="ph-process-stat">294 MB</span></div>`).join('')}</div>
        </div></div></div></div></main><div data-mock-footer style="position:absolute;bottom:0;height:64px;left:0;right:0;padding:18px 54px">A Seleziona &nbsp; B Indietro</div></div>`);
      await page.addScriptTag({ content: focusCode });
      await page.waitForTimeout(300);
      for (const index of [0, 2, 6]) {
        const row = page.locator('.ph-process-row').nth(index);
        await row.evaluate(element => {
          element.focus({ preventScroll: true });
          window.focusProcessRow({ currentTarget: element });
        });
        const geometry = await page.evaluate(() => {
          const list = document.querySelector('.ph-process-list');
          const clip = list.getBoundingClientRect();
          const rows = [...list.children].map(row => row.getBoundingClientRect());
          const panel = document.querySelector('.ph-process-panel').getBoundingClientRect();
          const page = document.querySelector('.ph-system-page');
          return { visible: rows.filter(row => row.top >= clip.top && row.bottom <= clip.bottom).length,
            focus: document.activeElement.getBoundingClientRect().toJSON(), clip: clip.toJSON(),
            panel: panel.toJSON(), footerTop: document.querySelector('[data-mock-footer]').getBoundingClientRect().top,
            outerScroll: page.scrollTop, scrollTop: list.scrollTop };
        });
        await page.screenshot({ path: join(outputDirectory(), `${width}x${height}-process-${index}.png`) });
        const evidence = JSON.stringify(geometry);
        assert.ok(geometry.visible >= 5, `Fewer than five complete process rows: ${evidence}`);
        assert.ok(geometry.panel.bottom >= geometry.footerTop - 90,
          `Process panel stops short of the footer: ${evidence}`);
        assert.ok(geometry.focus.top - 3 >= geometry.clip.top, evidence);
        assert.ok(geometry.focus.bottom + 3 <= geometry.clip.bottom, evidence);
        assert.ok(geometry.panel.bottom <= geometry.footerTop, evidence);
        assert.equal(geometry.outerScroll, 0, 'Five rows must fit without scrolling the whole page');
        if (index === 0) assert.equal(geometry.scrollTop, 0, 'Initial focus must not fake capacity by scrolling');
      }
      await page.close();
    }
  } finally { await browser.close(); }
});

test('window title grows with the approved card expansion without changing title-row height', browserOptions, async () => {
  const browser = await openBrowser();
  try {
    const page = await browser.newPage({ viewport: {width:1280,height:720} });
    await page.setContent(`<style>${css}</style><div class="ph-dashboard"><div class="ph-window-rail">
      ${[1,2].map(i=>`<div tabindex="0" class="ph-window-card"><div class="ph-window-title"><span>Applicazione ${i} - Un titolo molto lungo che deve restare leggibile senza uscire dalla finestra</span></div><div class="ph-window-frame"><div class="ph-window-placeholder">Anteprima finestra</div></div></div>`).join('')}</div></div>`);
    await page.waitForTimeout(350);
    const card = page.locator('.ph-window-card').first();
    const sample = () => card.evaluate(element => {
      const title = element.querySelector('.ph-window-title'), span = title.querySelector('span');
      return { font:parseFloat(getComputedStyle(span).fontSize), titleHeight:title.getBoundingClientRect().height,
        basis:parseFloat(getComputedStyle(element).flexBasis), duration:getComputedStyle(span).transitionDuration,
        curve:getComputedStyle(span).transitionTimingFunction, textOverflow:getComputedStyle(span).textOverflow,
        clippedText:span.scrollWidth>span.clientWidth, titleRight:title.getBoundingClientRect().right,
        textRight:span.getBoundingClientRect().right };
    });
    const before = await sample();
    await card.focus(); await page.waitForTimeout(120);
    const middle = await sample();
    await page.waitForTimeout(220);
    const after = await sample();
    await page.screenshot({path:join(outputDirectory(),'1280x720-window-title.png')});
    assert.equal(before.font,18); assert.equal(after.font,23);
    assert.ok(middle.font>before.font && middle.font<after.font,JSON.stringify({before,middle,after}));
    assert.ok(middle.basis>before.basis && middle.basis<after.basis);
    assert.equal(after.duration,'0.26s');
    assert.equal(after.curve,'cubic-bezier(0.2, 0.82, 0.2, 1)');
    assert.ok(Math.abs(before.titleHeight-after.titleHeight)<0.1, 'Title row keeps its fixed height within subpixel rounding');
    assert.equal(after.textOverflow,'ellipsis'); assert.equal(after.clippedText,true);
    assert.ok(after.textRight<=after.titleRight);
  } finally { await browser.close(); }
});
