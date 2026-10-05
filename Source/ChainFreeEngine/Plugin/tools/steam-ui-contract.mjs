// Derives the Steam UI surface the real Playhub plugin uses, straight from its
// sources. The adapter and its test read the same list, so a new component in
// the plugin cannot silently reach an unverified host.
import { readFileSync, readdirSync, statSync } from 'node:fs';
import path from 'node:path';

const DESTRUCTURED = /const\s*\{([^}]*)\}\s*=\s*DFL\b/g;
const MEMBER = /\bDFL\.([A-Za-z_][A-Za-z0-9_]*)/g;

function walk(dir) {
  const files = [];
  for (const entry of readdirSync(dir)) {
    const full = path.join(dir, entry);
    if (statSync(full).isDirectory()) {
      if (entry === 'node_modules' || entry === 'dist' || entry === 'vendor') continue;
      files.push(...walk(full));
    } else if (/\.tsx?$/.test(entry)) files.push(full);
  }
  return files;
}

export function collectSteamUiNames(roots) {
  const names = new Set();
  for (const root of roots) {
    for (const file of walk(root)) {
      const code = readFileSync(file, 'utf8');
      for (const match of code.matchAll(DESTRUCTURED))
        for (const part of match[1].split(','))
          { const name = part.split(':')[0].trim(); if (name) names.add(name); }
      for (const match of code.matchAll(MEMBER)) names.add(match[1]);
    }
  }
  return [...names].sort();
}

if (import.meta.url === `file://${process.argv[1]?.split(path.sep).join('/')}` || process.argv[1]?.endsWith('steam-ui-contract.mjs')) {
  const roots = process.argv.slice(2);
  console.log(JSON.stringify(collectSteamUiNames(roots), null, 2));
}
