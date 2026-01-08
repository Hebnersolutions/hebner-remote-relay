# Service ↔ Tray IPC Implementation Plan

## Overview

Implement Named Pipe IPC for consent handling between the Service and Tray applications.

## Requirements Analysis

- Pipe name: "hebner.remote.ipc"
- Service sends CONSENT_REQUEST JSON when BrokerCommandType.RequestConsent is received
- Tray shows modal dialog and sends CONSENT_RESPONSE back
- Automatic reconnection if either side restarts
- JSON message format
- Non-blocking, background thread operation
- Logging to C:\ProgramData\HebnerRemoteSupport\logs\ipc.log

## Implementation Steps

### 1. Create IPC Message Contracts

- Add new message types to Contracts.cs
- Define JSON serialization attributes

### 2. Implement Service Side IPC

- Create NamedPipeServerStream listener
- Add IPC service component
- Integrate with existing Worker.cs command handling
- Add logging for all IPC messages

### 3. Implement Tray Side IPC

- Create NamedPipeClientStream connection
- Add IPC client component
- Create consent dialog window
- Integrate with existing MainWindow

### 4. Add Logging Infrastructure

- Create dedicated IPC logger
- Ensure logs go to correct directory

### 5. Update Project Dependencies

- Add necessary NuGet packages if needed
- Update project files

### 6. Testing and Integration

- Test IPC communication
- Verify reconnection behavior
- Test consent flow end-to-end

## File Structure

```
src/Hebner.Agent.Shared/
├── Contracts.cs (update existing)

src/Hebner.Agent.Service/
├── IpcService.cs (new)
├── IpcLogger.cs (new)
└── Worker.cs (update)

src/Hebner.Agent.Tray/
├── IpcClient.cs (new)
├── ConsentDialog.xaml (new)
├── ConsentDialog.xaml.cs (new)
├── IpcLogger.cs (new)
└── MainWindow.xaml.cs (update)
```

## Message Format

```json
// Request
{
  "type": "CONSENT_REQUEST",
  "sessionId": "string",
  "requester": "string"
}

// Response
{
  "type": "CONSENT_RESPONSE",
  "sessionId": "string",
  "allowed": true|false
}
```
