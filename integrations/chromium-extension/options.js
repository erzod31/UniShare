const port = document.querySelector('#port');
const pairingKey = document.querySelector('#pairingKey');
const status = document.querySelector('#status');
const uiLanguage = document.querySelector('#uiLanguage');

UniShareI18n.initialize().then(() => chrome.storage.local.get(['port', 'pairingKey', 'uiLanguage'])).then(values => {
  port.value = values.port || 47831;
  pairingKey.value = values.pairingKey || '';
  uiLanguage.value = values.uiLanguage || UniShareI18n.language;
  initializeUpdates();
});

async function initializeUpdates() {
  const updateStatus = document.querySelector('#updateStatus');
  const check = document.querySelector('#checkUpdate');
  const apply = document.querySelector('#applyUpdate');
  const manifest = chrome.runtime.getManifest();
  let available = null;
  document.querySelector('#extensionVersion').textContent = UniShareI18n.t('installedVersion') + manifest.version;
  check.disabled = true;
  let updater, channel;
  try {
    const response = await fetch(chrome.runtime.getURL('distribution.json'));
    if (!response.ok) throw new Error('Distribution metadata missing');
    updater = UniShareExtensionUpdates.create(chrome.runtime, chrome.management, await response.json());
    channel = await updater.inspect();
  } catch {
    updateStatus.textContent = UniShareI18n.t('invalidUpdateChannel');
    return;
  }
  const native = channel.status === 'store';
  document.querySelector('#extensionChannel').textContent = UniShareI18n.t('updateChannel') +
    (native ? (channel.store === 'edge' ? 'Microsoft Edge Add-ons' : 'Chrome Web Store') : UniShareI18n.t('developmentChannel'));
  updateStatus.textContent = UniShareI18n.t(native ? 'browserUpdates' : 'developmentUpdates');
  check.disabled = !native;
  check.addEventListener('click', async () => {
    check.disabled = true;
    try {
      available = null;
      apply.hidden = true;
      const result = await updater.check();
      available = result.status === 'update_available' ? result.version : null;
      apply.hidden = !available;
      updateStatus.textContent = UniShareI18n.t(result.status === 'update_available' ? 'browserUpdateReady' :
        result.status === 'throttled' ? 'updateThrottled' : 'browserNoUpdate') + (available || '');
    } catch { updateStatus.textContent = UniShareI18n.t('updateFailed'); }
    finally { check.disabled = !native; }
  });
  chrome.runtime.onUpdateAvailable.addListener(async details => {
    try { if (native && await updater.onAvailable(details)) {
      available = details.version;
      apply.hidden = false;
      updateStatus.textContent = UniShareI18n.t('browserUpdateReady') + available;
    } } catch { available = null; apply.hidden = true; updateStatus.textContent = UniShareI18n.t('updateFailed'); }
  });
  apply.addEventListener('click', async () => {
    try { if (available && await updater.apply(window.confirm(UniShareI18n.t('confirmBrowserUpdate')))) {
      available = null; apply.hidden = true;
    } } catch { available = null; apply.hidden = true; updateStatus.textContent = UniShareI18n.t('updateFailed'); }
  });
}

document.querySelector('#save').addEventListener('click', async () => {
  const numericPort = Number(port.value);
  const key = pairingKey.value.trim();
  if (!Number.isInteger(numericPort) || numericPort < 1024 || numericPort > 65535 || key.length < 32) {
    status.textContent = UniShareI18n.t('invalidSettings');
    return;
  }
  await chrome.storage.local.set({ port: numericPort, pairingKey: key });
  await UniShareI18n.setLanguage(uiLanguage.value);
  status.textContent = UniShareI18n.t('settingsSaved');
  window.setTimeout(() => window.location.reload(), 500);
});
