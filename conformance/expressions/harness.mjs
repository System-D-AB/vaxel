// jsdom harness for the vaxel expression evaluator & bindings (D-level).
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

  window.fetch = opts.fetchImpl || function () {
    return Promise.resolve({
      headers: { get: () => '' },
      text: () => Promise.resolve(''),
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

// Convert AST node from Vaxel._parseExpression / Vaxel._parseTemplate into canonical S-expression
export function toCanonical(ast) {
  if (!ast) return '';
  switch (ast.kind) {
    case 'Literal':
      if (ast.value === null) return 'null';
      if (typeof ast.value === 'boolean') return ast.value ? 'true' : 'false';
      if (typeof ast.value === 'number') return String(ast.value);
      return JSON.stringify(ast.value);
    case 'Identifier':
      return ast.name;
    case 'MemberAccess':
      return `(. ${toCanonical(ast.object)} ${ast.property})`;
    case 'UnaryOp':
      return `(u${ast.op} ${toCanonical(ast.argument)})`;
    case 'BinaryOp':
      return `(${ast.op} ${toCanonical(ast.left)} ${toCanonical(ast.right)})`;
    case 'Ternary':
      return `(? ${toCanonical(ast.condition)} ${toCanonical(ast.consequent)} ${toCanonical(ast.alternate)})`;
    case 'Call':
      if (ast.args.length === 0) return `(call ${ast.callee})`;
      return `(call ${ast.callee} ${ast.args.map(toCanonical).join(' ')})`;
    case 'Template':
      return `(tpl ${ast.parts.map((p) => (p.kind === 'Text' ? JSON.stringify(p.value) : toCanonical(p.expr))).join(' ')})`;
    default:
      throw new Error(`Unknown AST kind: ${ast.kind}`);
  }
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
