namespace Workbench.Signals;

/// <summary>
/// Strongly-typed signals schema for the Workbench reference application.
/// </summary>
public sealed record WorkbenchSignals(
    string Tab = "overview",
    bool RailOpen = false,
    int DraftSeq = 1,
    string Filter = "",
    int Count = 0,
    string Since = ""
);
