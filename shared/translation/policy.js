/* Shared local translation policy. No network or platform capabilities. */
(function (root) {
  'use strict';
  const VERSION = 'bergamot4-models3-policy1';
  function route(source, target, catalog) {
    if (!['es', 'en'].includes(target)) throw new Error('Unsupported target');
    if (!source) return null;
    if (source === target) return [];
    const direct = catalog.pairs[`${source}-${target}`];
    if (direct) return [direct];
    const first = catalog.pairs[`${source}-en`];
    const second = catalog.pairs[`en-${target}`];
    return first && second ? [first, second] : null;
  }
  function segments(text) {
    // URLs and social identifiers are literal. Never pass them through a language model.
    const protectedToken = /(https?:\/\/[^\s<>"']+|[#@][\p{L}\p{N}_]+|\p{Extended_Pictographic}(?:\uFE0F|\p{Emoji_Modifier}|\u200D\p{Extended_Pictographic})*|[\p{Regional_Indicator}]{2}|\r\n|[\r\n])/giu;
    const result = [];
    let start = 0;
    const prose = value => {
      const match = /^(\s*)([\s\S]*?)(\s*)$/u.exec(value);
      if (match[1]) result.push({ text:match[1], literal:true });
      if (match[2]) result.push({ text:match[2], literal: !/\p{L}/u.test(match[2]) });
      if (match[3]) result.push({ text:match[3], literal:true });
    };
    for (const match of text.matchAll(protectedToken)) {
      if (match.index > start) prose(text.slice(start, match.index));
      result.push({ text:match[0], literal:true });
      start = match.index + match[0].length;
    }
    if (start < text.length) prose(text.slice(start));
    return result;
  }
  function validateRequest(request) {
    if (!request || typeof request.id !== 'string' || !/^[a-zA-Z0-9-]{1,80}$/.test(request.id) ||
        typeof request.text !== 'string' || request.text.length > 100000 || !['es','en'].includes(request.target)) {
      throw new Error('Invalid translation request');
    }
  }
  const api = { VERSION, route, segments, validateRequest };
  root.UniShareTranslationPolicy = api;
  if (typeof module === 'object') module.exports = api;
})(globalThis);
