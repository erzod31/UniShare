# Desarrollo de UniShare

## Requisitos iniciales

- Windows 10/11 x64.
- .NET SDK indicado por `global.json`.
- PowerShell 7 o compatible.

Android se documenta en `platforms/android/README.md`.
La firma de producción y la continuidad de las actualizaciones se documentan en [SIGNING](docs/SIGNING.md).

Los contratos de descripción se comparten en `tests/fixtures/origin-metadata.json` entre .NET,
Android JVM, Android instrumentado y Chromium. Para la extensión se necesita Node.js en desarrollo:
`node --test tests/chromium/metadata.test.cjs`. No es una dependencia de los clientes instalados.

## Validación del repositorio

La actualización del propio binario se documenta en [APP_UPDATES](docs/APP_UPDATES.md).
Durante DG-0018, sólo se prueban lanzadores falsos: nunca ejecutar instaladores/actualizadores reales
en el Windows compartido. El empaquetado admite `build-installer.ps1 -Version 0.6.25 -SmokeOnly`;
ese modo valida arranque/búsqueda/cierre de la app en un perfil temporal sin ejecutar el Setup.
Contratos de extensión: `node --test tests/chromium/*.test.cjs tests/translation/*.test.cjs`.

```powershell
dotnet restore UniShare.slnx --locked-mode
dotnet build UniShare.slnx -c Release --no-restore
dotnet test UniShare.slnx -c Release --no-build
dotnet test UniShare.slnx -c Release --no-build --coverage --coverage-output-format cobertura --results-directory artifacts/test-results
dotnet format UniShare.slnx --verify-no-changes --no-restore
```

## Ejecutar Windows

```powershell
dotnet run --project src/UniShare.Desktop/UniShare.Desktop.csproj
```

Para comprobar el arranque y la creación del perfil sin abrir una ventana:

```powershell
dotnet run --project src/UniShare.Desktop/UniShare.Desktop.csproj -- --smoke-test --profile artifacts/smoke-profile
```

## Generar el instalador Windows

Instala Inno Setup 6 y ejecuta:

```powershell
.\packaging\windows\build-installer.ps1 -Version 0.6.25
```

El script usa los bloqueos `packages.win-x64.lock.json`, publica una aplicación autocontenida y crea
`artifacts\UniShare-Windows-Setup-0.6.25.exe`. La restauración normal de la solución continúa usando los
bloqueos multiplataforma `packages.lock.json`.

También funciona desde el ZIP fuente oficial sin `.git`: `SOURCE_REVISION` conserva su revisión de
procedencia mediante `git archive`. El binario llevará `.archive` porque sin Git no se pueden detectar
ediciones posteriores. En un checkout normal se conserva la detección de cambios con `.dirty`.

## Traducción local

Los assets revisados ya están en `shared/translation`; una compilación normal no descarga motores ni
modelos. Para regenerarlos: `npm ci --ignore-scripts` y `npm run build` desde `tools/translation`.
`update-catalog.mjs` es una operación explícita de mantenimiento: revisar sus cambios antes de publicar.
Ninguna actualización de catálogo se realiza en segundo plano en los clientes.

Contratos: `node --test tests/translation/*.test.cjs`. Inferencia y UI Windows con perfil nuevo aislado:
`dotnet run --project tests/UniShare.TranslationRuntimeHarness -c Release -- artifacts/translation-qa`.
El harness descarga modelos públicos y después bloquea su red para verificar inferencia offline.
Android: ejecutar `org.unishare.app.LocalTranslationRuntimeTest` por separado mediante instrumentación
en la APK `.qa`; requiere red sólo para modelos que aún no estén descargados. La segunda ronda bloquea
la descarga e infiere desde modelos locales; otra prueba verifica idioma de Ajustes y vista original.

## Benchmark de búsqueda

Genera un perfil temporal con 100 000 items, mide 30 consultas FTS5 y falla si p95 supera 300 ms:

```powershell
dotnet run --project tools/UniShare.Benchmarks/UniShare.Benchmarks.csproj -c Release
```

## Respaldo, inspección y restauración

```powershell
dotnet run --project tools/UniShare.Backup/UniShare.Backup.csproj -c Release -- export --profile artifacts/smoke-profile --output artifacts/library.unishare.zip
dotnet run --project tools/UniShare.Backup/UniShare.Backup.csproj -c Release -- inspect --input artifacts/library.unishare.zip
dotnet run --project tools/UniShare.Backup/UniShare.Backup.csproj -c Release -- restore --input artifacts/library.unishare.zip --profile artifacts/restored-profile
```

La restauración exige una ruta que no exista para evitar sobrescribir datos.
Los respaldos no son el transporte de sincronización. El nodo HTTP local se inicia con la aplicación
Windows en `127.0.0.1:47831`; para publicarlo sólo en la tailnet se usa
`tailscale serve --bg --https=8443 47831`.

## Reglas de datos de desarrollo

- No usar datos personales reales en fixtures o capturas.
- Los perfiles de pruebas se crean bajo directorios temporales y nunca apuntan al perfil real.
- No borrar manualmente blobs sin ejecutar antes una comprobación de referencias.

## Distribución coordinada

Ver [RELEASE_PREPARATION](docs/RELEASE_PREPARATION.md) para generar los diez candidatos y sus
expedientes sin despliegue. Las pruebas de navegador usan un perfil nuevo y un Playwright de desarrollo
ya instalado; "UNISHARE_PLAYWRIGHT_MODULE" puede indicar su módulo. No es dependencia del producto.
