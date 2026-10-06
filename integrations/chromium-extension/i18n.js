const translations = {
  es: {
    saveInUniShare: 'Guardar en UniShare', title: 'Título', folderOptional: 'Carpeta (opcional)',
    readingPlaceholder: 'Lecturas', saveOffline: 'Guardar también una copia offline',
    offlineHelp: 'Conserva el texto renderizado y hasta 12 imágenes visibles, sin scripts.',
    saveTab: 'Guardar pestaña', configureConnection: 'Configurar conexión', configureUniShare: 'Configurar UniShare',
    connectUniShare: 'Conectar con UniShare', connectionHelp: 'Abre UniShare en Windows y copia los datos desde Opciones y herramientas → Conectar teléfono. Allí verás el puerto y la clave privada por separado.',
    localPort: 'Puerto local', privateKey: 'Clave privada', language: 'Idioma', saveSettings: 'Guardar configuración',
    invalidTab: 'Esta pestaña no es un enlace HTTP o HTTPS.', preparingOffline: 'Preparando copia offline…',
    saving: 'Guardando…', configureKey: 'Configura primero la clave de conexión.',
    noRenderedText: 'La página no expuso texto renderizado para guardar offline.',
    savedOffline: 'Guardado con copia offline. Ya puedes cerrar esta ventana.',
    savedLink: 'Enlace guardado. Ya puedes cerrar esta ventana.', connectFailed: 'No se pudo conectar con UniShare.',
    invalidSettings: 'Revisa el puerto y pega la clave completa.', settingsSaved: 'Configuración guardada en este navegador.',
    serverResponse: status => `UniShare respondió ${status}.`,
  },
  en: {
    saveInUniShare: 'Save to UniShare', title: 'Title', folderOptional: 'Folder (optional)',
    readingPlaceholder: 'Reading', saveOffline: 'Also save an offline copy',
    offlineHelp: 'Keeps the rendered text and up to 12 visible images, without scripts.',
    saveTab: 'Save tab', configureConnection: 'Configure connection', configureUniShare: 'Configure UniShare',
    connectUniShare: 'Connect to UniShare', connectionHelp: 'Open UniShare on Windows and copy the details from Options and tools → Connect phone. The port and private key are shown separately.',
    localPort: 'Local port', privateKey: 'Private key', language: 'Language', saveSettings: 'Save settings',
    invalidTab: 'This tab is not an HTTP or HTTPS link.', preparingOffline: 'Preparing offline copy…',
    saving: 'Saving…', configureKey: 'Configure the connection key first.',
    noRenderedText: 'The page did not expose rendered text for offline storage.',
    savedOffline: 'Saved with an offline copy. You can close this window.',
    savedLink: 'Link saved. You can close this window.', connectFailed: 'Could not connect to UniShare.',
    invalidSettings: 'Check the port and paste the complete key.', settingsSaved: 'Settings saved in this browser.',
    serverResponse: status => `UniShare responded with ${status}.`,
  },
};

window.UniShareI18n = {
  language: 'es',
  async initialize() {
    const stored = await chrome.storage.local.get(['uiLanguage']);
    this.language = stored.uiLanguage || (chrome.i18n.getUILanguage().toLowerCase().startsWith('en') ? 'en' : 'es');
    document.documentElement.lang = this.language;
    document.querySelectorAll('[data-i18n]').forEach(element => {
      element.textContent = this.t(element.dataset.i18n);
    });
    document.querySelectorAll('[data-i18n-placeholder]').forEach(element => {
      element.placeholder = this.t(element.dataset.i18nPlaceholder);
    });
    document.title = this.t(document.body.dataset.titleKey);
    return this.language;
  },
  t(key, ...args) {
    const value = translations[this.language]?.[key] ?? translations.es[key] ?? key;
    return typeof value === 'function' ? value(...args) : value;
  },
  async setLanguage(language) {
    await chrome.storage.local.set({ uiLanguage: language });
    this.language = language;
  },
};
