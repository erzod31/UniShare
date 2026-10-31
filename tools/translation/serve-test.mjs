// Local QA host for the shipped runtime. Only immutable, verified public models may be fetched.
import http from 'node:http';
import { readFile, writeFile, mkdir, rename } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const assets = path.join(root, 'shared/translation');
const cache = path.join(root, 'artifacts/qa-0.6.23/translation-models');
await mkdir(cache, { recursive:true });
const catalog = JSON.parse(await readFile(path.join(assets, 'models.json'), 'utf8'));
const files = new Map(Object.values(catalog.pairs).flatMap(p => Object.values(p.files)).map(f => [f.sha256,f]));
const inflight = new Map();
const sha = data => createHash('sha256').update(data).digest('hex');
async function model(hash) {
  const file = files.get(hash);
  if (!file) throw new Error('Unknown model');
  const cached = path.join(cache, hash);
  try { const data = await readFile(cached); if (data.length === file.size && sha(data) === hash) return data; } catch {}
  if (process.env.UNISHARE_TRANSLATION_TEST_OFFLINE === '1') throw new Error('Offline');
  if (!inflight.has(hash)) inflight.set(hash, (async () => {
    const response = await fetch(file.url);
    if (!response.ok) throw new Error(`Public model: ${response.status}`);
    const data = new Uint8Array(await response.arrayBuffer());
    if (data.length !== file.size || sha(data) !== hash) throw new Error('Model integrity');
    await writeFile(cached + '.partial', data);
    await rename(cached + '.partial', cached);
    console.log(`Verified model ${hash.slice(0,12)} (${data.length} bytes)`);
    return data;
  })().finally(() => inflight.delete(hash)));
  return inflight.get(hash);
}
const mime = { '.html':'text/html', '.js':'text/javascript', '.json':'application/json', '.wasm':'application/wasm' };
const server = http.createServer(async (req,res) => {
  try {
    const name = decodeURIComponent(new URL(req.url, 'http://localhost').pathname).replace(/^\//,'') || 'index.html';
    if (name.startsWith('models/')) {
      const hash = name.slice(7);
      if (!/^[a-f0-9]{64}$/.test(hash)) throw new Error('Invalid hash');
      res.setHeader('content-type','application/zstd'); res.end(await model(hash)); return;
    }
    const file = path.resolve(assets, name);
    if (!file.startsWith(assets + path.sep)) throw new Error('Invalid path');
    res.setHeader('content-type', mime[path.extname(file)] || 'application/octet-stream');
    res.end(await readFile(file));
  } catch { res.statusCode=503; res.end('Local translation asset unavailable'); }
});
server.listen(47839,'127.0.0.1',() => console.log('Local translation QA: http://127.0.0.1:47839/'));
