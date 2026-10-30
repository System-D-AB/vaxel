// v2.0-rc5-shortcomings (B7, B8, B9, S13, S14): Layout-shell architecture, transitions, & navigation regression cases
import { makeAgent, assert, assertEqual, tick } from '../harness.mjs';

export const name = 'us11-v2-rc5-shortcomings';

export async function run() {
  // --- B7: <vx-directive reload> (valueless and reload="1") navigates to requestUrl via location.assign ---
  {
    const { window } = makeAgent({
      url: 'http://localhost/dashboard',
      html: `<div id="pane" vx-region><a id="to-login" href="/login" vx-get="/login">Login</a></div>`,
    });

    const assignedUrls = [];
    window.Vaxel._test.onNavigate = function (u) {
      assignedUrls.push(u);
    };

    // Case 1: Valueless <vx-directive reload />
    window.Vaxel.processDocument(`<vx-directive reload />`, true, null, '/login', 'GET');
    assertEqual(assignedUrls.length, 1, 'B7: valueless <vx-directive reload /> triggers navigation');
    assertEqual(assignedUrls[0], '/login', 'B7: navigates to requestUrl (/login) instead of reloading old URL (/dashboard)');

    // Case 2: <vx-directive reload="1" />
    window.Vaxel.processDocument(`<vx-directive reload="1" />`, true, null, '/checkout', 'GET');
    assertEqual(assignedUrls.length, 2, 'B7: <vx-directive reload="1" /> triggers navigation');
    assertEqual(assignedUrls[1], '/checkout', 'B7: navigates to /checkout');
  }

  // --- B8 & S14: Async document.startViewTransition captures old state before mutating DOM/URL/scroll + vx:after-apply detail.targets ---
  {
    const { window } = makeAgent({
      url: 'http://localhost/page-1',
      html: `<main id="content" vx-region vx-transition><h1 id="hdr">Page 1</h1></main><div id="badge" vx-region>0</div>`,
    });

    let transitionCallback = null;
    let skippedPrev = 0;
    window.document.startViewTransition = function (cb) {
      // Simulate real browser: startViewTransition returns before invoking cb asynchronously on next frame
      transitionCallback = cb;
      return {
        finished: Promise.resolve(),
        skipTransition: function () {
          skippedPrev++;
        },
      };
    };

    const afterApplyDetails = [];
    const scrollCalls = [];
    window.scrollTo = function (opts) {
      scrollCalls.push(opts);
    };
    window.document.addEventListener('vx:after-apply', (e) => afterApplyDetails.push(e.detail));

    window.Vaxel.processDocument(
      `<vx-patch target="#content"><main id="content" vx-region vx-transition><h1 id="hdr">Page 2</h1></main></vx-patch>
       <vx-patch target="#badge"><div id="badge" vx-region>3</div></vx-patch>
       <vx-directive title="Page 2" push-url="/page-2" scroll="top" />`,
      true,
      null,
      '/page-2',
      'GET'
    );

    // Before startViewTransition invokes its callback, old DOM and old URL must still be intact for old-state snapshot
    assert(transitionCallback !== null, 'B8: startViewTransition registered callback');
    assertEqual(
      window.document.getElementById('hdr').textContent,
      'Page 1',
      'B8: DOM not mutated synchronously before startViewTransition captures old snapshot'
    );
    assertEqual(window.location.pathname, '/page-1', 'B8: URL not pushed before transition callback runs');
    assertEqual(scrollCalls.length, 0, 'B8: scroll="top" not executed before transition callback runs');
    assertEqual(afterApplyDetails.length, 0, 'B8: vx:after-apply not emitted before transition callback runs');

    // Now browser invokes the view-transition update callback
    transitionCallback();

    assertEqual(window.document.getElementById('hdr').textContent, 'Page 2', 'B8: DOM updated inside transition callback');
    assertEqual(window.location.pathname, '/page-2', 'B8: push-url applied inside transition callback');
    assertEqual(window.document.title, 'Page 2', 'B8: title applied inside transition callback');
    assertEqual(scrollCalls.length, 1, 'B8: scroll="top" executed inside transition callback');
    assertEqual(afterApplyDetails.length, 1, 'S14: vx:after-apply emitted once');
    assertEqual(
      JSON.stringify(afterApplyDetails[0].targets),
      JSON.stringify(['#content', '#badge']),
      'S14: vx:after-apply detail.targets lists applied patch selectors in order'
    );
  }

  // --- B9: Click modifier guards (Ctrl+Click, Cmd+Click, middle-click, target="_blank", download) ---
  {
    const { window } = makeAgent({
      url: 'http://localhost/items',
      html: `
        <a id="normal" href="/items/1" vx-get="/items/1">Item 1</a>
        <a id="blank" href="/items/2" vx-get="/items/2" target="_blank">Item 2</a>
        <a id="dl" href="/items/3.csv" vx-get="/items/3.csv" download>Download</a>
        <a id="hash" href="#details" vx-get="#details">Jump</a>
      `,
    });

    const normal = window.document.getElementById('normal');
    const blank = window.document.getElementById('blank');
    const dl = window.document.getElementById('dl');
    const hash = window.document.getElementById('hash');

    // Ctrl+Click must NOT be intercepted
    const ctrlEvt = new window.MouseEvent('click', { bubbles: true, cancelable: true, ctrlKey: true, button: 0 });
    normal.dispatchEvent(ctrlEvt);
    assertEqual(ctrlEvt.defaultPrevented, false, 'B9: Ctrl+Click is not prevented');
    assertEqual(window.__fetches.length, 0, 'B9: Ctrl+Click does not trigger fetch');

    // Cmd+Click (metaKey) must NOT be intercepted
    const metaEvt = new window.MouseEvent('click', { bubbles: true, cancelable: true, metaKey: true, button: 0 });
    normal.dispatchEvent(metaEvt);
    assertEqual(metaEvt.defaultPrevented, false, 'B9: Cmd+Click is not prevented');
    assertEqual(window.__fetches.length, 0, 'B9: Cmd+Click does not trigger fetch');

    // Middle-click (button: 1) must NOT be intercepted
    const midEvt = new window.MouseEvent('click', { bubbles: true, cancelable: true, button: 1 });
    normal.dispatchEvent(midEvt);
    assertEqual(midEvt.defaultPrevented, false, 'B9: Middle-click is not prevented');
    assertEqual(window.__fetches.length, 0, 'B9: Middle-click does not trigger fetch');

    // target="_blank", download, and #hash must NOT be intercepted
    blank.dispatchEvent(new window.MouseEvent('click', { bubbles: true, cancelable: true, button: 0 }));
    dl.dispatchEvent(new window.MouseEvent('click', { bubbles: true, cancelable: true, button: 0 }));
    hash.dispatchEvent(new window.MouseEvent('click', { bubbles: true, cancelable: true, button: 0 }));
    assertEqual(window.__fetches.length, 0, 'B9: _blank, download, and #hash links are not intercepted');

    // Unmodified left-click IS intercepted
    const clickEvt = new window.MouseEvent('click', { bubbles: true, cancelable: true, button: 0 });
    normal.dispatchEvent(clickEvt);
    assertEqual(clickEvt.defaultPrevented, true, 'B9: Unmodified left click is intercepted');
    assertEqual(window.__fetches.length, 1, 'B9: Unmodified left click triggers fetch');
  }

  // --- S13: Delegated same-origin link interception (vx-boost, vx-boost-exclude, ancestor vx-target) ---
  {
    const { window } = makeAgent({
      url: 'http://localhost/catalog',
      html: `
        <body vx-boost vx-target="#content" vx-boost-exclude="/auth /checkout /api">
          <nav>
            <a id="nav-item" href="/catalog/item-1">Item 1</a>
            <a id="nav-checkout" href="/checkout/pay">Checkout</a>
            <a id="nav-auth" href="/auth">Login</a>
            <a id="nav-authors" href="/authors/jane">Authors (prefix should not match /auth)</a>
            <a id="nav-optout" href="/catalog/raw" vx-boost="off">Raw page</a>
            <a id="nav-ext" href="https://example.com/outside">External</a>
          </nav>
          <main id="content" vx-region>Initial</main>
        </body>
      `,
    });

    // Excluded links (/checkout/pay, /auth), vx-boost="off", and external links are NOT intercepted
    window.document.getElementById('nav-checkout').dispatchEvent(new window.MouseEvent('click', { bubbles: true, cancelable: true, button: 0 }));
    window.document.getElementById('nav-auth').dispatchEvent(new window.MouseEvent('click', { bubbles: true, cancelable: true, button: 0 }));
    window.document.getElementById('nav-optout').dispatchEvent(new window.MouseEvent('click', { bubbles: true, cancelable: true, button: 0 }));
    window.document.getElementById('nav-ext').dispatchEvent(new window.MouseEvent('click', { bubbles: true, cancelable: true, button: 0 }));
    assertEqual(window.__fetches.length, 0, 'S13: excluded, opt-out, and external links are not boosted');

    // Qualifying same-origin link (/catalog/item-1) IS boosted and inherits vx-target="#content" from <body>
    window.document.getElementById('nav-item').dispatchEvent(new window.MouseEvent('click', { bubbles: true, cancelable: true, button: 0 }));
    assertEqual(window.__fetches.length, 1, 'S13: same-origin link inside vx-boost is intercepted');
    assertEqual(window.__fetches[0].url, '/catalog/item-1', 'S13: boosted link fetches href');
    assertEqual(window.__fetches[0].init.headers['VX-Target'], '#content', 'S13: boosted link inherits VX-Target from ancestor');

    // /authors/jane is NOT excluded by /auth prefix (segment-boundary match)
    window.document.getElementById('nav-authors').dispatchEvent(new window.MouseEvent('click', { bubbles: true, cancelable: true, button: 0 }));
    assertEqual(window.__fetches.length, 2, 'S13: /authors/jane is boosted (not falsely matched by /auth)');
    assertEqual(window.__fetches[1].url, '/authors/jane', 'S13: /authors/jane fetched');
  }
}
