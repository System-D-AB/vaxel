// US-4: Null and absence semantics (delete-on-null) across store, vx-persist, vx-url-sync, and VX-Signals header
import { makeAgent, assert, assertEqual } from '../harness.mjs';

export const name = 'us4-null-absence-semantics';

export async function run() {
  const { window } = makeAgent({
    url: 'http://localhost/shell',
    response: { body: '<vx-patch target="#r"><div id="r">ok</div></vx-patch>' },
    html: `
      <div id="r"
           vx-signals='{"tab":"overview","filter":"active"}'
           vx-persist="tab"
           vx-persist-session="filter"
           vx-url-sync="tab filter">
        <a id="go" href="/items" vx-get="/items">Go</a>
      </div>
    `,
  });
  const V = window.Vaxel;

  // Initial seed populated store, storage, and URL
  assertEqual(V.get('tab'), 'overview', 'seeded tab');
  assertEqual(V.get('filter'), 'active', 'seeded filter');
  assertEqual(window.localStorage.getItem('vx:tab'), '"overview"', 'localStorage seeded');
  assertEqual(window.sessionStorage.getItem('vx:filter'), '"active"', 'sessionStorage seeded');
  assert(window.location.search.includes('tab=overview'), 'URL synced tab');
  assert(window.location.search.includes('filter=active'), 'URL synced filter');

  // Set 'tab' to null -> deleted from store, localStorage, and URL query string
  V.set('tab', null);
  assertEqual(V.get('tab'), undefined, 'tab is undefined after set(null)');
  assert(!('tab' in V.all()), 'tab key omitted from Vaxel.all()');
  assertEqual(window.localStorage.getItem('vx:tab'), null, 'localStorage key removed on null');
  assert(!window.location.search.includes('tab='), 'URL query param removed on null');

  // Patch 'filter' to null -> deleted from store, sessionStorage, and URL query string
  V.patch({ filter: null });
  assertEqual(V.get('filter'), undefined, 'filter is undefined after patch({filter: null})');
  assert(!('filter' in V.all()), 'filter key omitted from Vaxel.all()');
  assertEqual(window.sessionStorage.getItem('vx:filter'), null, 'sessionStorage key removed on null');
  assertEqual(window.location.search, '', 'URL query string empty after both nulled');

  // Re-setting a deleted signal works and notifies subscribers
  const events = [];
  const unsub = V.subscribe('tab', (v) => events.push(v));
  V.set('tab', 'inbox');
  assertEqual(V.get('tab'), 'inbox', 're-set tab');
  assertEqual(JSON.stringify(events), JSON.stringify(['inbox']), 'subscriber notified on re-set');
  assertEqual(window.localStorage.getItem('vx:tab'), '"inbox"', 'localStorage restored on re-set');
  assertEqual(window.location.search, '?tab=inbox', 'URL restored on re-set');
  unsub();

  // Trigger a request and verify VX-Signals header contains only non-null keys
  const link = window.document.getElementById('go');
  link.click();
  assertEqual(window.__fetches.length, 1, 'request sent');
  const sigHeader = window.__fetches[0].init.headers['VX-Signals'];
  assertEqual(sigHeader, '{"tab":"inbox"}', 'VX-Signals omits deleted keys and keeps active ones');
}
