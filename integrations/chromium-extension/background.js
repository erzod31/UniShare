const CONTEXT_MENU_ID = 'unishare-save-link';

function registerContextMenus() {
  if (!chrome.contextMenus) return;
  chrome.contextMenus.removeAll(() => {
    chrome.contextMenus.create({
      id: CONTEXT_MENU_ID,
      title: chrome.i18n?.getMessage('actionTitle') || 'Guardar en UniShare',
      contexts: ['link', 'page'],
    });
  });
}

async function flashBadge(text, color) {
  if (!chrome.action?.setBadgeText) return;
  await chrome.action.setBadgeBackgroundColor({ color });
  await chrome.action.setBadgeText({ text });
  setTimeout(() => {
    chrome.action.setBadgeText({ text: '' }).catch(() => {});
  }, 2500);
}

async function quickCaptureUrl(rawUrl, preferredTitle) {
  if (!/^https?:\/\//i.test(rawUrl || '')) {
    await flashBadge('!', '#B42318');
    return;
  }
  const { port = 47831, pairingKey = '' } = await chrome.storage.local.get(['port', 'pairingKey']);
  if (!pairingKey) {
    chrome.runtime.openOptionsPage();
    return;
  }
  const fallbackHost = new URL(rawUrl).hostname;
  const cleanTitle = (preferredTitle || fallbackHost).trim().slice(0, 500) || fallbackHost;
  try {
    const response = await fetch(`http://127.0.0.1:${port}/api/v1/capture`, {
      method: 'POST',
      headers: {
        'Authorization': `Bearer ${pairingKey}`,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        url: rawUrl,
        title: cleanTitle,
        collection: null,
        saveOffline: false,
      }),
    });
    if (!response.ok) {
      throw new Error(`HTTP ${response.status}`);
    }
    await flashBadge('✓', '#167A82');
  } catch {
    await flashBadge('!', '#B42318');
  }
}

chrome.runtime.onInstalled.addListener(details => {
  registerContextMenus();
  if (details.reason === 'install') {
    chrome.runtime.openOptionsPage();
  }
});

if (chrome.contextMenus?.onClicked) {
  chrome.contextMenus.onClicked.addListener((info, tab) => {
    if (info.menuItemId !== CONTEXT_MENU_ID) return;
    const targetUrl = info.linkUrl || info.pageUrl || tab?.url || '';
    const title = info.linkUrl
      ? (info.selectionText || '')
      : (tab?.title || '');
    quickCaptureUrl(targetUrl, title);
  });
}

if (chrome.commands?.onCommand) {
  chrome.commands.onCommand.addListener(async command => {
    if (command !== 'save-current-tab') return;
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    if (tab?.url) {
      await quickCaptureUrl(tab.url, tab.title || '');
    }
  });
}
