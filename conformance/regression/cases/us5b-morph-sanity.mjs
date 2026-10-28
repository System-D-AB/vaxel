// US-5 guard: the morph advancement fix must not regress ordinary morphs.
import { makeAgent, assert, assertEqual } from '../harness.mjs';

export const name = 'us5b-morph-sanity';

export async function run() {
  // Keyed reorder: #a,#b -> #b,#a keeps both nodes, new order.
  {
    const { window } = makeAgent({ html: '<div id="r"><p id="a">A</p><p id="b">B</p></div>' });
    const a0 = window.document.getElementById('a');
    const b0 = window.document.getElementById('b');
    window.Vaxel.processDocument(
      '<vx-patch target="#r" mode="morph"><p id="b">B</p><p id="a">A</p></vx-patch>',
      false,
      null,
    );
    const r = window.document.getElementById('r');
    assertEqual(r.children.length, 2, 'reorder keeps two children');
    assertEqual(r.children[0].id, 'b', 'first is #b after reorder');
    assertEqual(r.children[1].id, 'a', 'second is #a after reorder');
    assert(window.document.getElementById('a') === a0, 'node identity of #a preserved');
    assert(window.document.getElementById('b') === b0, 'node identity of #b preserved');
  }

  // Idless in-place text update: <p>old> -> <p>new> morphs the same node.
  {
    const { window } = makeAgent({ html: '<div id="r"><p>old</p></div>' });
    const p0 = window.document.querySelector('#r p');
    window.Vaxel.processDocument(
      '<vx-patch target="#r" mode="morph"><p>new</p></vx-patch>',
      false,
      null,
    );
    const r = window.document.getElementById('r');
    assertEqual(r.children.length, 1, 'one child after idless morph');
    assertEqual(r.children[0].textContent, 'new', 'text updated in place');
    assert(window.document.querySelector('#r p') === p0, 'idless node reused in place');
  }

  // Append: incoming has more children than existing.
  {
    const { window } = makeAgent({ html: '<ul id="r"><li id="x">x</li></ul>' });
    window.Vaxel.processDocument(
      '<vx-patch target="#r" mode="morph"><li id="x">x</li><li id="y">y</li></vx-patch>',
      false,
      null,
    );
    const r = window.document.getElementById('r');
    assertEqual(r.children.length, 2, 'appended new li');
    assert(window.document.getElementById('y'), '#y added');
  }

  // Insert a new idless node before kept id nodes without dropping them.
  {
    const { window } = makeAgent({ html: '<div id="r"><p id="a">A</p></div>' });
    window.Vaxel.processDocument(
      '<vx-patch target="#r" mode="morph"><span>lead</span><p id="a">A</p></vx-patch>',
      false,
      null,
    );
    const r = window.document.getElementById('r');
    assertEqual(r.children.length, 2, 'span inserted, #a kept');
    assertEqual(r.children[0].tagName, 'SPAN', 'new span leads');
    assert(window.document.getElementById('a'), '#a preserved');
  }
}
