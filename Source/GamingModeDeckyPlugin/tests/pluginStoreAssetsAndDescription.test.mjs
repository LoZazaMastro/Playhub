import assert from "node:assert/strict";
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { spawnSync } from "node:child_process";
import test from "node:test";
import vm from "node:vm";
import ts from "typescript";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const compile = (source) => ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, esModuleInterop: true },
}).outputText;
const modules = new Map();
function load(file) {
  if (modules.has(file)) return modules.get(file);
  const exports = {};
  modules.set(file, exports);
  vm.runInNewContext(compile(readFileSync(file, "utf8")), {
    exports, URL, Set, Map, Date, Number, JSON, Intl,
    require: (specifier) => {
      const path = resolve(dirname(file), specifier);
      if (/\.(png|jpg)$/.test(path)) return path;
      if (path.endsWith(".json")) return JSON.parse(readFileSync(path, "utf8"));
      return load(path + ".ts");
    },
  });
  return exports;
}
const assets = load(join(root, "src/pluginStoreAssets.ts"));
const data = load(join(root, "src/pluginStoreData.ts"));

test("every offered native catalog plugin uses its real desktop JPG and excludes integrated legacy plugins", () => {
  const catalog = JSON.parse(readFileSync(join(root, "../../catalog/plugins.json"), "utf8"));
  const native = catalog.plugins.filter((plugin) => plugin.active !== false && plugin.catalogSource === "playhub");
  assert.equal(native.length, 11);
  assert.ok(!native.some(plugin => ["LoZazaMastro/Quick-Settings", "LoZazaMastro/Shortcuts"].includes(plugin.repository)));
  for (const plugin of native) {
    const cover = assets.bundledPluginCover(` ${plugin.repository.toUpperCase()} `);
    const expected = join(root, "../Playhub/Assets/PluginImages", plugin.name + ".jpg");
    assert.equal(cover, expected, plugin.repository);
    const bytes = readFileSync(cover);
    assert.equal(bytes.subarray(0, 3).toString("hex"), "ffd8ff");
    assert.ok(bytes.length > 100_000, `unexpected placeholder: ${cover}`);
  }
  assert.equal(assets.bundledPluginCover("constructor"), "");
  assert.equal(assets.bundledPluginCover("__proto__"), "");
  assert.equal(assets.bundledPluginCover("someone/Now-Playing"), "");
});

test("cover fallbacks prefer offline artwork then real catalog/README media before GitHub previews", () => {
  const sources = Array.from(assets.pluginCoverSources("LoZazaMastro/Now-Playing", "https://example.com/cover.jpg", [
    "https://example.com/cover.jpg", "javascript:alert(1)", "http://example.com/a.jpg", "https://example.com/readme.png",
  ]));
  assert.equal(sources[0], assets.bundledPluginCover("LoZazaMastro/Now-Playing"));
  assert.deepEqual(sources.slice(1), [
    "https://example.com/cover.jpg", "https://example.com/readme.png",
    "https://opengraph.githubassets.com/playhub-store/LoZazaMastro/Now-Playing",
  ]);
  assert.deepEqual(Array.from(assets.pluginCoverSources("../bad")), []);
});

test("repository media reject schemes and invalid repository/branch values", () => {
  for (const target of ["javascript:alert(1)", "DATA:image/png,x", "http://example.com/a.png", "//evil.example/a.png", "..\\a.png"]) {
    assert.equal(data.resolveRepositoryMediaUrl("owner/repo", "main", target), "");
  }
  assert.equal(data.resolveRepositoryMediaUrl("../repo", "main", "a.png"), "");
  assert.equal(data.resolveRepositoryMediaUrl("owner/repo", "../main", "a.png"), "");
  assert.equal(data.resolveRepositoryMediaUrl("owner/repo", "main", "./assets/a.png"), "https://raw.githubusercontent.com/owner/repo/main/assets/a.png");
});

const browser = process.env.PLAYHUB_TEST_BROWSER || [
  "C:/Program Files/Google/Chrome/Application/chrome.exe",
  "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
  "/usr/bin/chromium", "/usr/bin/google-chrome",
].find(existsSync);

test("description parser uses a real inert browser DOM and emits reader-compatible Markdown", { skip: !browser }, () => {
  const directory = mkdtempSync(join(tmpdir(), "playhub-description-test-"));
  try {
    const compiled = compile(readFileSync(join(root, "src/pluginStoreDescription.ts"), "utf8"));
    const script = `const exports = {};\n${compiled}\n` + String.raw`
      const failures = [];
      const parse = exports.descriptionToMarkdown;
      function equal(actual, expected, name) { if (actual !== expected) failures.push({name, actual, expected}); }
      equal(parse('<h2>Title &amp; more</h2><p>Hello <strong>bold</strong>.</p><ul><li>One</li><li>Two</li></ul>'),
        '## Title & more\n\nHello **bold**.\n\n- One\n- Two', 'headings, entities, bold, lists');
      equal(parse('<ol><li>First</li><li>Second</li></ol><blockquote>Quote<br>Next</blockquote>'),
        '1. First\n2. Second\n\n> Quote\n> Next', 'ordered list and quote');
      equal(parse('<table><tr><th>Name</th><th>Value</th></tr><tr><td>Audio</td><td><b>On</b></td></tr></table>'),
        '- **Name:** Audio; **Value:** **On**', 'readable table');
      equal(parse('<a href="https://example.com/a(b)">Safe</a> <a href="javascript:alert(1)">Unsafe</a> <a href="//example.com/path">Relative</a>', 'https://github.com/owner/repo/'),
        '[Safe](https://example.com/a%28b%29) Unsafe [Relative](https://example.com/path)', 'links');
      equal(parse('<a href="jav&#x61;script:alert(1)">Bad</a><a href="data:text/html,hi">Data</a><a href="https://user:pass@example.com">Credentials</a>'),
        'BadDataCredentials', 'encoded schemes and credentials');
      equal(parse('<script>window.executed = true</script><style>bad css</style><iframe srcdoc="bad">hidden</iframe><svg><script>bad</script></svg><p onclick="window.executed=true">Visible<img src="https://invalid.example/track" onerror="window.executed=true"></p>'),
        'Visible', 'active content removed');
      equal(window.executed, undefined, 'no scripts executed');
      equal(parse('<p>Unclosed <b>bold'), 'Unclosed **bold**', 'malformed HTML');
      equal(parse('<div>\n\n# Markdown\n\n**Existing**\n\n- List\n\n</div>'),
        '# Markdown\n\n**Existing**\n\n- List', 'mixed markdown preserved');
      equal(parse('## Native\n\n\u2022 First\n\u2022 Second'), '## Native\n\n- First\n- Second', 'native bullets');
      equal(parse('<p hidden>Hidden</p><!-- comment --><p>Kept</p>'), 'Kept', 'hidden content');
      equal(parse('[](https://github.com/LoZazaMastro/Launch-Curtain/releases/latest) [](LICENSE)'), '', 'empty badge links');
      equal(parse('[]([https://example.com](https://example.com)) [Read more](https://example.com)'), '[Read more](https://example.com)', 'empty nested links');
      equal(parse('<a href="https://example.com"><img src="badge.png"></a>'), '', 'image-only HTML link');
      document.body.textContent = failures.length ? JSON.stringify(failures) : 'DESCRIPTION_TESTS_OK';
    `;
    const html = join(directory, "test.html");
    writeFileSync(html, `<html><body><script>eval(atob('${Buffer.from(script).toString("base64")}'))</script></body></html>`);
    const result = spawnSync(browser, [
      "--headless", "--disable-gpu", "--no-first-run", "--no-default-browser-check",
      "--disable-background-networking", "--host-resolver-rules=MAP * ~NOTFOUND",
      `--user-data-dir=${join(directory, "profile")}`, "--dump-dom", pathToFileURL(html).href,
    ], { encoding: "utf8", timeout: 30_000, windowsHide: true });
    assert.equal(result.error, undefined, String(result.error));
    assert.match(result.stdout, /<body>DESCRIPTION_TESTS_OK<\/body>/, result.stdout + result.stderr);
  } finally {
    rmSync(directory, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 });
  }
});

