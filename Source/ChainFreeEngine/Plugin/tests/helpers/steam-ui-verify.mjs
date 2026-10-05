// Mirror of the adapter's verification, kept executable outside Steam: the
// adapter itself imports @decky/ui, which only loads inside Steam's renderer.
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
const root = path.dirname(path.dirname(path.dirname(fileURLToPath(import.meta.url))));
const contract = readFileSync(path.join(root, 'src/steamUiContract.ts'), 'utf8');
export const REQUIRED = [...contract.matchAll(/'([A-Za-z_][A-Za-z0-9_]*)',/g)].map((match) => match[1]);
export const missing = (ui) => REQUIRED.filter((name) => ui?.[name] === undefined || ui?.[name] === null);
export const verify = (ui) => {
  const absent = missing(ui);
  if (absent.length) throw new Error('Steam UI components unavailable: ' + absent.join(', '));
  return ui;
};
