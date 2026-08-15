import { spawn } from 'child_process';
import fs from 'fs';

const edgePath = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const userDataDir = 'C:\\Users\\yuan2\\AppData\\Local\\Temp\\edge_blank_debug';

const args = [
  '--headless',
  '--incognito',
  '--remote-debugging-port=9224',
  `--user-data-dir=${userDataDir}`,
  '--disable-gpu',
  '--no-sandbox',
  'http://192.168.134.9:5000/'
];

console.log('1. Starting Edge for blank screen debugging...');
const proc = spawn(edgePath, args);

setTimeout(async () => {
  try {
    const listRes = await fetch('http://127.0.0.1:9224/json/list');
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
      console.log('3. Enabling domains...');
      send('Network.enable');
      send('Runtime.enable');
      send('Log.enable');
      send('Console.enable');
      send('Page.enable');
    };

    ws.onmessage = (event) => {
      const msg = JSON.parse(event.data.toString());
      if (msg.method === 'Runtime.consoleAPICalled') {
        console.log(`[Browser Console ${msg.params.type}]`, msg.params.args.map(a => a.value || JSON.stringify(a)).join(' '));
      } else if (msg.method === 'Runtime.exceptionThrown') {
        console.error('>>> [BROWSER EXCEPTION!]', msg.params.exceptionDetails.text, msg.params.exceptionDetails.exception?.description || msg.params.exceptionDetails);
      } else if (msg.method === 'Network.responseReceived') {
        if (msg.params.response.status >= 400) {
          console.error(`>>> [HTTP ERROR ${msg.params.response.status}]`, msg.params.response.url);
        }
      }
    };

    setTimeout(() => {
      send('Runtime.evaluate', {
        expression: 'document.getElementById("app") ? document.getElementById("app").innerHTML : "NO #app ELEMENT"'
      });
      setTimeout(() => {
        ws.close();
        proc.kill();
      }, 2000);
    }, 3000);

  } catch (err) {
    console.error('Debug error:', err);
    proc.kill();
  }
}, 2500);
