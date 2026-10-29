/**
 * vaxel Client Agent (v0.2)
 * Server-driven web framework for .NET.
 * Zero-eval, strict CSP compatible, self-contained.
 */
(function (global) {
  'use strict';

  // --- 1. Signal Store (Auto-tracking Signal & EffectScope + Private Scheduler) ---
  var activeEffect = null;
  var pending = new Set(); // Set<EffectScope>
  var writeDepth = 0;
  var isDraining = false;

  function beginWrite() {
    writeDepth++;
  }

  function endWrite() {
    writeDepth--;
    if (writeDepth === 0 && !isDraining) {
      drain();
    }
  }

  function drain() {
    if (isDraining) return;
    isDraining = true;
    var pass = 0;
    try {
      while (pending.size > 0) {
        pass++;
        if (pass > 100) {
          pending.clear();
          emitEvent('vx:error', { reason: 'Effect cycle exceeded 100 drain passes; aborting drain.' });
          break;
        }
        var currentPass = Array.from(pending);
        pending.clear();
        for (var i = 0; i < currentPass.length; i++) {
          var scope = currentPass[i];
          if (!scope._disposed) {
            scope.run();
          }
        }
      }
    } finally {
      isDraining = false;
    }
  }

  function EffectScope(fn) {
    this._fn = fn;
    this._signals = new Set();
    this._disposed = false;
    this._isRunning = false;
    var self = this;

    this.addSignal = function (sig) {
      self._signals.add(sig);
    };

    this.run = function () {
      if (self._disposed || self._isRunning) return;
      self._isRunning = true;
      beginWrite();
      self._signals.forEach(function (sig) {
        sig.removeSubscriber(self);
      });
      self._signals.clear();

      var prev = activeEffect;
      activeEffect = self;
      try {
        self._fn();
      } catch (e) {
        console.error(e);
      } finally {
        activeEffect = prev;
        self._isRunning = false;
        endWrite();
      }
    };

    this.dispose = function () {
      if (self._disposed) return;
      self._disposed = true;
      self._signals.forEach(function (sig) {
        sig.removeSubscriber(self);
      });
      self._signals.clear();
      pending.delete(self);
    };
  }

  function Signal(initialValue) {
    this._value = initialValue;
    this._subscribers = new Set();
  }

  Object.defineProperty(Signal.prototype, 'value', {
    get: function () {
      if (activeEffect) {
        this._subscribers.add(activeEffect);
        activeEffect.addSignal(this);
      }
      return this._value;
    },
    set: function (next) {
      if (this._value !== next) {
        this._value = next;
        beginWrite();
        try {
          var subs = Array.from(this._subscribers);
          for (var i = 0; i < subs.length; i++) {
            var scope = subs[i];
            if (!scope._disposed) {
              pending.add(scope);
            }
          }
        } finally {
          endWrite();
        }
      }
    }
  });

  Signal.prototype.removeSubscriber = function (sub) {
    this._subscribers.delete(sub);
  };

  Signal.prototype.peek = function () {
    return this._value;
  };

  Signal.prototype._subscriberCount = function () {
    return this._subscribers.size;
  };

  function signal(initialValue) {
    return new Signal(initialValue);
  }

  function effect(fn) {
    var scope = new EffectScope(fn);
    scope.run();
    if (!isDraining && writeDepth === 0 && pending.size > 0) {
      drain();
    }
    return scope.dispose;
  }

  var store = {};
  var signalMap = {}; // key -> Signal
  var subscribers = {}; // retained for compatibility introspection
  var persistLocalKeys = new Set();
  var persistSessionKeys = new Set();
  var urlSyncKeys = new Set();

  function ensureSignal(key) {
    if (!Object.prototype.hasOwnProperty.call(signalMap, key)) {
      var initial = Object.prototype.hasOwnProperty.call(store, key) ? store[key] : undefined;
      signalMap[key] = new Signal(initial);
    }
    return signalMap[key];
  }

  function readSignalTracked(key) {
    return ensureSignal(key).value;
  }

  function getSignal(key) {
    if (Object.prototype.hasOwnProperty.call(signalMap, key)) {
      return signalMap[key].peek();
    }
    return store[key];
  }

  function notify(key, value) {
    if (subscribers[key]) {
      subscribers[key].forEach(function (cb) {
        try { cb(value); } catch (e) { console.error(e); }
      });
    }
  }

  function setSignal(key, value) {
    var sig = ensureSignal(key);
    var isNullish = value === null || value === undefined;
    var nextVal = isNullish ? undefined : value;
    var hadKey = Object.prototype.hasOwnProperty.call(store, key);

    if (isNullish && !hadKey && sig.peek() === undefined) return;
    if (!isNullish && hadKey && store[key] === nextVal && sig.peek() === nextVal) return;

    beginWrite();
    try {
      if (isNullish) {
        delete store[key];
        sig.value = undefined;
      } else {
        store[key] = nextVal;
        sig.value = nextVal;
      }
      notify(key, nextVal);
    } finally {
      endWrite();
    }
    savePersisted();
    syncUrl();
  }

  function patchSignals(obj, onlyIfMissing) {
    if (!obj || typeof obj !== 'object') return;
    var changed = [];
    beginWrite();
    try {
      for (var k in obj) {
        if (Object.prototype.hasOwnProperty.call(obj, k)) {
          if (onlyIfMissing && Object.prototype.hasOwnProperty.call(store, k)) {
            continue;
          }
          var v = obj[k];
          var sig = ensureSignal(k);
          if (v === null || v === undefined) {
            if (Object.prototype.hasOwnProperty.call(store, k)) {
              delete store[k];
              sig.value = undefined;
              changed.push(k);
            }
          } else if (!Object.prototype.hasOwnProperty.call(store, k) || store[k] !== v) {
            store[k] = v;
            sig.value = v;
            changed.push(k);
          }
        }
      }
      for (var i = 0; i < changed.length; i++) {
        notify(changed[i], store[changed[i]]);
      }
    } finally {
      endWrite();
    }
    if (changed.length > 0) {
      savePersisted();
      syncUrl();
      emitEvent('vx:signals-changed', { changed: changed, store: getAllSignals() });
    }
  }

  function subscribeSignal(key, cb) {
    var sig = ensureSignal(key);
    var initialized = false;
    var lastVal = sig.peek();
    return effect(function () {
      var val = sig.value;
      if (!initialized) {
        initialized = true;
        lastVal = val;
        return;
      }
      if (val !== lastVal) {
        lastVal = val;
        try { cb(val); } catch (e) { console.error(e); }
      }
    });
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
    var loaded = {};
    var anyLoaded = false;
    try {
      persistLocalKeys.forEach(function (k) {
        var v = localStorage.getItem('vx:' + k);
        if (v !== null) {
          try { loaded[k] = JSON.parse(v); } catch (_) { loaded[k] = v; }
          anyLoaded = true;
        }
      });
    } catch (_) {}
    try {
      persistSessionKeys.forEach(function (k) {
        var v = sessionStorage.getItem('vx:' + k);
        if (v !== null) {
          try { loaded[k] = JSON.parse(v); } catch (_) { loaded[k] = v; }
          anyLoaded = true;
        }
      });
    } catch (_) {}
    // Load from URL if present
    try {
      var params = new URLSearchParams(window.location.search);
      urlSyncKeys.forEach(function (k) {
        if (params.has(k)) {
          var raw = params.get(k);
          try { loaded[k] = JSON.parse(raw); } catch (_) { loaded[k] = raw; }
          anyLoaded = true;
        }
      });
    } catch (_) {}
    if (anyLoaded) {
      beginWrite();
      try {
        for (var k in loaded) {
          if (Object.prototype.hasOwnProperty.call(loaded, k)) {
            var val = loaded[k];
            if (val === null || val === undefined) {
              delete store[k];
              ensureSignal(k).value = undefined;
            } else {
              store[k] = val;
              ensureSignal(k).value = val;
            }
          }
        }
      } finally {
        endWrite();
      }
    }
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

  // --- 2. Closed Expression Engine (lifted from Centralen expressions/*.ts) ---
  var EXPR_MAX_LENGTH = 512;
  var EXPR_MAX_DEPTH = 32;
  var EXPR_PRECEDENCE = { QUESTION: 1, OR: 2, AND: 3, EQ: 4, NEQ: 4, LT: 5, LTE: 5, GT: 5, GTE: 5, PLUS: 6, MINUS: 6, STAR: 7, SLASH: 7, PERCENT: 7, DOT: 9, LPAREN: 9 };
  var EXPR_ESCAPES = { '\\': '\\', n: '\n', t: '\t', r: '\r', "'": "'", '"': '"', '@': '@' };
  var EXPR_TWO_CHAR = { '==': 'EQ', '!=': 'NEQ', '<=': 'LTE', '>=': 'GTE', '&&': 'AND', '||': 'OR' };
  var EXPR_ONE_CHAR = { '+': 'PLUS', '-': 'MINUS', '*': 'STAR', '/': 'SLASH', '%': 'PERCENT', '<': 'LT', '>': 'GT', '!': 'NOT', '?': 'QUESTION', ':': 'COLON', '(': 'LPAREN', ')': 'RPAREN', ',': 'COMMA', '.': 'DOT' };

  function exprOk(v) { return { ok: true, value: v }; }
  function exprRefuse(code, reason, remedy, path) { return { ok: false, refusal: { code: code, reason: reason || code, remedy: remedy || '', path: path } }; }
  function synErr(msg, pos) { return exprRefuse('expressions.syntax', msg, 'Check expression syntax', pos != null ? '@pos ' + pos : undefined); }
  function typeErr(msg) { return exprRefuse('expressions.type', msg, 'Check operand or argument types'); }

  function tokenizeExpr(input) {
    if (typeof input !== 'string') return typeErr('Expression must be a string');
    if (input.length > EXPR_MAX_LENGTH) return synErr('Max length exceeded', EXPR_MAX_LENGTH);
    var tokens = [], i = 0, len = input.length;
    while (i < len) {
      var ch = input[i];
      if (ch === ' ' || ch === '\t' || ch === '\n' || ch === '\r') { i++; continue; }
      var pos = i;
      if (ch === "'" || ch === '"') {
        var quote = ch, str = '', closed = false;
        i++;
        while (i < len) {
          var c = input[i];
          if (c === '\\') {
            i++;
            if (i >= len) return synErr('Unfinished escape', i);
            var esc = input[i];
            if (esc === quote) str += quote;
            else if (Object.prototype.hasOwnProperty.call(EXPR_ESCAPES, esc)) str += EXPR_ESCAPES[esc];
            else return synErr('Invalid escape', i);
            i++;
          } else if (c === quote) { closed = true; i++; break; }
          else { str += c; i++; }
        }
        if (!closed) return synErr('Unclosed string', pos);
        tokens.push({ type: 'STRING', value: str, pos: pos });
        continue;
      }
      if ((ch >= '0' && ch <= '9') || (ch === '.' && i + 1 < len && input[i + 1] >= '0' && input[i + 1] <= '9')) {
        var numStr = '', hasDot = false;
        while (i < len) {
          var nc = input[i];
          if (nc >= '0' && nc <= '9') { numStr += nc; i++; }
          else if (nc === '.' && !hasDot && i + 1 < len && input[i + 1] >= '0' && input[i + 1] <= '9') { hasDot = true; numStr += nc; i++; }
          else break;
        }
        tokens.push({ type: 'NUMBER', value: hasDot ? parseFloat(numStr) : parseInt(numStr, 10), pos: pos });
        continue;
      }
      if ((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || ch === '_') {
        var idStr = '';
        while (i < len) {
          var ic = input[i];
          if ((ic >= 'a' && ic <= 'z') || (ic >= 'A' && ic <= 'Z') || (ic >= '0' && ic <= '9') || ic === '_') { idStr += ic; i++; }
          else break;
        }
        if (idStr === 'true' || idStr === 'false') tokens.push({ type: 'BOOLEAN', value: idStr === 'true', pos: pos });
        else if (idStr === 'null') tokens.push({ type: 'NULL', value: null, pos: pos });
        else tokens.push({ type: 'IDENTIFIER', value: idStr, pos: pos });
        continue;
      }
      var two = i + 1 < len ? ch + input[i + 1] : '';
      if (two && EXPR_TWO_CHAR[two]) {
        tokens.push({ type: EXPR_TWO_CHAR[two], value: two, pos: pos });
        i += 2;
        continue;
      }
      if (EXPR_ONE_CHAR[ch]) {
        tokens.push({ type: EXPR_ONE_CHAR[ch], value: ch, pos: pos });
        i++;
        continue;
      }
      return synErr('Unexpected character', pos);
    }
    tokens.push({ type: 'EOF', value: null, pos: len });
    return exprOk(tokens);
  }

  function parseExprTokens(tokens) {
    var index = 0;
    function cur() { return tokens[index] || { type: 'EOF', value: null, pos: -1 }; }
    function adv() { return tokens[index++] || { type: 'EOF', value: null, pos: -1 }; }
    function match(t) { if (cur().type === t) { adv(); return true; } return false; }

    function parseExpr(prec, depth) {
      if (depth > EXPR_MAX_DEPTH) return synErr('Max depth exceeded', cur().pos);
      var tok = adv();
      var pRes = parsePrefix(tok, depth);
      if (!pRes.ok) return pRes;
      var left = pRes.value;

      while (prec < (EXPR_PRECEDENCE[cur().type] || 0)) {
        var inf = cur();
        if (inf.type === 'QUESTION') {
          adv();
          var cRes = parseExpr(0, depth + 1);
          if (!cRes.ok) return cRes;
          if (!match('COLON')) return synErr("Expected ':'", cur().pos);
          var aRes = parseExpr(EXPR_PRECEDENCE.QUESTION - 1, depth + 1);
          if (!aRes.ok) return aRes;
          left = { kind: 'Ternary', condition: left, consequent: cRes.value, alternate: aRes.value };
          continue;
        }
        if (inf.type === 'DOT') {
          adv();
          var prop = adv();
          if (prop.type !== 'IDENTIFIER') return synErr('Expected identifier', prop.pos);
          left = { kind: 'MemberAccess', object: left, property: prop.value };
          continue;
        }
        if (inf.type === 'LPAREN') {
          if (left.kind !== 'Identifier') return synErr('Invalid callee', inf.pos);
          adv();
          var args = [];
          if (cur().type !== 'RPAREN') {
            while (true) {
              var argRes = parseExpr(0, depth + 1);
              if (!argRes.ok) return argRes;
              args.push(argRes.value);
              if (match('COMMA')) {
                if (cur().type === 'COMMA' || cur().type === 'RPAREN') return synErr('Trailing comma', cur().pos);
                continue;
              }
              break;
            }
          }
          if (!match('RPAREN')) return synErr("Expected ')'", cur().pos);
          left = { kind: 'Call', callee: left.name, args: args };
          continue;
        }
        adv();
        var rRes = parseExpr(EXPR_PRECEDENCE[inf.type], depth + 1);
        if (!rRes.ok) return rRes;
        left = { kind: 'BinaryOp', op: inf.value, left: left, right: rRes.value };
      }
      return exprOk(left);
    }

    function parsePrefix(tok, depth) {
      if (tok.type === 'NUMBER' || tok.type === 'STRING' || tok.type === 'BOOLEAN' || tok.type === 'NULL') {
        return exprOk({ kind: 'Literal', value: tok.value });
      }
      if (tok.type === 'IDENTIFIER') return exprOk({ kind: 'Identifier', name: tok.value });
      if (tok.type === 'PLUS' || tok.type === 'MINUS' || tok.type === 'NOT') {
        var oRes = parseExpr(8, depth + 1);
        return oRes.ok ? exprOk({ kind: 'UnaryOp', op: tok.value, argument: oRes.value }) : oRes;
      }
      if (tok.type === 'LPAREN') {
        var eRes = parseExpr(0, depth + 1);
        if (!eRes.ok) return eRes;
        return match('RPAREN') ? eRes : synErr("Expected ')'", cur().pos);
      }
      return synErr('Unexpected token', tok.pos);
    }

    if (tokens.length === 0 || tokens[0].type === 'EOF') return synErr('Empty expression', 0);
    var res = parseExpr(0, 1);
    if (!res.ok) return res;
    return cur().type === 'EOF' ? res : synErr('Unexpected trailing token', cur().pos);
  }

  function isForbiddenProperty(p) {
    return p === '__proto__' || p === 'constructor' || p === 'prototype';
  }

  var EXPR_FNS = {
    abs: [1, 'n', function (a) { return exprOk(Math.abs(a)); }],
    round: [1, 'n', function (a) { return exprOk(Math.round(a)); }],
    floor: [1, 'n', function (a) { return exprOk(Math.floor(a)); }],
    ceil: [1, 'n', function (a) { return exprOk(Math.ceil(a)); }],
    min: [2, 'nn', function (a, b) { return exprOk(Math.min(a, b)); }],
    max: [2, 'nn', function (a, b) { return exprOk(Math.max(a, b)); }],
    len: [1, 's', function (a) { return exprOk(Array.from(a).length); }],
    trim: [1, 's', function (a) { return exprOk(a.trim()); }],
    toUpperCase: [1, 's', function (a) { return exprOk(a.toUpperCase()); }],
    toLowerCase: [1, 's', function (a) { return exprOk(a.toLowerCase()); }],
    encodeUri: [1, 's', function (a) { return exprOk(encodeURIComponent(a)); }],
    contains: [2, 'ss', function (a, b) { return exprOk(a.indexOf(b) !== -1); }],
    startsWith: [2, 'ss', function (a, b) { return exprOk(a.startsWith(b)); }],
    endsWith: [2, 'ss', function (a, b) { return exprOk(a.endsWith(b)); }],
    replace: [3, 'sss', function (a, b, c) { return exprOk(a.split(b).join(c)); }]
  };

  function evaluateExprCall(callee, args, lookup) {
    var ev = [];
    for (var i = 0; i < args.length; i++) {
      var r = evaluateAstNode(args[i], lookup);
      if (!r.ok) return r;
      ev.push(r.value);
    }
    if (Object.prototype.hasOwnProperty.call(EXPR_FNS, callee)) {
      var spec = EXPR_FNS[callee];
      if (ev.length !== spec[0]) return typeErr(callee + ' arg count');
      for (var j = 0; j < spec[1].length; j++) {
        var exp = spec[1][j] === 'n' ? 'number' : 'string';
        if (typeof ev[j] !== exp) return typeErr(callee + ' arg type');
      }
      return spec[2].apply(null, ev);
    }
    if (callee === 'substring') {
      if (ev.length < 2 || ev.length > 3) return typeErr('substring args');
      var s = ev[0], st = ev[1], en = ev[2];
      if (typeof s !== 'string' || typeof st !== 'number' || (en !== undefined && typeof en !== 'number')) return typeErr('substring types');
      if (st < 0 || (en !== undefined && en < st) || st > s.length || (en !== undefined && en > s.length)) return typeErr('substring bounds');
      return exprOk(en !== undefined ? s.substring(st, en) : s.substring(st));
    }
    if (callee === 'toBoolean') {
      if (ev.length !== 1) return typeErr('toBoolean args');
      var bv = ev[0];
      if (typeof bv === 'boolean') return exprOk(bv);
      if (typeof bv === 'number') return exprOk(bv !== 0);
      if (typeof bv === 'string' && (bv.toLowerCase() === 'true' || bv.toLowerCase() === 'false')) return exprOk(bv.toLowerCase() === 'true');
      return typeErr('toBoolean cast');
    }
    if (callee === 'toInteger') {
      if (ev.length !== 1) return typeErr('toInteger args');
      var iv = ev[0];
      if (typeof iv === 'number') return exprOk(Math.trunc(iv));
      if (typeof iv === 'boolean') return exprOk(iv ? 1 : 0);
      if (typeof iv === 'string' && iv.trim() !== '' && !isNaN(Number(iv))) return exprOk(Math.trunc(Number(iv)));
      return typeErr('toInteger cast');
    }
    if (callee === 'toNumber') {
      if (ev.length !== 1) return typeErr('toNumber args');
      var nv = ev[0];
      if (typeof nv === 'number') return exprOk(nv);
      if (typeof nv === 'string' && nv.trim() !== '' && !isNaN(Number(nv))) return exprOk(Number(nv));
      return typeErr('toNumber cast');
    }
    if (callee === 'toString') {
      if (ev.length !== 1) return typeErr('toString args');
      return exprOk(ev[0] === null ? 'null' : String(ev[0]));
    }
    return exprRefuse('expressions.unknown_function', 'Unknown function ' + callee, 'Use whitelisted function');
  }

  function evaluateAstNode(node, lookup) {
    if (node.kind === 'Literal') return exprOk(node.value);
    if (node.kind === 'Identifier') {
      return isForbiddenProperty(node.name)
        ? exprRefuse('expressions.unknown_field', 'Unknown ' + node.name, '', node.name)
        : lookup(node.name);
    }
    if (node.kind === 'MemberAccess') {
      var oRes = evaluateAstNode(node.object, lookup);
      if (!oRes.ok) return oRes;
      var o = oRes.value;
      if (o === null || o === undefined || typeof o !== 'object' || isForbiddenProperty(node.property)) return exprOk(null);
      if (Array.isArray(o) && node.property === 'length') return exprOk(o.length);
      return exprOk(Object.prototype.hasOwnProperty.call(o, node.property) ? o[node.property] : null);
    }
    if (node.kind === 'UnaryOp') {
      var uRes = evaluateAstNode(node.argument, lookup);
      if (!uRes.ok) return uRes;
      if (node.op === '!') return typeof uRes.value === 'boolean' ? exprOk(!uRes.value) : typeErr('! expects bool');
      return typeof uRes.value === 'number' ? exprOk(node.op === '-' ? -uRes.value : +uRes.value) : typeErr('unary expects number');
    }
    if (node.kind === 'Ternary') {
      var cRes = evaluateAstNode(node.condition, lookup);
      if (!cRes.ok) return cRes;
      if (typeof cRes.value !== 'boolean') return typeErr('?: expects bool');
      return evaluateAstNode(cRes.value ? node.consequent : node.alternate, lookup);
    }
    if (node.kind === 'BinaryOp') {
      if (node.op === '&&' || node.op === '||') {
        var lLog = evaluateAstNode(node.left, lookup);
        if (!lLog.ok) return lLog;
        if (typeof lLog.value !== 'boolean') return typeErr(node.op + ' expects bool');
        if (node.op === '&&' ? !lLog.value : lLog.value) return exprOk(node.op === '||');
        var rLog = evaluateAstNode(node.right, lookup);
        if (!rLog.ok) return rLog;
        return typeof rLog.value === 'boolean' ? exprOk(rLog.value) : typeErr(node.op + ' expects bool');
      }
      var lRes = evaluateAstNode(node.left, lookup);
      if (!lRes.ok) return lRes;
      var rRes = evaluateAstNode(node.right, lookup);
      if (!rRes.ok) return rRes;
      var l = lRes.value, r = rRes.value;
      if (node.op === '+') {
        if (typeof l === 'number' && typeof r === 'number') return exprOk(l + r);
        if (typeof l === 'string' || typeof r === 'string') return exprOk((l === null ? 'null' : String(l)) + (r === null ? 'null' : String(r)));
        return typeErr('+ types');
      }
      if (node.op === '-' || node.op === '*' || node.op === '/' || node.op === '%') {
        if (typeof l !== 'number' || typeof r !== 'number') return typeErr('arithmetic types');
        if (node.op === '-') return exprOk(l - r);
        if (node.op === '*') return exprOk(l * r);
        if (r === 0) return exprRefuse('expressions.division_by_zero', 'Division by zero');
        return exprOk(node.op === '/' ? l / r : l % r);
      }
      if (node.op === '==' || node.op === '!=') {
        var eq = l === null && r === null ? true : (l === null || r === null ? false : l === r);
        return exprOk(node.op === '==' ? eq : !eq);
      }
      if ((typeof l === 'number' && typeof r === 'number') || (typeof l === 'string' && typeof r === 'string')) {
        if (node.op === '<') return exprOk(l < r);
        if (node.op === '<=') return exprOk(l <= r);
        if (node.op === '>') return exprOk(l > r);
        if (node.op === '>=') return exprOk(l >= r);
      }
      return typeErr('comparison types');
    }
    if (node.kind === 'Call') return evaluateExprCall(node.callee, node.args, lookup);
    return synErr('Unsupported node', 0);
  }

  var parseCache = {};

  function parseBareExpression(exprStr) {
    if (typeof exprStr !== 'string') {
      return exprRefuse('expressions.type', 'Expression must be a string', 'Pass a string expression');
    }
    if (exprStr.length > EXPR_MAX_LENGTH) {
      return exprRefuse('expressions.syntax', 'Expression exceeds maximum length of ' + EXPR_MAX_LENGTH, 'Shorten expression', '@pos ' + EXPR_MAX_LENGTH);
    }
    var trimmed = exprStr.trim();
    if (trimmed.length === 0) {
      return exprRefuse('expressions.syntax', 'Empty expression', 'Provide a non-empty expression', '@pos 0');
    }
    if (Object.prototype.hasOwnProperty.call(parseCache, trimmed)) {
      return parseCache[trimmed];
    }
    var tokRes = tokenizeExpr(trimmed);
    if (!tokRes.ok) {
      parseCache[trimmed] = tokRes;
      return tokRes;
    }
    var parseRes = parseExprTokens(tokRes.value);
    parseCache[trimmed] = parseRes;
    return parseRes;
  }

  function unescapeAt(str) {
    return str.split('\\@').join('@');
  }

  function parseTemplateOrExpr(expression) {
    if (typeof expression !== 'string') {
      return exprRefuse('expressions.type', 'Expression must be a string', 'Pass a string expression');
    }
    if (expression.length > EXPR_MAX_LENGTH) {
      return exprRefuse('expressions.syntax', 'Expression exceeds maximum length of ' + EXPR_MAX_LENGTH, 'Shorten expression', '@pos ' + EXPR_MAX_LENGTH);
    }
    if (expression.indexOf('@{') === 0 && expression.charAt(expression.length - 1) === '}') {
      var inner = expression.slice(2, -1);
      if (inner.indexOf('@{') === -1 && inner.indexOf('}') === -1) {
        return parseBareExpression(inner);
      }
    }
    if (expression.indexOf('@{') === -1) {
      return exprOk({ kind: 'Literal', value: unescapeAt(expression) });
    }
    var parts = [];
    var textBuf = '';
    var i = 0;
    var len = expression.length;

    while (i < len) {
      if (expression[i] === '\\' && i + 2 < len && expression[i + 1] === '@' && expression[i + 2] === '{') {
        textBuf += '@{';
        i += 3;
        continue;
      }
      if (expression[i] === '@' && i + 1 < len && expression[i + 1] === '{') {
        if (textBuf.length > 0) {
          parts.push({ kind: 'Text', value: unescapeAt(textBuf) });
          textBuf = '';
        }
        var exprStart = i + 2;
        var depth = 1;
        var j = exprStart;
        var inSingleQuote = false;
        var inDoubleQuote = false;

        while (j < len && depth > 0) {
          var c = expression[j];
          if (c === '\\' && (inSingleQuote || inDoubleQuote)) {
            j += 2;
            continue;
          }
          if (c === "'" && !inDoubleQuote) inSingleQuote = !inSingleQuote;
          else if (c === '"' && !inSingleQuote) inDoubleQuote = !inDoubleQuote;
          else if (!inSingleQuote && !inDoubleQuote) {
            if (c === '{') depth++;
            else if (c === '}') depth--;
          }
          j++;
        }

        if (depth !== 0) {
          return exprRefuse('expressions.syntax', "Unclosed template expression '@{'", "Close expression with '}'", '@pos ' + i);
        }

        var exprContent = expression.slice(exprStart, j - 1);
        var parsedInner = parseBareExpression(exprContent);
        if (!parsedInner.ok) return parsedInner;
        parts.push({ kind: 'Expr', expr: parsedInner.value });
        i = j;
      } else {
        textBuf += expression[i];
        i++;
      }
    }
    if (textBuf.length > 0) {
      parts.push({ kind: 'Text', value: unescapeAt(textBuf) });
    }
    return exprOk({ kind: 'Template', parts: parts });
  }

  function makeStateLookup(state) {
    var safeState = state && typeof state === 'object' ? state : {};
    return function (name) {
      if (Object.prototype.hasOwnProperty.call(safeState, name)) {
        return exprOk(safeState[name]);
      }
      return exprRefuse('expressions.unknown_field', "Unknown field or identifier '" + name + "'", "Define '" + name + "' in the state or form schema", name);
    };
  }

  function makeSignalStoreLookup(extraContext) {
    return function (name) {
      if (extraContext && Object.prototype.hasOwnProperty.call(extraContext, name)) {
        return exprOk(extraContext[name]);
      }
      var sig = ensureSignal(name);
      var val = sig.value; // registers activeEffect on the signal!
      if (Object.prototype.hasOwnProperty.call(store, name)) {
        return exprOk(val);
      }
      return exprRefuse('expressions.unknown_field', "Unknown signal '" + name + "'", "Declare '" + name + "' in vx-signals", name);
    };
  }

  function evaluateBare(exprStr, state) {
    var parsed = parseBareExpression(exprStr);
    if (!parsed.ok) return parsed;
    return evaluateAstNode(parsed.value, makeStateLookup(state));
  }

  function evaluateBareTracked(exprStr, extraContext) {
    var parsed = parseBareExpression(exprStr);
    if (!parsed.ok) return parsed;
    return evaluateAstNode(parsed.value, makeSignalStoreLookup(extraContext));
  }

  function evaluateBareWithLookup(exprStr, lookup) {
    var parsed = parseBareExpression(exprStr);
    if (!parsed.ok) return parsed;
    return evaluateAstNode(parsed.value, lookup);
  }

  function evaluateTemplateOrExprWithLookup(expression, lookup) {
    var parsed = parseTemplateOrExpr(expression);
    if (!parsed.ok) return parsed;
    var node = parsed.value;
    if (node.kind === 'Template') {
      var out = '';
      for (var i = 0; i < node.parts.length; i++) {
        var p = node.parts[i];
        if (p.kind === 'Text') {
          out += p.value;
        } else {
          var r = evaluateAstNode(p.expr, lookup);
          if (!r.ok) return r;
          if (r.value !== null && r.value !== undefined) {
            out += String(r.value);
          }
        }
      }
      return exprOk(out);
    }
    return evaluateAstNode(node, lookup);
  }

  function evaluateTemplateOrExpr(expression, state) {
    return evaluateTemplateOrExprWithLookup(expression, makeStateLookup(state));
  }

  function isSimpleIdentifier(str) {
    return /^[a-zA-Z_][a-zA-Z0-9_]*$/.test(str);
  }

  // --- 3. DOM Bindings & Seeds ---
  function isBindingAttribute(name) {
    return name === 'vx-text' ||
           name === 'vx-show' ||
           name === 'vx-bind' ||
           name.indexOf('vx-class:') === 0 ||
           name.indexOf('vx-attr:') === 0 ||
           name.indexOf('vx-style:') === 0;
  }

  function disposeElementEffects(el) {
    if (el && el.__vxEffects) {
      for (var i = 0; i < el.__vxEffects.length; i++) {
        try { el.__vxEffects[i](); } catch (_) {}
      }
      el.__vxEffects = null;
    }
  }

  function disposeElementOn(el) {
    if (el && el.__vxDisposeOn) {
      try { el.__vxDisposeOn(); } catch (_) {}
      el.__vxDisposeOn = null;
      el.__vxOnBound = null;
    }
  }

  function disposeSubtree(node) {
    if (!node || node.nodeType !== Node.ELEMENT_NODE) return;
    var nodes = [node].concat(node.querySelectorAll ? Array.from(node.querySelectorAll('*')) : []);
    for (var i = 0; i < nodes.length; i++) {
      var el = nodes[i];
      disposeElementEffects(el);
      disposeElementOn(el);
      if (inFlightRequests.has(el)) {
        try { inFlightRequests.get(el).abort(); } catch (_) {}
        inFlightRequests.delete(el);
      }
    }
  }

  function bindTree(root) {
    if (!root || !root.querySelectorAll) return;

    // Seeds: vx-signals & vx-signals-if-missing
    var seedSelector = '[vx-signals],[vx-signals-if-missing],[vx-persist],[vx-persist-session],[vx-url-sync]';
    var seedEls = root.matches && root.matches(seedSelector)
      ? [root].concat(Array.from(root.querySelectorAll(seedSelector)))
      : Array.from(root.querySelectorAll(seedSelector));
    seedEls.forEach(function (el) {
      // Persist declarations first so patchSignals persists & syncs seeded keys
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

      var sigRaw = el.getAttribute('vx-signals');
      var ifMissingRaw = el.getAttribute('vx-signals-if-missing');
      if (sigRaw) {
        try { patchSignals(JSON.parse(sigRaw), false); } catch (e) { console.error('Invalid vx-signals JSON', e); }
      }
      if (ifMissingRaw) {
        try { patchSignals(JSON.parse(ifMissingRaw), true); } catch (e) { console.error('Invalid vx-signals-if-missing JSON', e); }
      }
      loadPersisted();
      savePersisted();
      syncUrl();
    });

    // Binding elements: text, show, class, attr, style, bind
    var all = [root].concat(Array.from(root.querySelectorAll('*')));
    all.forEach(function (el) {
      if (!el.attributes) return;
      disposeElementEffects(el);
      var ownedProps = new Set();

      function claimProp(propKey, attrName) {
        if (ownedProps.has(propKey)) {
          console.warn('vaxel one-owner conflict: multiple bindings write property "' + propKey + '" (via ' + attrName + ')');
        }
        ownedProps.add(propKey);
      }

      function addBindingEffect(fn) {
        if (!el.__vxEffects) el.__vxEffects = [];
        el.__vxEffects.push(effect(fn));
      }

      function evalBindingExpr(exprStr, attrName) {
        if (isSimpleIdentifier(exprStr)) {
          return exprOk(readSignalTracked(exprStr));
        }
        var res = evaluateBareTracked(exprStr);
        if (!res.ok) {
          emitEvent('vx:error', { attribute: attrName, expression: exprStr, refusal: res.refusal });
        }
        return res;
      }

      for (var i = 0; i < el.attributes.length; i++) {
        var attr = el.attributes[i];
        var name = attr.name;
        var val = attr.value.trim();

        // vx-bind and vx-style:* still take a single signal name (Rule R2: no expression in style or two-way bind)
        if ((name === 'vx-bind' || name.indexOf('vx-style:') === 0) && /[()<>=!&|+*\/;]/.test(val)) {
          console.warn('vaxel Rule R2 violation: expressions prohibited in attribute value: ' + val);
          continue;
        }

        if (name === 'vx-text') {
          claimProp('textContent', name);
          (function (exprStr) {
            var hasInterpolation = exprStr.indexOf('@{') !== -1;
            var simpleId = !hasInterpolation && isSimpleIdentifier(exprStr);
            addBindingEffect(function () {
              if (simpleId) {
                var v = readSignalTracked(exprStr);
                el.textContent = v !== undefined && v !== null ? String(v) : '';
                return;
              }
              var res = hasInterpolation
                ? evaluateTemplateOrExprWithLookup(exprStr, makeSignalStoreLookup())
                : evaluateBareTracked(exprStr);
              if (!res.ok) {
                el.textContent = '';
                emitEvent('vx:error', { attribute: 'vx-text', expression: exprStr, refusal: res.refusal });
                return;
              }
              el.textContent = res.value !== undefined && res.value !== null ? String(res.value) : '';
            });
          })(val);
        } else if (name === 'vx-show') {
          claimProp('hidden', name);
          claimProp('style:display', name);
          (function (exprStr) {
            addBindingEffect(function () {
              var res = evalBindingExpr(exprStr, 'vx-show');
              var truthy = res.ok ? Boolean(res.value && res.value !== '0' && res.value !== 'false') : false;
              el.hidden = !truthy;
              if (!truthy) el.style.display = 'none';
              else if (el.style.display === 'none') el.style.display = '';
            });
          })(val);
        } else if (name.startsWith('vx-class:')) {
          var cls = name.substring('vx-class:'.length);
          claimProp('class:' + cls, name);
          (function (className, exprStr) {
            addBindingEffect(function () {
              var res = evalBindingExpr(exprStr, 'vx-class:' + className);
              var truthy = res.ok ? Boolean(res.value && res.value !== '0' && res.value !== 'false') : false;
              el.classList.toggle(className, truthy);
            });
          })(cls, val);
        } else if (name.startsWith('vx-attr:')) {
          var targetAttr = name.substring('vx-attr:'.length);
          // US-1.5: Refuse dangerous attributes at runtime as well as render time
          var lowerTarget = targetAttr.toLowerCase();
          if (lowerTarget === 'href' || lowerTarget === 'src' || lowerTarget === 'action' ||
              lowerTarget === 'formaction' || lowerTarget === 'style' || lowerTarget.indexOf('on') === 0) {
            emitEvent('vx:error', { attribute: name, reason: 'Binding to forbidden attribute ' + targetAttr + ' refused (Rule R2).' });
            continue;
          }
          claimProp(targetAttr === 'hidden' ? 'hidden' : 'attr:' + targetAttr, name);
          (function (aName, exprStr) {
            addBindingEffect(function () {
              var res = evalBindingExpr(exprStr, 'vx-attr:' + aName);
              var v = res.ok ? res.value : false;
              if (v === false || v === null || v === undefined) {
                el.removeAttribute(aName);
              } else if (v === true) {
                el.setAttribute(aName, '');
              } else {
                el.setAttribute(aName, String(v));
              }
            });
          })(targetAttr, val);
        } else if (name.startsWith('vx-style:')) {
          var prop = name.substring('vx-style:'.length);
          claimProp('style:' + prop, name);
          (function (cssProp, sigKey) {
            addBindingEffect(function () {
              var v = readSignalTracked(sigKey);
              if (v === null || v === undefined) el.style.removeProperty(cssProp);
              else el.style.setProperty(cssProp, String(v));
            });
          })(prop, val);
        } else if (name === 'vx-bind') {
          claimProp('value', name);
          (function (sigKey) {
            var isCheckbox = el.type === 'checkbox';
            var isNumber = el.type === 'number' || el.type === 'range';
            addBindingEffect(function () {
              var v = readSignalTracked(sigKey);
              if (isCheckbox) el.checked = Boolean(v);
              else el.value = v !== undefined && v !== null ? v : '';
            });

            if (!el.__vxBoundInput) {
              el.__vxBoundInput = true;
              var handler = function () {
                var currentKey = (el.getAttribute('vx-bind') || '').trim();
                if (!currentKey) return;
                if (isCheckbox) setSignal(currentKey, el.checked);
                else if (isNumber) setSignal(currentKey, el.value === '' ? null : Number(el.value));
                else setSignal(currentKey, el.value);
              };
              el.addEventListener('input', handler);
              el.addEventListener('change', handler);
            }
          })(val);
        }
      }

      if (el.hasAttribute('vx-on')) {
        bindVxOn(el, el.getAttribute('vx-on') || '');
      } else if (el.__vxDisposeOn) {
        disposeElementOn(el);
      }
    });
  }

  function splitVxOnChain(str) {
    var parts = [];
    var cur = '';
    var quote = null;
    for (var i = 0; i < str.length; i++) {
      var ch = str[i];
      if (quote) {
        cur += ch;
        if (ch === '\\' && i + 1 < str.length) {
          cur += str[++i];
        } else if (ch === quote) {
          quote = null;
        }
      } else if (ch === '\'' || ch === '"') {
        quote = ch;
        cur += ch;
      } else if (ch === '|' && str[i + 1] !== '|' && (i === 0 || str[i - 1] !== '|')) {
        parts.push(cur.trim());
        cur = '';
      } else {
        cur += ch;
      }
    }
    if (cur.trim()) parts.push(cur.trim());
    return parts;
  }

  function makeEventLookup(el, evt) {
    return function (name) {
      if (name === 'key') {
        return exprOk(evt && typeof evt.key === 'string' ? evt.key : '');
      }
      if (name === 'value') {
        var targetVal = evt && evt.target && evt.target.value !== undefined
          ? evt.target.value
          : (el && el.value !== undefined ? el.value : '');
        return exprOk(targetVal !== null && targetVal !== undefined ? String(targetVal) : '');
      }
      if (name === 'checked') {
        var targetChecked = evt && evt.target && typeof evt.target.checked === 'boolean'
          ? evt.target.checked
          : Boolean(el && el.checked);
        return exprOk(targetChecked);
      }
      return exprOk(readSignalTracked(name));
    };
  }

  function bindVxOn(el, chainStr) {
    var trimmed = (chainStr || '').trim();
    if (!trimmed) {
      disposeElementOn(el);
      return;
    }
    if (el.__vxOnBound === trimmed) {
      return;
    }
    disposeElementOn(el);
    el.__vxOnBound = trimmed;

    var stages = splitVxOnChain(trimmed);
    if (stages.length === 0) return;

    var activeTimers = new Set();
    var activeIntervals = new Set();
    var cleanups = [];

    el.__vxDisposeOn = function () {
      activeTimers.forEach(function (id) { clearTimeout(id); });
      activeTimers.clear();
      activeIntervals.forEach(function (id) { clearInterval(id); });
      activeIntervals.clear();
      for (var i = 0; i < cleanups.length; i++) {
        try { cleanups[i](); } catch (_) {}
      }
      cleanups.length = 0;
    };

    var opStages = stages.slice(1);
    var sinkFn = null;

    if (opStages.length > 0) {
      var lastStage = opStages[opStages.length - 1];
      var setMatch = /^set\s+([a-zA-Z_][a-zA-Z0-9_]*)\s*=\s*([\s\S]+)$/.exec(lastStage);
      if (setMatch) {
        opStages.pop();
        var sinkIdent = setMatch[1];
        var sinkExpr = setMatch[2].trim();
        sinkFn = function (evt) {
          var res = evaluateBareWithLookup(sinkExpr, makeEventLookup(el, evt));
          if (!res.ok) {
            emitEvent('vx:error', { attribute: 'vx-on', expression: sinkExpr, refusal: res.refusal });
            return;
          }
          setSignal(sinkIdent, res.value);
        };
      }
    }

    if (!sinkFn) {
      sinkFn = function (evt) {
        triggerElementRequest(el, evt);
      };
    }

    var pipeline = sinkFn;

    for (var s = opStages.length - 1; s >= 0; s--) {
      var st = opStages[s];
      var spIdx = st.search(/\s/);
      var opName = spIdx === -1 ? st : st.substring(0, spIdx);
      var opArg = spIdx === -1 ? '' : st.substring(spIdx).trim();

      if (opName === 'once') {
        (function (next) {
          var fired = false;
          pipeline = function (evt) {
            if (fired) return;
            fired = true;
            next(evt);
          };
        })(pipeline);
      } else if (opName === 'distinct') {
        (function (next) {
          var hasLast = false;
          var lastVal;
          pipeline = function (evt) {
            var curVal = (evt && evt.target && evt.target.value !== undefined)
              ? evt.target.value
              : (el.value !== undefined ? el.value : evt);
            if (hasLast && lastVal === curVal) return;
            hasLast = true;
            lastVal = curVal;
            next(evt);
          };
        })(pipeline);
      } else if (opName === 'prevent') {
        (function (next) {
          pipeline = function (evt) {
            if (evt && typeof evt.preventDefault === 'function') evt.preventDefault();
            next(evt);
          };
        })(pipeline);
      } else if (opName === 'stop') {
        (function (next) {
          pipeline = function (evt) {
            if (evt && typeof evt.stopPropagation === 'function') evt.stopPropagation();
            next(evt);
          };
        })(pipeline);
      } else if (opName === 'debounce') {
        (function (next, ms) {
          var timerId = null;
          pipeline = function (evt) {
            if (timerId !== null) {
              clearTimeout(timerId);
              activeTimers.delete(timerId);
            }
            timerId = setTimeout(function () {
              activeTimers.delete(timerId);
              timerId = null;
              next(evt);
            }, ms);
            activeTimers.add(timerId);
          };
        })(pipeline, Math.max(0, Number(opArg) || 0));
      } else if (opName === 'throttle') {
        (function (next, ms) {
          var locked = false;
          pipeline = function (evt) {
            if (locked) return;
            locked = true;
            var timerId = setTimeout(function () {
              activeTimers.delete(timerId);
              locked = false;
            }, ms);
            activeTimers.add(timerId);
            next(evt);
          };
        })(pipeline, Math.max(0, Number(opArg) || 0));
      } else if (opName === 'delay') {
        (function (next, ms) {
          pipeline = function (evt) {
            var timerId = setTimeout(function () {
              activeTimers.delete(timerId);
              next(evt);
            }, ms);
            activeTimers.add(timerId);
          };
        })(pipeline, Math.max(0, Number(opArg) || 0));
      } else if (opName === 'key') {
        (function (next, expectedKey) {
          pipeline = function (evt) {
            if (!evt || evt.key !== expectedKey) return;
            next(evt);
          };
        })(pipeline, opArg);
      } else if (opName === 'filter') {
        (function (next, filterExpr) {
          pipeline = function (evt) {
            var res = evaluateBareWithLookup(filterExpr, makeEventLookup(el, evt));
            if (!res.ok) {
              emitEvent('vx:error', { attribute: 'vx-on', expression: filterExpr, refusal: res.refusal });
              return;
            }
            var truthy = Boolean(res.value && res.value !== '0' && res.value !== 'false');
            if (!truthy) return;
            next(evt);
          };
        })(pipeline, opArg);
      } else {
        emitEvent('vx:error', { attribute: 'vx-on', reason: 'Unknown operator: ' + opName });
      }
    }

    // Source stage (stages[0])
    var srcTokens = stages[0].split(/\s+/).filter(Boolean);
    var srcName = srcTokens[0];
    var srcScopes = srcTokens.slice(1);

    if (srcName === 'interval') {
      var requestedMs = Number(srcTokens[1]);
      var intervalMs = Math.max(1000, isNaN(requestedMs) ? 1000 : requestedMs);
      var intId = setInterval(function () {
        pipeline({ type: 'interval', target: el });
      }, intervalMs);
      activeIntervals.add(intId);
    } else if (srcName === 'load') {
      pipeline({ type: 'load', target: el });
    } else if (srcName === 'visible') {
      if (typeof IntersectionObserver !== 'undefined') {
        var obs = new IntersectionObserver(function (entries) {
          for (var ei = 0; ei < entries.length; ei++) {
            if (entries[ei].isIntersecting) {
              pipeline({ type: 'visible', target: el, entry: entries[ei] });
            }
          }
        });
        obs.observe(el);
        cleanups.push(function () { obs.disconnect(); });
      }
    } else if (srcName) {
      var hasOutside = srcScopes.indexOf('outside') !== -1;
      var hasWindow = srcScopes.indexOf('window') !== -1;
      var hasDocument = srcScopes.indexOf('document') !== -1;

      if (hasOutside) {
        var outsideHandler = function (evt) {
          if (evt && evt.target && !el.contains(evt.target)) {
            pipeline(evt);
          }
        };
        document.addEventListener(srcName, outsideHandler);
        cleanups.push(function () { document.removeEventListener(srcName, outsideHandler); });
      } else {
        var listenTarget = hasWindow ? window : (hasDocument ? document : el);
        var domHandler = function (evt) {
          pipeline(evt);
        };
        listenTarget.addEventListener(srcName, domHandler);
        cleanups.push(function () { listenTarget.removeEventListener(srcName, domHandler); });
      }
    }
  }

  // --- 3. DOM Morphing Engine ---
  function morphElement(fromEl, toEl) {
    if (fromEl.nodeType !== toEl.nodeType || fromEl.tagName !== toEl.tagName) {
      disposeSubtree(fromEl);
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
        if (fAttr.name === 'vx-on') disposeElementOn(fromEl);
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
      var advanceFrom = true; // did we consume the current old node?

      if (fromChild.nodeType === toChild.nodeType &&
          (fromChild.nodeType === Node.TEXT_NODE || fromChild.nodeType === Node.COMMENT_NODE)) {
        if (fromChild.textContent !== toChild.textContent) {
          fromChild.textContent = toChild.textContent;
        }
      } else if (fromChild.nodeType === Node.ELEMENT_NODE && toChild.nodeType === Node.ELEMENT_NODE) {
        if (fromChild.id && toChild.id && fromChild.id === toChild.id) {
          morphElement(fromChild, toChild);
        } else if (fromChild.tagName === toChild.tagName && !fromChild.id && !toChild.id) {
          // Soft match only when NEITHER side is identified. An identified old node must not
          // be repurposed for an idless (or differently-identified) new node — doing so kept a
          // previous screen's subtree alive when morphing to an unrelated view (US-5).
          morphElement(fromChild, toChild);
        } else {
          // The current old node is not this incoming node. Look ahead for an old node that
          // carries the incoming id and relocate it; otherwise insert the incoming node here.
          // Either way the current old node is NOT consumed — leave it for the next incoming
          // node, so unmatched old nodes fall through to the removal pass below (US-5).
          var matchInFrom = toChild.id ? fromEl.querySelector('#' + toChild.id) : null;
          if (matchInFrom && matchInFrom.parentNode === fromEl) {
            fromEl.insertBefore(matchInFrom, fromChild);
            morphElement(matchInFrom, toChild);
          } else {
            fromEl.insertBefore(toChild, fromChild);
          }
          advanceFrom = false;
        }
      } else {
        disposeSubtree(fromChild);
        fromEl.replaceChild(toChild, fromChild);
      }

      if (advanceFrom) {
        fromChild = fromNext;
      }
      toChild = toNext;
    }

    // Remove remaining old children
    while (fromChild) {
      var removeMe = fromChild;
      fromChild = fromChild.nextSibling;
      disposeSubtree(removeMe);
      fromEl.removeChild(removeMe);
    }
  }

  // Morph the incoming fragment INTO the target. If the fragment's first
  // element is the target itself (same id), morph that node. Otherwise the
  // fragment is the target's inner HTML (cookbook #pane patches) — keep the
  // target identity and morph its children.
  function morphIntoTarget(targetEl, fragment) {
    var incoming = fragment.firstElementChild;
    if (!incoming) {
      return;
    }
    if (incoming.id && targetEl.id && incoming.id === targetEl.id) {
      morphElement(targetEl, incoming);
      return;
    }
    var wrapper = targetEl.cloneNode(false);
    while (fragment.firstChild) {
      wrapper.appendChild(fragment.firstChild);
    }
    morphElement(targetEl, wrapper);
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
        morphIntoTarget(targetEl, fragment);
        break;

      case 'outer':
        if (fragment.firstElementChild) {
          morphElement(targetEl, fragment.firstElementChild);
        }
        break;

      case 'replace':
        if (fragment.firstElementChild) {
          disposeSubtree(targetEl);
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
          disposeSubtree(targetEl);
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

  function parseXmlAttrs(attrSource) {
    var attrs = {};
    if (!attrSource) return attrs;
    var re = /([^\s=]+)(?:\s*=\s*"([^"]*)"|\s*=\s*'([^']*)')?/g;
    var m;
    while ((m = re.exec(attrSource))) {
      attrs[m[1].toLowerCase()] = m[2] != null ? m[2] : (m[3] != null ? m[3] : '');
    }
    return attrs;
  }

  // Parse a single <vx-directive …> element with the DOM parser and read its attributes
  // through getAttribute, which decodes HTML entities. The server HTML-encodes directive
  // values (push-url, title, …); reading the raw attribute text left them encoded (US-3).
  function parseDirectiveAttrs(elementHtml) {
    var attrs = {};
    try {
      var doc = new DOMParser().parseFromString(elementHtml, 'text/html');
      var el = doc.querySelector('vx-directive');
      if (!el) return attrs;
      for (var i = 0; i < el.attributes.length; i++) {
        var a = el.attributes[i];
        attrs[a.name.toLowerCase()] = a.value;
      }
    } catch (e) {
      console.error('Invalid <vx-directive> element', e);
    }
    return attrs;
  }

  // Extract <vx-patch> from the raw document. Do not HTML-parse the wrapper:
  // a <section>/<div> inside an unknown element can be emptied by DOMParser,
  // which looks like "clicks do nothing".
  function processPatchDocument(htmlText, userInitiated, triggerEl) {
    emitEvent('vx:before-apply', { html: htmlText });

    var appliedCount = 0;
    var patchRe = /<vx-patch\b([^>]*)>([\s\S]*?)<\/vx-patch>/gi;
    var patchMatch;
    while ((patchMatch = patchRe.exec(htmlText))) {
      var patchAttrs = parseXmlAttrs(patchMatch[1]);
      var res = applyPatch(
        patchAttrs.target,
        patchMatch[2],
        patchAttrs.mode || 'morph',
        patchAttrs.namespace || 'html'
      );
      if (res && res.success) appliedCount++;
    }

    var sigMatch = /<vx-signals\b([^>]*)>([\s\S]*?)<\/vx-signals>/i.exec(htmlText);
    if (sigMatch) {
      var sigAttrs = parseXmlAttrs(sigMatch[1]);
      var sigText = sigMatch[2].trim();
      if (sigText) {
        try {
          patchSignals(JSON.parse(sigText), Object.prototype.hasOwnProperty.call(sigAttrs, 'only-if-missing'));
        } catch (e) {
          console.error('Invalid <vx-signals> payload', e);
        }
      }
    }

    var dirMatch = /<vx-directive\b[^>]*\/?>/i.exec(htmlText);
    var dirAttrs = dirMatch ? parseDirectiveAttrs(dirMatch[0]) : null;

    if (dirAttrs) {
      var redirect = dirAttrs.redirect;
      if (redirect) {
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

      if (dirAttrs.reload === '1') {
        window.location.reload();
        return;
      }

      var pushUrl = dirAttrs['push-url'];
      var replaceUrl = dirAttrs['replace-url'];
      if (pushUrl) {
        window.history.pushState({}, '', pushUrl);
      } else if (replaceUrl) {
        window.history.replaceState({}, '', replaceUrl);
      }

      if (dirAttrs.title) document.title = dirAttrs.title;
      if (dirAttrs.announce) announceText(dirAttrs.announce);

      var focusTarget = dirAttrs.focus;
      if (userInitiated && focusTarget) {
        var elToFocus = document.querySelector(focusTarget);
        if (elToFocus && elToFocus.focus) elToFocus.focus();
      }

      var scrollTarget = dirAttrs.scroll;
      if (scrollTarget) {
        var behavior = dirAttrs['scroll-behavior'] || 'instant';
        var block = dirAttrs['scroll-block'] || 'start';
        var inline = dirAttrs['scroll-inline'] || 'nearest';
        if (scrollTarget === 'top') {
          window.scrollTo({ top: 0, behavior: behavior });
        } else {
          var scrollEl = document.querySelector(scrollTarget);
          if (scrollEl && scrollEl.scrollIntoView) {
            scrollEl.scrollIntoView({ behavior: behavior, block: block, inline: inline });
            if (dirAttrs['scroll-focus'] === '1' && scrollEl.focus) {
              scrollEl.focus();
            }
          }
        }
      }
    } else if (userInitiated) {
      if (triggerEl && document.body.contains(triggerEl) && triggerEl.focus) {
        triggerEl.focus();
      }
    }

    if (appliedCount === 0 && userInitiated && triggerEl) {
      var fallback = triggerEl.getAttribute('href') || triggerEl.getAttribute('action');
      if (fallback) {
        window.location.href = fallback;
        return;
      }
    }

    emitEvent('vx:after-apply', { patchesApplied: appliedCount });
  }

  // --- 6. Fetch & Concurrency ---
  var inFlightRequests = new Map(); // trigger -> AbortController
  var sequenceCounter = 0;

  function hasHttpVerbAttribute(el) {
    return Boolean(el && el.hasAttribute && (
      el.hasAttribute('vx-get') ||
      el.hasAttribute('vx-post') ||
      el.hasAttribute('vx-put') ||
      el.hasAttribute('vx-patch') ||
      el.hasAttribute('vx-delete')
    ));
  }

  function triggerElementRequest(el, evt) {
    var requestEl = el;
    if (!hasHttpVerbAttribute(requestEl)) {
      var parentForm = el.form || (el.closest ? el.closest('form') : null);
      if (parentForm && hasHttpVerbAttribute(parentForm)) {
        requestEl = parentForm;
      } else {
        return;
      }
    }

    var confirmMsg = el.getAttribute('vx-confirm') || (requestEl !== el ? requestEl.getAttribute('vx-confirm') : null);
    if (confirmMsg && !window.confirm(confirmMsg)) {
      if (evt && typeof evt.preventDefault === 'function') evt.preventDefault();
      return;
    }

    var method = (requestEl instanceof HTMLFormElement) ? 'POST' : 'GET';
    var url = requestEl.getAttribute('href') || requestEl.getAttribute('action') || window.location.href;
    ['vx-get', 'vx-post', 'vx-put', 'vx-patch', 'vx-delete'].forEach(function (attr) {
      if (requestEl.hasAttribute(attr)) {
        method = attr.substring(3).toUpperCase();
        var customUrl = requestEl.getAttribute(attr);
        if (customUrl) url = customUrl;
      }
    });

    if (evt && typeof evt.preventDefault === 'function' &&
        (evt.type === 'submit' || (evt.type === 'click' && (requestEl.tagName === 'A' || requestEl.tagName === 'BUTTON')))) {
      evt.preventDefault();
    }

    executeRequest(requestEl, method, url);
  }

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
    var indicatorSig = null;
    var indicatorSel = (el.getAttribute('vx-indicator') || '').trim();
    if (indicatorSel) {
      if (isSimpleIdentifier(indicatorSel) && Object.prototype.hasOwnProperty.call(signalCells, indicatorSel)) {
        indicatorSig = indicatorSel;
        setSignal(indicatorSig, true);
      } else {
        try { indicatorEl = document.querySelector(indicatorSel); } catch (_) {}
        if (!indicatorEl && isSimpleIdentifier(indicatorSel)) {
          indicatorSig = indicatorSel;
          setSignal(indicatorSig, true);
        }
      }
    }
    if (indicatorEl) {
      indicatorEl.classList.add('vx-loading');
      indicatorEl.setAttribute('aria-busy', 'true');
    }
    var disableTarget = el.hasAttribute('vx-disable');
    if (disableTarget) el.disabled = true;

    function clearIndicators() {
      if (indicatorEl) {
        indicatorEl.classList.remove('vx-loading');
        indicatorEl.removeAttribute('aria-busy');
      }
      if (indicatorSig) {
        setSignal(indicatorSig, false);
      }
      if (disableTarget) el.disabled = false;
    }

    // Headers
    var headers = {
      'VX-Request': '1',
      'VX-Protocol': '1',
      'VX-Sequence': String(seq),
      'VX-Url': window.location.pathname + window.location.search
    };
    if (target) headers['VX-Target'] = target;

    // CSRF meta. The server renders the configured antiforgery header name in data-header;
    // fall back to X-CSRF when it is absent (US-7).
    var csrfMeta = document.querySelector('meta[name="vx-csrf"]');
    if (csrfMeta && csrfMeta.content) {
      var csrfHeader = csrfMeta.getAttribute('data-header') || 'X-CSRF';
      headers[csrfHeader] = csrfMeta.content;
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
        // Collect vx-vals-* (and input name/value if el is a form control)
        var vals = new URLSearchParams();
        if (el.name && (el instanceof HTMLInputElement || el instanceof HTMLSelectElement || el instanceof HTMLTextAreaElement)) {
          vals.set(el.name, el.type === 'checkbox' ? String(el.checked) : el.value);
        }
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
    } else {
      // GET / HEAD carry their fields in the query string, merged with any query the URL
      // already holds (US-4). A vx-get form's fields become the query; a non-form trigger's
      // vx-vals-* are added the same way.
      var getUrl = new URL(url, window.location.href);
      if (el instanceof HTMLFormElement) {
        new FormData(el).forEach(function (v, k) {
          getUrl.searchParams.append(k, typeof v === 'string' ? v : '');
        });
      } else {
        if (el.name && (el instanceof HTMLInputElement || el instanceof HTMLSelectElement || el instanceof HTMLTextAreaElement)) {
          getUrl.searchParams.set(el.name, el.type === 'checkbox' ? String(el.checked) : el.value);
        }
        for (var gi = 0; gi < el.attributes.length; gi++) {
          var ga = el.attributes[gi];
          if (ga.name.indexOf('vx-vals-') === 0) {
            getUrl.searchParams.set(ga.name.substring('vx-vals-'.length), ga.value);
          }
        }
      }
      url = getUrl.pathname + getUrl.search + getUrl.hash;
    }

    var evtDetail = { url: url, method: method, headers: headers, body: body };
    if (!emitEvent('vx:before-request', evtDetail, true)) {
      inFlightRequests.delete(el);
      clearIndicators();
      return;
    }

    fetch(url, {
      method: method,
      headers: headers,
      body: body,
      signal: abortCtrl.signal
    }).then(function (res) {
      inFlightRequests.delete(el);
      clearIndicators();

      // US-3 (takeUntil morph): if the request was aborted or the trigger element was
      // removed from the document while in flight, abandon the response.
      if (abortCtrl.signal.aborted || !document.body.contains(el)) {
        return;
      }

      // Check protocol major version
      var resProto = res.headers.get('VX-Protocol');
      if (resProto && resProto.split('.')[0] !== '1') {
        window.location.href = url;
        return;
      }

      var contentType = res.headers.get('Content-Type') || '';
      if (contentType.indexOf('text/vnd.vaxel-patch+html') !== -1) {
        return res.text().then(function (html) {
          if (abortCtrl.signal.aborted || !document.body.contains(el)) return;
          processPatchDocument(html, true, el);
        });
      }
      window.location.href = url;
    }).catch(function (err) {
      inFlightRequests.delete(el);
      clearIndicators();
      if (err.name !== 'AbortError') {
        emitEvent('vx:error', { error: err });
      }
    });
  }

  // --- 7. Event Delegator ---
  function initTriggers() {
    document.addEventListener('click', function (e) {
      var trigger = e.target.closest('a[vx-get], a[vx-post], a[vx-put], a[vx-patch], a[vx-delete], button[vx-get], button[vx-post], button[vx-put], button[vx-patch], button[vx-delete]');
      if (!trigger || trigger.hasAttribute('vx-on')) return;
      triggerElementRequest(trigger, e);
    });

    document.addEventListener('submit', function (e) {
      var form = e.target;
      if (!hasHttpVerbAttribute(form) || form.hasAttribute('vx-on')) {
        return;
      }
      triggerElementRequest(form, e);
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
    processDocument: processPatchDocument,
    _applyPatchXml: function (html) { processPatchDocument(html, false, null); },
    _signal: signal,
    _effect: effect,
    _read: readSignalTracked,
    _evaluateBare: evaluateBare,
    _evaluateTemplateOrExpr: evaluateTemplateOrExpr,
    _parseBare: parseBareExpression,
    _parseTemplateOrExpr: parseTemplateOrExpr
  };

})(typeof window !== 'undefined' ? window : this);
