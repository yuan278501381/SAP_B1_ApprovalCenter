import { spawn } from 'child_process';
import fs from 'fs';

const edgePath = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const userDataDir = 'C:\\Users\\yuan2\\AppData\\Local\\Temp\\edge_perf_profile';

const args = [
  '--headless',
  '--incognito',
  '--remote-debugging-port=9223',
  `--user-data-dir=${userDataDir}`,
  '--disable-gpu',
  '--no-sandbox',
  'http://192.168.134.9:5000/'
];

console.log('1. Starting Edge...');
const proc = spawn(edgePath, args);

setTimeout(async () => {
  try {
    const listRes = await fetch('http://127.0.0.1:9223/json/list');
    const pages = await listRes.json();
    const appPage = pages.find(p => p.url.includes('192.168.134.9:5000') || p.title.includes('approval-web'));
    
    if (!appPage) {
      console.error('App page not found in:', pages);
      proc.kill();
      return;
    }

    console.log('2. Connected to page:', appPage.title, appPage.webSocketDebuggerUrl);
    const ws = new WebSocket(appPage.webSocketDebuggerUrl);

    let msgId = 1;
    const send = (method, params = {}) => {
      ws.send(JSON.stringify({ id: msgId++, method, params }));
    };

    ws.onopen = () => {
      console.log('3. WebSocket open, clearing browser cache and enabling runtime...');
      send('Network.enable');
      send('Network.clearBrowserCache');
      send('Network.setCacheDisabled', { cacheDisabled: true });
      send('Page.enable');
      send('Page.reload', { ignoreCache: true });
      send('Runtime.enable');
      send('Log.enable');
      send('Console.enable');
    };

    ws.onmessage = (event) => {
      const msg = JSON.parse(event.data.toString());
      if (msg.method === 'Runtime.consoleAPICalled') {
        console.log(`[Browser Console ${msg.params.type}]`, msg.params.args.map(a => a.value || a.description).join(' '));
      } else if (msg.method === 'Runtime.exceptionThrown') {
        console.error('[Browser EXCEPTION!]', msg.params.exceptionDetails.text, msg.params.exceptionDetails.exception?.description);
      }
    };

    // 4 秒后开始模拟按 J / K
    setTimeout(() => {
      console.log('4. Dispatching KeyDown "j"...');
      send('Input.dispatchKeyEvent', { type: 'keyDown', key: 'j', code: 'KeyJ', windowsVirtualKeyCode: 74 });
      send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'j', code: 'KeyJ', windowsVirtualKeyCode: 74 });

      setTimeout(() => {
        console.log('5. Dispatching KeyDown "k"...');
        send('Input.dispatchKeyEvent', { type: 'keyDown', key: 'k', code: 'KeyK', windowsVirtualKeyCode: 75 });
        send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'k', code: 'KeyK', windowsVirtualKeyCode: 75 });

        setTimeout(() => {
          console.log('6. Diagnostic test finished.');
          ws.close();
          proc.kill();
        }, 3000);
      }, 2000);
    }, 4000);

  } catch (err) {
    console.error('Diagnostic error:', err);
    proc.kill();
  }
}, 2500);
