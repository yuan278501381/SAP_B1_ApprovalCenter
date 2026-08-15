import http from 'http';
import { exec } from 'child_process';

console.log('Testing page availability...');

const req = http.get('http://192.168.134.9:5000/', (res) => {
  console.log('Status Code:', res.statusCode);
  let data = '';
  res.on('data', chunk => data += chunk);
  res.on('end', () => {
    console.log('HTML Length:', data.length);
    console.log('HTML snippet:', data.substring(0, 300));
  });
});

req.on('error', (err) => {
  console.error('Request failed:', err.message);
});
