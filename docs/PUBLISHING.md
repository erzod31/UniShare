# Publicar desde una única carpeta / Publishing from one workspace

## Español

`UniShare` es la única carpeta permanente de desarrollo. La antigua copia `UniShare-Public` no es
necesaria. El repositorio mantiene dos líneas de historial separadas:

- `master`: desarrollo local; puede contener información histórica privada. Nunca se publica.
- `public`: instantáneas saneadas, descendientes de `origin/main`, que conservan el historial público.

`origin` apunta al repositorio público existente. El refspec local permite enviar `public` a `main`;
el hook `pre-push` bloquea otras ramas, borrados, sustitución de etiquetas e historial privado.
No uses `--no-verify`, `--mirror`, `--all` ni `--force` para publicar desde este repositorio.

### Preparar y revisar

Requiere Git y PowerShell 7. Guarda y confirma primero los cambios de código y documentación:

```powershell
git status --short
git fetch --no-tags origin
.\packaging\public\test-publication.ps1
.\packaging\public\prepare-publication.ps1 -Version 0.6.25
git diff --stat origin/main public
git log --oneline origin/main..public
git diff origin/main public
```

El script exige un árbol limpio, usa el exportador existente y revisa el ZIP por rutas privadas,
credenciales y archivos generados. Crea la revisión pública mediante un índice temporal, sin cambiar
la rama activa, el índice de desarrollo ni los archivos de trabajo. Conserva como padre el historial
público; nunca incorpora los padres de `master`. Retira los temporales al terminar y no envía nada
a GitHub. Una segunda ejecución con el mismo contenido no crea otra revisión.

Los informes operativos locales `docs/*DG-*.json`, sus planes de limpieza y `AGENTS.md` se conservan
en la máquina y quedan fuera de las instantáneas. Las bibliotecas del usuario, claves de firma,
emparejamientos, modelos y SDK no se trasladan ni eliminan al preparar una publicación.

Si GitHub tiene cambios posteriores que no están en `public`, el script se detiene. Integra esos
cambios sobre la línea pública y repite la preparación; no fuerces el envío ni mezcles `master`.

### Publicar, sólo después de revisar y autorizar

```powershell
git push --dry-run origin public:main
git push origin public:main
```

Estos comandos publican código. No ejecutarlos como parte de una limpieza o preparación local.
El hook es una protección local contra errores y puede deshabilitarse; no sustituye las protecciones
del servidor. Protege `main` en GitHub, exige CI y prohíbe force-push y borrados.

Para una release, etiqueta una revisión de `public`, nunca una de `master`. Antes de adjuntar binarios,
compila, prueba y empaqueta todos los clientes afectados desde la misma revisión de producto; comprueba
versión, firma, fecha, tamaño y hashes. Un cambio exclusivo del flujo de publicación no valida ni
convierte en nuevos los instaladores ya existentes.

### ZIP fuente independiente

```powershell
.\packaging\public\build-source-package.ps1 -Version 0.6.25
# Una vista previa puede usar otro nombre dentro de artifacts, sin sustituir el paquete de entrega:
.\packaging\public\build-source-package.ps1 -Version 0.6.25 -OutputPath artifacts/source-preview.zip
```

### Configuración de esta máquina y recuperación

La consolidación configura `remote.origin.push=refs/heads/public:refs/heads/main`,
`push.default=nothing`, `core.hooksPath=packaging/public/hooks` y `unishare.privateRoot` con las raíces
del historial privado. No configures `master` para seguir `origin/main`.

Las referencias de la copia anterior están en `refs/archive/unishare-public/`; las etiquetas recuperadas
de GitHub están en `refs/archive/github-tags/`. `.git/consolidation-20261031/` conserva una copia privada
de los metadatos Git anteriores, su manifiesto y el registro de consolidación. No publiques esa carpeta.
Una clonación pública normal no necesita estas referencias ni esta configuración local: sólo contiene
el historial público y puede trabajar con `main` normalmente.

## English

Use `UniShare` as the only permanent workspace. Local `master` keeps private development history;
`public` contains sanitized snapshots based on the existing GitHub `main`. Never push `master`.

With Git and PowerShell 7 installed, commit your source changes, fetch `origin`, run
`packaging/public/test-publication.ps1`, then run `packaging/public/prepare-publication.ps1 -Version 0.6.25`.
Review `git diff origin/main public`. Preparation changes no working files, preserves public ancestry,
scans the exported source for private content, removes temporary staging and never pushes.

Only after review and publication authorization, use `git push --dry-run origin public:main`, followed
by `git push origin public:main`. The local pre-push hook rejects private history, other branches,
deletions, rewritten tags and non-fast-forward main updates. Do not bypass it. Also protect `main`
and require CI on GitHub. Public clones use ordinary `main` and do not require this private workspace
configuration. Operational reports, credentials, local profiles and Git metadata stay on this machine.

Release binaries still require coordinated Windows, Android and extension builds and validation.
Preparing source or changing publication tools does not rebuild or certify existing installers.
