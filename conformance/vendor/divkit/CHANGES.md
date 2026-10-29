# conformance/vendor/divkit — provenance & change log

Every expression fixture lifted from DivKit (Apache-2.0) via Centralen (`8d58b3637b93d36f81cd90186ddeb07ee831f21d`) is recorded here: source path in upstream, upstream commit, date lifted, local path, and modifications.

| Upstream path | Upstream commit | Lifted on | Local path | Modifications |
|---|---|---|---|---|
| `test_data/expression_test_data/arithmetic.json` | `f8796582dc9f426f16d4b378459f0864316a5c71` (`divkit/divkit`) | 2026-10-29 | `conformance/expressions/cases/divkit_arithmetic.json` | Converted to Centralen/Vaxel fixture JSON format (`name`, `expression`, `state`, `expect`). |
| `test_data/expression_test_data/comparison.json` | `f8796582dc9f426f16d4b378459f0864316a5c71` (`divkit/divkit`) | 2026-10-29 | `conformance/expressions/cases/divkit_comparison.json` | Converted to Centralen/Vaxel fixture JSON format (`name`, `expression`, `state`, `expect`). |
| `test_data/expression_test_data/logical.json` | `f8796582dc9f426f16d4b378459f0864316a5c71` (`divkit/divkit`) | 2026-10-29 | `conformance/expressions/cases/divkit_logical.json` | Converted to Centralen/Vaxel fixture JSON format (`name`, `expression`, `state`, `expect`). |
| `test_data/expression_test_data/ternary.json` | `f8796582dc9f426f16d4b378459f0864316a5c71` (`divkit/divkit`) | 2026-10-29 | `conformance/expressions/cases/divkit_ternary.json` | Converted to Centralen/Vaxel fixture JSON format (`name`, `expression`, `state`, `expect`). |
| `test_data/expression_test_data/strings.json` | `f8796582dc9f426f16d4b378459f0864316a5c71` (`divkit/divkit`) | 2026-10-29 | `conformance/expressions/cases/divkit_strings.json` | Converted to Centralen/Vaxel fixture JSON format; added string interpolation cases. |
| `test_data/expression_test_data/functions.json` | `f8796582dc9f426f16d4b378459f0864316a5c71` (`divkit/divkit`) | 2026-10-29 | `conformance/expressions/cases/divkit_functions.json` | Converted to Centralen/Vaxel fixture JSON format (`math`, `cast`, `string` functions). |
| `test_data/expression_test_data/errors.json` | `f8796582dc9f426f16d4b378459f0864316a5c71` (`divkit/divkit`) | 2026-10-29 | `conformance/expressions/cases/divkit_errors.json` | Mapped DivKit error codes to `expressions.*` standard refusal codes. |
| `src/client/runtime/src/signals.ts` | `8d58b3637b93d36f81cd90186ddeb07ee831f21d` (Centralen) | 2026-10-29 | `src/Vaxel.Client/wwwroot/_vaxel/vaxel.js` | Internal lift of `Signal`/`EffectScope` with private write-depth scheduler. |
| `src/client/expressions/src/*.ts` | `8d58b3637b93d36f81cd90186ddeb07ee831f21d` (Centralen) | 2026-10-29 | `src/Vaxel.Client/wwwroot/_vaxel/vaxel.js` | Internal lift of closed expression lexer/parser/evaluator with own-property + proto guard and length/depth caps. |
