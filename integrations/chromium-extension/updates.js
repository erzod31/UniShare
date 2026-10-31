/* Binary updates are exclusively performed and verified by the browser's native update service. */
(function (root) {
  const api = {
    hasNativeChannel(manifest) {
      try { return new URL(manifest.update_url).protocol === 'https:'; } catch { return false; }
    },
    newer(current, available) {
      const parse = value => typeof value === 'string' && /^(0|[1-9]\d*)(\.(0|[1-9]\d*)){0,3}$/.test(value)
        ? value.split('.').map(Number) : null;
      const left = parse(current), right = parse(available);
      if (!left || !right || [...left, ...right].some(value => value > 65535)) return false;
      for (let index = 0; index < 4; index++) {
        if ((left[index] || 0) !== (right[index] || 0)) return (right[index] || 0) > (left[index] || 0);
      }
      return false;
    },
    async check(runtime) {
      const manifest = runtime.getManifest();
      if (!this.hasNativeChannel(manifest)) return { status: 'development' };
      const result = await runtime.requestUpdateCheck();
      if (!['update_available', 'no_update', 'throttled'].includes(result.status)) throw new Error('Invalid browser update result');
      if (result.status === 'update_available' && !this.newer(manifest.version, result.version)) throw new Error('Invalid browser update version');
      return result;
    },
    apply(runtime, version, confirmed) {
      const manifest = runtime.getManifest();
      if (!confirmed || !this.hasNativeChannel(manifest) || !this.newer(manifest.version, version)) return false;
      runtime.reload(); // Applies an already downloaded, browser-verified package; never downloads ZIPs.
      return true;
    },
  };
  if (typeof module !== 'undefined') module.exports = api;
  else root.UniShareExtensionUpdates = api;
})(globalThis);
