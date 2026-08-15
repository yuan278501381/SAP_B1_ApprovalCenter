import { spawn } from 'child_process';
import http from 'http';
import fs from 'fs';

const edgePath = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
if (!fs.existsSync(edgePath)) {
  console.log('Edge not at default path, checking chrome...');
}

const args = [
  '--headless',
  '--remote-debugging-port=9222',
  '--disable-gpu',
  '--no-sandbox',
  'http://192.168.134.9:5000/'
];

console.log('Launching headless browser...');
const proc = spawn(edgePath, args);

proc.stderr.on('data', (d) => {
  // console.log('Edge stderr:', d.toString());
});

setTimeout(async () => {
  try {
    const listRes = await fetch('http://127.0.0.1:9222/json/list');
    const pages = await listRes.json();
    console.log('Open pages:', pages);
  } catch (err) {
    console.error('CDP connect error:', err.message);
  } finally {
    proc.kill();
  }
}, 3000);
