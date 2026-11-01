// Optional development check: uses an existing Playwright installation, never a user's browser profile.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { chromium } = require(process.env.UNISHARE_PLAYWRIGHT_MODULE || 'playwright');
const root = path.resolve(__dirname, '../..');
const source = path.join(root, 'integrations/chromium-extension');
const output = path.join(root, 'output/playwright/DG-0034');
(async () => {
  await fs.mkdir(output, { recursive: true });
  const profile = await fs.mkdtemp(path.join(output, 'isolated-profile-'));
  const context = await chromium.launchPersistentContext(profile, {
    channel: 'chromium', headless: true, viewport: { width: 1280, height: 800 }, colorScheme: 'light',
    args: [`--disable-extensions-except=${source}`, `--load-extension=${source}`, '--no-first-run', '--no-default-browser-check'],
  });
  const checks = [], errors = [];
  try {
    const worker = context.serviceWorkers()[0] || await context.waitForEvent('serviceworker');
    const id = new URL(worker.url()).host;
    assert.match(id, /^[a-p]{32}$/);
    const identity = await worker.evaluate(() => chrome.management.getSelf());
    assert.equal(identity.installType, 'development');
    assert.equal(identity.version, '0.6.25');
    checks.push('Real unpacked browser identity is development, version 0.6.25');
    const page = await context.newPage();
    page.on('pageerror', error => errors.push(error.message));
    for (const language of ['es', 'en']) {
      await worker.evaluate(async language => chrome.storage.local.set({ uiLanguage: language }), language);
      await page.goto(`chrome-extension://${id}/options.html`);
      await page.locator('#extensionChannel').filter({ hasText: language === 'es' ? 'Desarrollo' : 'Development' }).waitFor();
      assert.equal(await page.locator('#checkUpdate').isDisabled(), true);
      assert.equal(await page.locator('#applyUpdate').isHidden(), true);
      assert.match(await page.locator('#updateStatus').innerText(), language === 'es' ? /ZIP de desarrollo/ : /Development ZIP/);
      await page.screenshot({ path: path.join(output, `options-${language}-1280x800.png`) });
      checks.push(`${language}: native update check disabled and no binary update claimed for unpacked ZIP`);
    }
    await page.locator('#port').fill('123');
    await page.locator('#save').click();
    await page.getByRole('status').filter({ hasText: 'Check the port' }).waitFor();
    checks.push('Invalid settings are rejected by actual options UI');
    await page.locator('#port').fill('49123');
    await page.locator('#pairingKey').fill('synthetic-qa-key-not-a-real-pairing-key-1234567890');
    await page.locator('#uiLanguage').selectOption('es');
    await page.locator('#save').click();
    await page.waitForTimeout(800);
    await page.locator('#extensionChannel').filter({ hasText: 'Desarrollo' }).waitFor();
    assert.equal(await page.locator('#port').inputValue(), '49123');
    assert.equal(await page.locator('#uiLanguage').inputValue(), 'es');
    checks.push('Synthetic settings and language persist after options reload');
    assert.deepEqual(errors, []);
    // Promotional artwork is separate from unmodified UI screenshots.
    const icon = (await fs.readFile(path.join(source, 'icons/icon-128.png'))).toString('base64');
    const tile = await context.newPage();
    await tile.setViewportSize({ width: 440, height: 280 });
    await tile.setContent(`<html><body style="margin:0;background:#f6f7fb;color:#172033;font:18px system-ui;text-align:center;padding:24px;box-sizing:border-box"><img width="88" height="88" src="data:image/png;base64,${icon}"><h1 style="margin:8px;font-size:32px">UniShare</h1><div>Links. Offline copies. Your library.</div><p style="font-size:14px;color:#44516a">Save directly to UniShare for Windows.</p></body></html>`);
    await tile.screenshot({ path: path.join(output, 'small-tile-440x280.png') });
    await fs.writeFile(path.join(output, 'browser-result.json'), JSON.stringify({
      version: identity.version, browser: context.browser().version(), installType: identity.installType,
      checks, pageErrors: errors, realStoreInstallOrUpdateTested: false, nativeInstallersExecuted: 0,
      userBrowserProfileTouched: false, qaProfile: path.basename(profile),
    }, null, 2));
    console.log(JSON.stringify({ checks: checks.length, pageErrors: errors.length, storeUpdateVerified: false }));
  } finally { await context.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
