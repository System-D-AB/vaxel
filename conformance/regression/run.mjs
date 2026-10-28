// Runs every regression case. Exits 0 when all pass, 1 otherwise.
// Each case module exports { name, run } where run() throws on failure.
import { readdirSync } from 'node:fs';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { dirname, resolve } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const casesDir = resolve(here, 'cases');

const files = readdirSync(casesDir).filter((f) => f.endsWith('.mjs')).sort();

let failures = 0;
let passed = 0;

for (const file of files) {
  const mod = await import(pathToFileURL(resolve(casesDir, file)).href);
  const label = mod.name || file;
  try {
    await mod.run();
    passed++;
    console.log(`PASS  ${label}`);
  } catch (err) {
    failures++;
    console.error(`FAIL  ${label}`);
    console.error(`      ${err && err.message ? err.message : err}`);
  }
}

console.log(`\n${passed} passed, ${failures} failed, ${files.length} total`);
process.exit(failures === 0 ? 0 : 1);
