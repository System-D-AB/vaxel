// US-5: morphing a region whose new children share no ids with the old must end with
// exactly the new children; stale old nodes (e.g. a previous screen's #query-form) must go.
import { makeAgent, assert, assertEqual } from '../harness.mjs';

export const name = 'us5-morph-unrelated-trees';

export async function run() {
  // AC-1 / AC-3: no shared ids — old subtree must be gone, one new child remains.
  {
    const { window } = makeAgent({
      html: '<div id="r"><section id="old"><form id="f"><input id="q"></form></section></div>',
    });
    const doc =
      '<vx-patch target="#r" mode="morph"><section class="new"><p>schema</p></section></vx-patch>';
    window.Vaxel.processDocument(doc, false, null);

    const r = window.document.getElementById('r');
    assertEqual(r.children.length, 1, 'AC-1: #r must have exactly one child after morph');
    assert(!window.document.getElementById('old'), 'AC-1: #old must be removed');
    assert(!window.document.getElementById('f'), 'AC-1: stale #f form must be removed');
    assert(!window.document.getElementById('q'), 'AC-1: stale #q input must be removed');
    assertEqual(r.children[0].className, 'new', 'AC-1: the surviving child is the incoming one');
    assertEqual(r.children[0].tagName, 'SECTION', 'AC-1: surviving child tag');
  }

  // AC-2: shared ids are kept; the rest are removed. Here #keep survives, #drop goes.
  {
    const { window } = makeAgent({
      html: '<div id="r"><div id="keep">old</div><div id="drop">gone</div></div>',
    });
    const doc =
      '<vx-patch target="#r" mode="morph"><div id="keep">new</div><span class="extra">x</span></vx-patch>';
    window.Vaxel.processDocument(doc, false, null);

    const r = window.document.getElementById('r');
    assert(window.document.getElementById('keep'), 'AC-2: #keep must survive');
    assert(!window.document.getElementById('drop'), 'AC-2: #drop must be removed');
    assertEqual(r.children.length, 2, 'AC-2: #keep + the new span');
    assertEqual(window.document.getElementById('keep').textContent, 'new', 'AC-2: kept node takes new content');
  }
}
