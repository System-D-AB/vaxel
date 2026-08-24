/**
 * vaxel v0.1 Temporary htmx Bridge Driver
 * 
 * NOTE: This is a temporary adapter for milestone v0.1 that bridges vx-* attributes
 * to htmx and Idiomorph. It will be replaced by the native 12 KB vaxel.js agent in v0.2.
 * View authors write vx-* exclusively; views must never contain hx-* attributes.
 */
(function () {
  'use strict';

  function initVaxelTriggers() {
    // Bridges vx-get, vx-post, vx-target, vx-indicator, and signals to htmx
    document.querySelectorAll('[vx-get], [vx-post], [vx-put], [vx-patch], [vx-delete]').forEach(function (el) {
      if (el.__vaxel_init) return;
      el.__vaxel_init = true;

      var target = el.getAttribute('vx-target') || el.closest('[vx-region]')?.id;
      if (target && !target.startsWith('#')) {
        target = '#' + target;
      }

      el.addEventListener('htmx:configRequest', function (evt) {
        evt.detail.headers['VX-Request'] = '1';
        evt.detail.headers['VX-Protocol'] = '1';

        var csrfMeta = document.querySelector('meta[name="vx-csrf"]');
        if (csrfMeta && csrfMeta.content) {
          evt.detail.headers['X-CSRF'] = csrfMeta.content;
        }

        // Collect signals from vx-vals-*
        var vals = {};
        for (var i = 0; i < el.attributes.length; i++) {
          var attr = el.attributes[i];
          if (attr.name.startsWith('vx-vals-')) {
            var key = attr.name.substring('vx-vals-'.length);
            vals[key] = attr.value;
          }
        }
        if (Object.keys(vals).length > 0) {
          evt.detail.headers['VX-Signals'] = JSON.stringify(vals);
        }
      });
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initVaxelTriggers);
  } else {
    initVaxelTriggers();
  }
})();
