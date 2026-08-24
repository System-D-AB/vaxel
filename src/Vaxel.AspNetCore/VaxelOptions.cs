namespace Vaxel;

/// <summary>
/// Configuration options for vaxel.
/// </summary>
public sealed class VaxelOptions
{
    public string RequestHeaderName { get; set; } = "VX-Request";
    public string ProtocolHeaderName { get; set; } = "VX-Protocol";
    public string HistoryHeaderName { get; set; } = "VX-History";
    public string SignalsHeaderName { get; set; } = "VX-Signals";
    public string AntiforgeryHeaderName { get; set; } = "X-CSRF";
    public string AttributePrefix { get; set; } = "vx-";
    public int MaxSignalsBytes { get; set; } = 8 * 1024;
    public bool NoStoreWhenSignalsRead { get; set; } = true;
    public SwapMode DefaultSwap { get; set; } = SwapMode.Morph;
    public ISignalSchema? SignalSchema { get; set; }
    public VaxelPushOptions Push { get; set; } = new();
}

public sealed class VaxelPushOptions
{
    public int HeartbeatSeconds { get; set; } = 20;
    public int MaxConnectionsPerIdentity { get; set; } = 4;
    public bool AllowAnonymous { get; set; } = false;
    public string UserIdClaimType { get; set; } = System.Security.Claims.ClaimTypes.NameIdentifier;
    public string GroupIdClaimType { get; set; } = System.Security.Claims.ClaimTypes.Role;
}
