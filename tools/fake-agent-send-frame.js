// Fake agent: connects to ws://host/ws/agent/<session> and sends N frame messages then exits
// Usage: node tools/fake-agent-send-frame.js [session=testsession] [port=5189] [count=3]

const WebSocket = require('ws');
const session = process.argv[2] || 'testsession';
const port = process.argv[3] || '5189';
const count = parseInt(process.argv[4] || '3', 10);

// Tiny 1x1 JPEG base64 (very small)
const tinyJpegBase64 = "/9j/4AAQSkZJRgABAQAAAQABAAD/2wCEAAkGBxAPDxAQEA8QEA8PDw8PDw8ODw8PDw8PFREWFhURExUYHSggGBolHRUVITEhJSkrLi4uFx8zODMsNygtLisBCgoKDg0OGhAQGi0lHyUtLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLf/AABEIAAEAAQMBIgACEQEDEQH/xAAXAAEBAQEAAAAAAAAAAAAAAAAAAQID/8QAFgEBAQEAAAAAAAAAAAAAAAAAAAEH/2gAMAwEAAhADEAAAAf8A/8QAFhEBAQEAAAAAAAAAAAAAAAAAAAER/9oACAEBAAE/AFP/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oACAECAQE/AFP/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oACAEDAQE/AFP/2Q==";

const url = `ws://127.0.0.1:${port}/ws/agent/${encodeURIComponent(session)}`;
const ws = new WebSocket(url);

ws.on('open', () => {
  console.log('fake-agent connected to', url);
  for (let i = 0; i < count; i++) {
    const payload = { t: 'frame', fmt: 'jpg', data: tinyJpegBase64 };
    ws.send(JSON.stringify(payload));
    console.log('sent frame', i+1);
  }
  setTimeout(()=> ws.close(), 500);
});

ws.on('message', (m)=> console.log('msg from broker:', m.toString()));
ws.on('close', ()=> setTimeout(()=>process.exit(0), 200));
ws.on('error', (e)=> { console.error('ws error', e); process.exit(2); });
