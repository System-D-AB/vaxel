/**
 * Växel Client Agent (v0.2)
 * Server-driven web framework for .NET.
 * Zero-eval, strict CSP compatible, self-contained.
 */
(function (global) {
  'use strict';

  // --- 1. Signal Store ---
  var store = {};
  var subscribers = {}; // key -> Set of callbacks
  var persistLocalKeys = new Set();
  var persistSessionKeys = new Set();
  var urlSyncKeys = new Set();

  function getSignal(key) {
    return store[key];
  }

  function setSignal(key, value) {
    if (store[key] === value) return;
    if (value === null || value === undefined) {
      delete store[key];
    } else {
      store[key] = value;
    }
    notify(key, value);
    savePersisted();
    syncUrl();
  }

  function patchSignals(obj, onlyIfMissing) {
    if (!obj || typeof obj !== 'object') return;
    var changed = [];
    for (var k in obj) {
      if (Object.prototype.hasOwnProperty.call(obj, k)) {
        if (onlyIfMissing && Object.prototype.hasOwnProperty.call(store, k)) {
          continue;
        }
        var v = obj[k];
        if (v === null || v === undefined) {
          if (Object.prototype.hasOwnProperty.call(store, k)) {
            delete store[k];
            changed.push(k);
          }
        } else if (store[k] !== v) {
          store[k] = v;
          changed.push(k);
        }
      }
    }
    for (var i = 0; i < changed.length; i++) {
      notify(changed[i], store[changed[i]]);
    }
    if (changed.length > 0) {
      savePersisted();
      syncUrl();
      emitEvent('vx:signals-changed', { changed: changed, store: getAllSignals() });
    }
  }

  function subscribeSignal(key, cb) {
    if (!subscribers[key]) subscribers[key] = new Set();
    subscribers[key].add(cb);
    return function () {
      if (subscribers[key]) subscribers[key].delete(cb);
    };
  }

  function notify(key, value) {
    if (subscribers[key]) {
      subscribers[key].forEach(function (cb) {
        try { cb(value); } catch (e) { console.error(e); }
      });
    }
  }

  function getAllSignals() {
    var copy = {};
    for (var k in store) {
      if (Object.prototype.hasOwnProperty.call(store, k)) {
        copy[k] = store[k];
      }
    }
    return copy;
  }

  function loadPersisted() {
    try {
      persistLocalKeys.forEach(function (k) {
        var v = localStorage.getItem('vx:' + k);
        if (v !== null) {
          try { store[k] = JSON.parse(v); } catch (_) { store[k] = v; }
        }
      });
    } catch (_) {}
    try {
      persistSessionKeys.forEach(function (k) {
        var v = sessionStorage.getItem('vx:' + k);
        if (v !== null) {
          try { store[k] = JSON.parse(v); } catch (_) { store[k] = v; }
        }
      });
    } catch (_) {}
    // Load from URL if present
    try {
      var params = new URLSearchParams(window.location.search);
      urlSyncKeys.forEach(function (k) {
        if (params.has(k)) {
          var raw = params.get(k);
          try { store[k] = JSON.parse(raw); } catch (_) { store[k] = raw; }
        }
      });
    } catch (_) {}
  }

  function savePersisted() {
    try {
      persistLocalKeys.forEach(function (k) {
        if (Object.prototype.hasOwnProperty.call(store, k)) {
          localStorage.setItem('vx:' + k, JSON.stringify(store[k]));
        } else {
          localStorage.removeItem('vx:' + k);
        }
      });
    } catch (_) {}
    try {
      persistSessionKeys.forEach(function (k) {
        if (Object.prototype.hasOwnProperty.call(store, k)) {
          sessionStorage.setItem('vx:' + k, JSON.stringify(store[k]));
        } else {
          sessionStorage.removeItem('vx:' + k);
        }
      });
    } catch (_) {}
  }

  function syncUrl() {
    if (urlSyncKeys.size === 0) return;
    try {
      var url = new URL(window.location.href);
      var changed = false;
      urlSyncKeys.forEach(function (k) {
        var current = url.searchParams.get(k);
        if (Object.prototype.hasOwnProperty.call(store, k)) {
          var valStr = typeof store[k] === 'string' ? store[k] : JSON.stringify(store[k]);
          if (current !== valStr) {
            url.searchParams.set(k, valStr);
            changed = true;
          }
        } else if (current !== null) {
          url.searchParams.delete(k);
          changed = true;
        }
      });
      if (changed) {
        window.history.replaceState(window.history.state, '', url.pathname + url.search + url.hash);
      }
    } catch (_) {}
  }

  // --- 2. DOM Bindings & Seeds ---
  function bindTree(root) {
    if (!root || !root.querySelectorAll) return;

    // Seeds: vx-signals & vx-signals-if-missing
    var seedEls = root.matches && root.matches('[vx-signals],[vx-signals-if-missing]')
      ? [root] : Array.from(root.querySelectorAll('[vx-signals],[vx-signals-if-missing]'));
    seedEls.forEach(function (el) {
      var sigRaw = el.getAttribute('vx-signals');
      var ifMissingRaw = el.getAttribute('vx-signals-if-missing');
      if (sigRaw) {
        try { patchSignals(JSON.parse(sigRaw), false); } catch (e) { console.error('Invalid vx-signals JSON', e); }
      }
      if (ifMissingRaw) {
        try { patchSignals(JSON.parse(ifMissingRaw), true); } catch (e) { console.error('Invalid vx-signals-if-missing JSON', e); }
      }
      // Persist declarations
      var persist = el.getAttribute('vx-persist');
      if (persist) {
        persist.trim().split(/\s+/).forEach(function (k) { if (k) persistLocalKeys.add(k); });
      }
      var persistSession = el.getAttribute('vx-persist-session');
      if (persistSession) {
        persistSession.trim().split(/\s+/).forEach(function (k) { if (k) persistSessionKeys.add(k); });
      }
      var urlSync = el.getAttribute('vx-url-sync');
      if (urlSync) {
        urlSync.trim().split(/\s+/).forEach(function (k) { if (k) urlSyncKeys.add(k); });
      }
      loadPersisted();
    });

    // Binding elements: text, show, class, attr, style, bind
    var all = [root].concat(Array.from(root.querySelectorAll('*')));
    all.forEach(function (el) {
      if (!el.attributes) return;

      for (var i = 0; i < el.attributes.length; i++) {
        var attr = el.attributes[i];
        var name = attr.name;
        var val = attr.value.trim();

        // R2 Check: Disallow expression operators in signal names
        if (name.startsWith('vx-') && /[()<>=!&|+*\/;]/.test(val)) {
          console.warn('Växel Rule R2 violation: expressions prohibited in attribute value: ' + val);
          continue;
        }

        if (name === 'vx-text') {
          (function (sigKey) {
            subscribeSignal(sigKey, function (v) { el.textContent = v !== undefined && v !== null ? String(v) : ''; });
            el.textContent = store[sigKey] !== undefined && store[sigKey] !== null ? String(store[sigKey]) : '';
          })(val);
        } else if (name === 'vx-show') {
          (function (sigKey) {
            function updateShow(v) {
              var truthy = Boolean(v && v !== '0' && v !== 'false');
              el.hidden = !truthy;
              if (!truthy) el.style.display = 'none';
              else if (el.style.display === 'none') el.style.display = '';
            }
            subscribeSignal(sigKey, updateShow);
            updateShow(store[sigKey]);
          })(val);
        } else if (name.startsWith('vx-class:')) {
          var cls = name.substring('vx-class:'.length);
          (function (className, sigKey) {
            function updateClass(v) {
              var truthy = Boolean(v && v !== '0' && v !== 'false');
              el.classList.toggle(className, truthy);
            }
            subscribeSignal(sigKey, updateClass);
            updateClass(store[sigKey]);
          })(cls, val);
        } else if (name.startsWith('vx-attr:')) {
          var targetAttr = name.substring('vx-attr:'.length);
          (function (aName, sigKey) {
            function updateAttr(v) {
              if (v === false || v === null || v === undefined) {
                el.removeAttribute(aName);
              } else if (v === true) {
                el.setAttribute(aName, '');
              } else {
                el.setAttribute(aName, String(v));
              }
            }
            subscribeSignal(sigKey, updateAttr);
            updateAttr(store[sigKey]);
          })(targetAttr, val);
        } else if (name.startsWith('vx-style:')) {
          var prop = name.substring('vx-style:'.length);
          (function (cssProp, sigKey) {
            function updateStyle(v) {
              if (v === null || v === undefined) el.style.removeProperty(cssProp);
              else el.style.setProperty(cssProp, String(v));
            }
            subscribeSignal(sigKey, updateStyle);
            updateStyle(store[sigKey]);
          })(prop, val);
        } else if (name === 'vx-bind') {
          (function (sigKey) {
            var isCheckbox = el.type === 'checkbox';
            var isNumber = el.type === 'number' || el.type === 'range';
            function updateInput(v) {
              if (isCheckbox) el.checked = Boolean(v);
              else el.value = v !== undefined && v !== null ? v : '';
            }
            subscribeSignal(sigKey, updateInput);
            updateInput(store[sigKey]);

            var handler = function () {
              if (isCheckbox) setSignal(sigKey, el.checked);
              else if (isNumber) setSignal(sigKey, el.value === '' ? null : Number(el.value));
              else setSignal(sigKey, el.value);
            };
            el.addEventListener('input', handler);
            el.addEventListener('change', handler);
          })(val);
        }
      }
    });
  }

  // --- 3. DOM Morphing Engine ---
  function morphElement(fromEl, toEl) {
    if (fromEl.nodeType !== toEl.nodeType || fromEl.tagName !== toEl.tagName) {
      fromEl.replaceWith(toEl);
      return;
    }

    if (fromEl.nodeType === Node.TEXT_NODE || fromEl.nodeType === Node.COMMENT_NODE) {
      if (fromEl.textContent !== toEl.textContent) {
        fromEl.textContent = toEl.textContent;
      }
      return;
    }

    // Preservation check
    if (fromEl.hasAttribute && fromEl.hasAttribute('vx-preserve')) {
      return;
    }

    // Preserve attributes list (e.g. open on details)
    var preserveAttrs = (fromEl.getAttribute && fromEl.getAttribute('vx-preserve-attr') || '').split(/\s+/).filter(Boolean);

    // Sync attributes
    var toAttrs = toEl.attributes;
    for (var i = 0; i < toAttrs.length; i++) {
      var attr = toAttrs[i];
      if (preserveAttrs.indexOf(attr.name) === -1 && fromEl.getAttribute(attr.name) !== attr.value) {
        fromEl.setAttribute(attr.name, attr.value);
      }
    }
    var fromAttrs = fromEl.attributes;
    for (var j = fromAttrs.length - 1; j >= 0; j--) {
      var fAttr = fromAttrs[j];
      if (!toEl.hasAttribute(fAttr.name) && preserveAttrs.indexOf(fAttr.name) === -1) {
        fromEl.removeAttribute(fAttr.name);
      }
    }

    // Preserve dirty input state
    if (fromEl instanceof HTMLInputElement || fromEl instanceof HTMLTextAreaElement || fromEl instanceof HTMLSelectElement) {
      if (!fromEl.hasAttribute('vx-overwrite-dirty')) {
        // If dirty/focused, keep value and selection
        if (document.activeElement === fromEl) {
          toEl.value = fromEl.value;
        } else if (fromEl.value !== toEl.value && fromEl.defaultValue !== fromEl.value) {
          // Input was edited by user
          toEl.value = fromEl.value;
        }
      }
      if (fromEl instanceof HTMLInputElement && (fromEl.type === 'checkbox' || fromEl.type === 'radio')) {
        if (fromEl.defaultChecked !== fromEl.checked) {
          toEl.checked = fromEl.checked;
        }
      }
      if (fromEl.value !== toEl.value) fromEl.value = toEl.value;
      if (fromEl instanceof HTMLInputElement && fromEl.checked !== toEl.checked) fromEl.checked = toEl.checked;
    }

    // Sync Children (keyed by id, then by tag/position)
    var fromChild = fromEl.firstChild;
    var toChild = toEl.firstChild;

    while (toChild) {
      if (!fromChild) {
        var nextTo = toChild.nextSibling;
        fromEl.appendChild(toChild);
        toChild = nextTo;
        continue;
      }

      var fromNext = fromChild.nextSibling;
      var toNext = toChild.nextSibling;

      if (fromChild.nodeType === toChild.nodeType &&
          (fromChild.nodeType === Node.TEXT_NODE || fromChild.nodeType === Node.COMMENT_NODE)) {
        if (fromChild.textContent !== toChild.textContent) {
          fromChild.textContent = toChild.textContent;
        }
      } else if (fromChild.nodeType === Node.ELEMENT_NODE && toChild.nodeType === Node.ELEMENT_NODE) {
        if (fromChild.id && toChild.id && fromChild.id === toChild.id) {
          morphElement(fromChild, toChild);
        } else if (fromChild.tagName === toChild.tagName && (!fromChild.id || !toChild.id)) {
          morphElement(fromChild, toChild);
        } else {
          // Look ahead for matching id
          var matchInFrom = toChild.id ? fromEl.querySelector('#' + toChild.id) : null;
          if (matchInFrom && matchInFrom.parentNode === fromEl) {
            fromEl.insertBefore(matchInFrom, fromChild);
            morphElement(matchInFrom, toChild);
          } else {
            fromEl.insertBefore(toChild, fromChild);
          }
        }
      } else {
        fromEl.replaceChild(toChild, fromChild);
      }

      fromChild = fromNext;
      toChild = toNext;
    }

    // Remove remaining old children
    while (fromChild) {
      var removeMe = fromChild;
      fromChild = fromChild.nextSibling;
      fromEl.removeChild(removeMe);
    }
  }

  function applyPatch(targetSelector, fragmentHtml, mode, namespace) {
    var targetEl = document.querySelector(targetSelector);
    if (!targetEl) {
      return { success: false, ignored: true, reason: 'Target not found in DOM' };
    }

    mode = (mode || 'morph').toLowerCase();
    namespace = (namespace || 'html').toLowerCase();

    // Parse fragment
    var parser = new DOMParser();
    var doc = parser.parseFromString(fragmentHtml || '', 'text/html');
    var fragment = doc.body;

    if (namespace === 'svg') {
      var svgDoc = parser.parseFromString('<svg xmlns="http://www.w3.org/2000/svg">' + fragmentHtml + '</svg>', 'image/svg+xml');
      fragment = svgDoc.documentElement;
    }

    switch (mode) {
      case 'morph':
      case 'outer':
        if (fragment.firstElementChild) {
          morphElement(targetEl, fragment.firstElementChild);
        }
        break;

      case 'replace':
        if (fragment.firstElementChild) {
          targetEl.replaceWith(fragment.firstElementChild);
        }
        break;

      case 'inner':
        var tempWrapper = targetEl.cloneNode(false);
        while (fragment.firstChild) tempWrapper.appendChild(fragment.firstChild);
        morphElement(targetEl, tempWrapper);
        break;

      case 'append':
        while (fragment.firstChild) {
          targetEl.appendChild(fragment.firstChild);
        }
        break;

      case 'prepend':
        var first = targetEl.firstChild;
        while (fragment.firstChild) {
          targetEl.insertBefore(fragment.firstChild, first);
        }
        break;

      case 'before':
        if (targetEl.parentNode) {
          while (fragment.firstChild) {
            targetEl.parentNode.insertBefore(fragment.firstChild, targetEl);
          }
        }
        break;

      case 'after':
        if (targetEl.parentNode) {
          var next = targetEl.nextSibling;
          while (fragment.firstChild) {
            targetEl.parentNode.insertBefore(fragment.firstChild, next);
          }
        }
        break;

      case 'remove':
        if (targetEl.parentNode) {
          targetEl.parentNode.removeChild(targetEl);
        }
        break;

      default:
        return { success: false, error: 'Unknown swap mode: ' + mode };
    }

    bindTree(document.body);
    return { success: true };
  }

  // --- 4. Live Region & A11y ---
  var liveRegion = null;
  function ensureLiveRegion() {
    if (!liveRegion || !document.body.contains(liveRegion)) {
      liveRegion = document.createElement('div');
      liveRegion.id = 'vx-live-region';
      liveRegion.setAttribute('aria-live', 'polite');
      liveRegion.setAttribute('aria-atomic', 'true');
      liveRegion.style.position = 'absolute';
      liveRegion.style.width = '1px';
      liveRegion.style.height = '1px';
      liveRegion.style.margin = '-1px';
      liveRegion.style.padding = '0';
      liveRegion.style.overflow = 'hidden';
      liveRegion.style.clip = 'rect(0, 0, 0, 0)';
      liveRegion.style.border = '0';
      document.body.appendChild(liveRegion);
    }
  }

  function announceText(text) {
    if (!text) return;
    ensureLiveRegion();
    liveRegion.textContent = '';
    setTimeout(function () { liveRegion.textContent = text; }, 50);
  }

  // --- 5. Patch Document Processor ---
  function processPatchDocument(htmlText, userInitiated, triggerEl) {
    emitEvent('vx:before-apply', { html: htmlText });

    var parser = new DOMParser();
    var doc = parser.parseFromString(htmlText, 'text/html');

    var patches = Array.from(doc.querySelectorAll('vx-patch'));
    var signalsEl = doc.querySelector('vx-signals');
    var directiveEl = doc.querySelector('vx-directive');

    // 1. Apply Patches
    var appliedCount = 0;
    patches.forEach(function (patch) {
      var target = patch.getAttribute('target');
      var mode = patch.getAttribute('mode') || 'morph';
      var ns = patch.getAttribute('namespace') || 'html';
      var content = patch.innerHTML;

      var res = applyPatch(target, content, mode, ns);
      if (res.success) appliedCount++;
    });

    // 2. Signals
    if (signalsEl) {
      var sigText = signalsEl.textContent.trim();
      var onlyIfMissing = signalsEl.hasAttribute('only-if-missing');
      if (sigText) {
        try {
          patchSignals(JSON.parse(sigText), onlyIfMissing);
        } catch (e) {
          console.error('Invalid <vx-signals> payload', e);
        }
      }
    }

    // 3. Directive
    if (directiveEl) {
      // Redirect
      var redirect = directiveEl.getAttribute('redirect');
      if (redirect) {
        // Validate same-origin or relative
        try {
          var dest = new URL(redirect, window.location.href);
          if (dest.origin === window.location.origin) {
            window.location.href = dest.href;
            return;
          } else {
            emitEvent('vx:error', { reason: 'Cross-origin redirect rejected', url: redirect });
          }
        } catch (_) {}
      }

      // Reload
      if (directiveEl.getAttribute('reload') === '1') {
        window.location.reload();
        return;
      }

      // History
      var pushUrl = directiveEl.getAttribute('push-url');
      var replaceUrl = directiveEl.getAttribute('replace-url');
      if (pushUrl) {
        window.history.pushState({}, '', pushUrl);
      } else if (replaceUrl) {
        window.history.replaceState({}, '', replaceUrl);
      }

      // Title
      var title = directiveEl.getAttribute('title');
      if (title) document.title = title;

      // Announce
      var announce = directiveEl.getAttribute('announce');
      if (announce) announceText(announce);

      // Focus
      var focusTarget = directiveEl.getAttribute('focus');
      if (userInitiated && focusTarget) {
        var elToFocus = document.querySelector(focusTarget);
        if (elToFocus && elToFocus.focus) elToFocus.focus();
      }

      // Scroll
      var scrollTarget = directiveEl.getAttribute('scroll');
      if (scrollTarget) {
        var behavior = directiveEl.getAttribute('scroll-behavior') || 'instant';
        var block = directiveEl.getAttribute('scroll-block') || 'start';
        var inline = directiveEl.getAttribute('scroll-inline') || 'nearest';
        if (scrollTarget === 'top') {
          window.scrollTo({ top: 0, behavior: behavior });
        } else {
          var scrollEl = document.querySelector(scrollTarget);
          if (scrollEl && scrollEl.scrollIntoView) {
            scrollEl.scrollIntoView({ behavior: behavior, block: block, inline: inline });
            if (directiveEl.getAttribute('scroll-focus') === '1' && scrollEl.focus) {
              scrollEl.focus();
            }
          }
        }
      }
    } else if (userInitiated) {
      // Default focus restoration
      if (triggerEl && document.body.contains(triggerEl) && triggerEl.focus) {
        triggerEl.focus();
      }
    }

    emitEvent('vx:after-apply', { patchesApplied: appliedCount });
  }

  // --- 6. Fetch & Concurrency ---
  var inFlightRequests = new Map(); // trigger -> AbortController
  var sequenceCounter = 0;

  function executeRequest(el, method, url) {
    method = (method || 'GET').toUpperCase();
    var syncPolicy = el.getAttribute('vx-sync') || 'replace';

    if (syncPolicy === 'drop' && inFlightRequests.has(el)) {
      return;
    }

    if (syncPolicy === 'abort') {
      if (inFlightRequests.has(el)) {
        inFlightRequests.get(el).abort();
        inFlightRequests.delete(el);
      }
      return;
    }

    if (syncPolicy === 'replace' && inFlightRequests.has(el)) {
      inFlightRequests.get(el).abort();
      inFlightRequests.delete(el);
    }

    var abortCtrl = new AbortController();
    inFlightRequests.set(el, abortCtrl);

    var seq = ++sequenceCounter;
    var target = el.getAttribute('vx-target') || '';

    // Indicators & Disables
    var indicatorEl = null;
    var indicatorSel = el.getAttribute('vx-indicator');
    if (indicatorSel) indicatorEl = document.querySelector(indicatorSel);
    if (indicatorEl) {
      indicatorEl.classList.add('vx-loading');
      indicatorEl.setAttribute('aria-busy', 'true');
    }
    var disableTarget = el.hasAttribute('vx-disable');
    if (disableTarget) el.disabled = true;

    // Headers
    var headers = {
      'VX-Request': '1',
      'VX-Protocol': '1',
      'VX-Sequence': String(seq),
      'VX-Url': window.location.pathname + window.location.search
    };
    if (target) headers['VX-Target'] = target;

    // CSRF meta
    var csrfMeta = document.querySelector('meta[name="vx-csrf"]');
    if (csrfMeta && csrfMeta.content) {
      headers['X-CSRF'] = csrfMeta.content;
    }

    // Signals payload
    var allSignals = getAllSignals();
    var sigJson = JSON.stringify(allSignals);
    if (sigJson.length > 8192) {
      headers['VX-Signals-Omitted'] = '1';
    } else {
      headers['VX-Signals'] = sigJson;
    }

    // Body
    var body = undefined;
    var isJsonEncoding = el.getAttribute('vx-encoding') === 'json';

    if (method !== 'GET' && method !== 'HEAD') {
      if (el instanceof HTMLFormElement) {
        var formData = new FormData(el);
        if (isJsonEncoding) {
          var formObj = {};
          formData.forEach(function (v, k) { formObj[k] = v; });
          body = JSON.stringify(formObj);
          headers['Content-Type'] = 'application/json';
        } else {
          body = new URLSearchParams(formData).toString();
          headers['Content-Type'] = 'application/x-www-form-urlencoded';
        }
      } else {
        // Collect vx-vals-*
        var vals = new URLSearchParams();
        for (var i = 0; i < el.attributes.length; i++) {
          var a = el.attributes[i];
          if (a.name.startsWith('vx-vals-')) {
            vals.set(a.name.substring('vx-vals-'.length), a.value);
          }
        }
        if (isJsonEncoding) {
          var valsObj = {};
          vals.forEach(function (v, k) { valsObj[k] = v; });
          body = JSON.stringify(valsObj);
          headers['Content-Type'] = 'application/json';
        } else if (Array.from(vals.keys()).length > 0) {
          body = vals.toString();
          headers['Content-Type'] = 'application/x-www-form-urlencoded';
        }
      }
    }

    var evtDetail = { url: url, method: method, headers: headers, body: body };
    if (!emitEvent('vx:before-request', evtDetail, true)) {
      inFlightRequests.delete(el);
      if (indicatorEl) indicatorEl.classList.remove('vx-loading');
      if (disableTarget) el.disabled = false;
      return;
    }

    fetch(url, {
      method: method,
      headers: headers,
      body: body,
      signal: abortCtrl.signal
    }).then(function (res) {
      inFlightRequests.delete(el);
      if (indicatorEl) {
        indicatorEl.classList.remove('vx-loading');
        indicatorEl.removeAttribute('aria-busy');
      }
      if (disableTarget) el.disabled = false;

      // Check protocol major version
      var resProto = res.headers.get('VX-Protocol');
      if (resProto && resProto.split('.')[0] !== '1') {
        window.location.href = url;
        return;
      }

      var contentType = res.headers.get('Content-Type') || '';
      if (contentType.indexOf('text/vnd.vaxel-patch+html') !== -1 || contentType.indexOf('text/html') !== -1) {
        return res.text().then(function (html) {
          processPatchDocument(html, true, el);
        });
      }
    }).catch(function (err) {
      inFlightRequests.delete(el);
      if (indicatorEl) {
        indicatorEl.classList.remove('vx-loading');
        indicatorEl.removeAttribute('aria-busy');
      }
      if (disableTarget) el.disabled = false;
      if (err.name !== 'AbortError') {
        emitEvent('vx:error', { error: err });
      }
    });
  }

  // --- 7. Event Delegator ---
  function initTriggers() {
    document.addEventListener('click', function (e) {
      var trigger = e.target.closest('a[vx-get], a[vx-post], a[vx-put], a[vx-patch], a[vx-delete], button[vx-get], button[vx-post], button[vx-put], button[vx-patch], button[vx-delete]');
      if (!trigger) return;

      var confirmMsg = trigger.getAttribute('vx-confirm');
      if (confirmMsg && !window.confirm(confirmMsg)) {
        e.preventDefault();
        return;
      }

      var method = 'GET';
      var url = trigger.getAttribute('href') || window.location.href;
      ['vx-get', 'vx-post', 'vx-put', 'vx-patch', 'vx-delete'].forEach(function (attr) {
        if (trigger.hasAttribute(attr)) {
          method = attr.substring(3).toUpperCase();
          var customUrl = trigger.getAttribute(attr);
          if (customUrl) url = customUrl;
        }
      });

      e.preventDefault();
      executeRequest(trigger, method, url);
    });

    document.addEventListener('submit', function (e) {
      var form = e.target;
      if (!form.hasAttribute('vx-post') && !form.hasAttribute('vx-get') &&
          !form.hasAttribute('vx-put') && !form.hasAttribute('vx-patch') &&
          !form.hasAttribute('vx-delete')) {
        return;
      }

      var confirmMsg = form.getAttribute('vx-confirm');
      if (confirmMsg && !window.confirm(confirmMsg)) {
        e.preventDefault();
        return;
      }

      var method = 'POST';
      var url = form.getAttribute('action') || window.location.href;
      ['vx-get', 'vx-post', 'vx-put', 'vx-patch', 'vx-delete'].forEach(function (attr) {
        if (form.hasAttribute(attr)) {
          method = attr.substring(3).toUpperCase();
          var customUrl = form.getAttribute(attr);
          if (customUrl) url = customUrl;
        }
      });

      e.preventDefault();
      executeRequest(form, method, url);
    });

    // Popstate History Restore
    window.addEventListener('popstate', function () {
      loadPersisted();
      fetch(window.location.href, {
        headers: {
          'VX-Request': '1',
          'VX-Protocol': '1',
          'VX-History': 'restore',
          'VX-Signals': JSON.stringify(getAllSignals())
        }
      }).then(function (res) {
        return res.text();
      }).then(function (html) {
        processPatchDocument(html, false, null);
      }).catch(function () {
        window.location.reload();
      });
    });
  }

  // --- 8. SSE Client ---
  var sseSource = null;
  function initSse() {
    var sseEl = document.querySelector('[vx-sse]');
    if (!sseEl) return;
    var sseUrl = sseEl.getAttribute('vx-sse');
    if (!sseUrl || sseSource) return;

    try {
      sseSource = new EventSource(sseUrl);
      sseSource.onopen = function () {
        emitEvent('vx:sse-state', { state: 'open', url: sseUrl });
      };
      sseSource.addEventListener('vx-patch', function (e) {
        if (e.data) {
          processPatchDocument(e.data, false, null);
        }
      });
      sseSource.addEventListener('vx-reload', function () {
        window.location.reload();
      });
      sseSource.onerror = function (err) {
        emitEvent('vx:sse-state', { state: 'error', error: err });
      };
    } catch (e) {
      emitEvent('vx:sse-state', { state: 'error', error: e });
    }
  }

  function emitEvent(name, detail, cancelable) {
    var evt = new CustomEvent(name, {
      bubbles: true,
      cancelable: Boolean(cancelable),
      detail: detail || {}
    });
    return document.dispatchEvent(evt);
  }

  // Bootstrap
  function init() {
    loadPersisted();
    bindTree(document.body);
    initTriggers();
    initSse();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }

  // Public API for inspections / tests
  global.Vaxel = {
    get: getSignal,
    set: setSignal,
    patch: patchSignals,
    all: getAllSignals,
    subscribe: subscribeSignal,
    applyPatch: applyPatch,
    processDocument: processPatchDocument
  };

})(typeof window !== 'undefined' ? window : this);
