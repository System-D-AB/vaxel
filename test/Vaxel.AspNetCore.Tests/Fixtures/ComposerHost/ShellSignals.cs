namespace Vaxel.AspNetCore.Tests.Fixtures.ComposerHost;

public sealed record ShellSignals(
    string Tab = "overview",
    string? Filter = null,
    bool RailOpen = true,
    int Count = 0,
    DateTime? Since = null
);
