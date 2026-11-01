/* The browser alone downloads, verifies and installs extension binaries. */
(function (root) {
  const channels = Object.freeze({
    edge: 'https://edge.microsoft.com/extensionwebstorebase/v1/crx',
    chrome: 'https://clients2.google.com/service/update2/crx',
  });
  const validId = value => typeof value === 'string' && /^[a-p]{32}$/.test(value);
  function newer(current, available) {
    const parse = value => typeof value === 'string' && /^(0|[1-9]\d*)(\.(0|[1-9]\d*)){0,3}$/.test(value)
      ? value.split('.').map(Number) : null;
    const left = parse(current), right = parse(available);
    if (!left || !right || [...left, ...right].some(value => value > 65535)) return false;
    for (let index = 0; index < 4; index++) {
      if ((left[index] || 0) !== (right[index] || 0)) return (right[index] || 0) > (left[index] || 0);
    }
    return false;
  }
  function create(runtime, management, distribution) {
    let pending = null;
    async function inspect() {
      const manifest = runtime.getManifest();
      if (distribution?.schemaVersion !== 1 || distribution.productId !== 'unishare' ||
          !['development', 'edge', 'chrome'].includes(distribution.store) ||
          (distribution.extensionId !== null && !validId(distribution.extensionId))) throw new Error('Invalid distribution');
      const self = await management.getSelf(); // This method does not require the management permission.
      if (!validId(runtime.id) || self.id !== runtime.id || self.version !== manifest.version || self.type !== 'extension') {
        throw new Error('Extension identity mismatch');
      }
      if (self.installType === 'development' || distribution.store === 'development') return { status: 'development' };
      const expectedUrl = channels[distribution.store];
      // The store adds its update URL on installation. Never manufacture one in an unpacked ZIP.
      if (self.installType !== 'normal' || self.updateUrl !== expectedUrl ||
          (manifest.update_url !== undefined && manifest.update_url !== expectedUrl) ||
          (distribution.extensionId !== null && distribution.extensionId !== self.id)) throw new Error('Unverified store channel');
      return { status: 'store', store: distribution.store, extensionId: self.id, version: self.version };
    }
    return {
      inspect,
      async check() {
        pending = null;
        if ((await inspect()).status !== 'store') return { status: 'development' };
        const result = await runtime.requestUpdateCheck();
        if (!['update_available', 'no_update', 'throttled'].includes(result?.status)) throw new Error('Invalid browser result');
        if (result.status === 'update_available') {
          if (!newer(runtime.getManifest().version, result.version)) throw new Error('Invalid browser update version');
          pending = result.version;
        }
        return result;
      },
      async onAvailable(details) {
        if ((await inspect()).status !== 'store' || !newer(runtime.getManifest().version, details?.version)) return false;
        pending = details.version;
        return true;
      },
      async apply(confirmed) {
        if (!confirmed || !pending || (await inspect()).status !== 'store' || !newer(runtime.getManifest().version, pending)) return false;
        pending = null;
        runtime.reload(); // Request application of the browser's pending package, not a ZIP reload.
        return true;
      },
    };
  }
  const api = { create, newer, channels };
  if (typeof module !== 'undefined') module.exports = api;
  else root.UniShareExtensionUpdates = api;
})(globalThis);
