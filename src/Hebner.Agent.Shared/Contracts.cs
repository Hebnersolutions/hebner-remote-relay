namespace Hebner.Agent.Shared;

public static class Brand
{
    public const string ProductName = "Hebner Solutions Remote Support";
    public const string Company = "Hebner Solutions";
    public const string SupportHost = "support.hebnersolutions.com";
}

public enum AgentConnectionState
{
    Offline = 0,
    Online = 1,
    InSession = 2
}

public record DeviceInfo(
    string DeviceId,
    string DeviceName,
    string Hostname,
    string OsVersion,
    string AgentVersion
);

public record MonitorInfo(
    string MonitorId,
    string Name,
    int Width,
    int Height,
    double Scale,
    bool IsPrimary,
    int SortOrder
);

public record HeartbeatPayload(
    DeviceInfo Device,
    AgentConnectionState State,
    List<MonitorInfo> Monitors,
    DateTimeOffset TimestampUtc
);

public enum BrokerCommandType
{
    StartAttendedSession,
    StartUnattendedSession,
    EndSession,
    RequestConsent,        // attended
    SelectMonitor,
    SetAllDisplaysMode,
    RequestPermissions
}

public record BrokerCommand(
    BrokerCommandType Type,
    string SessionId,
    Dictionary<string, string>? Args = null
);

public enum PermissionFlag
{
    Control,
    Clipboard,
    FileTransfer,
    Reboot
}

public record PermissionRequest(
    string SessionId,
    List<PermissionFlag> Requested,
    string RequestedBy
);

public enum AgentEventType
{
    AgentStarted,
    AgentStopped,
    HeartbeatSent,
    SessionCreated,
    ConsentRequired,
    ConsentGranted,
    ConsentDenied,
    MonitorSelected,
    PermissionsGranted,
    PermissionsDenied,
    SessionEnded,
    Error
}

public record AgentEvent(
    AgentEventType Type,
    string? SessionId,
    string Message,
    Dictionary<string, string>? Details = null,
    DateTimeOffset? TimestampUtc = null
);

// IPC Message Contracts for Service ↔ Tray Communication
public enum IpcMessageType
{
    CONSENT_REQUEST,
    CONSENT_RESPONSE
}

public record IpcMessageBase
{
    public required IpcMessageType Type { get; init; }
    public required string SessionId { get; init; }
}

public record ConsentRequestMessage : IpcMessageBase
{
    public required string Requester { get; init; }
}

public record ConsentResponseMessage : IpcMessageBase
{
    public required bool Allowed { get; init; }
}
