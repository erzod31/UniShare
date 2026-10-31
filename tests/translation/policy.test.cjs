const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { createHash } = require('node:crypto');
const root = path.resolve(__dirname, '../..');
const assets = path.join(root, 'shared/translation');
const policy = require(path.join(assets, 'policy.js'));
const catalog = JSON.parse(fs.readFileSync(path.join(assets, 'models.json')));

test('Same language does not load models; supported foreign text takes a direct or one-pivot route', () => {
  assert.deepEqual(policy.route('es','es',catalog),[]);
  assert.equal(policy.route('en','es',catalog).length,1);
  assert.deepEqual(policy.route('fr','es',catalog).map(p => p.source + '-' + p.target), ['fr-en','en-es']);
  assert.equal(policy.route('ko','es',catalog),null);
  assert.throws(() => policy.route('en','../other',catalog));
});

test('Links, social identifiers, line breaks, emoji and whitespace survive segmentation literally', () => {
  const text = '  Read this https://example.com/a?x=1&y=2 🌍 👩🏽‍💻 #Obsidian @someone\r\nMore text!\n';
  const segments = policy.segments(text);
  assert.equal(segments.map(s => s.text).join(''), text);
  for (const value of ['https://example.com/a?x=1&y=2','🌍','👩🏽‍💻','#Obsidian','@someone','\r\n','\n']) {
    assert.ok(segments.some(s => s.literal && s.text === value), value);
  }
  assert.ok(segments.some(s => !s.literal));
});

test('Empty, symbol-only and untrusted descriptions stay plain text and cannot select arbitrary resources', () => {
  for (const text of ['', '  ', '🌍 #tag @name https://example.com/']) {
    assert.ok(policy.segments(text).every(s => s.literal));
  }
  policy.validateRequest({id:'a-1',text:'</script><script>document.cookie</script>',target:'es'});
  assert.throws(() => policy.validateRequest({id:'../../other',text:'Hello',target:'es'}));
  assert.throws(() => policy.validateRequest({id:'a',text:'Hello',target:'es&key=other'}));
  assert.throws(() => policy.validateRequest({id:'a',text:'a'.repeat(100001),target:'es'}));
});

test('Bundled runtime assets match their pinned provenance', () => {
  const provenance = JSON.parse(fs.readFileSync(path.join(assets, 'vendor/provenance.json')));
  for (const [name,file] of Object.entries(provenance.files)) {
    const bytes = fs.readFileSync(path.join(assets,'vendor',name));
    assert.equal(bytes.length,file.size);
    assert.equal(createHash('sha256').update(bytes).digest('hex'),file.sha256);
  }
});

test('Catalog contains only complete pinned public models, bounded sizes and valid content hashes', () => {
  for (const pair of Object.values(catalog.pairs)) {
    assert.ok(pair.files.model);
    assert.ok(pair.files.vocab || (pair.files.srcvocab && pair.files.trgvocab));
    for (const file of Object.values(pair.files)) {
      assert.match(file.sha256,/^[a-f0-9]{64}$/);
      assert.match(file.decompressedSha256,/^[a-f0-9]{64}$/);
      assert.equal(new URL(file.url).host,'firefox-settings-attachments.cdn.mozilla.net');
      assert.equal(new URL(file.url).protocol,'https:');
      assert.ok(file.size>0 && file.size<=67108864 && file.decompressedSize<=134217728);
    }
  }
});

test('Language detection is local, independent of installed models and ignores link/tag noise', () => {
  const context = vm.createContext({});
  vm.runInContext(fs.readFileSync(path.join(assets,'vendor/language-detection.js'),'utf8'),context);
  const detect = context.UniShareLanguageDetection.detectLanguage;
  assert.equal(detect('This is a practical guide to organizing your personal library and saving files for offline use. https://ejemplo.es #español'),'en');
  assert.equal(detect('Esta es una guía práctica para organizar tu biblioteca personal y guardar archivos para usarlos sin conexión.'),'es');
  assert.equal(detect('Ce guide explique comment organiser votre bibliothèque personnelle et enregistrer des fichiers sans connexion Internet.'),'fr');
  assert.equal(detect('🌍 #hello https://example.com/'),'');
});
