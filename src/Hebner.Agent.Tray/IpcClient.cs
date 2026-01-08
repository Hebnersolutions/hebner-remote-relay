using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Hebner.Agent.Shared;
using System.Windows;

namespace Hebner.Agent.Tray;

public sealed class IpcClient : IDisposable
{
    private const string PipeName = "hebner.remote.ipc";
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private volatile bool _disposed = false;
    private volatile bool _connected = false;
    private Task? _connectionTask;
    private CancellationTokenSource? _cts;

    public IpcClient()
    {
        StartConnectionLoop();
    }

    private void StartConnectionLoop()
    {
        _cts = new CancellationTokenSource();
        _connectionTask = Task.Run(async () => await ConnectionLoop(_cts.Token));
    }

    private async Task ConnectionLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && !_disposed)
        {
            try
            {
                await _connectLock.WaitAsync(ct);
                _connected = false;

                try
                {
                    await using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                    await client.ConnectAsync(5000, ct);

                    _connected = true;
                    await HandleServerAsync(client, ct);
                }
                catch (TimeoutException)
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), ct);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested || _disposed)
            {
                break;
            }
            catch (Exception)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
            finally
            {
                _connectLock.Release();
            }
        }
    }

    public async Task<bool> SendConsentResponseAsync(string sessionId, bool allowed, CancellationToken ct)
    {
        if (!_connected)
        {
            return false;
        }

        var response = new ConsentResponseMessage
        {
            Type = IpcMessageType.CONSENT_RESPONSE,
            SessionId = sessionId,
            Allowed = allowed
        };

        var json = JsonSerializer.Serialize(response, _jsonOptions);

        try
        {
            await _connectLock.WaitAsync(ct);

            var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(5000, ct);

            var writer = new StreamWriter(client) { AutoFlush = true };
            await writer.WriteLineAsync(json);
            await writer.FlushAsync();

            await client.DisposeAsync();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private async Task HandleServerAsync(NamedPipeClientStream client, CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(client);
            using var writer = new StreamWriter(client) { AutoFlush = true };

            while (!ct.IsCancellationRequested && !_disposed && client.IsConnected)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrEmpty(line)) break;

                if (TryParseConsentRequest(line, out var request))
                {
                    await HandleConsentRequestAsync(request, ct);
                }
            }
        }
        catch (Exception)
        {
            // Ignore errors
        }
        finally
        {
            _connected = false;
        }
    }

    private bool TryParseConsentRequest(string json, out ConsentRequestMessage? request)
    {
        request = null;
        try
        {
            var msg = JsonSerializer.Deserialize<IpcMessageBase>(json, _jsonOptions);
            if (msg?.Type == IpcMessageType.CONSENT_REQUEST)
            {
                request = JsonSerializer.Deserialize<ConsentRequestMessage>(json, _jsonOptions);
                return request is not null;
            }
        }
        catch (Exception)
        {
            // Ignore parsing errors
        }
        return false;
    }

    private async Task HandleConsentRequestAsync(ConsentRequestMessage request, CancellationToken ct)
    {
        try
        {
            var result = await ShowConsentDialogAsync(request, ct);
            await SendConsentResponseAsync(request.SessionId, result, ct);
        }
        catch (Exception)
        {
            // Ignore errors
        }
    }

    private async Task<bool> ShowConsentDialogAsync(ConsentRequestMessage request, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<bool>();

        Application.Current.Dispatcher.Invoke(() =>
        {
            var dialog = new ConsentDialog(request);
            dialog.ConsentResult += (allowed) => tcs.SetResult(allowed);
            dialog.Show();
        });

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(5));

        try
        {
            return await tcs.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _cts?.Cancel();
        _connectLock?.Dispose();
    }
}
