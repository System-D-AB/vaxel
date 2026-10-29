// jsdom harness for the vaxel client agent signal store (D-level, docs/10).
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

  const fetches = [];
  window.__fetches = fetches;
  window.fetch = opts.fetchImpl || function (url, init) {
    const record = { url: String(url), init: init || {} };
    fetches.push(record);
    const resolved = typeof opts.response === 'function' ? opts.response(record) : opts.response;
    const body = resolved && resolved.body !== undefined ? resolved.body : '';
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

  if (window.document.readyState === 'loading') {
    window.document.dispatchEvent(new window.Event('DOMContentLoaded', { bubbles: true, cancelable: false }));
  }

  return { dom, window };
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

export function tick(window, ms = 0) {
  return new Promise((r) => window.setTimeout(r, ms));
}
