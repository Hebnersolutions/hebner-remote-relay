using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using Hebner.Agent.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<JsonOptions>(o => o.SerializerOptions.PropertyNamingPolicy = null);
var app = builder.Build();

var commandQueue = new Dictionary<string, Queue<BrokerCommand>>();

// In-memory token store for dev: techToken -> (sessionId, expiresUtc)
var techTokens = new Dictionary<string, (string sessionId, DateTime expiresUtc)>();

// Connections tracked by sessionId
var helperConns = new Dictionary<string, List<System.Net.WebSockets.WebSocket>>();
var agentConns = new Dictionary<string, List<System.Net.WebSockets.WebSocket>>();

// Session stats for testing/verification
var sessionStats = new Dictionary<string, (int agentFrames, int helperMessages)>();

int SafeGetAgentFrames(string sid) => sessionStats.TryGetValue(sid, out var v) ? v.agentFrames : 0;
int SafeGetHelperMessages(string sid) => sessionStats.TryGetValue(sid, out var v) ? v.helperMessages : 0;
void IncrementAgentFrames(string sid)
{
    if (!sessionStats.ContainsKey(sid)) sessionStats[sid] = (0,0);
    var v = sessionStats[sid];
    v.agentFrames = v.agentFrames + 1;
    sessionStats[sid] = v;
}
void IncrementHelperMessages(string sid)
{
    if (!sessionStats.ContainsKey(sid)) sessionStats[sid] = (0,0);
    var v = sessionStats[sid];
    v.helperMessages = v.helperMessages + 1;
    sessionStats[sid] = v;
}

app.MapPost("/api/agent/heartbeat", (HeartbeatPayload hb) =>
{
    Console.WriteLine($"HB {hb.Device.DeviceId} {hb.State} monitors={hb.Monitors.Count} @ {hb.TimestampUtc}");
    return Results.Ok(new { ok = true });
});

app.MapGet("/api/agent/next-command", (string deviceId) =>
{
    if (commandQueue.TryGetValue(deviceId, out var q) && q.Count > 0)
        return Results.Ok(q.Dequeue());
    return Results.Ok(null);
});

app.MapPost("/api/agent/command-ack", (object ack) =>
{
    Console.WriteLine($"ACK {ack}");
    return Results.Ok(new { ok = true });
});

// Dev endpoint to enqueue a command
app.MapPost("/api/dev/enqueue", (string deviceId, BrokerCommand cmd) =>
{
    if (!commandQueue.ContainsKey(deviceId)) commandQueue[deviceId] = new Queue<BrokerCommand>();
    commandQueue[deviceId].Enqueue(cmd);
    return Results.Ok(new { ok = true });
});

// Issue a short-lived tech token for the session
app.MapPost("/api/sessions/{sessionId}/issue-tech-token", (string sessionId) =>
{
    var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace("=",""
        ).Replace('+','-').Replace('/','_');
    var expires = DateTime.UtcNow.AddMinutes(30);
    techTokens[token] = (sessionId, expires);
    Console.WriteLine($"Issued tech token for session {sessionId}: {Mask(token)} expires {expires:O}");
    return Results.Ok(new { techToken = token, expiresAtUtc = expires });
});

// Dev page to connect as helper for debugging (simple message dump)
app.MapGet("/dev/ws-test", async (HttpContext ctx) =>
{
    ctx.Response.ContentType = "text/html";
    await ctx.Response.WriteAsync(@"<!doctype html>
<html><body>
<h3>WS Test</h3>
SessionId: <input id=session size=30 value='test-session'/><br/>
Token: <input id=token size=60 value=''/><br/>
<button onclick='connect()'>Connect as helper</button>
<pre id=log style='height:400px;overflow:auto;border:1px solid #ccc'></pre>
<script>
let ws;
function log(m){document.getElementById('log').textContent += m + '\n';}
function connect(){
  const s=document.getElementById('session').value;
  const t=encodeURIComponent(document.getElementById('token').value);
  ws=new WebSocket((location.protocol==='https:'?'wss://':'ws://')+location.host+'/ws/helper/'+s+'?techToken='+t);
  ws.onopen = ()=>log('open');
  ws.onclose = ()=>log('closed');
  ws.onmessage = (ev)=>log('msg: '+ev.data);
  ws.onerror = (e)=>log('err');
}
</script>
</body></html>");
});

// Dev console page: a simple Console.js implementation (canvas + WS + input capture)
app.MapGet("/dev/console", async (HttpContext ctx) =>
{
    ctx.Response.ContentType = "text/html";
    await ctx.Response.WriteAsync(@"<!doctype html>
<html><head><meta charset='utf-8'><title>Dev Console</title></head><body style='font-family:sans-serif'>
<h2>Dev Helper Console</h2>
<div style='display:flex;gap:20px'>
  <div style='width:720px'>
    <div>Session ID: <input id=s size=24 value='test-session' /></div>
    <div>techToken: <input id=t size=48 /></div>
    <div style='margin-top:8px'>WS Status: <span id=status>Disconnected</span> <button id=btnConnect>Connect</button> <button id=btnReconnect>Reconnect</button></div>
    <div style='margin-top:8px'>
      <canvas id=can width=1024 height=768 style='border:1px solid #ccc; width:100%; height:480px; background:#000'></canvas>
    </div>
  </div>
  <div style='width:320px'>
    <div style='font-weight:bold'>Controls</div>
    <div style='margin-top:8px'>Monitors: <select id=mon></select> <button id=selMon>Select Monitor</button></div>
    <div style='margin-top:8px'><button id=reqControl>Request Control</button></div>
    <div style='margin-top:12px; font-weight:bold'>Log</div>
    <pre id=log style='height:420px;overflow:auto;border:1px solid #ccc'></pre>
  </div>
</div>

<script>
(function(){
  const statusEl = document.getElementById('status');
  const logEl = document.getElementById('log');
  const can = document.getElementById('can');
  const ctx = can.getContext('2d');
  let ws = null;
  let reconnectTimer = null;
  let backoff = 1000;
  let session = document.getElementById('s');
  let token = document.getElementById('t');

  function log(m){ logEl.textContent += m + '\n'; logEl.scrollTop = logEl.scrollHeight; }
  function setStatus(s){ statusEl.textContent = s; }

  function connect(){
    if (ws && (ws.readyState === WebSocket.OPEN || ws.readyState === WebSocket.CONNECTING)) return;
    const sid = encodeURIComponent(session.value);
    const tok = encodeURIComponent(token.value);
    const url = (location.protocol === 'https:' ? 'wss://' : 'ws://') + location.host + '/ws/helper/' + sid + '?techToken=' + tok;
    log('Connecting to ' + url);
    setStatus('Connecting');
    ws = new WebSocket(url);
    ws.onopen = () => { log('WS open'); setStatus('Connected'); backoff = 1000; };
    ws.onclose = () => { log('WS closed'); setStatus('Disconnected'); scheduleReconnect(); };
    ws.onerror = (e) => { log('WS error'); setStatus('Disconnected'); };
    ws.onmessage = (ev) => { handleMessage(ev.data); };
  }

  function scheduleReconnect(){
    if (reconnectTimer) return;
    setStatus('Reconnecting in ' + (backoff/1000) + 's');
    reconnectTimer = setTimeout(() => { reconnectTimer = null; connect(); backoff = Math.min(backoff*2, 30000); }, backoff);
  }

  document.getElementById('btnConnect').onclick = connect;
  document.getElementById('btnReconnect').onclick = () => { if (ws) try{ ws.close(); } catch{}; connect(); };

  // Simple monitor list
  const monSel = document.getElementById('mon');
  function setMonitors(list){ monSel.innerHTML = ''; list.forEach(m=>{ const opt = document.createElement('option'); opt.value=m.id; opt.textContent = m.name; monSel.appendChild(opt); }); }
  setMonitors([{id:1,name:'Monitor 1'},{id:2,name:'Monitor 2'}]);

  document.getElementById('selMon').onclick = ()=>{
    const id = monSel.value;
    log('Selecting monitor '+id);
    // Call existing Base44 hook if present
    if (window.sendSignalingMessage) try{ window.sendSignalingMessage({ type:'monitor_select', id: id }); } catch(e){}
    // Also send over WS
    sendWS({ t:'monitor_select', id: id });
  };

  document.getElementById('reqControl').onclick = ()=>{
    log('Requesting control');
    if (window.sendSignalingMessage) try{ window.sendSignalingMessage({ type:'permission_request', request:'control' }); } catch(e){}
    sendWS({ t:'permission_request', request:'control' });
  };

  function sendWS(obj){ if (!ws || ws.readyState!==WebSocket.OPEN) { log('WS not open; cannot send'); return; } ws.send(JSON.stringify(obj)); }

  function handleMessage(msg){
    // Expect JSON messages
    try{
      const obj = JSON.parse(msg);
      if (obj.t === 'frame' && obj.data){
        const img = new Image();
        img.onload = ()=>{ try{ ctx.drawImage(img,0,0,can.width,can.height); } catch(e){ log('Draw error '+e); } };
        img.onerror = ()=> { log('Image load error'); };
        img.src = 'data:image/'+(obj.fmt||'jpeg')+';base64,'+obj.data;
        return;
      }
      log('msg: ' + JSON.stringify(obj));
    } catch(e){ log('raw msg: '+msg); }
  }

  // Mouse and keyboard events
  can.addEventListener('mousemove', (ev)=>{
    const rect = can.getBoundingClientRect();
    const x = Math.round((ev.clientX - rect.left) * (can.width / rect.width));
    const y = Math.round((ev.clientY - rect.top) * (can.height / rect.height));
    sendWS({ t:'mouse', type:'move', x, y });
  });
  can.addEventListener('mousedown', (ev)=>{ const b = ev.button===0? 'left': ev.button===2? 'right':'middle'; sendWS({ t:'mouse', type:'down', btn:b }); });
  can.addEventListener('mouseup', (ev)=>{ const b = ev.button===0? 'left': ev.button===2? 'right':'middle'; sendWS({ t:'mouse', type:'up', btn:b }); });

  window.addEventListener('keydown', (ev)=>{ sendWS({ t:'kbd', type:'down', code: ev.code, key: ev.key }); });
  window.addEventListener('keyup', (ev)=>{ sendWS({ t:'kbd', type:'up', code: ev.code, key: ev.key }); });

  // Quick connect if query params present
  const qs = new URLSearchParams(location.search);
  if (qs.get('id')) session.value = qs.get('id');
  if (qs.get('techToken')) token.value = qs.get('techToken');
  if (qs.get('auto') === '1') connect();
})();
</script>
</body></html>");
});

// /console remnant removed — tests should use the real frontend at http://localhost:3000/console


// Accept websocket connections for helper
app.UseWebSockets();
app.Map("/ws/helper/{sessionId}", async (HttpContext ctx) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest)
    {
        ctx.Response.StatusCode = 400;
        return;
    }

    var sessionId = ctx.Request.RouteValues["sessionId"]?.ToString() ?? "";
    var token = ctx.Request.Query["techToken"].ToString();
    var headerToken = ctx.Request.Headers["X-Tech-Token"].ToString();
    // Allow either query token or header (for backwards compatibility)
    var provided = string.IsNullOrEmpty(token) ? headerToken : token;

    // Validate token: if stored and matches sessionId and not expired, or in dev allow any non-empty
    var ok = false;
    if (!string.IsNullOrEmpty(provided))
    {
        if (techTokens.TryGetValue(provided, out var info) && info.sessionId == sessionId && info.expiresUtc > DateTime.UtcNow)
        {
            ok = true;
        }
        else
        {
            // In dev: accept any non-empty token but log masked
            // Use environment variable or always allowed in this stub
            ok = true; // permissive for dev
        }
    }

    if (!ok)
    {
        ctx.Response.StatusCode = 401;
        await ctx.Response.WriteAsync("unauthorized");
        return;
    }

    var ws = await ctx.WebSockets.AcceptWebSocketAsync();
    if (!helperConns.ContainsKey(sessionId)) helperConns[sessionId] = new List<System.Net.WebSockets.WebSocket>();
    helperConns[sessionId].Add(ws);

    Console.WriteLine($"Helper connected for session {sessionId} token={Mask(provided)}");

    var buffer = new byte[64 * 1024];
    try
    {
        while (ws.State == System.Net.WebSockets.WebSocketState.Open)
        {
            var res = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            if (res.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) break;
            var msg = System.Text.Encoding.UTF8.GetString(buffer, 0, res.Count);
            Console.WriteLine($"[Helper {sessionId}] {msg}");
            IncrementHelperMessages(sessionId);

            // For now, just echo or forward to agents
            if (agentConns.TryGetValue(sessionId, out var agents))
            {
                foreach (var ag in agents.ToArray())
                {
                    if (ag.State == System.Net.WebSockets.WebSocketState.Open)
                    {
                        var bytes = System.Text.Encoding.UTF8.GetBytes(msg);
                        await ag.SendAsync(bytes, System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                }
            }
        }
    }
    catch (Exception ex){ Console.WriteLine(ex); }
    finally
    {
        helperConns[sessionId].Remove(ws);
        Console.WriteLine($"Helper disconnected for session {sessionId}");
        try { await ws.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); } catch { }
    }
});

// Accept websocket connections for agent (to forward frames)
app.Map("/ws/agent/{sessionId}", async (HttpContext ctx) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest)
    {
        ctx.Response.StatusCode = 400;
        return;
    }

    var sessionId = ctx.Request.RouteValues["sessionId"]?.ToString() ?? "";
    var ws = await ctx.WebSockets.AcceptWebSocketAsync();
    if (!agentConns.ContainsKey(sessionId)) agentConns[sessionId] = new List<System.Net.WebSockets.WebSocket>();
    agentConns[sessionId].Add(ws);

    Console.WriteLine($"Agent connected for session {sessionId}");

    var buffer = new byte[128 * 1024];
    try
    {
        while (ws.State == System.Net.WebSockets.WebSocketState.Open)
        {
            var res = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            if (res.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) break;
            var msg = System.Text.Encoding.UTF8.GetString(buffer, 0, res.Count);
            Console.WriteLine($"[Agent {sessionId}] {msg.Substring(0, Math.Min(200, msg.Length))}");

            // If this is a frame message, increment counter
            try
            {
                var obj = System.Text.Json.JsonDocument.Parse(msg);
                if (obj.RootElement.TryGetProperty("t", out var jt) && jt.GetString() == "frame")
                {
                    IncrementAgentFrames(sessionId);
                }
            }
            catch { }

            // forward frames to helpers
            if (helperConns.TryGetValue(sessionId, out var helpers))
            {
                foreach (var h in helpers.ToArray())
                {
                    if (h.State == System.Net.WebSockets.WebSocketState.Open)
                    {
                        var bytes = System.Text.Encoding.UTF8.GetBytes(msg);
                        await h.SendAsync(bytes, System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                }
            }
        }
    }
    catch (Exception ex){ Console.WriteLine(ex); }
    finally
    {
        agentConns[sessionId].Remove(ws);
        Console.WriteLine($"Agent disconnected for session {sessionId}");
        try { await ws.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); } catch { }
    }
});


// Dev status endpoint for tests
app.MapGet("/dev/status", (string? sessionId) =>
{
    if (string.IsNullOrEmpty(sessionId))
    {
        return Results.Ok(new { sessions = sessionStats });
    }
    if (sessionStats.TryGetValue(sessionId, out var v))
    {
        return Results.Ok(new { sessionId, agentFrames = v.agentFrames, helperMessages = v.helperMessages });
    }
    return Results.Ok(new { sessionId, agentFrames = 0, helperMessages = 0 });
});

string Mask(string s)
{
    if (string.IsNullOrEmpty(s)) return "(empty)";
    if (s.Length <= 8) return s;
    return s.Substring(0,4)+"-****-"+s.Substring(s.Length-4);
}

app.Run("http://127.0.0.1:5189");
