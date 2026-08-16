import { spawn } from 'child_process';
import fs from 'fs';

const edgePath = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const userDataDir = 'C:\\Users\\yuan2\\AppData\\Local\\Temp\\edge_debug_table_check';

const args = [
  '--headless',
  '--incognito',
  '--remote-debugging-port=9226',
  `--user-data-dir=${userDataDir}`,
  '--disable-gpu',
  '--no-sandbox',
  '--window-size=1920,1080',
  'about:blank'
];

const proc = spawn(edgePath, args);

setTimeout(async () => {
  try {
    const listRes = await fetch('http://127.0.0.1:9226/json/list');
    const pages = await listRes.json();
    const appPage = pages[0];

    const ws = new WebSocket(appPage.webSocketDebuggerUrl);
    let msgId = 1;
    const send = (method, params = {}) => {
      const id = msgId++;
      ws.send(JSON.stringify({ id, method, params }));
      return id;
    };

    ws.onopen = () => {
      send('Page.enable');
      send('Page.navigate', { url: 'http://192.168.134.9:5000/?companyId=DB_KCC&objectCode=CHORDR&objectKey=1001' });
    };

    ws.onmessage = async (event) => {
      const msg = JSON.parse(event.data.toString());
      if (msg.method === 'Page.loadEventFired') {
        setTimeout(() => {
          send('Page.captureScreenshot', { format: 'png' });
        }, 3000);
      } else if (msg.result && msg.result.data) {
        const buf = Buffer.from(msg.result.data, 'base64');
        const outPath = 'C:\\Users\\yuan2\\.gemini\antigravity\\brain\\29197658-c279-4c19-bd03-7c1aa2fc64bf\\table_fixed_view.png';
        fs.writeFileSync(outPath, buf);
        console.log('SCREENSHOT_SAVED:', outPath);
        ws.close();
        proc.kill();
        process.exit(0);
      }
    };
  } catch (err) {
    console.error(err);
    proc.kill();
    process.exit(1);
  }
}, 1500);
