// jsdom harness for the vaxel client agent (D-level, docs/10 §8).
// Loads the shipped bundle into a jsdom window, stubs the network, and gives each
// case a fresh DOM. Used by the v1.1.0 regression fixtures.
import { JSDOM } from 'jsdom';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));

// Prefer the served copy (AspNetCore embeds it); fall back to the Client copy.
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

// Create a window with the bundle loaded and the agent initialised.
// opts.html       — body (or full doc) markup; a <div id="r"> default region if omitted.
// opts.url        — document URL (defaults to a shell page).
// opts.fetchImpl  — custom fetch; defaults to a recorder exposed as window.__fetches.
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

  // Network stubs: jsdom provides DOMParser, FormData, URL, localStorage.
  const fetches = [];
  window.__fetches = fetches;
  window.fetch = opts.fetchImpl || function (url, init) {
    const record = { url: String(url), init: init || {} };
    fetches.push(record);
    const resolved = typeof opts.response === 'function' ? opts.response(record) : opts.response;
    const body = resolved && resolved.body !== undefined ? resolved.body : '';
    // Default to a vaxel patch content-type so the agent takes the patch path and never the
    // navigation fallback (jsdom cannot navigate). Tests assert on the request, not the response.
    const headers = Object.assign(
      { 'Content-Type': 'text/vnd.vaxel-patch+html; charset=utf-8', 'VX-Protocol': '1' },
      (resolved && resolved.headers) || {},
    );
    return Promise.resolve({
      headers: { get: (k) => headers[k] ?? headers[String(k).toLowerCase()] ?? '' },
      text: () => Promise.resolve(body),
    });
  };
  window.EventSource = function () {
    this.addEventListener = function () {};
    this.close = function () {};
  };

  window.eval(code);

  // jsdom reports readyState 'loading' when we eval; the agent then waits for
  // DOMContentLoaded to bind its triggers. Fire it so init() runs, mirroring a real load.
  if (window.document.readyState === 'loading') {
    window.document.dispatchEvent(new window.Event('DOMContentLoaded', { bubbles: true, cancelable: false }));
  }

  return { dom, window };
}

// Minimal assertion helpers.
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

// Let queued microtasks/timers settle (fetch .then, setTimeout(50) announcers).
export function tick(window, ms = 0) {
  return new Promise((r) => window.setTimeout(r, ms));
}
