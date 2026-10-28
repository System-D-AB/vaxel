// US-4: a vx-get form must send its fields as the query string, merged with any query the
// action already carries; vx-vals-* on a GET trigger likewise; requestSubmit() is intercepted.
import { makeAgent, assert, assertEqual, tick } from '../harness.mjs';

export const name = 'us4-get-form-fields';

export async function run() {
  // AC-1 / AC-4: a GET form with two fields fetches action?a=1&b=2.
  {
    const { window } = makeAgent({
      html:
        '<form id="filter" method="get" vx-get action="/shell" vx-target="#r">' +
        '<input name="a" value="1"><input name="b" value="2"></form>' +
        '<div id="r"></div>',
    });
    const form = window.document.getElementById('filter');
    form.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true }));
    await tick(window);

    assert(window.__fetches.length === 1, 'AC-1: exactly one request issued');
    const u = new window.URL(window.__fetches[0].url, window.location.href);
    assertEqual(u.pathname, '/shell', 'AC-1: path is the action');
    assertEqual(u.searchParams.get('a'), '1', 'AC-1: field a in query');
    assertEqual(u.searchParams.get('b'), '2', 'AC-1: field b in query');
    assertEqual(window.__fetches[0].init.method, 'GET', 'AC-1: method is GET');
  }

  // AC-1 (merge): the action already carries a query; form fields merge with it.
  {
    const { window } = makeAgent({
      html:
        '<form id="filter" method="get" vx-get action="/shell?tab=docs" vx-target="#r">' +
        '<input name="q" value="hello"></form><div id="r"></div>',
    });
    const form = window.document.getElementById('filter');
    form.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true }));
    await tick(window);

    const u = new window.URL(window.__fetches[0].url, window.location.href);
    assertEqual(u.searchParams.get('tab'), 'docs', 'merge: existing query kept');
    assertEqual(u.searchParams.get('q'), 'hello', 'merge: form field added');
  }

  // AC-3: programmatic requestSubmit() is intercepted like a user submit.
  {
    const { window } = makeAgent({
      html:
        '<form id="filter" method="get" vx-get action="/shell" vx-target="#r">' +
        '<input name="a" value="9"></form><div id="r"></div>',
    });
    const form = window.document.getElementById('filter');
    if (typeof form.requestSubmit === 'function') {
      form.requestSubmit();
    } else {
      // jsdom fallback: requestSubmit dispatches a cancelable submit event.
      form.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true }));
    }
    await tick(window);

    assert(window.__fetches.length === 1, 'AC-3: requestSubmit issues one request');
    const u = new window.URL(window.__fetches[0].url, window.location.href);
    assertEqual(u.searchParams.get('a'), '9', 'AC-3: field present on programmatic submit');
  }

  // AC-2: vx-vals-* on a GET anchor trigger add to the query string.
  {
    const { window } = makeAgent({
      html: '<a id="t" href="/shell" vx-get vx-vals-tab="submissions" vx-vals-page="2">go</a><div id="r"></div>',
    });
    const a = window.document.getElementById('t');
    a.dispatchEvent(new window.MouseEvent('click', { bubbles: true, cancelable: true }));
    await tick(window);

    const u = new window.URL(window.__fetches[0].url, window.location.href);
    assertEqual(u.searchParams.get('tab'), 'submissions', 'AC-2: vx-vals-tab in query');
    assertEqual(u.searchParams.get('page'), '2', 'AC-2: vx-vals-page in query');
  }
}
