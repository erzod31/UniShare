import { build } from 'esbuild';
import { decompress } from 'fzstd';
import { createHash } from 'node:crypto';
import { readFile, writeFile, mkdir, copyFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const destination = path.join(root, 'shared/translation/vendor');
await mkdir(destination, { recursive: true });
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const engine = {
  url: 'https://firefox-settings-attachments.cdn.mozilla.net/main-workspace/translations-wasm-v2/a76f9efe-b185-4a7f-9c3b-5f5d216d12e6.zst',
  sha256: '327fcfc7b7e9d95f6fa0844ebf899cfc05469872ef02d3aef5783182839a6255',
  decompressedSha256: 'f38ef807636a7c994afedaab7ff8ffe0d590d21897f05139f747b80fd7bbe926',
  size: 1211955,
  decompressedSize: 4960506,
};
// Matching v0.6.0 glue and WASM, as shipped by Firefox 144 (engine registry version 4.0).
const revision = '5ce8ed1e2fceea5c633c63dae85538d3241a733f';
const engineSourceRevision = '1de4a085d3a7afb625c51a60aabb5ad298e4059f';
const glue = `https://raw.githubusercontent.com/mozilla-firefox/firefox/${revision}/toolkit/components/translations/bergamot-translator/bergamot-translator.js`;
const fetchBytes = async url => {
  const response = await fetch(url);
  if (!response.ok) throw new Error(`Cannot fetch public translation asset: ${response.status} (${url})`);
  return new Uint8Array(await response.arrayBuffer());
};
const compressed = await fetchBytes(engine.url);
if (compressed.length !== engine.size || sha(compressed) !== engine.sha256) throw new Error('Engine download integrity failed');
const wasm = decompress(compressed, new Uint8Array(engine.decompressedSize));
if (wasm.length !== engine.decompressedSize || sha(wasm) !== engine.decompressedSha256) throw new Error('Engine decompression integrity failed');
await writeFile(path.join(destination, 'bergamot-translator.wasm'), wasm);
const glueBytes = await fetchBytes(glue);
await writeFile(path.join(destination, 'bergamot-translator.js'), glueBytes);
await build({ entryPoints: [path.join(root, 'tools/translation/language-detection.mjs')], outfile: path.join(destination, 'language-detection.js'),
  bundle: true, minify: true, format: 'iife', globalName: 'UniShareLanguageDetection', target: 'es2020', legalComments: 'eof' });
const pkgRoot = path.join(root, 'tools/translation/node_modules');
await copyFile(path.join(pkgRoot, 'fzstd/umd/index.js'), path.join(destination, 'fzstd.js'));
await copyFile(path.join(pkgRoot, 'fzstd/LICENSE'), path.join(root, 'licenses/fzstd-MIT.txt'));
const francLicense = await fetchBytes('https://raw.githubusercontent.com/wooorm/franc/398af8b77088974aff4f6c9e4bfd548ea4fb261e/license');
const detectionLicenses = [Buffer.from(francLicense).toString('utf8')];
for (const name of ['trigram-utils', 'n-gram', 'collapse-white-space']) {
  detectionLicenses.push(`\n--- ${name} ---\n` + await readFile(path.join(pkgRoot, name, 'license'), 'utf8'));
}
await writeFile(path.join(root, 'licenses/franc-MIT.txt'), detectionLicenses.join('\n'));
const license = await fetchBytes(`https://raw.githubusercontent.com/mozilla-firefox/firefox/${revision}/LICENSE`);
await writeFile(path.join(root, 'licenses/Mozilla-MPL-2.0.txt'), license);
const thirdParty = [];
for (const name of ['LICENSE.md', 'src/3rd_party/CLI/LICENSE', 'src/3rd_party/cnpy/LICENSE', 'src/3rd_party/mio/LICENSE',
  'src/3rd_party/pathie-cpp/LICENSE', 'src/3rd_party/phf/LICENSE', 'src/3rd_party/spdlog/LICENSE',
  'src/3rd_party/yaml-cpp/LICENSE', 'src/3rd_party/zstr/LICENSE', 'src/3rd_party/zlib/README']) {
  const url = `https://raw.githubusercontent.com/mozilla/translations/${engineSourceRevision}/inference/marian-fork/${name}`;
  thirdParty.push(`--- ${url} ---\n` + Buffer.from(await fetchBytes(url)).toString('utf8'));
}
for (const [repository, ref, name] of [
  ['browsermt/ssplit-cpp', 'a311f9865ade34db1e8e080e6cc146f55dafb067', 'LICENSE.md'],
  ['browsermt/sentencepiece', 'ae41b7740d7006596bb9257e83340b2620db9d00', 'LICENSE'],
  ['kpu/intgemm', 'f7401513da71758dacce52fed1c7855549abee59', 'LICENSE'],
]) {
  const url = `https://raw.githubusercontent.com/${repository}/${ref}/${name}`;
  thirdParty.push(`--- ${url} ---\n` + Buffer.from(await fetchBytes(url)).toString('utf8'));
}
await writeFile(path.join(root, 'licenses/Bergamot-third-party.txt'),
  thirdParty.join('\n\n').split('\n').map(line => line.trimEnd()).join('\n').trimEnd() + '\n');
const files = {};
for (const name of ['bergamot-translator.js','bergamot-translator.wasm','language-detection.js','fzstd.js']) {
  const bytes = await readFile(path.join(destination, name));
  files[name] = { size: bytes.length, sha256: sha(bytes) };
}
await writeFile(path.join(destination, 'provenance.json'), JSON.stringify({ engineVersion:'4.0', modelVersion:'3.0', revision, engineSourceRevision, glue, engine,
  packages: { 'franc-min':'6.2.0', fzstd:'0.1.1', esbuild:'0.28.2' }, files }, null, 2) + '\n');
console.log(JSON.stringify({ generated: Object.keys(files), engineBytes: wasm.length }));
