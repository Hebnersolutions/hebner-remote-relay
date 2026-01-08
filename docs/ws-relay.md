Relay WebSocket changes and dev test instructions

Overview
- Implemented a helper WebSocket relay in BrokerStub to support the browser-based helper console.
- New endpoints:
  - POST /api/sessions/{sessionId}/issue-tech-token -> { techToken, expiresAtUtc }
    - Issues a short-lived (30 min) tech token, stored in-memory (dev-only)
  - ws://<host>/ws/helper/{sessionId}?techToken=<token>
    - Helper (browser) connects here. Token may also be supplied as X-Tech-Token header for backwards compatibility.
  - ws://<host>/ws/agent/{sessionId}
    - Agent connects here and sends frame messages which are forwarded to helpers for that session.
  - GET /dev/ws-test
    - Simple web page to test helper connections and view messages.

Token rules
- BrokerStub accepts query param techToken or X-Tech-Token header.
- In this dev stub any non-empty techToken is accepted (to simplify local testing) and is logged masked.
- In production you should validate by looking up stored token or verifying a signed token.

Message format
- Frame messages (from agent to helpers): { t: "frame", fmt: "jpg"|"png", data: "<base64>" }
- Helper input messages (from helper to agent): any message the Console.js sends; we forward text messages bidirectionally.

Dev testing steps
1) Start BrokerStub (scripts/run-dev.bat starts it). It listens on http://127.0.0.1:5189 by default.
2) Issue a token for a session:
   curl -X POST http://127.0.0.1:5189/api/sessions/test-session/issue-tech-token
   -> returns { techToken: "...", expiresAtUtc: "..." }
3) Open http://127.0.0.1:5189/dev/ws-test and paste session id and token, click "Connect as helper". You should see `open` and log messages.
4) Simulate agent frames with the provided node script:
   node scripts/ws-send-frame.js test-session path/to/sample.jpg
   The helper dev page should receive the frame JSON message.
5) Console.js in the frontend should connect to wss://support.hebnersolutions.com/ws/helper/{sessionId}?techToken=<token>
   and handle messages { t:"frame", fmt:"jpg", data:"<base64>" } by drawing the decoded image to the canvas.

Notes for Frontend / Base44 integration
- Replace existing Console.js with the provided WS+canvas implementation. Ensure it reads session id from ?id=... and techToken from ?techToken=...
- Add a WS status indicator in Console UI (Connected / Reconnecting / Disconnected).
- Wherever you generate the Open Console link, append the short-lived techToken returned by POST /api/sessions/{sessionId}/issue-tech-token as &techToken=<token>.
- To propagate tokens server-side, add a Base44 backend function issueTechToken(sessionId) which calls the BrokerStub API or generates signed tokens and returns { techToken }.

Security
- This BrokerStub is intentionally permissive for local dev. For production, store tokens with limited TTL, scope them to sessionId, and validate properly; or use an HMAC-signed token.
- Never log full tokens. The broker logs masked tokens like AAAA-****-ZZZZ.
