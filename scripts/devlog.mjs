// Отладка: принимает журнал приложения на ТВ (тот же протокол, что у Stash) и печатает его.
// POST /log — строки текста; POST /hello — JSON {app, version, model, tizen, user}.
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
  req.on('end', () => { console.log(req.url.startsWith('/hello') ? 'HELLO ' + b : b); res.end('ok'); });
}).listen(8778, '0.0.0.0', () => console.log('devlog :8778'));
