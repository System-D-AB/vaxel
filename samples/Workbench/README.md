# vaxel Workbench Reference Application

The official reference application for **vaxel v1.0**.

This application demonstrates the end-to-end architecture and patterns of vaxel:
- **Mounted Layout & Shell**: `#pane`, `#rail`, and `#notices` regions navigated via `PageOrPatch` and `IFragmentComposer`.
- **Progressive Degradation (Rule R3)**: Every screen works with zero JavaScript enabled (full 200 HTML page fallback).
- **Strongly Typed Signal Schema (Rule R2)**: `AddSignalSchema<WorkbenchSignals>()` ensuring compile/render validation of signal bindings with zero `eval` or client runtime expressions.
- **Governed Refusals (Rule R4)**: Multi-recipient patch updates combined with `Patch.Refused` into `#notices` for permission/role checks.
- **Server Push**: Scoped SSE updates via `MapVaxelStream("/_vaxel/stream")`.

## Running the Application

```bash
dotnet run --project samples/Workbench/Workbench.csproj
```

Open your browser to `http://localhost:5000` (or `https://localhost:5001`).

## Security & CSP Headers

The application enforces a strict Content-Security-Policy (CSP):

```http
Content-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; connect-src 'self'; object-src 'none'; base-uri 'self';
```

There is **zero `unsafe-eval`**, **zero `new Function()`**, and **zero string timers**.

## Reverse Proxy & Hosting Configuration

When hosting behind reverse proxies with Server-Sent Events (`/_vaxel/stream`):

### Nginx
```nginx
location /_vaxel/stream {
    proxy_pass http://backend;
    proxy_set_header Connection '';
    proxy_http_version 1.1;
    chunked_transfer_encoding off;
    proxy_buffering off;
    proxy_cache off;
}
```

### IIS / web.config
```xml
<configuration>
  <system.webServer>
    <aspNetCore processPath="dotnet" arguments=".\Workbench.dll" stdoutLogEnabled="false">
      <environmentVariables>
        <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
      </environmentVariables>
    </aspNetCore>
    <serverRuntime responseBufferLimit="0" />
  </system.webServer>
</configuration>
```
