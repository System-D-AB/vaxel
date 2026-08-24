/**
 * Växel Development Overlay (v0.4)
 * Development-only diagnostic tool for tracking morph patches, ignored targets, and signal diffs.
 * Excluded from production bundle. Zero-eval, strict CSP compliant.
 */
(function () {
  'use strict';

  if (typeof window === 'undefined' || !window.document) return;

  function initDevOverlay() {
    var panel = document.createElement('div');
    panel.id = 'vaxel-dev-overlay';
    panel.style.position = 'fixed';
    panel.style.bottom = '12px';
    panel.style.right = '12px';
    panel.style.zIndex = '999999';
    panel.style.maxWidth = '360px';
    panel.style.padding = '10px 14px';
    panel.style.background = '#1e1e24';
    panel.style.color = '#f0f0f5';
    panel.style.fontFamily = 'ui-monospace, monospace';
    panel.style.fontSize = '12px';
    panel.style.borderRadius = '8px';
    panel.style.boxShadow = '0 4px 16px rgba(0,0,0,0.4)';
    panel.style.border = '1px solid #333';
    panel.style.lineHeight = '1.4';

    var header = document.createElement('div');
    header.style.fontWeight = 'bold';
    header.style.marginBottom = '6px';
    header.style.color = '#61afef';
    header.textContent = '⚡ Växel Dev Inspector';
    panel.appendChild(header);

    var content = document.createElement('div');
    content.id = 'vaxel-dev-content';
    content.textContent = 'Awaiting events...';
    panel.appendChild(content);

    document.body.appendChild(panel);

    document.addEventListener('vx:after-apply', function (e) {
      var patches = e.detail && e.detail.patchesApplied !== undefined ? e.detail.patchesApplied : 0;
      content.innerHTML = '<div style="color:#98c379;">✔ Patches applied: ' + patches + '</div>';
    });

    document.addEventListener('vx:error', function (e) {
      var reason = e.detail && e.detail.reason ? e.detail.reason : (e.detail && e.detail.error ? e.detail.error.message : 'Unknown error');
      content.innerHTML = '<div style="color:#e06c75;">✖ ' + reason + '</div>';
    });

    document.addEventListener('vx:signals-changed', function (e) {
      var changed = e.detail && e.detail.changed ? e.detail.changed.join(', ') : '';
      if (changed) {
        var diffEl = document.createElement('div');
        diffEl.style.marginTop = '4px';
        diffEl.style.color = '#e5c07b';
        diffEl.textContent = 'Signals changed: ' + changed;
        content.appendChild(diffEl);
      }
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initDevOverlay);
  } else {
    initDevOverlay();
  }
})();
