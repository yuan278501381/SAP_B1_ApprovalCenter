import { spawn } from 'child_process';
import fs from 'fs';

const edgePath = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const userDataDir = 'C:\\Users\\yuan2\\AppData\\Local\\Temp\\edge_debug_exd';

const args = [
  '--headless',
  '--incognito',
  '--remote-debugging-port=9227',
  `--user-data-dir=${userDataDir}`,
  '--disable-gpu',
  '--no-sandbox',
  '--window-size=1920,1080',
  'about:blank'
];

const proc = spawn(edgePath, args);

setTimeout(async () => {
  try {
    const listRes = await fetch('http://127.0.0.1:9227/json/list');
    const pages = await listRes.json();
    const appPage = pages[0];

    const ws = new WebSocket(appPage.webSocketDebuggerUrl);
    let msgId = 1;
    const callbacks = new Map();

    const send = (method, params = {}) => {
      return new Promise((resolve) => {
        const id = msgId++;
        callbacks.set(id, resolve);
        ws.send(JSON.stringify({ id, method, params }));
      });
    };

    ws.onmessage = (event) => {
      const msg = JSON.parse(event.data.toString());
      if (msg.id && callbacks.has(msg.id)) {
        callbacks.get(msg.id)(msg.result);
        callbacks.delete(msg.id);
      }
    };

    ws.onopen = async () => {
      await send('Page.enable');
      await send('Runtime.enable');
      await send('Emulation.setDeviceMetricsOverride', {
        width: 1920,
        height: 1080,
        deviceScaleFactor: 1,
        mobile: false
      });

      await send('Page.navigate', { url: 'http://192.168.134.9:5000/?user=manager' });
      await new Promise(r => setTimeout(r, 2000));

      // 点击第一条任务
      await send('Runtime.evaluate', {
        expression: `
          const card = document.querySelector('.compact-card') || document.querySelector('.workbench-table tbody tr');
          if (card) card.click();
        `
      });

      await new Promise(r => setTimeout(r, 2500));

      const shot = await send('Page.captureScreenshot', { format: 'png' });
      fs.writeFileSync('C:/Users/yuan2/.gemini/antigravity/brain/29197658-c279-4c19-bd03-7c1aa2fc64bf/live_drawer_view.png', Buffer.from(shot.data, 'base64'));
      console.log('Captured full desktop screenshot!');
      ws.close();
      proc.kill();
    };
  } catch (err) {
    console.error('Error:', err);
    proc.kill();
  }
}, 2000);
