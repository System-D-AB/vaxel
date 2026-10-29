import { readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { makeAgent, assert, assertEqual } from './harness.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const FRAME_MS = 50;

const marbleFiles = readdirSync(here)
  .filter((f) => f.endsWith('.json') && !f.startsWith('package') && !f.startsWith('results'))
  .sort();

let passed = 0;
let failed = 0;
const results = {};

function runMarbleCase(spec) {
  const opStr = spec.args && spec.args.length > 0
    ? `${spec.operator} ${spec.args.join(' ')}`
    : spec.operator;

  let currentFrame = 0;
  const emittedAtFrame = new Map();

  const { window, clock } = makeAgent({
    html: `
      <form id="f" action="/search" vx-get vx-target="#r" vx-on="input | ${opStr}">
        <input id="inp" name="q" type="text" value="" />
      </form>
      <div id="r" vx-region></div>
    `,
    fetchImpl: (url) => {
      const u = new URL(url, 'http://localhost');
      emittedAtFrame.set(currentFrame, u.searchParams.get('q') || '');
      return Promise.resolve({
        headers: { get: (h) => (String(h).toLowerCase() === 'content-type' ? 'text/vnd.vaxel-patch+html' : '') },
        text: () => Promise.resolve('<vx-patch target="#r"><div id="r">ok</div></vx-patch>'),
      });
    },
  });

  const inp = window.document.getElementById('inp');
  const outChars = [];

  for (let i = 0; i < spec.input.length; i++) {
    currentFrame = i;
    if (i > 0) {
      clock.tick(FRAME_MS);
    }

    const ch = spec.input[i];
    if (ch !== '-' && ch !== '|') {
      inp.value = ch;
      inp.dispatchEvent(new window.Event('input', { bubbles: true, cancelable: true }));
    }

    if (emittedAtFrame.has(i)) {
      outChars.push(emittedAtFrame.get(i));
    } else if (ch === '|') {
      outChars.push('|');
    } else {
      outChars.push('-');
    }
  }

  const actual = outChars.join('');
  assertEqual(actual, spec.expect, `${spec.name} (${opStr})`);
  results[spec.operator] = { name: spec.name, pass: true, expect: spec.expect, actual };
}

for (const file of marbleFiles) {
  const spec = JSON.parse(readFileSync(resolve(here, file), 'utf8'));
  try {
    runMarbleCase(spec);
    console.log(`  PASS  ${spec.name} (${spec.operator})`);
    passed++;
  } catch (err) {
    console.error(`  FAIL  ${spec.name} (${spec.operator}): ${err.message}`);
    results[spec.operator] = { name: spec.name, pass: false, error: err.message };
    failed++;
  }
}

const extraTests = [
  {
    name: 'us1-filter-and-set-sink-over-signals-and-event-props',
    run: () => {
      const { window } = makeAgent({
        html: `
          <div vx-signals='{"minLen":3,"accepted":"","enterCount":0}'>
            <input id="box" type="text" vx-on="input | filter len(value) >= minLen | set accepted = toUpperCase(value)" />
            <input id="keybox" type="text" vx-on="keydown | key Enter | prevent | stop | set enterCount = enterCount + 1" />
            <span id="out" vx-text="accepted"></span>
            <span id="cnt" vx-text="enterCount"></span>
          </div>
        `,
      });
      const box = window.document.getElementById('box');
      const keybox = window.document.getElementById('keybox');
      const out = window.document.getElementById('out');
      const cnt = window.document.getElementById('cnt');

      box.value = 'hi';
      box.dispatchEvent(new window.Event('input', { bubbles: true }));
      assertEqual(out.textContent, '', 'filter rejects length < 3');

      box.value = 'vaxel';
      box.dispatchEvent(new window.Event('input', { bubbles: true }));
      assertEqual(out.textContent, 'VAXEL', 'filter passes length >= 3 and set sink writes signal');

      const escEvt = new window.KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
      keybox.dispatchEvent(escEvt);
      assertEqual(cnt.textContent, '0', 'key Enter ignores Escape');
      assert(!escEvt.defaultPrevented, 'Escape not prevented');

      const enterEvt = new window.KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true });
      keybox.dispatchEvent(enterEvt);
      assertEqual(cnt.textContent, '1', 'key Enter increments enterCount');
      assert(enterEvt.defaultPrevented, 'prevent operator called preventDefault');
    },
  },
  {
    name: 'us1-sources-interval-floor-visible-load-and-scopes',
    run: () => {
      const { window, clock, observers } = makeAgent({
        html: `
          <div id="wrap" vx-signals='{"ticks":0,"loaded":false,"seen":false,"winKey":"","outsideClosed":false}'>
            <div id="poller" vx-on="interval 200 | set ticks = ticks + 1"></div>
            <div id="loader" vx-on="load | set loaded = true"></div>
            <div id="vis" vx-on="visible | once | set seen = true"></div>
            <div id="modal" vx-on="keydown window | key Escape | set winKey = 'esc'">
              <button id="inside">Inside</button>
            </div>
            <div id="dropdown" vx-on="click outside | set outsideClosed = true">
              <span id="drop-child">Child</span>
            </div>
            <button id="outside-btn">Outside</button>
          </div>
        `,
      });

      assertEqual(window.Vaxel.get('loaded'), true, 'load source fires on initialization');
      assertEqual(window.Vaxel.get('ticks'), 0, 'interval has not fired at t=0');

      // 200ms requested, but 1000ms floor enforced (US-1 AC-5)
      clock.tick(500);
      assertEqual(window.Vaxel.get('ticks'), 0, 'interval 1s floor prevents firing at 500ms');
      clock.tick(500);
      assertEqual(window.Vaxel.get('ticks'), 1, 'interval fires at 1000ms');

      // Trigger visible via IntersectionObserver
      assert(observers.length > 0, 'IntersectionObserver registered for visible source');
      const visEl = window.document.getElementById('vis');
      observers[0].cb([{ target: visEl, isIntersecting: true }]);
      assertEqual(window.Vaxel.get('seen'), true, 'visible source sets seen = true');

      // Window keydown scope
      window.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
      assertEqual(window.Vaxel.get('winKey'), 'esc', 'window scope listener receives keydown');

      // Outside click scope
      window.document.getElementById('drop-child').click();
      assertEqual(window.Vaxel.get('outsideClosed'), false, 'click inside element does not fire outside trigger');
      window.document.getElementById('outside-btn').click();
      assertEqual(window.Vaxel.get('outsideClosed'), true, 'click outside element fires outside trigger');
    },
  },
  {
    name: 'us3-takeUntil-morph-disposes-timers-listeners-and-abandons-inflight',
    run: async () => {
      let resolveInFlight = null;
      const { window, clock } = makeAgent({
        html: `
          <div id="pane" vx-region vx-signals='{"ticks":0}'>
            <a id="slow-link" href="/slow" vx-get="/slow" vx-target="#pane">Load slow</a>
            <div id="ticker" vx-on="interval 1000 | set ticks = ticks + 1"></div>
          </div>
          <a id="fast-nav" href="/other" vx-get="/other" vx-target="#pane">Other</a>
        `,
        fetchImpl: (url, init) => {
          if (String(url).includes('/slow')) {
            return new Promise((resolve, reject) => {
              if (init && init.signal) {
                init.signal.addEventListener('abort', () => {
                  const err = new Error('Aborted');
                  err.name = 'AbortError';
                  reject(err);
                });
              }
              resolveInFlight = () => resolve({
                headers: { get: (h) => (String(h).toLowerCase() === 'content-type' ? 'text/vnd.vaxel-patch+html' : '') },
                text: () => Promise.resolve('<vx-patch target="#pane"><div id="pane" vx-region>STALE SLOW</div></vx-patch>'),
              });
            });
          }
          return Promise.resolve({
            headers: { get: (h) => (String(h).toLowerCase() === 'content-type' ? 'text/vnd.vaxel-patch+html' : '') },
            text: () => Promise.resolve('<vx-patch target="#pane"><div id="pane" vx-region>FRESH OTHER</div></vx-patch>'),
          });
        },
      });

      // 1. Start slow request from inside #pane
      window.document.getElementById('slow-link').click();
      assert(resolveInFlight !== null, 'slow request started');

      // 2. Replace #pane via #fast-nav
      window.document.getElementById('fast-nav').click();
      await new Promise((r) => setImmediate(r));
      await new Promise((r) => setImmediate(r));

      assertEqual(window.document.getElementById('pane').textContent, 'FRESH OTHER', 'pane updated to FRESH OTHER');

      // 3. Even if slow request resolves now, it was abandoned (takeUntil morph)
      resolveInFlight();
      await new Promise((r) => setImmediate(r));
      await new Promise((r) => setImmediate(r));
      assertEqual(window.document.getElementById('pane').textContent, 'FRESH OTHER', 'abandoned slow request did not overwrite pane');

      // 4. Ticker inside replaced #pane was disposed
      clock.tick(3000);
      assertEqual(window.Vaxel.get('ticks'), 0, 'disposed interval timer never ticks after morph removal');
      assertEqual(clock.activeTimerCount, 0, 'zero active timers after element removal');
    },
  },
  {
    name: 'us3-1000-morph-cycles-no-leaked-timers-or-listeners',
    run: () => {
      const { window, clock } = makeAgent({
        html: `
          <div id="pane" vx-region vx-signals='{"k":0}'>
            <button id="b" vx-on="keydown window | key Enter | set k = k + 1">Btn</button>
            <div id="t" vx-on="interval 1000 | set k = k + 1"></div>
          </div>
        `,
      });

      // Apply 1000 morph cycles replacing #pane's children
      for (let i = 0; i < 1000; i++) {
        window.Vaxel._applyPatchXml(
          `<vx-patch target="#pane" mode="morph"><div id="pane" vx-region><button id="b-${i % 2}" vx-on="keydown window | key Enter | set k = k + 1">Btn</button><div id="t-${i % 2}" vx-on="interval 1000 | set k = k + 1"></div></div></vx-patch>`
        );
      }

      assertEqual(clock.activeTimerCount, 1, 'after 1000 morphs, only 1 interval timer is active');
      window.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
      assertEqual(window.Vaxel.get('k'), 1, 'after 1000 morphs, window keydown listener fires exactly once');
    },
  },
];

for (const t of extraTests) {
  try {
    await t.run();
    console.log(`  PASS  ${t.name}`);
    passed++;
  } catch (err) {
    console.error(`  FAIL  ${t.name}: ${err.message}`);
    failed++;
  }
}

writeFileSync(resolve(here, 'results.browser.json'), JSON.stringify(results, null, 2), 'utf8');
console.log(`\n${passed} passed, ${failed} failed`);
if (failed > 0) process.exit(1);
