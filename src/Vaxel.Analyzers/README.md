# Vaxel.Analyzers

Roslyn analyzers and diagnostic rules for **vaxel** applications.

**Repository:** [github.com/System-D-AB/vaxel](https://github.com/System-D-AB/vaxel)

## Installation

Add as a development dependency:

```xml
<PackageReference Include="Vaxel.Analyzers" Version="1.0.0">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
</PackageReference>
```

## Rules Enforced

- **VAXEL001 (Warning)**: Security warning when authentication, authorization, or role-shaped property names are declared in client signals.
- **VAXEL002 (Error)**: Progressive degradation error when `vx-*` action triggers are placed on non-interactive HTML tags without fallback links/forms.
- **VAXEL003 (Error)**: Target selector error when `vx-target` is not an `#id` selector.
