import { spawn } from 'child_process';

const edgePath = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const userDataDir = 'C:\\Users\\yuan2\\AppData\\Local\\Temp\\edge_debug_nav';

const args = [
  '--headless',
  '--incognito',
  '--remote-debugging-port=9225',
  `--user-data-dir=${userDataDir}`,
  '--disable-gpu',
  '--no-sandbox',
  'about:blank'
];

console.log('1. Starting Edge...');
const proc = spawn(edgePath, args);

setTimeout(async () => {
  try {
    const listRes = await fetch('http://127.0.0.1:9225/json/list');
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
      console.log('2. WebSocket open, enabling domains and navigating to app...');
      send('Runtime.enable');
      send('Log.enable');
      send('Console.enable');
      send('Network.enable');
      send('Page.enable');

      send('Page.navigate', { url: 'http://192.168.134.9:5000/' });
    };

    ws.onmessage = (event) => {
      const msg = JSON.parse(event.data.toString());
      if (msg.method === 'Runtime.consoleAPICalled') {
        console.log(`[Console ${msg.params.type}]`, msg.params.args.map(a => a.value || JSON.stringify(a)).join(' '));
      } else if (msg.method === 'Runtime.exceptionThrown') {
        console.error('>>> [RUNTIME EXCEPTION!]', msg.params.exceptionDetails.text, msg.params.exceptionDetails.exception?.description);
      } else if (msg.method === 'Network.responseReceived') {
        const { url, status, statusText } = msg.params.response;
        if (status >= 400) {
          console.error(`>>> [HTTP ${status} ${statusText}] ${url}`);
        } else {
          console.log(`[HTTP ${status}] ${url}`);
        }
      } else if (msg.method === 'Network.loadingFailed') {
        console.error(`>>> [NET FAILED] ${msg.params.errorText}`);
      } else if (msg.result && msg.result.result) {
        console.log('[EVAL RESULT]', msg.result.result.value);
      }
    };

    setTimeout(() => {
      console.log('3. Evaluating page DOM structure...');
      send('Runtime.evaluate', {
        expression: '`Title: ${document.title} | #app Children: ${document.getElementById("app") ? document.getElementById("app").children.length : 0} | HTML length: ${document.documentElement.outerHTML.length}`'
      });

      setTimeout(() => {
        ws.close();
        proc.kill();
      }, 2000);
    }, 4000);

  } catch (err) {
    console.error('Test error:', err);
    proc.kill();
  }
}, 2000);
