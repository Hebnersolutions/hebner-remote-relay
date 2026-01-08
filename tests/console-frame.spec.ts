import { test, expect } from '@playwright/test';
import { spawn, ChildProcess } from 'child_process';

function waitForHttp(url: string, timeout = 20000): Promise<void> {
  const start = Date.now();
  return new Promise((resolve, reject) => {
    (function poll() {
      fetch(url).then(r => {
        if (r.status === 200) return resolve();
        if (Date.now() - start > timeout) return reject(new Error('timeout'));
        setTimeout(poll, 200);
      }).catch(()=>{
        if (Date.now() - start > timeout) return reject(new Error('timeout'));
        setTimeout(poll, 200);
      });
    })();
  });
}

let brokerProc: ChildProcess | null = null;
let fakeAgentProc: ChildProcess | null = null;

test.beforeAll(async () => {
  // Start BrokerStub
  brokerProc = spawn('dotnet', ['run', '--project', 'src/Hebner.Agent.BrokerStub/Hebner.Agent.BrokerStub.csproj'], { stdio: 'inherit' });
  // Wait for broker to be available
  await waitForHttp('http://127.0.0.1:5189/dev/status');
});

test.afterAll(async () => {
  if (fakeAgentProc) fakeAgentProc.kill();
  if (brokerProc) brokerProc.kill();
});

test('helper console renders frames and sends input against real frontend', async ({ page }) => {
  // Capture console errors
  const errors: string[] = [];
  page.on('console', msg => { if (msg.type() === 'error') errors.push(msg.text()); });

  // Helper: ignore expected benign errors (image load from tiny base64 may cause ERR_INVALID_URL in headless)
  function filteredErrors(arr: string[]) {
    return arr.filter(e => !(e.includes('ERR_INVALID_URL') || e.includes('Failed to load resource') || e.includes('Image load error')));
  }

  const FRONTEND_BASE = process.env.FRONTEND_BASE || 'http://localhost:3000';
  const RELAY_WS_BASE = process.env.RELAY_WS_BASE || 'ws://127.0.0.1:5189';
  const frontendUrl = `${FRONTEND_BASE.replace(/\/$/, '')}/console?id=testsession&techToken=testtoken&auto=1`;

  // Ensure frontend is up
  try {
    await waitForHttp(frontendUrl, 8000);
  } catch (e) {
    throw new Error(`Frontend not available at ${FRONTEND_BASE}. Start the Base44 frontend on ${FRONTEND_BASE} and retry.`);
  }

  // Inject a small init script to rewrite WebSocket URLs to point at our local relay if needed.
  await page.addInitScript({ script: `(() => {
    const orig = window.WebSocket;
    const relay = "${RELAY_WS_BASE}";
    function mapUrl(url) {
      try {
        const u = new URL(url, location.href);
        if (u.pathname.startsWith('/ws/') || url.includes('support.hebnersolutions.com') || u.pathname.includes('/ws/')) {
          return relay.replace(/\/$/, '') + u.pathname + u.search;
        }
        return url;
      } catch (e) {
        return url;
      }
    }
    function WS(url, protocols) {
      const mapped = mapUrl(url);
      if (protocols !== undefined) return new orig(mapped, protocols);
      return new orig(mapped);
    }
    WS.prototype = orig.prototype;
    WS.CONNECTING = orig.CONNECTING; WS.OPEN = orig.OPEN; WS.CLOSING = orig.CLOSING; WS.CLOSED = orig.CLOSED;
    window.WebSocket = WS;
  })();` });

  await page.goto(frontendUrl);
  await expect(page.locator('canvas')).toHaveCount(1);

  // Start fake agent to send frames
  fakeAgentProc = spawn('node', ['tools/fake-agent-send-frame.js', 'testsession', '5189', '3'], { stdio: 'inherit' });

  // Wait for broker to record frames
  let frames = 0;
  for (let i=0;i<30;i++){
    const r = await fetch('http://127.0.0.1:5189/dev/status?sessionId=testsession');
    const j = await r.json();
    frames = j.agentFrames ?? 0;
    if (frames > 0) break;
    await new Promise(r => setTimeout(r, 200));
  }
  expect(frames).toBeGreaterThan(0);

  // Ensure canvas has non-blank image
  const dataUrl = await page.evaluate(() => (document.querySelector('canvas') as HTMLCanvasElement).toDataURL());
  expect(typeof dataUrl).toBe('string');
  expect(dataUrl.length).toBeGreaterThan(200); // not blank

  // Simulate input: move and click
  const box = await page.locator('canvas').boundingBox();
  if (box) {
    await page.mouse.move(box.x + box.width/2, box.y + box.height/2);
    await page.mouse.click(box.x + box.width/2, box.y + box.height/2);
  }

  // Wait for helper messages count to increase
  let helperMsgs = 0;
  for (let i=0;i<30;i++){
    const r = await fetch('http://127.0.0.1:5189/dev/status?sessionId=testsession');
    const j = await r.json();
    helperMsgs = j.helperMessages ?? 0;
    if (helperMsgs > 0) break;
    await new Promise(r => setTimeout(r, 200));
  }
  expect(helperMsgs).toBeGreaterThan(0);

  // Filter benign errors and assert none remain
  const remaining = filteredErrors(errors);
  if (remaining.length > 0) console.log('Remaining console errors:', remaining);
  expect(remaining).toEqual([]);
});
