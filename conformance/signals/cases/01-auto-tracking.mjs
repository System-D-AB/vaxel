// US-1: Auto-tracking signals and effects
// - effect(fn) re-runs when read signals A or B change, NOT when unread C changes
// - strict equality (===) no-op
// - dynamic dependency re-collection
// - dispose removes all subscribers (1000 cycles leave subscriber counts stable)
// - .peek() does not register a dependency
// - re-entrant write inside effect completes without recursion and settles
import { makeAgent, assert, assertEqual } from '../harness.mjs';

export const name = 'us1-auto-tracking';

export async function run() {
  const { window } = makeAgent();
  const V = window.Vaxel;

  assert(typeof V._signal === 'function', 'Vaxel._signal factory must exist for auto-tracking primitives');
  assert(typeof V._effect === 'function', 'Vaxel._effect must exist for auto-tracking primitives');

  // AC-1 & AC-2: effect tracks A and B, ignores C, and === is a no-op
  {
    const a = V._signal(1);
    const b = V._signal(2);
    const c = V._signal(100);
    let runs = 0;
    let sum = 0;

    const dispose = V._effect(() => {
      runs++;
      sum = a.value + b.value;
    });

    assertEqual(runs, 1, 'initial run');
    assertEqual(sum, 3, 'initial sum');

    // Unread signal C changes -> no re-run
    c.value = 200;
    assertEqual(runs, 1, 'unread signal C must not trigger effect');

    // Strictly equal write -> no re-run (AC-2)
    a.value = 1;
    assertEqual(runs, 1, '=== write must not trigger effect');

    // Read signal A changes -> re-runs
    a.value = 10;
    assertEqual(runs, 2, 'changing A triggers effect');
    assertEqual(sum, 12, 'updated sum after A');

    // Read signal B changes -> re-runs
    b.value = 20;
    assertEqual(runs, 3, 'changing B triggers effect');
    assertEqual(sum, 30, 'updated sum after B');

    dispose();
  }

  // AC-3: dynamic dependency re-collection
  {
    const branch = V._signal(true);
    const left = V._signal('L1');
    const right = V._signal('R1');
    let runs = 0;
    let observed = '';

    const dispose = V._effect(() => {
      runs++;
      observed = branch.value ? left.value : right.value;
    });

    assertEqual(runs, 1, 'initial branch run');
    assertEqual(observed, 'L1', 'initial observed');

    // right is not currently tracked
    right.value = 'R2';
    assertEqual(runs, 1, 'unvisited branch right must not trigger');

    // switch branch to false -> now tracks branch and right, drops left
    branch.value = false;
    assertEqual(runs, 2, 'branch switch triggers');
    assertEqual(observed, 'R2', 'observed after switch');

    // left was only read on previous run -> must no longer trigger!
    left.value = 'L2';
    assertEqual(runs, 2, 'stale dependency left must not trigger after re-collection');

    // right now triggers
    right.value = 'R3';
    assertEqual(runs, 3, 'active dependency right triggers');
    assertEqual(observed, 'R3', 'observed R3');

    dispose();
  }

  // AC-4: dispose removes it from every signal, 1000 create/dispose cycles leak nothing
  {
    const s = V._signal(0);
    const baseline = s._subscriberCount ? s._subscriberCount() : s._subscribers.size;
    for (let i = 0; i < 1000; i++) {
      const d = V._effect(() => {
        void s.value;
      });
      d();
    }
    const after = s._subscriberCount ? s._subscriberCount() : s._subscribers.size;
    assertEqual(after, baseline, '1000 create/dispose cycles must leave subscriber count at 0');
  }

  // AC-5: effect writes a signal it also reads -> completes without recursion, re-runs once more, settles
  {
    const count = V._signal(0);
    const observed = [];

    const dispose = V._effect(() => {
      const v = count.value;
      observed.push(v);
      if (v < 1) {
        count.value = v + 1;
      }
    });

    assertEqual(count.peek(), 1, 'self-writing effect settled at 1');
    assertEqual(JSON.stringify(observed), JSON.stringify([0, 1]), 'effect observed 0 then 1 and settled');
    dispose();
  }

  // AC-6: .peek() reads without registering a dependency
  {
    const tracked = V._signal('a');
    const peeked = V._signal('x');
    let runs = 0;
    let combined = '';

    const dispose = V._effect(() => {
      runs++;
      combined = tracked.value + ':' + peeked.peek();
    });

    assertEqual(runs, 1, 'initial run with peek');
    assertEqual(combined, 'a:x', 'initial combined');

    peeked.value = 'y';
    assertEqual(runs, 1, '.peek() must not register a dependency');

    tracked.value = 'b';
    assertEqual(runs, 2, 'tracked signal triggers and sees latest peeked value');
    assertEqual(combined, 'b:y', 'updated combined');

    dispose();
  }
}
