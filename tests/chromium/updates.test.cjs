const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const source = process.env.UNISHARE_EXTENSION_SOURCE || path.resolve(__dirname, '../../integrations/chromium-extension');
const updates = require(path.join(source, 'updates.js'));
// Synthetic IDs are test fixtures only; no store identity is assigned by these tests.
function fixture(store = 'edge', result = { status: 'no_update' }) {
  const id = 'a'.repeat(32), version = '0.6.25';
  const manifest = { version, update_url: updates.channels[store] };
  const self = { id, version, type: 'extension', installType: 'normal', updateUrl: updates.channels[store] };
  const distribution = { schemaVersion: 1, productId: 'unishare', store, extensionId: null };
  const runtime = { id, checks: 0, reloads: 0, getManifest: () => manifest,
    async requestUpdateCheck() { this.checks++; return result; }, reload() { this.reloads++; } };
  const management = { async getSelf() { return self; } };
  return { runtime, manifest, self, distribution, management, updater: updates.create(runtime, management, distribution) };
}
test('Unpacked store ZIP with an official update URL cannot pretend to be a native channel', async () => {
  const f = fixture(); f.self.installType = 'development';
  assert.equal((await f.updater.check()).status, 'development');
  assert.equal(f.runtime.checks, 0);
  assert.equal(await f.updater.onAvailable({ version: '0.6.26' }), false);
  assert.equal(await f.updater.apply(true), false);
  assert.equal(f.runtime.reloads, 0);
});
test('Development artifact never requests a native binary update', async () => {
  const f = fixture(); f.distribution.store = 'development';
  assert.equal((await f.updater.check()).status, 'development');
  assert.equal(f.runtime.checks, 0);
});
test('Both real browser endpoints and identities must agree', async () => {
  for (const store of ['edge', 'chrome']) {
    const f = fixture(store); delete f.manifest.update_url;
    assert.deepEqual(await f.updater.inspect(), { status: 'store', store, extensionId: f.runtime.id, version: '0.6.25' });
    assert.equal((await f.updater.check()).status, 'no_update');
  }
});
test('HTTPS alone is not a valid update channel', async () => {
  for (const url of ['https://example.invalid/update', 'https://edge.microsoft.com.evil.invalid/extensionwebstorebase/v1/crx',
    updates.channels.edge + '?redirect=evil', 'http://edge.microsoft.com/extensionwebstorebase/v1/crx', updates.channels.chrome]) {
    const f = fixture(); f.self.updateUrl = url; f.manifest.update_url = url;
    await assert.rejects(() => f.updater.check()); assert.equal(f.runtime.checks, 0);
  }
});
test('Expected store ID, runtime ID and package identity cannot be substituted', async () => {
  for (const change of [f => { f.distribution.extensionId = 'b'.repeat(32); }, f => { f.runtime.id = 'b'.repeat(32); },
    f => { f.self.version = '0.6.24'; }, f => { f.self.type = 'theme'; }, f => { f.distribution.productId = 'another-product'; },
    f => { f.distribution.extensionId = 'not-an-id'; }, f => { f.distribution.schemaVersion = 2; },
    f => { f.manifest.update_url = updates.channels.chrome; }]) {
    const f = fixture(); change(f); await assert.rejects(() => f.updater.check()); assert.equal(f.runtime.checks, 0);
  }
});
test('Sideloaded, policy-managed and unknown installations are not advertised as store installs', async () => {
  for (const type of ['sideload', 'admin', 'other', undefined]) {
    const f = fixture(); f.self.installType = type; await assert.rejects(() => f.updater.check());
  }
});
test('Native checks distinguish current, available and throttled', async () => {
  for (const result of [{ status: 'no_update' }, { status: 'throttled' }, { status: 'update_available', version: '0.6.26' }]) {
    const f = fixture('edge', result); assert.deepEqual(await f.updater.check(), result); assert.equal(f.runtime.checks, 1);
  }
});
test('Reload requires a real pending browser response and explicit confirmation', async () => {
  const f = fixture('edge', { status: 'update_available', version: '0.6.26' });
  assert.equal(await f.updater.apply(true), false);
  await f.updater.check();
  assert.equal(await f.updater.apply(false), false); assert.equal(f.runtime.reloads, 0);
  assert.equal(await f.updater.apply(true), true); assert.equal(f.runtime.reloads, 1);
  assert.equal(await f.updater.apply(true), false); assert.equal(f.runtime.reloads, 1);
});
test('A native onUpdateAvailable event can prepare an update without polling', async () => {
  const f = fixture('chrome');
  assert.equal(await f.updater.onAvailable({ version: '0.6.25' }), false);
  assert.equal(await f.updater.onAvailable({ version: 'broken' }), false);
  assert.equal(await f.updater.onAvailable({ version: '0.6.26' }), true);
  assert.equal(await f.updater.apply(true), true); assert.equal(f.runtime.checks, 0);
});
test('A failed or empty subsequent check clears a previously pending result', async () => {
  for (const fail of [false, true]) {
    const f = fixture(); await f.updater.onAvailable({ version: '0.6.26' });
    if (fail) f.runtime.requestUpdateCheck = async () => { throw new Error('Synthetic offline'); };
    if (fail) await assert.rejects(() => f.updater.check()); else await f.updater.check();
    assert.equal(await f.updater.apply(true), false); assert.equal(f.runtime.reloads, 0);
  }
});
test('Identity is checked again before requesting application of a pending package', async () => {
  const f = fixture(); await f.updater.onAvailable({ version: '0.6.26' }); f.self.id = 'b'.repeat(32);
  await assert.rejects(() => f.updater.apply(true)); assert.equal(f.runtime.reloads, 0);
});
test('Native errors, non-increasing versions and malformed versions remain failures', async () => {
  for (const result of [{ status: 'update_available', version: '0.6.24' }, { status: 'unexpected' },
    { status: 'update_available', version: '65536.0.0' }, { status: 'update_available', version: '00.6.26' }, null]) {
    const f = fixture('edge', result); await assert.rejects(() => f.updater.check()); assert.equal(await f.updater.apply(true), false);
  }
});
