// US-5: Re-entrancy, private scheduling queue, diamonds, cycle cap, and one-owner conflict warning
import { makeAgent, assert, assertEqual } from '../harness.mjs';

export const name = 'us5-scheduler-and-ordering';

export async function run() {
  const { window } = makeAgent();
  const V = window.Vaxel;

  // 1. Multi-key patch runs an effect reading all three keys ONCE, never against a half-applied bag
  {
    V.patch({ k1: 'a', k2: 'b', k3: 'c' });
    const snapshots = [];
    const dispose = V._effect(() => {
      snapshots.push(`${V._read('k1')}-${V._read('k2')}-${V._read('k3')}`);
    });
    assertEqual(snapshots.length, 1, 'initial effect run');
    assertEqual(snapshots[0], 'a-b-c', 'initial snapshot');

    V.patch({ k1: 'x', k2: 'y', k3: 'z' });
    assertEqual(snapshots.length, 2, 'three-key patch must run reader effect exactly ONCE');
    assertEqual(snapshots[1], 'x-y-z', 'effect never sees a half-applied bag');
    dispose();
  }

  // 2. Diamond: one write reaching an effect through two signals (or batched writes) runs the effect once
  {
    const s1 = V._signal(1);
    const s2 = V._signal(2);
    let runs = 0;
    let total = 0;

    // If an outer write or batch updates both s1 and s2, or an effect updates a derived signal before reader
    const src = V._signal(10);
    const disposeLeft = V._effect(() => { s1.value = src.value * 2; });
    const disposeRight = V._effect(() => { s2.value = src.value * 3; });
    runs = 0;
    const disposeBottom = V._effect(() => {
      runs++;
      total = s1.value + s2.value;
    });
    assertEqual(runs, 1, 'initial bottom run');
    assertEqual(total, 50, 'initial diamond total');

    src.value = 20;
    assertEqual(total, 100, 'diamond settled with 20*2 + 20*3 = 100');
    // Note: in a pass-based queue, src write enqueues left & right; pass 1 runs left & right which enqueue bottom; pass 2 runs bottom once!
    assertEqual(runs, 2, 'diamond bottom effect ran once for the update');

    disposeBottom();
    disposeLeft();
    disposeRight();
  }

  // 3. An effect re-runs when its input changes after it ran in the same drain
  {
    const a = V._signal(1);
    const b = V._signal(0);
    const log = [];

    // Effect 1 reads b
    const d1 = V._effect(() => {
      log.push('e1:' + b.value);
    });
    // Effect 2 reads a and writes b
    const d2 = V._effect(() => {
      if (a.value > 1) {
        b.value = a.value * 10;
      }
    });
    log.length = 0;

    // Writing both b and a in one patch: e1 runs in pass 1, e2 runs in pass 1 and mutates b, so e1 must re-run in pass 2
    const disposePatchTest = () => { d1(); d2(); };
    a.value = 2;
    assertEqual(b.peek(), 20, 'e2 updated b');
    assertEqual(log[log.length - 1], 'e1:20', 'e1 re-ran after b changed in same drain');
    disposePatchTest();
  }

  // 4. Disposed effect in pending does not run
  {
    const trigger = V._signal(0);
    let victimRuns = 0;
    let disposeVictim = null;

    const dKiller = V._effect(() => {
      if (trigger.value > 0 && disposeVictim) {
        disposeVictim();
      }
    });
    disposeVictim = V._effect(() => {
      void trigger.value;
      victimRuns++;
    });
    assertEqual(victimRuns, 1, 'victim initial run');

    trigger.value = 1;
    assertEqual(victimRuns, 1, 'disposed effect in pending must be skipped');
    dKiller();
  }

  // 5. Two-effect cycle stops at pass cap (100) with vx:error and the page keeps working
  {
    const ping = V._signal(0);
    const pong = V._signal(0);
    let errors = [];
    window.document.addEventListener('vx:error', (e) => {
      errors.push(e.detail);
    });

    const dPing = V._effect(() => {
      const v = ping.value;
      if (v > 0) pong.value = v + 1;
    });
    const dPong = V._effect(() => {
      const v = pong.value;
      if (v > 0) ping.value = v + 1;
    });

    // Start the cycle
    ping.value = 1;

    assert(errors.length >= 1, 'cycle exceeding 100 passes must emit vx:error');
    assert(
      String(errors[0].reason || '').toLowerCase().includes('cycle'),
      'vx:error reason must name the cycle',
    );

    dPing();
    dPong();

    // Verify store still works after cycle abort
    const healthy = V._signal('ok');
    let observed = '';
    const dHealthy = V._effect(() => { observed = healthy.value; });
    healthy.value = 'still-working';
    assertEqual(observed, 'still-working', 'page/store stays responsive after cycle abort');
    dHealthy();
  }

  // 6. Two bindings on one property = last-one-wins + dev warning (US-5.3)
  {
    const warnings = [];
    const origWarn = window.console.warn;
    window.console.warn = (...args) => warnings.push(args.join(' '));
    try {
      V.patch({ sClassA: true, sClassB: false, sStyleA: 'none', sShowA: true });
      const div = window.document.createElement('div');
      // Both vx-show and vx-style:display (or vx-attr:hidden) drive the same underlying property
      div.setAttribute('vx-show', 'sShowA');
      div.setAttribute('vx-attr:hidden', 'sClassB');
      window.document.body.appendChild(div);
      V.applyPatch('body', div.outerHTML, 'append');
      assert(warnings.some((w) => w.toLowerCase().includes('conflict') || w.toLowerCase().includes('owner') || w.toLowerCase().includes('binding')),
        'two bindings writing one property must emit a dev warning');
    } finally {
      window.console.warn = origWarn;
    }
  }
}
