namespace Vaxel;

/// <summary>
/// Wire-level protocol constants for Vaxel HTTP requests and patch responses.
/// </summary>
public static class VaxelProtocol
{
    public const string PatchContentType = "text/vnd.vaxel-patch+html; charset=utf-8";
    public const string RequestHeader = "VX-Request";
    public const string ProtocolHeader = "VX-Protocol";
    public const string HistoryHeader = "VX-History";
    public const string TargetHeader = "VX-Target";
    public const string SignalsHeader = "VX-Signals";
    public const string SequenceHeader = "VX-Sequence";
    public const string UrlHeader = "VX-Url";
}
