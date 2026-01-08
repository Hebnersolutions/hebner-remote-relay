using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Hebner.Agent.Shared;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hebner.Agent.Service;

public sealed class IpcService : BackgroundService
{
    private const string PipeName = "hebner.remote.ipc";
    private readonly ILogger<IpcService> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly SemaphoreSlim _consentResponseLock = new(1, 1);
    private readonly TaskCompletionSource<ConsentResponseMessage?> _consentResponseTcs = new();
    private volatile bool _disposed = false;

    public IpcService(ILogger<IpcService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("IPC Service starting. Pipe name: {PipeName}", PipeName);

        while (!stoppingToken.IsCancellationRequested && !_disposed)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    transmissionMode: PipeTransmissionMode.Byte,
                    options: PipeOptions.Asynchronous);

                _logger.LogInformation("Waiting for tray client connection...");
                await server.WaitForConnectionAsync(stoppingToken);

                if (stoppingToken.IsCancellationRequested || _disposed)
                {
                    if (server.IsConnected) server.Disconnect();
                    break;
                }

                _logger.LogInformation("Tray client connected.");
                await HandleClientAsync(server, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested || _disposed)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IPC server error. Will retry in 2s.");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }

        _logger.LogInformation("IPC Service stopped.");
    }

    public async Task<ConsentResponseMessage?> RequestConsentAsync(string sessionId, string requester, CancellationToken ct)
    {
        var request = new ConsentRequestMessage
        {
            Type = IpcMessageType.CONSENT_REQUEST,
            SessionId = sessionId,
            Requester = requester
        };

        var json = JsonSerializer.Serialize(request, _jsonOptions);
        LogIpcMessage("OUT", json);

        try
        {
            await _consentResponseLock.WaitAsync(ct);
            _consentResponseTcs.TrySetResult(null); // Reset

            // Send request
            var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(5000, ct);

            var writer = new StreamWriter(client) { AutoFlush = true };
            await writer.WriteLineAsync(json);
            await writer.FlushAsync();

            // Wait for response with timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            var responseTask = _consentResponseTcs.Task.WaitAsync(cts.Token);
            var result = await responseTask;

            await client.DisposeAsync();
            return result;
        }
        finally
        {
            _consentResponseLock.Release();
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream server, CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(server);
            using var writer = new StreamWriter(server) { AutoFlush = true };

            while (!ct.IsCancellationRequested && !_disposed && server.IsConnected)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrEmpty(line)) break;

                LogIpcMessage("IN", line);

                if (TryParseConsentResponse(line, out var response))
                {
                    _consentResponseTcs.TrySetResult(response);
                }
                else
                {
                    _logger.LogWarning("Unknown IPC message: {Message}", line);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling client");
        }
        finally
        {
            if (server.IsConnected) server.Disconnect();
        }
    }

    private bool TryParseConsentResponse(string json, out ConsentResponseMessage? response)
    {
        response = null;
        try
        {
            var msg = JsonSerializer.Deserialize<IpcMessageBase>(json, _jsonOptions);
            if (msg?.Type == IpcMessageType.CONSENT_RESPONSE)
            {
                response = JsonSerializer.Deserialize<ConsentResponseMessage>(json, _jsonOptions);
                return response is not null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse consent response: {Json}", json);
        }
        return false;
    }

    private void LogIpcMessage(string direction, string json)
    {
        _logger.LogInformation("[{Direction}] {Json}", direction, json);
    }

    public override void Dispose()
    {
        _disposed = true;
        _consentResponseLock?.Dispose();
        base.Dispose();
    }

    private sealed class IpcLogger { }
}
