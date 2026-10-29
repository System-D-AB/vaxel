// v2.0-bugs (B1, B3, B4, S1, S2, S3): StowDB Explorer v2.0.0-rc1 findings regression cases
import { JSDOM } from 'jsdom';
import { readBundle, makeAgent, assert, assertEqual, tick } from '../harness.mjs';

export const name = 'us8-v2-rc1-bugs';

export async function run() {
  // --- B1: vx-persist + vx-signals-if-missing on the same element preserves localStorage across reload ---
  {
    const { code } = readBundle();
    const html = `<!DOCTYPE html><html><head></head>
      <body vx-persist="tabs theme" vx-signals-if-missing='{"tabs":[],"theme":"auto"}'>
        <div id="r"></div>
      </body></html>`;
    const dom = new JSDOM(html, {
      url: 'http://localhost/shell',
      runScripts: 'outside-only',
      pretendToBeVisual: true,
    });
    const { window } = dom;
    // Pre-populate localStorage['vx:tabs'] before agent init
    window.localStorage.setItem('vx:tabs', JSON.stringify(['orders', 'customers']));
    window.EventSource = function () { this.addEventListener = () => {}; this.close = () => {}; };
    window.eval(code);
    if (window.document.readyState === 'loading') {
      window.document.dispatchEvent(new window.Event('DOMContentLoaded', { bubbles: true }));
    }

    assertEqual(
      JSON.stringify(window.Vaxel.get('tabs')),
      JSON.stringify(['orders', 'customers']),
      'B1: stored tabs loaded instead of being clobbered by vx-signals-if-missing []'
    );
    assertEqual(window.Vaxel.get('theme'), 'auto', 'B1: missing theme seeded with default');
    assertEqual(
      window.localStorage.getItem('vx:tabs'),
      JSON.stringify(['orders', 'customers']),
      'B1: localStorage[vx:tabs] preserved'
    );
  }

  // --- B3: vx-sse dynamic morph discovery & pagehide disconnect ---
  {
    const { code } = readBundle();
    const html = `<!DOCTYPE html><html><head></head><body><div id="pane" vx-region><a id="nav" href="/indexes" vx-get="/indexes" vx-target="#pane">Indexes</a></div></body></html>`;
    const dom = new JSDOM(html, {
      url: 'http://localhost/server',
      runScripts: 'outside-only',
      pretendToBeVisual: true,
    });
    const { window } = dom;
    const sseInstances = [];
    window.EventSource = function (url) {
      this.url = url;
      this.closed = false;
      this.addEventListener = function () {};
      this.close = function () { this.closed = true; };
      sseInstances.push(this);
    };
    window.fetch = function () {
      return Promise.resolve({
        headers: { get: (k) => (String(k).toLowerCase() === 'content-type' ? 'text/vnd.vaxel-patch+html' : '1') },
        text: () => Promise.resolve('<vx-patch target="#pane"><div id="pane" vx-region><div id="idx" vx-sse="/_vaxel/stream">Live</div></div></vx-patch>'),
      });
    };
    window.eval(code);
    if (window.document.readyState === 'loading') {
      window.document.dispatchEvent(new window.Event('DOMContentLoaded', { bubbles: true }));
    }

    assertEqual(sseInstances.length, 0, 'B3: no EventSource before [vx-sse] exists');

    window.document.getElementById('nav').click();
    await tick(window, 10);

    assertEqual(sseInstances.length, 1, 'B3: EventSource opened when [vx-sse] morphed into #pane');
    assertEqual(sseInstances[0].url, '/_vaxel/stream', 'B3: EventSource URL matches');
    assertEqual(sseInstances[0].closed, false, 'B3: EventSource is open');

    window.dispatchEvent(new window.Event('pagehide'));
    assertEqual(sseInstances[0].closed, true, 'B3: EventSource closed immediately on pagehide');
  }

  // --- B4: <form vx-get> with non-submit vx-on still intercepts submit ---
  {
    const { window } = makeAgent({
      url: 'http://localhost/shell',
      response: { body: '<vx-patch target="#tree"><div id="tree">filtered</div></vx-patch>' },
      html: `
        <form id="filter-form" method="get" action="/shell" vx-get="/shell" vx-target="#tree" vx-on="input | debounce 150 | distinct">
          <input id="f" name="filter" value="abc" />
        </form>
        <div id="tree"></div>
      `,
    });

    const form = window.document.getElementById('filter-form');
    const submitEvt = new window.Event('submit', { bubbles: true, cancelable: true });
    form.dispatchEvent(submitEvt);
    await tick(window, 10);

    assertEqual(submitEvt.defaultPrevented, true, 'B4: submit default prevented by Vaxel');
    assertEqual(window.__fetches.length, 1, 'B4: form submit sent Vaxel GET request despite vx-on="input | ..."');
    assert(window.__fetches[0].url.includes('filter=abc'), 'B4: form field serialized into GET URL');
  }

  // --- S1, S2, S3: multi-chain vx-on (;), showModal/close/focus sinks, and dataset.* in expressions ---
  {
    const { window } = makeAgent({
      url: 'http://localhost/shell',
      html: `
        <div vx-signals='{"confirmText":"","filter":"initial"}'>
          <input id="search"
                 vx-bind="filter"
                 vx-on="keydown | key Escape | set filter = ''; keydown | key Enter | focus #open-btn" />
          <button id="open-btn"
                  type="button"
                  vx-on="click | set confirmText = ''; click | showModal #drop-db">Open</button>
          <dialog id="drop-db">
            <input id="confirm-input" vx-bind="confirmText" />
            <button id="confirm-btn"
                    type="button"
                    data-match="prod-shop"
                    vx-attr:disabled="confirmText != dataset.match">Drop</button>
            <button id="cancel-btn" type="button" vx-on="click | close">Cancel</button>
          </dialog>
        </div>
      `,
    });

    const dialog = window.document.getElementById('drop-db');
    let modalShown = false;
    let modalClosed = false;
    dialog.showModal = function () { modalShown = true; dialog.setAttribute('open', ''); };
    dialog.close = function () { modalClosed = true; dialog.removeAttribute('open'); };

    const confirmBtn = window.document.getElementById('confirm-btn');
    assertEqual(confirmBtn.hasAttribute('disabled'), true, 'S3: disabled when confirmText != dataset.match');

    window.Vaxel.set('confirmText', 'prod-shop');
    assertEqual(confirmBtn.hasAttribute('disabled'), false, 'S3: enabled when confirmText == dataset.match');

    // Click open-btn -> runs both chains: sets confirmText = '' AND calls showModal #drop-db
    window.document.getElementById('open-btn').click();
    assertEqual(window.Vaxel.get('confirmText'), '', 'S1: first chain reset confirmText');
    assertEqual(modalShown, true, 'S2: second chain called dialog.showModal()');
    assertEqual(confirmBtn.hasAttribute('disabled'), true, 'S3: disabled again after confirmText reset');

    // Click cancel-btn -> closes enclosing <dialog>
    window.document.getElementById('cancel-btn').click();
    assertEqual(modalClosed, true, 'S2: close sink called dialog.close()');

    // Multi-chain on #search: Escape clears filter; Enter focuses #open-btn
    const searchInput = window.document.getElementById('search');
    searchInput.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    assertEqual(window.Vaxel.get('filter'), '', 'S1: Escape chain cleared filter');

    searchInput.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    assertEqual(window.document.activeElement, window.document.getElementById('open-btn'), 'S2: focus #open-btn focused button');
  }
}
