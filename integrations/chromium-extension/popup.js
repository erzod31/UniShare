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
  const captionText = value => {
    if (typeof value !== 'string') return '';
    const characters = Array.from(value.replace(/\r\n?/g, '\n').trim());
    return characters.length > 4000 ? characters.slice(0, 3999).join('') + '…' : characters.join('');
  };
  const limitField = value => {
    if (typeof value !== 'string') return '';
    const text = value.trim();
    const end = /[\uD800-\uDBFF]/.test(text[499] || '') ? 499 : 500;
    return text.length > 500 ? text.slice(0, end) : text;
  };
  const titleFromCaption = value => {
    if (typeof value !== 'string') return '';
    let text = value.replace(/\r\n?/g, '\n').trim().split('\n')[0].trim().replace(/\s+/gu, ' ');
    const tail = text.match(/\s+(?:#\S+|https?:\/\/\S+)(?:\s+(?:#\S+|https?:\/\/\S+))*\s*$/iu);
    if (tail && Array.from(text.slice(0, tail.index)).length >= 12 &&
        (tail[0].trim().split(' ').length > 1 || tail[0].includes('://'))) text = text.slice(0, tail.index);
    const characters = Array.from(text);
    if (characters.length <= 80) return text;
    let prefix = characters.slice(0, 79).join('');
    const boundary = prefix.lastIndexOf(' ');
    if (boundary > 0 && Array.from(prefix.slice(0, boundary)).length >= 40) prefix = prefix.slice(0, boundary);
    return prefix.trimEnd() + '…';
  };
  const parseJson = value => {
    if (typeof value !== 'string' || value.length > 2 * 1024 * 1024) return null;
    let depth = 0, quoted = false, escaped = false;
    for (const character of value) {
      if (quoted) {
        if (escaped) escaped = false;
        else if (character === '\\') escaped = true;
        else if (character === '"') quoted = false;
      } else if (character === '"') quoted = true;
      else if (character === '{' || character === '[') { if (++depth > 64) return null; }
      else if (character === '}' || character === ']') { if (--depth < 0) return null; }
    }
    if (quoted || depth !== 0) return null;
    try { return JSON.parse(value); } catch { return null; }
  };
  const pageUrl = new URL(location.href);
  const tikTokId = /(^|\.)tiktok\.com$/i.test(pageUrl.hostname.replace(/\.$/, '')) &&
    /^https?:$/.test(pageUrl.protocol) && !pageUrl.username && !pageUrl.password
    ? pageUrl.pathname.match(/^\/(?:@[^/]+\/(?:video|photo)|share\/video|embed(?:\/v2)?|player\/v1|v)\/([0-9]{10,25})(?:\.html)?\/?$/)?.[1] : null;
  let directPost = null;
  if (tikTokId) {
    for (const script of document.scripts) {
      const scriptId = script.id || script.getAttribute('id');
      if (!['__UNIVERSAL_DATA_FOR_REHYDRATION__', 'SIGI_STATE', 'sigi-persisted-data'].includes(scriptId)) continue;
      const state = parseJson(script.textContent || '');
      const posts = scriptId === '__UNIVERSAL_DATA_FOR_REHYDRATION__'
        ? [state?.__DEFAULT_SCOPE__?.['webapp.video-detail']?.itemInfo?.itemStruct,
          state?.__DEFAULT_SCOPE__?.['webapp.reflow.video.detail']?.itemInfo?.itemStruct]
        : [state?.ItemModule?.[tikTokId]];
      for (const post of posts) {
        if (post?.id !== tikTokId || typeof post.desc !== 'string') continue;
        const author = post.author ?? post.authorInfo;
        directPost = {
          caption: captionText(post.desc),
          title: limitField(post.title) || limitField(post.imagePost?.title),
          author: limitField(typeof author === 'string' ? author :
            typeof author?.nickname === 'string' ? author.nickname : author?.uniqueId),
        };
        break;
      }
      if (directPost) break;
    }
    if (!directPost) {
      const visible = Array.from(document.querySelectorAll('[data-e2e="browse-video-desc"], [data-e2e="video-desc"], [data-e2e="photo-desc"]'))
        .filter(node => node.getClientRects().length && !node.closest('nav, aside, footer, dialog'));
      if (visible.length === 1) directPost = { caption: captionText(visible[0].innerText || visible[0].textContent), author: '' };
    }
  }
  const normalizeUrl = value => {
    const url = new URL(value, pageUrl);
    url.hash = '';
    url.pathname = url.pathname.replace(/\/+$/, '') || '/';
    url.search = url.search.slice(1).split('&').filter(pair => {
      const name = decodeURIComponent(pair.split('=')[0].replace(/\+/g, ' ')).toLowerCase();
      return pair && !name.startsWith('utm_') && !['fbclid', 'gclid', 'mc_cid', 'mc_eid'].includes(name);
    }).join('&');
    return url.href;
  };
  const matchesContent = identity => {
    if (!identity) return true;
    try {
      const url = new URL(identity, pageUrl);
      return /^https?:$/.test(url.protocol) && normalizeUrl(url) === normalizeUrl(pageUrl);
    } catch { return false; }
  };
  const articleTypes = new Set(['article', 'newsarticle', 'blogposting', 'techarticle', 'report', 'scholarlyarticle']);
  const captionTypes = new Set(['videoobject', 'audioobject', 'podcastepisode', 'imageobject', 'photograph', 'socialmediaposting', 'discussionforumposting']);
  const articleBodies = [], descriptions = [], titles = [];
  const visitStructured = value => {
    if (!value || typeof value !== 'object') return;
    if (!Array.isArray(value)) {
      const types = (Array.isArray(value['@type']) ? value['@type'] : [value['@type']])
        .filter(type => typeof type === 'string').map(type => type.replace(/^https?:\/\/schema\.org\//, '').toLowerCase());
      const isCaption = types.some(type => captionTypes.has(type));
      const isArticle = types.some(type => articleTypes.has(type));
      const identity = ['mainEntityOfPage', 'url', '@id'].map(key => typeof value[key] === 'string' ? value[key] : value[key]?.['@id'])
        .find(candidate => typeof candidate === 'string' && candidate.trim());
      if ((isCaption || isArticle) && matchesContent(identity)) {
        const heading = ['headline', 'name'].map(key => value[key]).find(candidate => typeof candidate === 'string' && candidate.trim());
        if (heading) titles.push({ text: normalize(heading), hasIdentity: !!identity });
        if (isArticle && !isCaption && typeof value.articleBody === 'string' && value.articleBody.trim()) {
          articleBodies.push({ text: value.articleBody, hasIdentity: !!identity });
        }
        const keys = isCaption ? ['caption', 'description', 'text', 'articleBody'] : ['description'];
        const text = keys.map(key => value[key]).find(candidate => typeof candidate === 'string' && candidate.trim());
        if (text) descriptions.push({ text, isCaption, hasIdentity: !!identity });
      }
    }
    for (const child of Array.isArray(value) ? value : Object.values(value)) visitStructured(child);
  };
  for (const script of document.querySelectorAll('script[type="application/ld+json"]')) {
    visitStructured(parseJson(script.textContent || ''));
  }
  const selectContent = candidates => {
    if (!candidates.length) return null;
    const hasIdentity = candidates.some(value => value.hasIdentity);
    const best = candidates.filter(value => value.hasIdentity === hasIdentity);
    return new Set(best.map(value => value.text)).size === 1 ? best[0] : null;
  };
  const structuredBody = selectContent(articleBodies)?.text || '';
  const structuredDescription = selectContent(descriptions);
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
  const paragraphs = usefulParagraphs(root);
  if (semanticRoot && !paragraphs.some(value => value.length >= 60)) {
    paragraphs.push(fallbackText(semanticRoot));
  }
  const result = {
    originTitle: limitField(directPost?.title || titleFromCaption(directPost?.caption) || selectContent(titles)?.text || meta('meta[property="og:title"]') || meta('meta[name="twitter:title"]') || document.title),
    siteName: directPost ? 'TikTok' : meta('meta[property="og:site_name"]') || meta('meta[name="application-name"]') || location.hostname,
    author: directPost?.author || meta('meta[name="author"]') || meta('meta[property="article:author"]'),
    description: directPost ? directPost.caption : summarize(structuredBody ? [structuredBody] : []) ||
      summarize(videoDescription ? [videoDescription] : []) ||
      (structuredDescription?.isCaption ? captionText(structuredDescription.text) : summarize(structuredDescription ? [structuredDescription.text] : [])) ||
      (tikTokId ? '' : summarize(paragraphs)),
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
