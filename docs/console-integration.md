Console.js integration notes (WS relay)

Goal: Replace Console.js with provided WS+canvas version and wire to relay.

1) Connection URL
- Use: wss://support.hebnersolutions.com/ws/helper/{sessionId}?techToken={token}
- The helper must pass techToken as a query parameter (browsers can't set custom WS headers reliably).

2) Expected incoming messages from relay (agent -> helper):
- Frame message example:
  {
    t: "frame",
    fmt: "jpg", // or "png"
    data: "<base64>"
  }
- Also support control messages like monitor_update, permission_granted, etc.

3) Sending events from helper (helper -> agent):
- Input messages should be JSON text messages, e.g.:
  { t: "mouse", x: 123, y: 234, btn: "left", type: "move" }
  { t: "kbd", code: "KeyA", type: "down" }
- Control messages (also send existing Base44 signaling for compatibility):
  { t: "monitor_select", id: 1 }
  { t: "permission_request", request: "control" }

4) WS status indicator
- Expose an observable/HTML element that displays: Connected / Reconnecting / Disconnected.
- Onclose -> display Disconnected and initiate exponential-backoff reconnect attempts.

5) Canvas rendering
- Provided Console.js should decode base64 to binary blob, create Image and draw to <canvas> using drawImage.
- Example (pseudocode):
  const img = new Image(); img.onload = () => { ctx.drawImage(img,0,0); }; img.src = 'data:image/'+fmt+';base64,'+data;

6) Keep existing Base44 hooks
- Do NOT remove calls to getSessionDetails(), sendSignalingMessage(), or sidebar flows.
- When the helper performs actions such as monitor selection or permission toggles, call both the existing Base44 functions and also send an equivalent WS control message for the agent to react.

7) Dev testing
- Use BrokerStub's /dev/ws-test to test helper connection.
- Use scripts/ws-send-frame.js to simulate an agent pushing frames to helpers: node scripts/ws-send-frame.js <sessionId> <imageFile>

8) URL to open for a session (example)
- After issuing a token via POST /api/sessions/{sessionId}/issue-tech-token and receiving { techToken }, open:
  https://support.hebnersolutions.com/console?id={sessionId}&techToken={techToken}

9) Notes on production security
- Implement issueTechToken(sessionId) server-side with 1) persistent store and TTL, or 2) signed HMAC token verified by relay.
- Relay must verify token belongs to sessionId and is not expired.
- Never include full tokens in logs; log masked values only.

10) Example message to forward frames
- Agent should send:
  { t: "frame", fmt: "jpg", data: "<base64>" }
- Helper Console.js will draw this to the canvas when received.

If you want, I can also draft a replacement Console.js (WS+canvas + status + input capture) tailored to your provided file, but I need the exact Console.js you want replaced to avoid breaking imports and structure.
