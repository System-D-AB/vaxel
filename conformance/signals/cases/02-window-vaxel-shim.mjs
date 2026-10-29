// US-3: window.Vaxel compatibility shim (get=peek, set, patch, all, subscribe returning disposer)
import { makeAgent, assert, assertEqual } from '../harness.mjs';

export const name = 'us3-window-vaxel-shim';

export async function run() {
  const { window } = makeAgent();
  const V = window.Vaxel;

  // Verify no public batch() is exposed (US-5.2)
  assertEqual(typeof V.batch, 'undefined', 'window.Vaxel must not expose batch');

  // get / set / all
  V.set('tab', 'overview');
  assertEqual(V.get('tab'), 'overview', 'get returns set value');
  assertEqual(JSON.stringify(V.all()), JSON.stringify({ tab: 'overview' }), 'all returns bag');

  // get uses peek (does not register into an active effect)
  let effectRuns = 0;
  const triggerSig = V._signal(0);
  const disposeEffect = V._effect(() => {
    effectRuns++;
    void triggerSig.value;
    void V.get('tab');
  });
  assertEqual(effectRuns, 1, 'initial effect run');
  V.set('tab', 'settings');
  assertEqual(effectRuns, 1, 'V.get must use peek and not register a dependency in an enclosing effect');
  disposeEffect();

  // subscribe(key, cb) invokes cb on change to that key and returns unsubscribe function
  const seen = [];
  const unsub = V.subscribe('count', (val) => {
    seen.push(val);
  });
  assert(typeof unsub === 'function', 'subscribe returns an unsubscribe function');

  V.set('count', 1);
  V.set('count', 1); // same value -> no-op
  V.set('count', 2);
  V.set('other', 'ignored');
  assertEqual(JSON.stringify(seen), JSON.stringify([1, 2]), 'subscribe fires only on distinct changes to key');

  unsub();
  V.set('count', 3);
  assertEqual(JSON.stringify(seen), JSON.stringify([1, 2]), 'unsubscribed callback does not fire');

  // patch(obj) updates multiple keys
  V.patch({ a: 10, b: 20 });
  assertEqual(V.get('a'), 10, 'patch a');
  assertEqual(V.get('b'), 20, 'patch b');
}
