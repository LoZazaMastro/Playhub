import fs from 'node:fs/promises';
const [mode, file] = process.argv.slice(2);
const targets = await (await fetch('http://127.0.0.1:8080/json')).json();
const target = targets.find(t => mode === 'screenshot' ? t.url.includes('browserType=3') : t.title === 'SharedJSContext');
if (!target) throw new Error('Required Steam target is unavailable');
const ws = new WebSocket(target.webSocketDebuggerUrl);
await new Promise((resolve, reject) => { ws.onopen = resolve; ws.onerror = reject; });
let timeout;
const pending = new Promise((resolve, reject) => {
  timeout = setTimeout(() => reject(new Error('CDP timeout')), 30000);
  ws.onmessage = event => { const message = JSON.parse(event.data); if (message.id === 1) resolve(message); };
  ws.onerror = reject;
});
const params = mode === 'screenshot' ? {format:'png'} : {expression:await fs.readFile(file,'utf8'),awaitPromise:true,returnByValue:true};
ws.send(JSON.stringify({id:1,method:mode === 'screenshot'?'Page.captureScreenshot':'Runtime.evaluate',params}));
try {
  const reply = await pending;
  if (reply.error || reply.result?.exceptionDetails) throw new Error(JSON.stringify(reply.error || reply.result.exceptionDetails));
  if (mode === 'screenshot') { await fs.writeFile(file, Buffer.from(reply.result.data,'base64')); console.log('Screenshot saved'); }
  else console.log(JSON.stringify(reply.result));
} finally {clearTimeout(timeout);ws.close();}
