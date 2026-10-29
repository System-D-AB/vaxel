// v2.0-rc3-bugs (B6, S8, S9, S10, S11): StowDB Explorer v2.0.0-rc3 findings + transitions & progress bar regression cases
import { makeAgent, assert, assertEqual, tick } from '../harness.mjs';

export const name = 'us10-v2-rc3-bugs';

export async function run() {
  // --- B6 (1): vx-indicator="<signal>" toggles boolean signal during flight and drives vx-attr:disabled + vx-class ---
  {
    let resolveFetch;
    const { window } = makeAgent({
      url: 'http://localhost/query',
      html: `
        <body vx-signals-if-missing='{"busy":false}'>
          <form id="qform" vx-get="/query?run=1" vx-target="#results" vx-indicator="busy">
            <button id="sub" type="submit" vx-attr:disabled="busy">Run</button>
          </form>
          <div id="results" vx-region vx-class:is-loading="busy">Initial</div>
        </body>
      `,
      fetchImpl: () =>
        new Promise((resolve) => {
          resolveFetch = () =>
            resolve({
              headers: {
                get: (k) =>
                  ({
                    'content-type': 'text/vnd.vaxel-patch+html; charset=utf-8',
                    'vx-protocol': '1',
                  })[String(k).toLowerCase()] || '',
              },
              text: () =>
                Promise.resolve(
                  `<vx-patch target="#results"><div id="results" vx-region vx-class:is-loading="busy">Loaded</div></vx-patch>`
                ),
            });
        }),
    });

    const form = window.document.getElementById('qform');
    const sub = window.document.getElementById('sub');
    const results = window.document.getElementById('results');

    assertEqual(window.Vaxel.get('busy'), false, 'B6: busy initially false');
    assertEqual(sub.disabled, false, 'B6: submit initially enabled');

    form.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true }));

    assertEqual(window.Vaxel.get('busy'), true, 'B6: busy is true while request is in flight');
    assertEqual(sub.disabled, true, 'B6: vx-attr:disabled="busy" disables button during flight');
    assert(results.classList.contains('is-loading'), 'B6: vx-class:is-loading="busy" adds class during flight');

    resolveFetch();
    await tick(window, 20);

    assertEqual(window.Vaxel.get('busy'), false, 'B6: busy is false after response');
    assertEqual(sub.disabled, false, 'B6: submit re-enabled after response');
    assert(
      !window.document.getElementById('results').classList.contains('is-loading'),
      'B6: is-loading class removed after response'
    );
  }

  // --- B6 (2): vx-indicator="<unseededSignal>" creates signal on the fly without throwing ---
  {
    let resolveFetch;
    const { window } = makeAgent({
      url: 'http://localhost/query',
      html: `<button id="btn" vx-post="/run" vx-indicator="unseededBusy">Run</button>`,
      fetchImpl: () =>
        new Promise((resolve) => {
          resolveFetch = () =>
            resolve({
              headers: {
                get: (k) =>
                  ({
                    'content-type': 'text/vnd.vaxel-patch+html; charset=utf-8',
                    'vx-protocol': '1',
                  })[String(k).toLowerCase()] || '',
              },
              text: () => Promise.resolve(''),
            });
        }),
    });

    const btn = window.document.getElementById('btn');
    btn.dispatchEvent(new window.Event('click', { bubbles: true, cancelable: true }));
    assertEqual(window.Vaxel.get('unseededBusy'), true, 'B6: unseeded signal set to true in flight');

    resolveFetch();
    await tick(window, 20);
    assertEqual(window.Vaxel.get('unseededBusy'), false, 'B6: unseeded signal set to false after flight');
  }

  // --- B6 (3): vx-indicator="#id" selector form still toggles .vx-loading and aria-busy="true" ---
  {
    let resolveFetch;
    const { window } = makeAgent({
      url: 'http://localhost/query',
      html: `
        <button id="btn" vx-get="/q" vx-indicator="#spin">Go</button>
        <div id="spin">Spinner</div>
      `,
      fetchImpl: () =>
        new Promise((resolve) => {
          resolveFetch = () =>
            resolve({
              headers: {
                get: (k) =>
                  ({
                    'content-type': 'text/vnd.vaxel-patch+html; charset=utf-8',
                    'vx-protocol': '1',
                  })[String(k).toLowerCase()] || '',
              },
              text: () => Promise.resolve(''),
            });
        }),
    });

    const btn = window.document.getElementById('btn');
    const spin = window.document.getElementById('spin');
    btn.dispatchEvent(new window.Event('click', { bubbles: true, cancelable: true }));

    assert(spin.classList.contains('vx-loading'), 'B6 selector: .vx-loading added in flight');
    assertEqual(spin.getAttribute('aria-busy'), 'true', 'B6 selector: aria-busy="true" set in flight');

    resolveFetch();
    await tick(window, 20);

    assert(!spin.classList.contains('vx-loading'), 'B6 selector: .vx-loading removed after flight');
    assertEqual(spin.getAttribute('aria-busy'), null, 'B6 selector: aria-busy removed after flight');
  }

  // --- S8: Pre-fetch exception inside trigger handler emits vx:error (reason: 'trigger') and falls back to native form submit ---
  {
    const { window } = makeAgent({
      url: 'http://localhost/query',
      html: `<form id="f" action="/fallback" method="get" vx-get="/fallback"><button type="submit">Go</button></form>`,
    });

    const errors = [];
    window.document.addEventListener('vx:error', (e) => errors.push(e.detail));

    let nativeSubmitCalls = 0;
    window.HTMLFormElement.prototype.submit = function () {
      nativeSubmitCalls++;
    };

    window.Vaxel._test.failNextRequest = true;
    const form = window.document.getElementById('f');
    form.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true }));

    assertEqual(errors.length, 1, 'S8: vx:error emitted on trigger exception');
    assertEqual(errors[0].reason, 'trigger', 'S8: detail.reason is trigger');
    assertEqual(errors[0].target, '#f', 'S8: detail.target is #f');
    assertEqual(errors[0].doc, 'docs/04#error-reporting', 'S8: detail.doc is docs/04#error-reporting');
    assertEqual(nativeSubmitCalls, 1, 'S8: native HTMLFormElement.prototype.submit called as fallback');
  }

  // --- S9: vx-disable (valueless and vx-disable="#id") + pre-disabled element preservation ---
  {
    let resolveFetch;
    const { window } = makeAgent({
      url: 'http://localhost/query',
      html: `
        <form id="f1" vx-get="/q1" vx-disable="#run">
          <button id="run" type="submit">Run</button>
        </form>
        <form id="f2" vx-get="/q2" vx-disable="#save">
          <button id="save" type="submit" disabled>Save</button>
        </form>
        <button id="self-btn" vx-post="/act" vx-disable>Act</button>
      `,
      fetchImpl: () =>
        new Promise((resolve) => {
          resolveFetch = () =>
            resolve({
              headers: {
                get: (k) =>
                  ({
                    'content-type': 'text/vnd.vaxel-patch+html; charset=utf-8',
                    'vx-protocol': '1',
                  })[String(k).toLowerCase()] || '',
              },
              text: () => Promise.resolve(''),
            });
        }),
    });

    // Case 1: vx-disable="#run"
    const f1 = window.document.getElementById('f1');
    const runBtn = window.document.getElementById('run');
    assertEqual(runBtn.disabled, false, 'S9: #run initially enabled');
    f1.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true }));
    assertEqual(runBtn.disabled, true, 'S9: vx-disable="#run" disables target button in flight');
    resolveFetch();
    await tick(window, 20);
    assertEqual(runBtn.disabled, false, 'S9: #run re-enabled after completion');

    // Case 2: pre-disabled target stays disabled after completion
    const f2 = window.document.getElementById('f2');
    const saveBtn = window.document.getElementById('save');
    assertEqual(saveBtn.disabled, true, 'S9: #save initially disabled by server');
    f2.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true }));
    assertEqual(saveBtn.disabled, true, 'S9: #save remains disabled in flight');
    resolveFetch();
    await tick(window, 20);
    assertEqual(saveBtn.disabled, true, 'S9: #save stays disabled after completion (was already disabled)');

    // Case 3: valueless vx-disable on button itself
    const selfBtn = window.document.getElementById('self-btn');
    selfBtn.dispatchEvent(new window.Event('click', { bubbles: true, cancelable: true }));
    assertEqual(selfBtn.disabled, true, 'S9: valueless vx-disable disables host button in flight');
    resolveFetch();
    await tick(window, 20);
    assertEqual(selfBtn.disabled, false, 'S9: valueless vx-disable re-enables host button after completion');
  }

  // --- S10: vx-transition-name and vx-transition (View Transitions API + .vx-settling class) ---
  {
    const { window } = makeAgent({
      url: 'http://localhost/docs',
      html: `
        <div id="pane" vx-region vx-transition>
          <div id="card" vx-transition-name="hero-card">Card 1</div>
        </div>
      `,
    });

    const card = window.document.getElementById('card');
    assertEqual(card.style.viewTransitionName, 'hero-card', 'S10: vx-transition-name sets style.viewTransitionName');

    let viewTransitionCalls = 0;
    window.document.startViewTransition = function (cb) {
      viewTransitionCalls++;
      cb();
      return { finished: Promise.resolve() };
    };

    window.Vaxel.processDocument(
      `<vx-patch target="#pane">
        <div id="pane" vx-region vx-transition>
          <div id="card" vx-transition-name="hero-card">Card 2</div>
        </div>
      </vx-patch>`,
      true,
      null,
      '/docs',
      'GET'
    );

    assertEqual(viewTransitionCalls, 1, 'S10: document.startViewTransition invoked when patching vx-transition region');
    assertEqual(window.document.getElementById('card').textContent, 'Card 2', 'S10: patch applied inside View Transition');
    assert(
      window.document.getElementById('pane').classList.contains('vx-settling'),
      'S10: .vx-settling class added to patched target'
    );
  }

  // --- S11: Top loading progress bar (#vx-progress, vx-progress="0", vx-progress="off") ---
  {
    let resolveFetch;
    const { window } = makeAgent({
      url: 'http://localhost/docs',
      html: `
        <button id="go" vx-get="/slow" vx-progress="0">Load</button>
        <button id="silent" vx-get="/fast" vx-progress="off">Silent</button>
      `,
      fetchImpl: () =>
        new Promise((resolve) => {
          resolveFetch = () =>
            resolve({
              headers: {
                get: (k) =>
                  ({
                    'content-type': 'text/vnd.vaxel-patch+html; charset=utf-8',
                    'vx-protocol': '1',
                  })[String(k).toLowerCase()] || '',
              },
              text: () => Promise.resolve(''),
            });
        }),
    });

    // Case 1: vx-progress="off" does not activate #vx-progress
    const silentBtn = window.document.getElementById('silent');
    silentBtn.dispatchEvent(new window.Event('click', { bubbles: true, cancelable: true }));
    const barBefore = window.document.getElementById('vx-progress');
    assert(
      !barBefore || !barBefore.classList.contains('vx-progress-active'),
      'S11: vx-progress="off" does not activate progress bar'
    );
    resolveFetch();
    await tick(window, 20);

    // Case 2: vx-progress="0" activates #vx-progress immediately and completes to 100% on response
    const goBtn = window.document.getElementById('go');
    goBtn.dispatchEvent(new window.Event('click', { bubbles: true, cancelable: true }));
    await tick(window, 10);

    const bar = window.document.getElementById('vx-progress');
    assert(bar !== null, 'S11: #vx-progress element created');
    assert(bar.classList.contains('vx-progress-active'), 'S11: #vx-progress has .vx-progress-active while in flight');

    resolveFetch();
    await tick(window, 20);
    assert(bar.classList.contains('vx-progress-done'), 'S11: #vx-progress reaches .vx-progress-done on completion');
    assertEqual(bar.style.width, '100%', 'S11: #vx-progress width reaches 100% on completion');
  }
}
