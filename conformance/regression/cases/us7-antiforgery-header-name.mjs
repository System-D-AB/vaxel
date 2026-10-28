// US-7 (client): the agent sends the antiforgery token under the header name the server
// rendered in the vx-csrf meta's data-header, falling back to X-CSRF when it is absent.
import { makeAgent, assert, assertEqual, tick } from '../harness.mjs';

export const name = 'us7-antiforgery-header-name';

export async function run() {
  // AC-1: configured header name is honoured.
  {
    const { window } = makeAgent({
      html:
        '<head><meta name="vx-csrf" content="tok123" data-header="X-My-CSRF"></head>' +
        '<body><form id="f" method="post" vx-post action="/save" vx-target="#r"></form><div id="r"></div></body>',
    });
    const form = window.document.getElementById('f');
    form.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true }));
    await tick(window);

    const headers = window.__fetches[0].init.headers;
    assertEqual(headers['X-My-CSRF'], 'tok123', 'AC-1: token under configured header name');
    assert(!('X-CSRF' in headers), 'AC-1: must not also send under the default name');
  }

  // AC-2: no data-header -> documented default X-CSRF.
  {
    const { window } = makeAgent({
      html:
        '<head><meta name="vx-csrf" content="tok456"></head>' +
        '<body><form id="f" method="post" vx-post action="/save" vx-target="#r"></form><div id="r"></div></body>',
    });
    const form = window.document.getElementById('f');
    form.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true }));
    await tick(window);

    const headers = window.__fetches[0].init.headers;
    assertEqual(headers['X-CSRF'], 'tok456', 'AC-2: default header name X-CSRF');
  }
}
