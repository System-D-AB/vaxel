// v2.0-rc2-bugs (B5, S5, S6, S7): StowDB Explorer v2.0.0-rc2 findings regression cases
import { makeAgent, assert, assertEqual, tick } from '../harness.mjs';

export const name = 'us9-v2-rc2-bugs';

export async function run() {
  // --- B5: <form> containing <input name="id"> (and action/tagName/attributes) morphs cleanly at same and shifted positions ---
  {
    const { window } = makeAgent({
      url: 'http://localhost/documents/orders/1',
      html: `
        <div id="pane" vx-region>
          <header id="doc-hdr">Order 1</header>
          <form method="post" action="/documents/save">
            <input type="hidden" name="collection" value="orders" />
            <input type="hidden" name="id" value="orders/1" />
            <input type="hidden" name="action" value="save" />
            <input type="hidden" name="tagName" value="custom" />
            <textarea name="json">{"total":10}</textarea>
          </form>
        </div>
      `,
    });

    // Case 1: Morph to another document where the form is at a shifted child position (extra banner inserted before form)
    window.Vaxel.processDocument(`
      <vx-patch target="#pane">
        <div id="pane" vx-region>
          <header id="doc-hdr">Order 2</header>
          <div class="notice">Read-only replica</div>
          <form method="post" action="/documents/save">
            <input type="hidden" name="collection" value="orders" />
            <input type="hidden" name="id" value="orders/2" />
            <input type="hidden" name="action" value="save" />
            <input type="hidden" name="tagName" value="custom" />
            <textarea name="json" vx-overwrite-dirty>{"total":25}</textarea>
          </form>
        </div>
      </vx-patch>
      <vx-directive push-url="/documents/orders/2" />
    `, true, null, '/documents/orders/2', 'GET');

    assertEqual(window.document.getElementById('doc-hdr').textContent, 'Order 2', 'B5: header morphed');
    assertEqual(
      window.document.querySelector('form input[name="id"]').value,
      'orders/2',
      'B5: form with <input name="id"> morphed at shifted child position without SyntaxError'
    );
    assertEqual(window.location.pathname, '/documents/orders/2', 'B5: push-url directive applied after form morph');

    // Case 2: Morph back where an identified sibling moves past the form
    window.Vaxel.processDocument(`
      <vx-patch target="#pane">
        <div id="pane" vx-region>
          <form method="post" action="/documents/save">
            <input type="hidden" name="id" value="orders/3" />
          </form>
          <header id="doc-hdr">Order 3</header>
        </div>
      </vx-patch>
    `, true, null, '/documents/orders/3', 'GET');

    assertEqual(window.document.getElementById('doc-hdr').textContent, 'Order 3', 'B5: identified sibling relocated past form');
    assertEqual(window.document.querySelector('form input[name="id"]').value, 'orders/3', 'B5: form input[name=id] updated');
  }

  // --- S5 & S6: Per-patch try/catch fallback to child replacement + structured vx:error detail ---
  {
    const { window } = makeAgent({
      url: 'http://localhost/documents/1',
      html: `<div id="pane" vx-region><p>Old content</p></div>`,
    });

    const errors = [];
    window.document.addEventListener('vx:error', (e) => errors.push(e.detail));

    // Inject a throw inside morphIntoTarget
    window.Vaxel._beforeMorph = function () {
      throw new Error('simulated morph failure');
    };

    window.Vaxel.processDocument(`
      <vx-patch target="#pane">
        <div id="pane" vx-region><p id="recovered">Recovered content</p></div>
      </vx-patch>
      <vx-directive push-url="/documents/2" title="Doc 2" />
    `, true, null, '/documents/2', 'GET');

    delete window.Vaxel._beforeMorph;

    const recovered = window.document.getElementById('recovered');
    assert(recovered !== null, 'S5: fallback child replacement rendered incoming content when morph threw');
    assertEqual(recovered.textContent, 'Recovered content', 'S5: fallback content matches');
    assertEqual(window.location.pathname, '/documents/2', 'S5: <vx-directive push-url> still executed after fallback');
    assertEqual(window.document.title, 'Doc 2', 'S5: <vx-directive title> still executed after fallback');

    assertEqual(errors.length, 1, 'S5/S6: vx:error emitted once');
    assertEqual(errors[0].reason, 'apply', 'S6: detail.reason is apply');
    assertEqual(errors[0].target, '#pane', 'S6: detail.target is #pane');
    assertEqual(errors[0].url, '/documents/2', 'S6: detail.url is /documents/2');
    assertEqual(errors[0].fallback, 'replace', 'S5: detail.fallback is replace');
    assertEqual(errors[0].doc, 'docs/02#morph', 'S6: detail.doc is docs/02#morph');
    assert(errors[0].error instanceof window.Error || (errors[0].error && errors[0].error.message === 'simulated morph failure'), 'S6: detail.error carries thrown Error');
  }

  // --- S7: Keyboard modifier fields (ctrlKey, metaKey, altKey, shiftKey) and typing guard in vx-on filter ---
  {
    const { window } = makeAgent({
      url: 'http://localhost/shell',
      html: `
        <body vx-on="keydown window | key k | filter ctrlKey || metaKey | prevent | showModal #palette; keydown window | key / | filter !typing | prevent | focus #tree-filter">
          <input id="tree-filter" type="search" />
          <textarea id="editor"></textarea>
          <dialog id="palette"><input id="palette-input" type="text" /></dialog>
        </body>
      `,
    });

    const palette = window.document.getElementById('palette');
    const treeFilter = window.document.getElementById('tree-filter');
    const editor = window.document.getElementById('editor');

    // Plain 'k' without ctrlKey/metaKey must NOT open the palette
    window.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'k', bubbles: true, cancelable: true }));
    assert(!palette.hasAttribute('open'), 'S7: plain k does not open #palette');

    // Ctrl+K opens #palette and calls preventDefault()
    const ctrlKEvt = new window.KeyboardEvent('keydown', { key: 'k', ctrlKey: true, bubbles: true, cancelable: true });
    window.dispatchEvent(ctrlKEvt);
    assert(palette.hasAttribute('open'), 'S7: Ctrl+K opens #palette');
    assert(ctrlKEvt.defaultPrevented, 'S7: Ctrl+K prevented default');

    // Pressing '/' while focused inside <textarea id="editor"> has typing === true, so filter !typing blocks it
    editor.focus();
    const slashInEditor = new window.KeyboardEvent('keydown', { key: '/', bubbles: true, cancelable: true });
    editor.dispatchEvent(slashInEditor);
    assert(!slashInEditor.defaultPrevented, 'S7: / inside textarea is ignored by filter !typing');
    assertEqual(window.document.activeElement, editor, 'S7: focus remains on editor when typing');

    // Pressing '/' on document.body has typing === false, so it focuses #tree-filter
    const slashOnBody = new window.KeyboardEvent('keydown', { key: '/', bubbles: true, cancelable: true });
    window.document.body.dispatchEvent(slashOnBody);
    assert(slashOnBody.defaultPrevented, 'S7: / outside inputs prevented default');
    assertEqual(window.document.activeElement, treeFilter, 'S7: / outside inputs focused #tree-filter');
  }
}
