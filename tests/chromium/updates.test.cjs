const test = require('node:test');
const assert = require('node:assert/strict');
const updates = require('../../integrations/chromium-extension/updates.js');
function runtime(result = { status: 'no_update' }) {
  return { checks: 0, reloads: 0,
    getManifest() { return { version: '0.6.24', update_url: 'https://example.com/browser-managed-test-channel' }; },
    async requestUpdateCheck() { this.checks++; return result; }, reload() { this.reloads++; } };
}
test('Development ZIP cannot pretend a reload updates its binary', async () => {
  const api = runtime(); api.getManifest = () => ({ version: '0.6.24' });
  assert.equal((await updates.check(api)).status, 'development');
  assert.equal(api.checks, 0);
  assert.equal(updates.apply(api, '0.6.25', true), false);
  assert.equal(api.reloads, 0);
});
test('Native browser check distinguishes current, available and throttled', async () => {
  for (const result of [{ status: 'no_update' }, { status: 'throttled' }, { status: 'update_available', version: '0.6.25' }]) {
    const api = runtime(result); assert.deepEqual(await updates.check(api), result); assert.equal(api.checks, 1);
  }
});
test('Only a newer browser update and explicit confirmation request native reload', () => {
  const api = runtime();
  assert.equal(updates.apply(api, '0.6.25', false), false);
  for (const version of ['0.6.24', '0.6.16', 'broken']) assert.equal(updates.apply(api, version, true), false);
  assert.equal(api.reloads, 0);
  assert.equal(updates.apply(api, '0.6.25', true), true);
  assert.equal(api.reloads, 1);
});
test('Native errors and incorrect available versions remain failures', async () => {
  await assert.rejects(() => updates.check(runtime({ status: 'update_available', version: '0.6.16' })));
  await assert.rejects(() => updates.check(runtime({ status: 'unexpected' })));
  const api = runtime(); api.requestUpdateCheck = async () => { throw new Error('Synthetic offline'); };
  await assert.rejects(() => updates.check(api));
});
