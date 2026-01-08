using System.Net.Http.Json;
using System.Runtime.InteropServices;
using Hebner.Agent.Shared;

namespace Hebner.Agent.Service;

public sealed class Worker : BackgroundService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<Worker> _logger;
    private readonly IpcService _ipcService;

    private readonly DeviceInfo _device;
    private AgentConnectionState _state = AgentConnectionState.Offline;

    public Worker(IHttpClientFactory httpFactory, ILogger<Worker> logger, IpcService ipcService)
    {
        _httpFactory = httpFactory;
        _logger = logger;
        _ipcService = ipcService;

        _device = new DeviceInfo(
            DeviceId: LoadOrCreateDeviceId(),
            DeviceName: Environment.MachineName,
            Hostname: Environment.MachineName,
            OsVersion: RuntimeInformation.OSDescription,
            AgentVersion: typeof(Worker).Assembly.GetName().Version?.ToString() ?? "0.1.0"
        );
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("{Product} service starting. DeviceId={DeviceId}", Brand.ProductName, _device.DeviceId);

        _state = AgentConnectionState.Online;

        // Main loop: heartbeat + poll commands (stub)
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var monitors = MonitorProbe.GetMonitorsSafe();
                var hb = new HeartbeatPayload(_device, _state, monitors, DateTimeOffset.UtcNow);

                var client = _httpFactory.CreateClient("broker");
                var resp = await client.PostAsJsonAsync("api/agent/heartbeat", hb, stoppingToken);
                resp.EnsureSuccessStatusCode();

                // In production: switch to websocket for commands + WebRTC signaling
                var cmd = await client.GetFromJsonAsync<BrokerCommand?>($"api/agent/next-command?deviceId={_device.DeviceId}", stoppingToken);
                if (cmd is not null)
                {
                    await HandleCommandAsync(cmd, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Service loop error");
            }

            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
    }

    private async Task HandleCommandAsync(BrokerCommand cmd, CancellationToken ct)
    {
        _logger.LogInformation("Command: {Type} Session={SessionId}", cmd.Type, cmd.SessionId);

        switch (cmd.Type)
        {
            case BrokerCommandType.StartAttendedSession:
            case BrokerCommandType.StartUnattendedSession:
                _state = AgentConnectionState.InSession;

                // TODO: Start WebRTC pipeline:
                // - Gather monitors
                // - Create peer connection
                // - Send offer/answer/ice via broker signaling
                // - Start capture stream (selected monitor or all-displays)
                // - Start input listener over data channel
                break;

            case BrokerCommandType.SelectMonitor:
                // TODO: switch capture source to monitorId
                break;

            case BrokerCommandType.SetAllDisplaysMode:
                // TODO: enable composite or instant-switch UI
                break;

            case BrokerCommandType.RequestConsent:
                await HandleConsentRequestAsync(cmd, ct);
                break;

            case BrokerCommandType.RequestPermissions:
                // TODO: apply permission gating & respond to broker
                break;

            case BrokerCommandType.EndSession:
                _state = AgentConnectionState.Online;
                // TODO: tear down pipeline, close channels
                break;

            default:
                break;
        }

        // Acknowledge command handled (dev stub)
        var client = _httpFactory.CreateClient("broker");
        await client.PostAsJsonAsync("api/agent/command-ack", new { deviceId = _device.DeviceId, cmd.SessionId, cmd.Type }, ct);
    }

    private async Task HandleConsentRequestAsync(BrokerCommand cmd, CancellationToken ct)
    {
        _logger.LogInformation("Requesting consent for session {SessionId}", cmd.SessionId);

        try
        {
            var requester = cmd.Args?.GetValueOrDefault("requester") ?? "Remote Technician";
            var response = await _ipcService.RequestConsentAsync(cmd.SessionId, requester, ct);

            if (response?.Allowed == true)
            {
                _logger.LogInformation("Consent granted for session {SessionId}", cmd.SessionId);
                // TODO: Start session
            }
            else
            {
                _logger.LogInformation("Consent denied for session {SessionId}", cmd.SessionId);
                // TODO: Notify broker of denial
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to request consent for session {SessionId}", cmd.SessionId);
        }
    }

    private static string LoadOrCreateDeviceId()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HebnerRemoteSupport");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "device-id.txt");
        if (File.Exists(path)) return File.ReadAllText(path).Trim();

        var id = Guid.NewGuid().ToString("N");
        File.WriteAllText(path, id);
        return id;
    }
}
