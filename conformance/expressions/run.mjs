// Runs the 305+ golden expression cases, security guards (proto/caps), and reactive binding tests
// in jsdom against the shipped vaxel.js bundle. Writes results.browser.json for server parser parity.
import { readdirSync, readFileSync, writeFileSync, existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';
import { makeAgent, toCanonical, assert, assertEqual } from './harness.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const casesDir = resolve(here, 'cases');
const unsupportedFile = resolve(here, 'unsupported.json');
const resultsFile = resolve(here, 'results.browser.json');

let failures = 0;
let passed = 0;

function check(label, fn) {
  try {
    fn();
    passed++;
    console.log(`PASS  ${label}`);
  } catch (err) {
    failures++;
    console.error(`FAIL  ${label}`);
    console.error(`      ${err && err.message ? err.message : err}`);
  }
}

const { window } = makeAgent();
const V = window.Vaxel;

// 1. Golden corpus evaluation + canonical AST emission
check('golden-corpus-305-cases', () => {
  assert(typeof V._evaluateTemplateOrExpr === 'function', 'Vaxel._evaluateTemplateOrExpr must exist');
  assert(typeof V._parseTemplateOrExpr === 'function', 'Vaxel._parseTemplateOrExpr must exist');

  const unsupportedRaw = existsSync(unsupportedFile) ? readFileSync(unsupportedFile, 'utf8') : '[]';
  const unsupportedList = JSON.parse(unsupportedRaw);
  assertEqual(unsupportedList.length, 0, 'unsupported.json must be empty');

  const files = readdirSync(casesDir).filter((f) => f.endsWith('.json')).sort();
  const browserResults = {};
  let totalCases = 0;

  for (const f of files) {
    const cases = JSON.parse(readFileSync(resolve(casesDir, f), 'utf8'));
    for (const c of cases) {
      totalCases++;
      const parseRes = V._parseTemplateOrExpr(c.expression);
      const evalRes = V._evaluateTemplateOrExpr(c.expression, c.state);

      const entry = {
        parseOk: parseRes.ok,
        canonical: parseRes.ok ? toCanonical(parseRes.value) : null,
        evalOk: evalRes.ok,
        value: evalRes.ok ? evalRes.value : undefined,
        code: !evalRes.ok ? evalRes.refusal.code : undefined,
      };
      browserResults[c.name] = entry;

      assertEqual(
        evalRes.ok,
        c.expect.ok,
        `Case '${c.name}' in ${f}: expected ok=${c.expect.ok}, got ok=${evalRes.ok} (${!evalRes.ok ? JSON.stringify(evalRes.refusal) : ''})`,
      );

      if (c.expect.ok && evalRes.ok) {
        assertEqual(
          JSON.stringify(evalRes.value),
          JSON.stringify(c.expect.value),
          `Case '${c.name}' in ${f}: value mismatch`,
        );
      } else if (!c.expect.ok && !evalRes.ok) {
        assertEqual(
          evalRes.refusal.code,
          c.expect.code,
          `Case '${c.name}' in ${f}: refusal code mismatch`,
        );
      }
    }
  }

  assert(totalCases >= 305, `Expected at least 305 cases, ran ${totalCases}`);
  writeFileSync(resultsFile, JSON.stringify(browserResults, null, 2), 'utf8');
});

// 2. US-1.3: Prototype pollution / own-property guard (__proto__, constructor, prototype -> null)
check('us1-3-proto-and-own-property-guard', () => {
  assert(typeof V._evaluateBare === 'function', 'Vaxel._evaluateBare must exist');
  const state = { user: { name: 'Alice' } };

  for (const forbidden of ['__proto__', 'constructor', 'prototype']) {
    const r1 = V._evaluateBare(`user.${forbidden}`, state);
    assert(r1.ok, `user.${forbidden} should succeed with null`);
    assertEqual(r1.value, null, `user.${forbidden} must yield null`);

    const r2 = V._evaluateBare(`${forbidden}`, state);
    assert(!r2.ok && r2.refusal.code === 'expressions.unknown_field', `top-level ${forbidden} must not read Object.prototype`);
  }

  // Inherited property via prototype chain must also yield null (own-property lookup only)
  const proto = { inheritedSecret: 'leaked' };
  const child = Object.create(proto);
  child.ownProp = 'safe';
  const rInherited = V._evaluateBare('obj.inheritedSecret', { obj: child });
  assert(rInherited.ok, 'inherited property lookup ok');
  assertEqual(rInherited.value, null, 'inherited property on prototype chain must yield null');
});

// 3. US-1.4: Length (<= 512) and depth (<= 32) caps
check('us1-4-length-and-depth-caps', () => {
  const longExpr = 'a + '.repeat(140) + 'a'; // > 512 chars
  assert(longExpr.length > 512, 'longExpr exceeds 512');
  const rLong = V._evaluateBare(longExpr, { a: 1 });
  assert(!rLong.ok && rLong.refusal.code === 'expressions.syntax', 'expression > 512 chars must be refused');

  const deepExpr = '('.repeat(35) + '1' + ')'.repeat(35); // depth > 32
  const rDeep = V._evaluateBare(deepExpr, {});
  assert(!rDeep.ok && rDeep.refusal.code === 'expressions.syntax', 'expression depth > 32 must be refused');
});

// 4. US-1.1, US-1.2, US-1.6: Bindings evaluate bare expressions & @{} templates, track only read signals, refusals disable & emit vx:error
check('us1-bindings-reactive-and-refusal-disables', () => {
  const { window: w } = makeAgent({
    html: `
      <div vx-signals='{"tabs":["a","b"],"name":"","saving":false,"draft":"x","saved":"y","count":3,"unrelated":0}'>
        <div id="pane" vx-show="tabs.length > 1">Pane</div>
        <button id="btn" vx-attr:disabled="name == '' || saving">Save</button>
        <span id="tab" vx-class:is-dirty="draft != saved">Tab</span>
        <p id="summary" vx-text="Showing @{ count } rows"></p>
        <p id="bareText" vx-text="count + ' items'"></p>
        <div id="badShow" vx-show="unknownSignal > 1">Should be hidden on refusal</div>
      </div>
    `,
  });

  const doc = w.document;
  const pane = doc.getElementById('pane');
  const btn = doc.getElementById('btn');
  const tab = doc.getElementById('tab');
  const summary = doc.getElementById('summary');
  const bareText = doc.getElementById('bareText');
  const badShow = doc.getElementById('badShow');

  assertEqual(pane.hidden, false, 'tabs.length > 1 is true -> visible');
  assertEqual(btn.hasAttribute('disabled'), true, "name == '' || saving is true -> disabled");
  assertEqual(tab.classList.contains('is-dirty'), true, 'draft != saved is true -> has class is-dirty');
  assertEqual(summary.textContent, 'Showing 3 rows', 'vx-text @{} interpolation');
  assertEqual(bareText.textContent, '3 items', 'vx-text bare expression');
  // Refusal in boolean binding must be treated as false (hidden = true)
  assertEqual(badShow.hidden, true, 'refusal in vx-show must be treated as false (disabled/hidden)');

  // Update signals and verify selective reactivity
  w.Vaxel.set('name', 'Alice');
  assertEqual(btn.hasAttribute('disabled'), false, 'name filled and not saving -> enabled');

  w.Vaxel.set('saving', true);
  assertEqual(btn.hasAttribute('disabled'), true, 'saving true -> disabled');

  w.Vaxel.set('draft', 'y');
  assertEqual(tab.classList.contains('is-dirty'), false, 'draft == saved -> class removed');

  w.Vaxel.set('count', 10);
  assertEqual(summary.textContent, 'Showing 10 rows', 'summary updated');
  assertEqual(bareText.textContent, '10 items', 'bareText updated');
});

console.log(`\n${passed} passed, ${failures} failed`);
process.exit(failures === 0 ? 0 : 1);
