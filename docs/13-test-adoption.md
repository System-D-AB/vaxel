# 13 — Adopting Datastar's tests as our scoreboard

Datastar is MIT-licensed and ships a black-box conformance suite for server SDKs. We take it, run it against vaxel, and let it tell us where we are. This document says exactly what exists upstream, what we adopt, how, and how the result is scored.

Upstream inventory, read from the repository tree on 2026-08-23:

| Upstream | Contents | Adoptable? |
|---|---|---|
| `sdk/test/` | **20 conformance cases** (19 GET, 1 POST) as `input.json` → `output.txt` pairs, plus `test-all.sh`, `test-get.sh`, `test-post.sh`, `compare-sse.sh` — driven by curl/awk against a server's `/test` endpoint | **Yes, wholesale.** This is the half of vaxel that matters most, and the suite is already language-agnostic |
| `sdk/README.md`, `sdk/ADR.md` | The SDK specification the cases enforce | **Yes, as a reference** for the compatibility adapter |
| `library/src/plugins/{attributes,actions,watchers}` | 17 attribute plugins, 4 actions, 2 watchers — the reference implementation | **Yes, as a behaviour specification.** Read per plugin; each becomes rows in our fixture suite |
| Browser/unit tests for the client library | **Not present in the repository tree.** There is no `library/tests` directory | Nothing to adopt; our client fixtures are ours to write |

The headline: **their ready-made suite tests servers, and vaxel's server half is the part we most want measured.** The client side has no upstream suite to inherit, so parity there is proven by fixtures we author against their plugin sources.

---

## 1. Adopt `sdk/test` by building the compatibility adapter early

Their harness expects a server exposing `/test`, accepting any method, reading signals to get an `events` array, and emitting Datastar-shaped SSE. Ours emits HTML patch documents. Rather than fork their cases (which would make the comparison meaningless), we make vaxel speak their wire **in one place**:

```
Vaxel.Datastar (adapter)  →  PatchDocument  →  Datastar SSE framing
                                            →  vaxel patch document (native)
```

`Vaxel.Datastar` was scheduled after v1.0 in [11 — Datastar reuse](11-datastar-reuse.md). **It is promoted: build it first, as a measuring instrument.** Reasons it is the right call rather than a shortcut:

- It converts 20 upstream cases into a scoreboard we did not have to write, on day one.
- It forces the server API to be independent of our own client — which is a property we claim and would otherwise never test.
- It is the compatibility story anyway, so the work is not thrown away.

The conformance host lives at `test/Vaxel.Conformance.Host`, exposes `/test`, and is run by their scripts unmodified:

```
$ ./test-all.sh http://localhost:5199
```

**Rule: their scripts are vendored unmodified**, pinned to a 40-character upstream commit recorded in `NOTICE`. Any case we cannot pass is recorded in the scoreboard with a reason — never edited into passing. A conformance suite you edit is a suite that tests your edits.

### Scoring their 20 cases

| Case group | Cases | Expected score |
|---|---|---|
| `patchElements*` (defaults, all options, without defaults, multiline) | 4 | **Must pass.** These are the core of the protocol |
| `patchSignals*` (defaults, all options, without defaults, multiline JSON, multiline signals) | 5 | **Must pass**, including `onlyIfMissing` |
| `removeElements*` (defaults, all options, without defaults) | 3 | **Must pass** — our `remove` mode |
| `removeSignals*` (defaults, all options) | 2 | **Must pass** — delete-by-null |
| `sendTwoEvents` | 1 | **Must pass** — multiple patches in one response |
| `readSignalsFromBody` (POST) | 1 | **Must pass through the adapter.** Native vaxel reads the `VX-Signals` header; the adapter accepts a body, which is exactly what a compatibility adapter is for |
| `executeScript*` (defaults, all options, without defaults, multiline) | 4 | **Declined, and asserted as declined.** We do not execute server-sent script ([12 §4](12-parity-with-datastar.md)). The adapter returns a documented refusal, and our fork of the scoreboard records `⛔ 4` rather than a false pass |

**Target: 16 pass, 4 declined, 0 failing.** Anything else is a defect.

## 2. Port their plugin behaviours into our fixtures

For each of the 17 attribute plugins and 4 actions, read the upstream source and derive fixtures for the *observable behaviour*, not the implementation:

```
conformance/parity/<plugin>/
  README.md          ← upstream file, commit, and what it does
  01-<behaviour>/{before.html, patch.html, expected.html, assert.json}
  …
  SCORE.md           ← Full / Outcome / Declined, with the vaxel construct named
```

Order of work, most load-bearing first: `bind` → `on` → `patchElements` semantics → `signals` → `attr`/`class`/`text`/`show` → `indicator` → `onIntersect`/`onInterval` → `style` → `init` → `persist`/`query-string` → the rest.

Where a plugin exists only to run expressions (`computed`, `effect`, `onSignalPatch`, `ref`), the fixture asserts the **outcome** through the vaxel construct and records the authoring cost — a round trip, or an island of *n* lines. That number is the honest measure of the trade this framework makes, and it belongs in the open rather than in a footnote.

## 3. Their examples as an acceptance corpus

[11 §4](11-datastar-reuse.md) already proposed scoring Datastar's example gallery. With the parity goal set, it gets a target: **every example is expressible.** Each is scored Same / Server-round-trip / Island / **Cannot**, and any `Cannot` is a defect against this document, not a curiosity — it means a construct is missing and the matrix in [12](12-parity-with-datastar.md) is wrong.

## 4. What we add that they do not test

Adoption is not the whole suite. Four areas are ours because they follow from decisions Datastar did not make:

1. **Parity harness** (R3) — every fragment target reachable as a page. Nothing upstream tests this because nothing upstream promises it.
2. **CSP suite** — strict policy, zero violations, no `eval`/`Function` in the bundle. Upstream cannot pass this by construction, which is the entire reason vaxel exists.
3. **Dirty-input arbitration and morph behaviour** under real focus, caret, selection and IME.
4. **Accessibility** — focus restoration, `aria-busy`, live-region announcements, and the rule that push-driven patches never steal focus.

## 5. Keeping the scoreboard honest

- The scoreboard is a file in the repository, not a claim in a README: [`parity/SCOREBOARD.md`](../parity/SCOREBOARD.md).
- It is regenerated by the test run, not hand-edited.
- Upstream is pinned; a bump is a deliberate commit that re-runs everything and updates the counts.
- A case that fails stays visible as failing. There is no "known issue" list that quietly grows — a case is passing, declined with a stated reason, or a bug.

## 6. Licensing and credit

Datastar is MIT. Vendored test scripts and cases keep their licence header and are recorded in `NOTICE` with the pinned commit SHA — a real 40-character hash, verifiable, never an abbreviated or invented one. Our derived fixtures state which upstream file they were derived from. Where we decline a behaviour, the fixture says so in one sentence, so a reader of the suite learns the design rather than guessing at an omission.
