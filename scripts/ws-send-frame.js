// Simple Node script to simulate an agent sending a frame to helpers via BrokerStub
// Usage: node ws-send-frame.js <sessionId> <imageFile>

const WebSocket = require('ws');
const fs = require('fs');

const session = process.argv[2] || 'test-session';
const imgFile = process.argv[3] || 'test.jpg';

const ws = new WebSocket('ws://127.0.0.1:5189/ws/agent/' + session);

ws.on('open', async ()=>{
  console.log('connected, sending frame...');
  let img;
  try{ img = fs.readFileSync(imgFile); } catch(e){
    console.log('Unable to read image file', imgFile); img = Buffer.from('placeholder');
  }
  const payload = { t: 'frame', fmt: 'jpg', data: img.toString('base64') };
  ws.send(JSON.stringify(payload));
  console.log('frame sent');
  setTimeout(()=>ws.close(), 1000);
});

ws.on('message', (m)=> console.log('msg from broker:', m.toString()));
ws.on('close', ()=> console.log('closed'));
ws.on('error', (e)=> console.error('err', e));
