# Coordinated release candidates / Candidatos coordinados

## Build locally, without deployment

From the single `UniShare` workspace, commit the reviewed source first. Require Git, PowerShell 7,
the .NET SDK pinned in `global.json`, the existing Android SDK/JDK, Node.js and Inno Setup 6.
Keep the historical Android signing key outside the repository. Do not let a clean build machine
generate a replacement debug key: an APK with a different certificate cannot update existing users.
Production Windows signing is optional for the existing unsigned lineage; its configuration and
limitations are documented in [SIGNING](SIGNING.md).

```powershell
.\packaging\public\test-publication.ps1
.\packaging\public\prepare-publication.ps1 -Version 0.6.25
.\packaging\releases\build-release.ps1 -Version 0.6.25
python .\packaging\releases\verify-release.py 0.6.25
```

The release builder always uses `-SmokeOnly`: it compiles the real Windows installer and starts
the application with a fresh temporary profile, multiple-instance permission and Tailscale disabled.
It never runs Setup, an updater, uninstall, registry or shortcut mutation. Android runs JVM tests,
Lint and assembly only; it never calls adb or touches a physical device. Existing same-version
outputs are preserved or rejected, never silently replaced by an older package.

Outputs: ten artifacts, their SHA-256 manifest, release notes and `artifacts/release-0.6.25.json`.
The plan records both the private build revision and the separate sanitized public revision; only
the latter may be pushed. Review artifact identity/certificate checks and independent native QA
before activating a stable release. ZIP integrity and a fake installer handoff do not prove a
successful native upgrade, its cancellation behavior or preserved shortcuts.

## Extension identity and stores

The builder creates separate development, Edge and Chrome payloads. `distribution.json` identifies
their intended channel. It contains no invented store ID. First uploads allow a null ID because
the store assigns it. Once the owner has that ID, pass the real value as `-EdgeExtensionId` or
`-ChromeExtensionId` and retain the same store product for every update.

Do not add a fabricated `update_url` or manifest key. The actual installed extension must have a
matching runtime/self identity, normal install type and the official store update endpoint.
`management.getSelf()` needs no extra management permission. The browser verifies/downloads
updates and owns their final application. Only a pending native update result/event enables Apply.
Unpacked ZIP reloads cannot update a binary and are displayed as development.

The submission dossiers include ES/EN listings, privacy text, 128px icon, 300px logo, genuine
1280×800 options screenshots and a 440×280 promotional tile. The screenshot profile is an unpacked
development installation and contains no user data. Never present it as proof of store delivery.
See [extension publication](../integrations/chromium-extension/PUBLISHING.md).

## Remote publication remains a reviewed last step

No build command pushes code, creates a remote draft or uploads assets. Review the concrete plan
and authorize its remote effects separately. Push only `public:main`, tag the reviewed public
revision, create a draft, upload the exact artifacts/checksum file, compare every GitHub asset's
authenticated size and SHA-256 digest, then enable the stable release as the last activation step.
The UI clients require GitHub's authenticated digest; a hand-written checksum alone is insufficient.

Activation makes packages discoverable by compatible installed updaters. Versions through 0.6.23
need an initial manual install to obtain the updater. Extension store certification is separate
and requires the owner's real publisher account/contact/consent; no fee, license or agreement is
accepted by the local scripts. The existing stable release is not replaced during preparation.

Before activation, discard a bad draft. After activation, withdraw the defective release and ship
a higher corrected version; don't downgrade user databases or uninstall to bypass identity checks.
Physical Windows/Android upgrade/cancel/reopen/data retention tests remain explicitly unverified
until performed in an independent disposable environment. Never use the shared Windows or the LG
as a substitute while the user's restriction is active.

## Español

La preparación genera instaladores y expedientes, pero no publica ni instala nada. Ejecuta los
cuatro comandos anteriores desde un árbol limpio. Revisa `artifacts/release-0.6.25.json`, las firmas,
los hashes y el informe vigente antes de autorizar el envío. Conserva las identidades y claves
históricas. Sólo se publica la rama `public`, nunca `master` ni su historial privado.

Las pruebas nativas en VM/dispositivo independiente y la certificación de las tiendas siguen
siendo pasos externos: no se sustituyen por mocks, recargas o cambios de producción. Los paquetes
de Edge y Chrome son expedientes de envío, no instalaciones de tienda ya aprobadas.
