/* UniShare's local-only Bergamot adapter. Runs inference away from the UI thread. */
importScripts('vendor/bergamot-translator.js', 'vendor/fzstd.js', 'vendor/language-detection.js', 'policy.js');
let enginePromise;
let activeRoute = '';
let loaded = [];
let service;
let queue = Promise.resolve();
const catalogPromise = fetch('models.json').then(r => { if (!r.ok) throw new Error('Catalog unavailable'); return r.json(); });
const hex = bytes => Array.from(new Uint8Array(bytes), n => n.toString(16).padStart(2, '0')).join('');
async function hash(bytes) { return hex(await crypto.subtle.digest('SHA-256', bytes)); }
async function engine() {
  if (!enginePromise) {
    enginePromise = (async () => {
      const response = await fetch('vendor/bergamot-translator.wasm');
      if (!response.ok) throw new Error('Engine unavailable');
      const wasmBinary = await response.arrayBuffer();
      return new Promise((resolve, reject) => {
        const instance = loadBergamot({ wasmBinary, INITIAL_MEMORY:41943040,
          print:() => {}, printErr:() => {}, onAbort:() => reject(new Error('Engine unavailable')),
          onRuntimeInitialized:() => Promise.resolve().then(() => resolve(instance)) });
      });
    })().catch(error => { enginePromise = undefined; throw error; });
  }
  return enginePromise;
}
async function modelFile(file) {
  // The platform serves only the immutable catalog hashes and caches verified downloads locally.
  const response = await fetch(`models/${file.sha256}`);
  if (!response.ok) throw new Error('Model unavailable');
  const compressed = new Uint8Array(await response.arrayBuffer());
  if (compressed.length !== file.size || await hash(compressed) !== file.sha256) throw new Error('Model integrity');
  if (file.decompressedSize < 1 || file.decompressedSize > 134217728) throw new Error('Model size');
  const bytes = fzstd.decompress(compressed, new Uint8Array(file.decompressedSize));
  if (bytes.length !== file.decompressedSize || await hash(bytes) !== file.decompressedSha256) throw new Error('Model integrity');
  return bytes;
}
function releaseModels() {
  for (const entry of loaded) {
    entry.model.delete();
    entry.vocabulary.delete();
    for (const value of Object.values(entry.memory)) value.delete();
  }
  loaded = [];
  activeRoute = '';
}
async function prepare(route, request) {
  const key = route.map(r => `${r.source}-${r.target}`).join('/');
  const bergamot = await engine();
  if (key === activeRoute) return bergamot;
  releaseModels();
  if (!service) service = new bergamot.BlockingService({ cacheSize:0 });
  try {
    for (const pair of route) {
      const memory = {};
      const vocabulary = new bergamot.AlignedMemoryList();
      const entry = { memory, vocabulary, model:null };
      // Keep partial allocations available for cleanup if download or construction fails.
      loaded.push(entry);
      for (const [type, file] of Object.entries(pair.files)) {
        postMessage({ id:request.id, type:'status', status:'preparing', source:pair.source, target:pair.target });
        const bytes = await modelFile(file);
        const aligned = new bergamot.AlignedMemory(bytes.length, type === 'model' ? 256 : 64);
        aligned.getByteArrayView().set(bytes);
        memory[type] = aligned;
      }
      if (memory.vocab) vocabulary.push_back(memory.vocab);
      else { vocabulary.push_back(memory.srcvocab); vocabulary.push_back(memory.trgvocab); }
      const precision = pair.files.model.name.endsWith('intgemm8.bin') ? 'int8shiftAll' : 'int8shiftAlphaAll';
      const config = `beam-size: 1\nnormalize: 1.0\nword-penalty: 0\nmax-length-break: 128\nmini-batch-words: 1024\nworkspace: 128\nmax-length-factor: 2.0\nskip-cost: true\ncpu-threads: 0\nquiet: true\nquiet-translation: true\ngemm-precision: ${precision}\nalignment: soft\n`;
      entry.model = new bergamot.TranslationModel(pair.source, pair.target, config,
        memory.model, memory.lex || null, vocabulary, null);
    }
    activeRoute = key;
    return bergamot;
  } catch (error) {
    for (const entry of loaded) {
      entry.model?.delete(); entry.vocabulary.delete();
      for (const value of Object.values(entry.memory)) value.delete();
    }
    loaded = []; activeRoute = '';
    throw error;
  }
}
async function translate(request) {
  UniShareTranslationPolicy.validateRequest(request);
  const source = UniShareLanguageDetection.detectLanguage(request.text);
  const route = UniShareTranslationPolicy.route(source, request.target, await catalogPromise);
  if (!request.text.trim() || route?.length === 0) return { status:'original', text:request.text, source };
  if (!route) return { status:'unsupported', text:request.text, source };
  const parts = UniShareTranslationPolicy.segments(request.text);
  if (!parts.some(p => !p.literal)) return { status:'original', text:request.text, source };
  const bergamot = await prepare(route, request);
  postMessage({ id:request.id, type:'status', status:'translating', source, target:request.target });
  // Bergamot splits long sentences internally at max-length-break; keep literal spans separate.
  const messages = new bergamot.VectorString();
  const options = new bergamot.VectorResponseOptions();
  let responses;
  try {
    for (const part of parts.filter(p => !p.literal)) {
      messages.push_back(part.text);
      options.push_back({ qualityScores:false, alignment:false, html:false });
    }
    responses = route.length === 1
      ? service.translate(loaded[0].model, messages, options)
      : service.translateViaPivoting(loaded[0].model, loaded[1].model, messages, options);
    let index = 0;
    const text = parts.map(p => {
      if (p.literal) return p.text;
      const response = responses.get(index++);
      try { return response.getTranslatedText(); } finally { response.delete(); }
    }).join('');
    if (!text.trim() || text.length > 400000) throw new Error('Invalid translation');
    return { status:'translated', text, source };
  } finally { responses?.delete(); options.delete(); messages.delete(); }
}
onmessage = ({ data }) => {
  queue = queue.then(async () => {
    try { postMessage({ id:data.id, type:'result', target:data.target, ...await translate(data) }); }
    catch { postMessage({ id:data?.id, type:'result', status:'unavailable', target:data?.target, text:data?.text || '', source:'' }); }
  });
};
