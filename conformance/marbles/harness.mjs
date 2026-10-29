// jsdom harness with virtual clock (fake timers) for vaxel trigger pipelines & marbles (D-level).
import { JSDOM } from 'jsdom';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));

const bundleCandidates = [
  resolve(here, '../../src/Vaxel.AspNetCore/wwwroot/_vaxel/vaxel.js'),
  resolve(here, '../../src/Vaxel.Client/wwwroot/_vaxel/vaxel.js'),
];

export function readBundle() {
  for (const p of bundleCandidates) {
    try {
      return { path: p, code: readFileSync(p, 'utf8') };
    } catch {
      /* try next */
    }
  }
  throw new Error('vaxel.js bundle not found at any known path.');
}

export function createVirtualClock(win) {
  let now = 1000;
  let nextId = 1;
  const tasks = new Map();

  function setTimeoutFake(fn, ms = 0) {
    const id = nextId++;
    const delay = Math.max(0, Number(ms) || 0);
    tasks.set(id, { id, fn, due: now + delay, interval: null });
    return id;
  }

  function clearTimeoutFake(id) {
    tasks.delete(id);
  }

  function setIntervalFake(fn, ms = 0) {
    const id = nextId++;
    const delay = Math.max(1, Number(ms) || 1);
    tasks.set(id, { id, fn, due: now + delay, interval: delay });
    return id;
  }

  function clearIntervalFake(id) {
    tasks.delete(id);
  }

  function tick(ms) {
    const target = now + ms;
    while (true) {
      let earliest = null;
      for (const t of tasks.values()) {
        if (t.due <= target && (!earliest || t.due < earliest.due || (t.due === earliest.due && t.id < earliest.id))) {
          earliest = t;
        }
      }
      if (!earliest) break;
      now = earliest.due;
      if (earliest.interval !== null) {
        earliest.due = now + earliest.interval;
      } else {
        tasks.delete(earliest.id);
      }
      earliest.fn();
    }
    now = target;
  }

  win.setTimeout = setTimeoutFake;
  win.clearTimeout = clearTimeoutFake;
  win.setInterval = setIntervalFake;
  win.clearInterval = clearIntervalFake;
  win.Date.now = () => now;
  if (win.performance) {
    win.performance.now = () => now;
  }

  return {
    get now() { return now; },
    get activeTimerCount() { return tasks.size; },
    tick,
  };
}

export function makeAgent(opts = {}) {
  const { code } = readBundle();
  const html = opts.html
    ? (opts.html.includes('<body') || opts.html.includes('<!DOCTYPE')
        ? opts.html
        : `<!DOCTYPE html><html><head></head><body>${opts.html}</body></html>`)
    : '<!DOCTYPE html><html><head></head><body><div id="r"></div></body></html>';

  const dom = new JSDOM(html, {
    url: opts.url || 'http://localhost/shell',
    runScripts: 'outside-only',
    pretendToBeVisual: true,
  });
  const { window } = dom;
  const clock = createVirtualClock(window);

  const calls = [];
  window.fetch = opts.fetchImpl || function (url, init) {
    calls.push({ url: String(url), init: init || {} });
    const body = (opts.response && opts.response.body) || '<vx-patch target="#r"><div id="r">ok</div></vx-patch>';
    return Promise.resolve({
      headers: { get: (h) => (String(h).toLowerCase() === 'content-type' ? 'text/vnd.vaxel-patch+html' : '') },
      text: () => Promise.resolve(body),
    });
  };

  window.EventSource = function () {
    this.addEventListener = function () {};
    this.close = function () {};
  };

  // Stub IntersectionObserver for `visible` source testing
  const observers = [];
  window.IntersectionObserver = function (cb) {
    const entry = { cb, elements: new Set(), disconnected: false };
    observers.push(entry);
    this.observe = (el) => entry.elements.add(el);
    this.unobserve = (el) => entry.elements.delete(el);
    this.disconnect = () => { entry.disconnected = true; entry.elements.clear(); };
  };
  window.__vxTestObservers = observers;

  window.eval(code);

  if (window.document.readyState === 'loading') {
    window.document.dispatchEvent(new window.Event('DOMContentLoaded', { bubbles: true, cancelable: false }));
  }

  return { dom, window, clock, calls, observers };
}

export function assertEqual(actual, expected, msg) {
  if (actual !== expected) {
    throw new Error(`${msg || 'assertEqual'}: expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}`);
  }
}

export function assert(cond, msg) {
  if (!cond) {
    throw new Error(msg || 'assertion failed');
  }
}
