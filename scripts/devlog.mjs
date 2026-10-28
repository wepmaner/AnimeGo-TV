// Отладка: принимает строки от приложения на ТВ (Debug-сборка) и печатает их.
// /wire — печатает сырые заголовки пришедшего запроса (проверка, что ТВ шлёт их байт в байт).
import http from 'node:http';
http.createServer((req, res) => {
  if (req.url.startsWith('/wire')) {
    console.log('WIRE ' + req.method + ' ' + req.url);
    for (let i = 0; i < req.rawHeaders.length; i += 2) console.log('  ' + req.rawHeaders[i] + ': ' + req.rawHeaders[i + 1]);
    res.end('{}');
    return;
  }
  let b = '';
  req.on('data', c => b += c);
  req.on('end', () => { console.log(b); res.end('ok'); });
}).listen(8778, '0.0.0.0', () => console.log('devlog :8778'));
