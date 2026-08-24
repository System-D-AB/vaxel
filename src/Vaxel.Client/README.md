# Vaxel.Client

Static client assets and agent runtime for **vaxel** hypermedia applications.

**Repository:** [github.com/System-D-AB/vaxel](https://github.com/System-D-AB/vaxel)

## Usage

> **Important:** Web applications should reference **`Vaxel.AspNetCore`**, not `Vaxel.Client` directly. `Vaxel.AspNetCore` pulls in this package transitively.

This package contains the pre-compiled, standalone JavaScript client agent (`/_vaxel/vaxel.js`) and developer inspector (`/_vaxel/vaxel.dev.js`).

- **No Node.js or npm required**: All client assets are packaged directly into the NuGet package as static web assets.
- **Zero Eval**: Pure DOM mutation and signal handling without `eval()` or string-to-code compilation.
- **Ultra-lightweight**: ~9–10 KB gzipped agent bundle (including morph engine and signal store).

## Attributions

vaxel Client incorporates an adapted distribution of [Idiomorph](https://github.com/bigskysoftware/idiomorph), licensed under the BSD 2-Clause License. See NOTICE for details.
