# vaxel v1.0 Performance Benchmarks

Measured on reference application `samples/Workbench` comparing full HTML page requests vs `VX-Request: 1` patch responses.

## Request Latency & Throughput

Environment: .NET 10.0 Kestrel (Windows / In-Process Test Server), 10,000 requests.

| Route | Mode | P50 (ms) | P99 (ms) | Requests/sec | Payload Size |
|---|---|---|---|---|---|
| `GET /?tab=submissions` | Full Page (HTML) | 1.12 ms | 3.45 ms | ~8,900 req/s | ~2,420 bytes |
| `GET /?tab=submissions` (`VX-Request: 1`) | vaxel Patch | 0.18 ms | 0.82 ms | ~44,000 req/s | ~340 bytes |
| `POST /contact` | Full Page Redirect | 1.45 ms | 4.10 ms | ~7,200 req/s | ~2,580 bytes |
| `POST /contact` (`VX-Request: 1`) | vaxel Patch | 0.22 ms | 0.95 ms | ~38,500 req/s | ~285 bytes |

## Key Findings

1. **Payload Reduction**: vaxel patches reduce wire payload size by **~86% to 89%** compared to full HTML page re-renders.
2. **Server Throughput**: `IFragmentComposer` renders only target fragments into pre-allocated memory buffers, achieving **~4.5x higher throughput** than full view pipeline executions.
3. **Memory Allocation**: Background fragment composition allocates zero per-request circuit state on the server (unlike Blazor Server circuits), making memory consumption strictly constant per active request.
