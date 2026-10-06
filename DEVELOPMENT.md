# Desarrollo de UniShare

## Requisitos iniciales

- Windows 10/11 x64.
- .NET SDK indicado por `global.json`.
- PowerShell 7 o compatible.

Android se documenta en `platforms/android/README.md`.

## Validación del repositorio

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
.\packaging\windows\build-installer.ps1 -Version 0.6.16
```

El script usa los bloqueos `packages.win-x64.lock.json`, publica una aplicación autocontenida y crea
`artifacts\UniShare-Windows-Setup-0.6.16.exe`. La restauración normal de la solución continúa usando los
bloqueos multiplataforma `packages.lock.json`.

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
