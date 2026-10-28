// US-3: directive attribute values are HTML-encoded by the server and must be decoded on
// the client before use (push-url, replace-url, title, announce, focus, scroll, redirect).
import { makeAgent, assert, assertEqual, tick } from '../harness.mjs';

export const name = 'us3-directive-decoding';

export async function run() {
  // AC-2 / AC-3: push-url with an entity-encoded & and a non-ASCII title round-trip.
  {
    const { window } = makeAgent({ url: 'http://localhost/documents' });
    const doc = '<vx-directive push-url="/a?x=1&amp;y=2" title="a &#xB7; b" />';
    window.Vaxel.processDocument(doc, false, null);

    assertEqual(window.location.search, '?x=1&y=2', 'AC-2: push-url & must decode');
    assertEqual(window.location.pathname, '/a', 'AC-2: push-url path');
    assertEqual(window.document.title, 'a \u00B7 b', 'AC-3: title &#xB7; must decode to a middot');
  }

  // A quote and a bare ampersand in replace-url also survive.
  {
    const { window } = makeAgent({ url: 'http://localhost/start' });
    const doc = '<vx-directive replace-url="/q?db=shop&amp;box=customers" announce="saved &quot;draft&quot;" />';
    window.Vaxel.processDocument(doc, false, null);

    assertEqual(window.location.search, '?db=shop&box=customers', 'replace-url & must decode');
  }
}
