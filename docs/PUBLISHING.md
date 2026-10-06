# Publicar UniShare como repositorio nuevo

El historial de desarrollo local puede contener nombres de autor o datos de diagnóstico ya retirados del
árbol actual. Para una publicación nueva, usa la instantánea de código fuente generada por el proyecto y
no copies la carpeta `.git` existente.

## 1. Crear la instantánea pública

Desde un árbol limpio:

```powershell
.\packaging\public\build-source-package.ps1 -Version 0.6.16
```

El script usa `git archive`, por lo que excluye `.git`, artefactos, cachés, bases de datos y cualquier
archivo marcado `export-ignore`. Después vuelve a inspeccionar el ZIP y falla si encuentra rutas o nombres
de archivo sensibles.

## 2. Crear un historial nuevo

Extrae `artifacts/UniShare-Source-0.6.16.zip`, entra en la carpeta extraída y ejecuta:

```powershell
git init -b main
git add .
git commit -m "Initial public release"
git remote add origin https://github.com/OWNER/UniShare.git
git push -u origin main
```

En GitHub crea antes un repositorio vacío, sin README, licencia ni `.gitignore` automáticos porque la
instantánea ya los incluye. Sustituye `OWNER` por la organización o cuenta que publicará el proyecto.

También puede usarse GitHub CLI después de iniciar el repositorio:

```powershell
gh repo create UniShare --public --source . --remote origin --push
```

La visibilidad y el propietario deben decidirse antes de ejecutar ese comando.

## 3. Publicar binarios

Crea una release para la etiqueta correspondiente y adjunta los siete artefactos coordinados de
`artifacts/`. Publica también sus SHA-256. No adjuntes perfiles, bases de datos, respaldos, claves,
certificados privados ni configuraciones Tailscale.

La versión 0.6.16 tiene dos limitaciones que deben permanecer visibles: Windows no está firmado con
Authenticode y el APK lateral usa una clave Android de desarrollo.

## 4. Configuración recomendada de GitHub

- Activa **Private vulnerability reporting** y Dependabot alerts.
- Protege `main` y exige que el workflow CI termine correctamente antes de fusionar.
- Desactiva force-push y borrado de `main`.
- Usa GitHub Releases para binarios; no los incorpores al historial Git.
- Revisa incidencias y capturas para evitar que terceros publiquen datos personales.
