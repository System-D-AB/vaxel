# Vaxel.Testing

Testing harness and assertion utilities for **vaxel** applications.

**Repository:** [github.com/System-D-AB/vaxel](https://github.com/System-D-AB/vaxel)

## Installation

Install in test projects only:

```bash
dotnet add package Vaxel.Testing
```

## Features

- **Test Framework Agnostic**: Contains zero dependencies on xUnit, NUnit, or MSTest.
- **Rule R3 Parity Harness**: Verify structural equivalence between full-page HTML renders and hypermedia patch documents using `VaxelParity.AssertAsync`.
- **SSE Stream Client**: `StreamClient` utility for testing Server-Sent Events push channels in integration tests.

## Example: Parity Assertion

```csharp
[Theory]
[InlineData("/contact", "/contact", "contact")]
[InlineData("/?tab=settings", "/?tab=settings", "pane")]
public async Task Route_Has_FullPage_And_Patch_Parity(string pageUrl, string patchUrl, string regionId)
{
    await VaxelParity.AssertAsync(_client, pageUrl, patchUrl, regionId);
}
```
