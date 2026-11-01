// Static server for a published WASM client (no compression, no caching headers): node serve.cjs <dir> <port>
// The load and frame scripts point a headless Chromium at it. Like the deploy, it serves the .br or .gz file the publish wrote when the browser asks.
const http = require('http');
const fs = require('fs');
const path = require('path');

const root = path.resolve(process.argv[2] || '.');
const port = Number(process.argv[3] || 5080);
const types = {
  '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json',
  '.wasm': 'application/wasm', '.dll': 'application/octet-stream', '.png': 'image/png', '.wav': 'audio/wav', '.ogg': 'audio/ogg',
  '.tmx': 'application/xml', '.ico': 'image/x-icon', '.svg': 'image/svg+xml', '.txt': 'text/plain', '.woff2': 'font/woff2', '.pdb': 'application/octet-stream',
};

http.createServer((req, res) => {
  const urlPath = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  let file = path.join(root, urlPath.endsWith('/') ? urlPath + 'index.html' : urlPath);
  if (!file.startsWith(root) || !fs.existsSync(file) || fs.statSync(file).isDirectory()) {
    file = path.join(root, 'index.html');
  }
  // Serve the precompressed sibling (the publish writes .br and .gz next to each file) when the browser accepts it, as the deploy does.
  const accept = String(req.headers['accept-encoding'] || '');
  const headers = { 'Content-Type': types[path.extname(file)] || 'application/octet-stream', 'Cache-Control': 'no-store', 'Vary': 'Accept-Encoding' };
  for (const [token, ext] of [['br', '.br'], ['gzip', '.gz']]) {
    if (accept.includes(token) && fs.existsSync(file + ext)) {
      headers['Content-Encoding'] = token;
      file = file + ext;
      break;
    }
  }
  res.writeHead(200, headers);
  fs.createReadStream(file).pipe(res);
}).listen(port, () => console.log(`serving ${root} on :${port}`));
