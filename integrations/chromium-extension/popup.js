const title = document.querySelector('#title');
const collection = document.querySelector('#collection');
const saveOffline = document.querySelector('#saveOffline');
const save = document.querySelector('#save');
const status = document.querySelector('#status');
const form = document.querySelector('#captureForm');
let currentTab;

function capturePage(includeRendered) {
  const meta = (selector) => document.querySelector(selector)?.content?.trim() || '';
  const normalize = (value) => (value || '').replace(/\s+/g, ' ').trim();
  const boilerplate = (value) => {
    const upper = normalize(value).toUpperCase();
    const navigationFragments = [
      'AUTEURSRECHT', 'CONTACTCREATORS', 'ADVERTEREN', 'ONTWIKKELAARS', 'VOORWAARDEN',
      'PRIVACYBELEID', 'ZO WERKT YOUTUBE', 'NIEUWE FUNCTIES TESTEN', 'COPYRIGHT',
      'CONTACT US', 'ADVERTISE', 'DEVELOPERS', 'TERMS', 'PRIVACY POLICY',
      'DERECHOS DE AUTOR', 'CONTACTAR', 'CREADORES', 'PUBLICIDAD', 'DESARROLLADORES',
      'TÉRMINOS', 'PRIVACIDAD', 'CÓMO FUNCIONA YOUTUBE'
    ];
    return upper.includes('POLÍTICA DE COOKIES') || upper.includes('COOKIE POLICY') ||
      upper.includes('TODOS LOS DERECHOS RESERVADOS') || upper.includes('ALL RIGHTS RESERVED') ||
      upper.startsWith('ACEPTAR COOKIES') || upper.startsWith('INICIAR SESIÓN') ||
      upper.startsWith('SIGN IN') || navigationFragments.filter(fragment => upper.includes(fragment)).length >= 4 ||
      upper.includes('OVERPERSAUTEURSRECHTCONTACTCREATORS') ||
      upper.includes('ABOUTPRESSCOPYRIGHTCONTACTCREATORS');
  };
  const prose = (value) => {
    const letters = Array.from(value).filter(character => /\p{L}/u.test(character)).length;
    if (letters < 25) return false;
    const latinLetters = Array.from(value).filter(character => /[A-Za-zÀ-ɏ]/u.test(character)).length;
    if (latinLetters * 4 < letters * 3) return true;
    const whitespace = Array.from(value).filter(character => /\s/u.test(character)).length;
    const camelTransitions = (value.match(/(?<=[a-zà-ÿ])(?=[A-Z])/gu) || []).length;
    return whitespace >= 5 && whitespace * 18 >= value.length && camelTransitions <= 3;
  };
  const summarize = (paragraphs) => {
    const normalizedTitle = normalize(document.title).toUpperCase();
    const sentences = [];
    for (const paragraph of paragraphs) {
      for (const sourceLine of (paragraph || '').split(/\r?\n/u)) {
        let clean = normalize(sourceLine);
        if (document.title && clean.toUpperCase().startsWith(normalizedTitle)) {
          clean = clean.slice(document.title.length).replace(/^[\s\-–—:|]+/, '');
        }
        if (clean.length < 35 || boilerplate(clean) || !prose(clean) ||
            clean.toUpperCase() === normalizedTitle || /^https?:\/\//iu.test(clean)) continue;
        for (const sentence of clean.split(/(?<=[.!?…])\s+/u)) {
          const candidate = normalize(sentence);
          if (candidate.length >= 35 && prose(candidate) && candidate.toUpperCase() !== normalizedTitle) {
            sentences.push(candidate);
          }
        }
      }
    }
    let summary = '';
    for (const sentence of sentences.slice(0, 4)) {
      if (summary.length + (summary ? 1 : 0) + sentence.length > 600) {
        if (!summary) {
          const boundary = sentence.lastIndexOf(' ', 599);
          return `${sentence.slice(0, boundary > 80 ? boundary : 599).trimEnd()}…`;
        }
        break;
      }
      summary += `${summary ? ' ' : ''}${sentence}`;
      if (summary.length >= 260) break;
    }
    return summary;
  };
  const findArticleBody = (value) => {
    if (!value || typeof value !== 'object') return '';
    if (typeof value.articleBody === 'string' && value.articleBody.trim()) return value.articleBody;
    const children = Array.isArray(value) ? value : Object.values(value);
    for (const child of children) {
      const result = findArticleBody(child);
      if (result) return result;
    }
    return '';
  };
  const contentTypes = new Set([
    'Article', 'NewsArticle', 'BlogPosting', 'TechArticle', 'Report', 'ScholarlyArticle',
    'VideoObject', 'AudioObject', 'PodcastEpisode'
  ]);
  const findStructuredDescription = (value) => {
    if (!value || typeof value !== 'object') return '';
    const types = Array.isArray(value['@type']) ? value['@type'] : [value['@type']];
    if (types.some(type => contentTypes.has(type)) && typeof value.description === 'string') {
      return value.description;
    }
    const children = Array.isArray(value) ? value : Object.values(value);
    for (const child of children) {
      const result = findStructuredDescription(child);
      if (result) return result;
    }
    return '';
  };
  let structuredBody = '';
  let structuredDescription = '';
  for (const script of document.querySelectorAll('script[type="application/ld+json"]')) {
    try {
      const value = JSON.parse(script.textContent || '');
      structuredBody ||= findArticleBody(value);
      structuredDescription ||= findStructuredDescription(value);
      if (structuredBody && structuredDescription) break;
    } catch {
      // Se usa el cuerpo visible si un sitio publica JSON-LD inválido.
    }
  }
  let videoDescription = '';
  if (/(^|\.)youtube\.com$|^youtu\.be$/i.test(location.hostname) &&
      (/^\/watch$/i.test(location.pathname) || /^\/(?:shorts|live)\//i.test(location.pathname) ||
       /^youtu\.be$/i.test(location.hostname))) {
    for (const script of document.scripts) {
      const match = (script.textContent || '').match(/"shortDescription"\s*:\s*"((?:\\.|[^"\\])*)"/s);
      if (!match) continue;
      try {
        videoDescription = JSON.parse(`"${match[1]}"`);
        break;
      } catch {
        // Se continúa con los metadatos estructurados o el contenido visible.
      }
    }
  }
  const usefulParagraphs = (root) => Array.from(root?.querySelectorAll('p') || [])
    .filter(paragraph => !paragraph.closest('nav, aside, footer, header, form, dialog, menu'))
    .map(paragraph => {
      const text = normalize(paragraph.innerText || paragraph.textContent || '');
      const linked = Array.from(paragraph.querySelectorAll('a'))
        .reduce((total, link) => total + normalize(link.innerText || link.textContent || '').length, 0);
      return linked * 2 > text.length ? '' : text;
    })
    .filter(text => text.length >= 60 && !boilerplate(text));
  const fallbackText = (root) => {
    if (!root) return '';
    const copy = root.cloneNode(true);
    copy.querySelectorAll('nav, aside, footer, header, form, dialog, menu, script, style, noscript, iframe, object, embed, svg, canvas')
      .forEach(node => node.remove());
    return normalize(copy.textContent || '');
  };
  const semanticCandidates = Array.from(document.querySelectorAll('article, main'));
  const semanticRoot = semanticCandidates
    .map(node => {
      const paragraphScore = usefulParagraphs(node).reduce((sum, text) => sum + text.length, 0);
      return { node, score: paragraphScore || fallbackText(node).length };
    })
    .sort((left, right) => right.score - left.score)[0]?.node;
  const root = semanticRoot || document.body;
  const directDescription = structuredBody || videoDescription || structuredDescription;
  const paragraphs = directDescription ? [directDescription] : usefulParagraphs(root);
  if (!directDescription && semanticRoot && !paragraphs.some(value => value.length >= 60)) {
    paragraphs.push(fallbackText(semanticRoot));
  }
  const result = {
    originTitle: meta('meta[property="og:title"]') || meta('meta[name="twitter:title"]') || document.title,
    siteName: meta('meta[property="og:site_name"]') || meta('meta[name="application-name"]') || location.hostname,
    author: meta('meta[name="author"]') || meta('meta[property="article:author"]'),
    description: summarize(paragraphs),
  };
  if (!includeRendered) return result;
  const text = (root?.innerText || document.body?.innerText || '')
    .replace(/\n{3,}/g, '\n\n')
    .trim()
    .slice(0, 1_000_000);
  const images = [];
  let encodedCharacters = 0;
  for (const image of Array.from(root?.querySelectorAll('img') || [])) {
    if (images.length >= 12 || encodedCharacters >= 7_000_000) break;
    const rectangle = image.getBoundingClientRect();
    if (!image.complete || image.naturalWidth < 120 || image.naturalHeight < 80 ||
        rectangle.width < 40 || rectangle.height < 40) continue;
    try {
      const scale = Math.min(1, 1280 / image.naturalWidth, 1280 / image.naturalHeight);
      const canvas = document.createElement('canvas');
      canvas.width = Math.max(1, Math.round(image.naturalWidth * scale));
      canvas.height = Math.max(1, Math.round(image.naturalHeight * scale));
      canvas.getContext('2d', { alpha: false }).drawImage(image, 0, 0, canvas.width, canvas.height);
      const dataUrl = canvas.toDataURL('image/jpeg', 0.82);
      if (dataUrl.length > 2_000_000 || encodedCharacters + dataUrl.length > 8_000_000) continue;
      images.push({ dataUrl, alternativeText: (image.alt || '').trim().slice(0, 500) });
      encodedCharacters += dataUrl.length;
    } catch {
      // Algunos recursos de otro origen no permiten lectura por canvas; el texto se conserva igualmente.
    }
  }
  return {
    ...result,
    renderedText: text,
    images,
  };
}

async function initialize() {
  await UniShareI18n.initialize();
  [currentTab] = await chrome.tabs.query({ active: true, currentWindow: true });
  title.value = currentTab?.title || '';
  if (!/^https?:\/\//i.test(currentTab?.url || '')) {
    save.disabled = true;
    status.textContent = UniShareI18n.t('invalidTab');
  }
}

form.addEventListener('submit', async event => {
  event.preventDefault();
  save.disabled = true;
  status.textContent = saveOffline.checked ? UniShareI18n.t('preparingOffline') : UniShareI18n.t('saving');
  try {
    const { port = 47831, pairingKey = '' } = await chrome.storage.local.get(['port', 'pairingKey']);
    if (!pairingKey) throw new Error(UniShareI18n.t('configureKey'));
    let rendered = {};
    if (saveOffline.checked) {
      const execution = await chrome.scripting.executeScript({
        target: { tabId: currentTab.id },
        func: capturePage,
        args: [true],
      });
      rendered = execution?.[0]?.result || {};
      if (!rendered.renderedText) {
        throw new Error(UniShareI18n.t('noRenderedText'));
      }
    } else {
      const execution = await chrome.scripting.executeScript({
        target: { tabId: currentTab.id },
        func: capturePage,
        args: [false],
      });
      rendered = execution?.[0]?.result || {};
    }
    const enteredTitle = title.value.trim();
    const defaultTitle = (currentTab?.title || '').trim();
    const originTitle = (rendered.originTitle || '').trim();
    const finalTitle = enteredTitle && enteredTitle !== defaultTitle
      ? enteredTitle
      : originTitle || enteredTitle || new URL(currentTab.url).hostname;
    delete rendered.originTitle;
    const response = await fetch(`http://127.0.0.1:${port}/api/v1/capture`, {
      method: 'POST',
      headers: {
        'Authorization': `Bearer ${pairingKey}`,
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        url: currentTab.url,
        title: finalTitle,
        collection: collection.value.trim() || null,
        saveOffline: saveOffline.checked,
        ...rendered,
      })
    });
    if (!response.ok) {
      const problem = await response.json().catch(() => ({}));
      throw new Error(problem.detail || UniShareI18n.t('serverResponse', response.status));
    }
    status.textContent = saveOffline.checked
      ? UniShareI18n.t('savedOffline')
      : UniShareI18n.t('savedLink');
  } catch (error) {
    status.textContent = error.message || UniShareI18n.t('connectFailed');
    save.disabled = false;
  }
});

initialize().catch(error => {
  status.textContent = error.message;
  save.disabled = true;
});
