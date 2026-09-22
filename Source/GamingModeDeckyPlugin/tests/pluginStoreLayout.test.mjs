import assert from "node:assert/strict";
import { readFileSync, mkdirSync } from "node:fs";
import { createRequire } from "node:module";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";
import vm from "node:vm";
import ts from "typescript";

const here = dirname(fileURLToPath(import.meta.url));
const source = readFileSync(join(here, "..", "src", "PluginStorePage.tsx"), "utf8");
const index = readFileSync(join(here, "..", "src", "index.tsx"), "utf8");
const layoutSource = readFileSync(join(here, "..", "src", "pluginStoreLayout.ts"), "utf8");
const layoutCompiled = ts.transpileModule(layoutSource, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText;
const layoutExports = {};
vm.runInNewContext(layoutCompiled, { exports: layoutExports });

function section(start, end) {
  return source.slice(source.indexOf(start), source.indexOf(end));
}

test("native Store has Discover, Search and Manage tabs in that order", () => {
  const tabBlock = source.match(/tabs=\{\[([\s\S]*?)\]\}/)?.[1] ?? "";
  assert.match(source, /<Tabs\b/);
  assert.deepEqual(
    Array.from(tabBlock.matchAll(/id:\s*"(discover|search|manage)"/g), (match) => match[1]),
    ["discover", "search", "manage"],
  );
  assert.match(source, /GamepadButton\.BUMPER_LEFT/);
  assert.match(source, /GamepadButton\.BUMPER_RIGHT/);
  assert.equal((tabBlock.match(/footer/g) ?? []).length, 3);
});

test("Featured is one controller-driven slide with no timer", () => {
  const slider = section("function FeaturedSlider", "function DiscoverTab");
  assert.match(slider, /ph-store-featured-slide/);
  assert.match(slider, /GamepadButton\.DIR_LEFT/);
  assert.match(slider, /GamepadButton\.DIR_RIGHT/);
  assert.match(slider, /move\(-1\)/);
  assert.match(slider, /move\(1\)/);
  assert.doesNotMatch(slider, /setInterval|setTimeout/);
  assert.match(source, /featuredPlugins\(shared\.plugins,\s*10\)/);
  assert.match(slider, /ph-store-featured-dots/);
  assert.doesNotMatch(slider, /ph-store-featured-arrow|ph-store-featured-count/);
  assert.doesNotMatch(source, /\{shared.copy.featured\}/);
  assert.match(source, /height:clamp\(240px,40vh,360px\)/);
  assert.match(source, /grid-template-rows:38px 66px minmax\(0,1fr\) 22px/);
});

test("details group artwork left and metadata right, with activatable media", () => {
  const detail = section("function PluginDetail", "function CategoryLink");
  assert.ok(detail.indexOf("ph-store-detail-hero") < detail.indexOf("ph-store-detail-summary"));
  assert.doesNotMatch(detail, /<h2>\{shared.copy.description\}/);
  assert.match(source, /function MediaViewer/);
  assert.match(source, /showModal\(<MediaViewer/);
  assert.match(source, /option.id === "all" \? option.label : <SourceBadge/);
  assert.doesNotMatch(source, /text-decoration:underline; text-decoration-thickness/);
});

test("back press suppression survives a detail-to-category remount", () => {
  assert.match(source, /let lastStoreCancelAt = -Infinity/);
  assert.match(source, /now - lastStoreCancelAt < 420/);
});

test("the Store uses rows only and removes obsolete browse controls", () => {
  assert.match(source, /function PluginRows/);
  assert.match(source, /className="ph-store-list"/);
  assert.doesNotMatch(source, /type ViewMode|function ViewToggle|ph-store-grid|TbLayoutGrid|<Dropdown/);
  assert.doesNotMatch(source, /copy\.installedOnly|kind:\s*"all"|copy\.allPlugins/);
  const category = section("function CategoryLink", "function FeaturedSlider");
  assert.doesNotMatch(category, /TbChevronRight/);
});

test("sorting is shown at the right and opened with X instead of a focusable dropdown", () => {
  assert.match(source, /data-layout="title-left-sort-right"/);
  assert.match(source, /\.ph-store-page-header\s*\{[^}]*grid-template-columns:minmax\(0,1fr\) auto/s);
  assert.match(source, /function SortPickerModal/);
  assert.match(source, /showModal\(/);
  assert.match(source, /onOptionsButton=\{showSort\}/);
  assert.match(source, /onOptionsActionDescription=\{shared\.copy\.sortBy\}/);
  assert.match(source, /initialSort="name"/);
  assert.doesNotMatch(source, /newestPlugins/);
  assert.doesNotMatch(source, /const categories = \[\s*"Novità"/);
  assert.doesNotMatch(source, /function SortDropdown/);
});

test("search is compact, has no clear X, no installed-only switch and keeps filters", () => {
  const search = section("function SearchTab", "export function PluginStorePage");
  assert.equal((source.match(/<TextField\b/g) ?? []).length, 1);
  assert.match(search, /<SourceFilters/);
  assert.ok(search.indexOf("<SourceFilters") < search.indexOf("<PluginRows"));
  assert.doesNotMatch(search, /bShowClearAction|bAlwaysShowClearAction|installedOnly|ViewToggle/);
  assert.match(source, /\.ph-store-search-shell input\s*\{[^}]*height:40px/s);
});

test("browse rows open details while Manage exposes only icon actions", () => {
  const row = section("function PluginRow", "function PluginRows");
  assert.match(row, /if \(manage \|\| !onOpen\) return <div/);
  assert.match(row, /ph-store-row-openable/);
  assert.match(row, /<PluginActionButton plugin=\{plugin\} shared=\{shared\}/);
  assert.match(row, /<UninstallButton plugin=\{plugin\} shared=\{shared\}/);
  assert.match(source, /aria-label=\{label\}/);
  assert.match(source, /aria-label=\{shared\.copy\.uninstall\}/);
  assert.doesNotMatch(source, />GitHub<\/|openExternal\(/);
});

test("covers and graphical source badges are used in rows, Featured and details", () => {
  assert.match(source, /pluginCoverSources\(plugin\.repository/);
  assert.match(source, /SOURCE_BADGES\.playhub/);
  assert.match(source, /SOURCE_BADGES\.deckyStore/);
  assert.match(source, /<TbBrandGithub \/>/);
  assert.match(source, /ph-store-detail-hero \.ph-store-image \{ object-fit:cover; object-position:center;/);
});

test("detail media follows actions and the reader supports A, B and smooth scrolling", () => {
  const detail = section("function PluginDetail", "function CategoryLink");
  assert.ok(detail.indexOf("ph-store-actions") < detail.indexOf("ph-store-media-section"));
  assert.ok(detail.indexOf("ph-store-media-section") < detail.indexOf("<ScrollReader"));
  const reader = section("function ScrollReader", "function SafeMedia");
  assert.match(reader, /onActivate=/);
  assert.match(reader, /onOKButton=/);
  assert.match(reader, /if \(reading\) \{ setReading\(false\); return; \}/);
  assert.match(reader, /onBack\(\)/);
  assert.match(reader, /scrollBy\(\{[^}]*behavior: "smooth"/s);
});

test("filters remain mounted for empty collections and layouts remain bounded", () => {
  const collection = section("function PluginCollection", "function PluginDetail");
  assert.ok(collection.indexOf("<SourceFilters") < collection.indexOf("<PluginRows"));
  assert.doesNotMatch(source, /WebView|MainWindow|Playhub\.exe|desktop app/i);
  const narrow = layoutExports.storeLayoutForWidth(560);
  const normal = layoutExports.storeLayoutForWidth(1280);
  assert.equal(narrow.headerColumns, "minmax(0,1fr) auto");
  assert.equal(normal.headerColumns, "minmax(0,1fr) auto");
});

test("detail and category back actions stay inside the Store", () => {
  const discover = section("function DiscoverTab", "function ManageTab");
  assert.match(discover, /view\.kind === "detail"/);
  assert.match(discover, /view\.category \? \{ kind: "category"/);
  assert.match(discover, /view\.kind === "category"/);
  const search = section("function SearchTab", "export function PluginStorePage");
  assert.match(search, /if \(detail\) \{ setDetail\(null\); return; \}/);
});

test("QAM Store has an untitled section and a download animation before the description", () => {
  const section = index.indexOf("store={<PanelSection>");
  assert.ok(section >= 0);
  assert.ok(section < index.indexOf("label={local.haptics}"));
  assert.match(index.slice(section), /<PluginStoreIntro description=\{getPlayhubDiscoverCopy\(locale\)\[1\]\} button=\{<ButtonItem[^>]+onClick=\{openPluginStore\}/);
  assert.match(index, /<PluginStoreIntro description=/);
  assert.doesNotMatch(index, /title=\{getPlayhubDiscoverCopy\(locale\)\[0\]\}/);
  assert.match(index, /@keyframes phShortcutTravel \{ 0%,100% \{[^}]+\} 50% \{/);
  assert.doesNotMatch(index, /dashboardWindowSwitch/);
});

test("QAM download animation follows button width rather than description length", () => {
  const intro = readFileSync(join(here, "..", "src", "PluginStoreIntro.tsx"), "utf8");
  assert.match(intro, /padding:0 0 14px/);
  assert.match(intro, /const scale = node.clientWidth \/ 174/);
  assert.match(intro, /setHeight\(Math.ceil\(84 \* scale\)\)/);
  assert.doesNotMatch(intro, /getBoundingClientRect\(\).height \* 3/);
  assert.match(intro, /margin:6px auto 0/);
  assert.match(intro, /setOverlap\(bounds.height \/ 2\)/);
  assert.match(intro, /launch.getBoundingClientRect\(\).bottom - bounds.bottom/);
  assert.match(intro, /marginTop: -wrapperGap/);
  assert.match(intro, /ph-store-launch\{position:relative;z-index:1\}/);
});

test("actual Store clipping edge follows the real button bottom with native wrapper padding", {
  skip: !process.env.PLAYHUB_BROWSER_QA && "Set PLAYHUB_BROWSER_QA=1 for isolated Chromium geometry",
}, async () => {
  const { chromium } = createRequire(import.meta.url)("playwright");
  const browser = await chromium.launch({ headless: true,
    ...(process.env.PLAYHUB_CHROMIUM_PATH ? { executablePath: process.env.PLAYHUB_CHROMIUM_PATH } : {}) });
  const component = ts.transpileModule(readFileSync(join(here, "..", "src", "PluginStoreIntro.tsx"), "utf8"), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.React },
  }).outputText;
  const output = process.env.PLAYHUB_QA_OUTPUT ?? join(tmpdir(), "playhub-store-edge-qa");
  mkdirSync(output, { recursive: true });
  try {
    const page = await browser.newPage({ viewport: { width: 800, height: 650 } });
    for (const width of [174, 268, 480]) for (const buttonHeight of [32, 42, 80]) for (const padding of [0, 10, 24]) {
      await page.setContent('<body style="margin:0;background:#171a21;color:white;font-family:Arial"><div id="fixture" style="margin:40px"></div></body>');
      const geometry = await page.evaluate(({ component, width, buttonHeight, padding }) => {
        const fixture = document.getElementById("fixture");
        fixture.style.width = `${width}px`;
        // An inert hook host mounts the actual component. Layout, refs, its resize
        // callback and all CSS are real browser operations, not precomputed rects.
        const hooks = []; const effects = []; let cursor = 0; let pending = false;
        const React = {
          createElement: (type, props, ...children) => typeof type === "function" ? type(props ?? {}) : ({ type, props: props ?? {}, children: children.flat() }),
          useRef: value => { const i = cursor++; return hooks[i] ??= { current: value }; },
          useState: value => { const i = cursor++; if (!(i in hooks)) hooks[i] = value;
            return [hooks[i], next => { if (hooks[i] !== next) { hooks[i] = next; pending = true; } }]; },
          useLayoutEffect: effect => { const i = cursor++; if (!(i in hooks)) { hooks[i] = true; effects.push(effect); } },
        };
        const exports = {};
        new Function("exports", "require", component)(exports, name => {
          if (name === "./decky") return { SP_REACT: React };
          if (name === "react-icons/tb") return Object.fromEntries(["TbCheck", "TbDeviceGamepad2", "TbMusic", "TbPhoto"].map(key => [key, props => React.createElement("svg", { ...props, "aria-hidden": "true" })]));
          throw Error(`Unexpected import ${name}`);
        });
        function patch(parent, previous, vnode) {
          const text = typeof vnode === "string" || typeof vnode === "number";
          const compatible = previous && (text ? previous.nodeType === 3 : previous.nodeType === 1 && previous.localName === vnode.type);
          const element = compatible ? previous : text ? document.createTextNode(String(vnode)) : document.createElement(vnode.type);
          if (!compatible) { if (previous) parent.replaceChild(element, previous); else parent.appendChild(element); }
          if (text) { element.textContent = String(vnode); return element; }
          for (const [key, value] of Object.entries(vnode.props)) {
            if (key === "ref") value.current = element;
            else if (key === "style") for (const [name, setting] of Object.entries(value)) element.style[name] = typeof setting === "number" ? `${setting}px` : setting;
            else element.setAttribute(key === "className" ? "class" : key, String(value));
          }
          const children = vnode.children.filter(child => child !== null && child !== undefined && child !== false);
          children.forEach((child, i) => patch(element, element.childNodes[i], child));
          while (element.childNodes.length > children.length) element.lastChild.remove();
          return element;
        }
        const button = React.createElement("div", { style: { paddingTop: 10, paddingBottom: padding } },
          React.createElement("button", { style: { display: "block", width: "100%", height: buttonHeight, padding: 0, border: "0", boxSizing: "border-box" } }, "Plugin Store"));
        const render = () => { cursor = 0; pending = false; patch(fixture, fixture.firstChild,
          exports.PluginStoreIntro({ description: "Store description", button })); };
        render();
        const cleanups = effects.map(effect => effect());
        for (let i = 0; pending && i < 5; i++) render();
        cleanups.forEach(cleanup => cleanup?.());
        const rect = selector => fixture.querySelector(selector).getBoundingClientRect().toJSON();
        const clip = fixture.querySelector(".ph-store-download");
        return { button: rect("button"), launch: rect(".ph-store-launch"), clip: rect(".ph-store-download"),
          inner: rect(".ph-store-download-viewport"), stage: rect(".ph-store-download-stage"), copy: rect(".ph-store-intro-copy"),
          overflow: getComputedStyle(clip).overflow, margin: parseFloat(getComputedStyle(clip).marginTop) };
      }, { component, width, buttonHeight, padding });
      const evidence = JSON.stringify({ width, buttonHeight, padding, geometry });
      assert.ok(Math.abs(geometry.launch.bottom - geometry.button.bottom - padding) < 0.1, `Fixture padding mismatch: ${evidence}`);
      assert.ok(Math.abs(geometry.clip.top - geometry.button.bottom) < 0.1, `Gap or overlap at actual button: ${evidence}`);
      assert.ok(Math.abs(geometry.inner.top - (geometry.button.top + geometry.button.height / 2)) < 0.1, `Origin moved: ${evidence}`);
      assert.equal(geometry.overflow, "hidden");
      assert.ok(Math.abs(geometry.margin + padding) < 0.1, `Wrapper gap not recovered: ${evidence}`);
      assert.ok(Math.abs(geometry.clip.width - geometry.button.width) < 0.1, `Width changed: ${evidence}`);
      assert.ok(Math.abs(geometry.stage.width - geometry.button.width) < 0.1, `Scale changed: ${evidence}`);
      assert.ok(Math.abs(geometry.copy.top - geometry.clip.bottom - 6) < 0.1, `Description gap changed: ${evidence}`);
      if (width === 268 && buttonHeight === 42) {
        console.log(evidence);
        await page.screenshot({ path: join(output, `store-wrapper-padding-${padding}.png`) });
      }
    }
  } finally { await browser.close(); }
});
