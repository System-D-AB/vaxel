# Example Corpus Scoring

Scoring of public hypermedia / reactive example patterns against Växel.

| Example Pattern | Datastar Pattern | Växel Construct | Verdict |
|---|---|---|---|
| Active Search | `data-on-input="$$get('/search')"` | `<input vx-get="/search" vx-target="#results" />` | ✅ Same |
| Bulk Update | `data-on-click="$$post('/bulk')"` | `<form vx-post="/bulk" vx-target="#table">` | ✅ Same |
| Click to Edit | `data-on-click="$$get('/edit')"` | `<a href="/edit" vx-get vx-target="#contact">Edit</a>` | ✅ Same |
| Click to Load / Infinite Scroll | `data-on-intersect="$$get('/more')"` | `<div vx-trigger-visible vx-get="/more" vx-target="#list">` | ✅ Same |
| Custom Validation | `data-custom-validity` | Server 422 Patch with error fragment | 🟡 Server-round-trip |
| Delete Row | `data-on-click="$$delete('/item/1')"` | `<button vx-delete="/item/1" vx-target="#row-1">` | ✅ Same |
| Edit Row inline | `data-on-click="$$get('/row/1/edit')"` | `<a href="/row/1/edit" vx-get vx-target="#row-1">` | ✅ Same |
| Lazy Load | `data-init="$$get('/lazy')"` | `<div vx-trigger-load vx-get="/lazy" vx-target="#content">` | ✅ Same |
| Progress Bar / Polling | `data-on-interval__1s="$$get('/status')"` | `<div vx-poll="1s" vx-get="/status" vx-target="#progress">` | ✅ Same |
| Server-Sent Events Push | SSE events stream | `app.MapVaxelStream("/_vaxel/stream")` | ✅ Same |
| Tabs Rail | `data-bind="tab"` | `vx-bind:tab` + `vx-class:is-active="tabIsActive"` | ✅ Same |
| Dialog / Modal | `data-show="open"` | `vx-show="modalOpen"` / native `<dialog>` | ✅ Same |
| Optimistic UI | `data-on-click` + local store | Server-driven fast patch + `<vx-signals>` | 🟡 Server-round-trip |
| Client-Side Canvas Chart | Canvas RAF draw loop | Web Component island on `vx:signals-changed` | 🟡 Island |
| Code Editor | Monaco / CodeMirror | Web Component island on `vx:signals-changed` | 🟡 Island |

## Summary

- Total Scored: 15 / 15 (100 %)
- Same: 11
- Server-round-trip: 2
- Island: 2
- Cannot: 0
