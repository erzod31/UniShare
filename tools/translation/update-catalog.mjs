// Explicit maintenance operation: review and commit the resulting catalog before distributing.
import { writeFile } from 'node:fs/promises';
const response = await fetch('https://firefox.settings.services.mozilla.com/v1/buckets/main/collections/translations-models-v2/records');
if (!response.ok) throw new Error(`Model registry: ${response.status}`);
const records = (await response.json()).data.filter(r => r.version === '3.0' && !r.filter_expression &&
  (r.sourceLanguage === 'en' || r.targetLanguage === 'en'));
const pairs = {};
for (const r of records.filter(r => r.fileType === 'model')) {
  const key = `${r.sourceLanguage}-${r.targetLanguage}`;
  const files = {};
  for (const file of records.filter(f => f.sourceLanguage === r.sourceLanguage && f.targetLanguage === r.targetLanguage && f.architecture === r.architecture)) {
    files[file.fileType] = { name:file.name, sha256:file.attachment.hash, size:file.attachment.size,
      url:`https://firefox-settings-attachments.cdn.mozilla.net/${file.attachment.location}`,
      decompressedSha256:file.decompressedHash, decompressedSize:file.decompressedSize };
  }
  if (!files.model || (!files.vocab && (!files.srcvocab || !files.trgvocab))) continue;
  const rank = { 'base-memory':3, base:2, tiny:1 };
  if (!pairs[key] || rank[r.architecture] > rank[pairs[key].architecture]) {
    pairs[key] = { source:r.sourceLanguage, target:r.targetLanguage, architecture:r.architecture, files };
  }
}
await writeFile(new URL('../../shared/translation/models.json', import.meta.url), JSON.stringify({
  version:'mozilla-v3-2026-10-26', pairs:Object.fromEntries(Object.entries(pairs).sort(([a],[b]) => a.localeCompare(b)))
}, null, 2) + '\n');
console.log(`Pinned ${Object.keys(pairs).length} complete language pairs`);
