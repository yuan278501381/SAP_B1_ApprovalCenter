import { spawn } from 'child_process';

const edgePath = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const userDataDir = 'C:\\Users\\yuan2\\AppData\\Local\\Temp\\edge_debug_diag';

const args = [
  '--headless',
  '--incognito',
  '--remote-debugging-port=9228',
  `--user-data-dir=${userDataDir}`,
  '--disable-gpu',
  '--no-sandbox',
  'about:blank'
];

const proc = spawn(edgePath, args);

setTimeout(async () => {
  try {
    const listRes = await fetch('http://127.0.0.1:9228/json/list');
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
      await send('Page.navigate', { url: 'http://192.168.134.9:5000/?user=manager' });

      await new Promise(r => setTimeout(r, 2000));

      const evalRes = await send('Runtime.evaluate', {
        expression: `
          (async () => {
            const resp = await fetch('/api/v1/metadata/objects/CHORDR?companyId=DB_KCC');
            const data = await resp.json();
            return {
              childTableKeys: Object.keys(data.data.childTableFields || {}),
              ch3Fields: Object.keys(data.data.childTableFields?.['@CH_ORDR_3'] || {}),
              expenseCodeMeta: data.data.childTableFields?.['@CH_ORDR_3']?.['U_ExpenseCode'],
              rawMeta: data.data
            };
          })()
        `,
        awaitPromise: true,
        returnByValue: true
      });

      console.log('API METADATA RESULT:', JSON.stringify(evalRes.result.value, null, 2));
      ws.close();
      proc.kill();
    };
  } catch (err) {
    console.error('Error:', err);
    proc.kill();
  }
}, 2000);
