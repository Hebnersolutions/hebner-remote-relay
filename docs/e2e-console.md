Playwright E2E: Helper Console frame rendering

Setup (local):

1) Install node dev deps and Playwright browsers:
   npm i
   npm run test:setup

2) Start test (this will start the BrokerStub, run the test, and stop it):
   npm run test:console

What the test does:
- Starts BrokerStub (`dotnet run` for BrokerStub project)
- Opens the real frontend Console page by default: http://localhost:3000/console?id=testsession&techToken=testtoken&auto=1 (can be overridden via env FRONTEND_BASE)
- Starts a fake agent that connects to ws://127.0.0.1:5189/ws/agent/testsession and sends 3 tiny frames
- Verifies that the broker recorded at least one frame (GET /dev/status?sessionId=testsession)
- Verifies the canvas has non-blank image via canvas.toDataURL()
- Simulates mouse click on canvas and verifies broker saw a helper message (GET /dev/status)

If tests fail in CI, ensure port 5189 is free and dotnet/run works in CI environment.

Notes:
- This uses the BrokerStub dev console at /dev/console for integration testing. Replace with the real Console URL if/when the frontend is available at :3000.
- The fake agent is in `tools/fake-agent-send-frame.js`.

Notes on frontend integration:
- The test targets the real frontend at `http://localhost:3000` by default. Set `FRONTEND_BASE` environment variable to change this.
- If the frontend does not have a runtime override for the relay WebSocket base, the test injects a small script that rewrites WebSocket URLs to point at the relay specified by `RELAY_WS_BASE` (default `ws://127.0.0.1:5189`). You can set `RELAY_WS_BASE` in your environment when running the test.
- The `/console` dev mirror was removed from BrokerStub — tests should exercise the real frontend instead.
