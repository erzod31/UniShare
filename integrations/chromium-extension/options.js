const port = document.querySelector('#port');
const pairingKey = document.querySelector('#pairingKey');
const status = document.querySelector('#status');
const uiLanguage = document.querySelector('#uiLanguage');

UniShareI18n.initialize().then(() => chrome.storage.local.get(['port', 'pairingKey', 'uiLanguage'])).then(values => {
  port.value = values.port || 47831;
  pairingKey.value = values.pairingKey || '';
  uiLanguage.value = values.uiLanguage || UniShareI18n.language;
});

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
