const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const fixtures = JSON.parse(fs.readFileSync(path.join(__dirname, '../fixtures/origin-metadata.json'), 'utf8')).cases;
const extensionSource = process.env.UNISHARE_EXTENSION_SOURCE || path.join(__dirname, '../../integrations/chromium-extension');
const popup = fs.readFileSync(path.join(extensionSource, 'popup.js'), 'utf8');
fixtures.push({
  name: 'unicode-caption-limit-is-marked-and-title-is-not-split',
  url: 'https://www.tiktok.com/@creadora/video/7300000000000000001',
  html: ['<script id="SIGI_STATE">' + JSON.stringify({ ItemModule: {
    '7300000000000000001': { id: '7300000000000000001', desc: '🌊'.repeat(4100) },
  } }) + '</script>'],
  description: '🌊'.repeat(3999) + '…',
  expectedTitle: '🌊'.repeat(79) + '…',
});
const decode = text => text.replace(/&amp;/g, '&').replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"');
fixtures.push({
  name: 'structured-title-uses-the-same-unicode-boundary',
  url: 'https://example.com/video',
  html: ["<script type='application/ld+json'>" + JSON.stringify({ '@type': 'VideoObject', name: '🌊'.repeat(501), description: 'Pie original' }) + '</script>'],
  description: 'Pie original', expectedTitle: '🌊'.repeat(250),
});
const attributes = text => Object.fromEntries(Array.from(text.matchAll(/([\w:-]+)\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>]+))/g),
  match => [match[1].toLowerCase(), decode(match[2] ?? match[3] ?? match[4])]));

// A minimal DOM for structured-metadata unit tests; browser execution is checked separately.
function element(html = '') {
  const text = decode(html.replace(/<[^>]*>/g, ''));
  return {
    textContent: text, innerText: text, value: '', checked: false,
    addEventListener() {}, closest() { return null; }, remove() {},
    getClientRects() { return [{}]; },
    cloneNode() { return element(html); },
    querySelectorAll(selector) {
      if (selector === 'p' || selector === 'a') {
        const regex = selector === 'p' ? /<p\b[^>]*>([\s\S]*?)<\/p>/gi : /<a\b[^>]*>([\s\S]*?)<\/a>/gi;
        return Array.from(html.matchAll(regex), match => element(match[1]));
      }
      return [];
    },
  };
}

for (const fixture of fixtures) {
  test(fixture.name, () => {
    const html = fixture.html.join('\n');
    const scripts = Array.from(html.matchAll(/<script\b([^>]*)>([\s\S]*?)<\/script>/gi), match => {
      const attrs = attributes(match[1]);
      return { ...attrs, textContent: match[2], getAttribute: name => attrs[name] ?? null };
    });
    const metas = Array.from(html.matchAll(/<meta\b([^>]*)>/gi), match => attributes(match[1]));
    const documentTitle = decode(html.match(/<title\b[^>]*>([\s\S]*?)<\/title>/i)?.[1] || '');
    const body = element(html.replace(/<script\b[^>]*>[\s\S]*?<\/script>/gi, ''));
    const controls = new Map();
    const document = {
      title: documentTitle, scripts, body,
      getElementById: id => scripts.find(script => script.id === id) || null,
      querySelector(selector) {
        if (selector.startsWith('#')) {
          if (!controls.has(selector)) controls.set(selector, element());
          return controls.get(selector);
        }
        const property = selector.match(/\[(name|property)="([^"]+)"\]/);
        return property ? metas.find(meta => meta[property[1]] === property[2]) || null : null;
      },
      querySelectorAll(selector) {
        if (selector.startsWith('script')) return scripts.filter(script => script.type === 'application/ld+json');
        if (selector === 'article, main') return Array.from(html.matchAll(/<(?:article|main)\b[^>]*>([\s\S]*?)<\/(?:article|main)>/gi), match => element(match[1]));
        return [];
      },
    };
    const context = vm.createContext({
      document, location: new URL(fixture.url), URL, console,
      UniShareI18n: { initialize: async () => {}, t: text => text },
      chrome: { tabs: { query: async () => [{ title: documentTitle, url: fixture.url }] } },
    });
    vm.runInContext(popup, context, { timeout: 1000 });
    const metadata = context.capturePage(false);
    assert.equal(metadata.description || null, fixture.description);
    if ('author' in fixture) assert.equal(metadata.author, fixture.author);
    if ('expectedTitle' in fixture) assert.equal(metadata.originTitle, fixture.expectedTitle);
  });
}
