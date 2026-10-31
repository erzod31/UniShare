# ADR 0033: traducción automática local dentro de UniShare

Fecha: 2026-10-26

Estado: aceptada; sustituye la traducción en navegador de ADR 0031.

Petición directa: traducir automáticamente dentro de la aplicación al idioma elegido en Ajustes.
El destino es español o inglés, los dos idiomas de interfaz actuales. Los títulos siguen siendo los
del contenido original.

Se utiliza Bergamot/Marian de Mozilla en WebAssembly: un núcleo local compartido por
Windows y Android, sin cuenta, clave API ni servidor de traducción. El primer uso de cada par necesita
descargar sus modelos públicos; las descripciones no salen del dispositivo. La biblioteca sigue
funcionando durante descargas o fallos. Se verifican tamaño y SHA-256 con un catálogo fijado.

Las traducciones son vistas derivadas y una caché local separada, indexada por texto original,
destino y versión del motor. No constituyen una edición, no se sincronizan como notas y no sustituyen
el original en backups ni Obsidian. Se puede consultar el original y modificarlo mediante el editor
existente. La traducción automática se puede desactivar en Ajustes.

## Hitos y aceptación

1. Motor compartido: traducción real español/inglés y con pivote, detección de idioma, Unicode,
   enlaces intactos, texto largo y fallos; licencias y procedencia revisadas.
2. Integración: biblioteca y lectura Windows/Android; ajuste persistente, destino según idioma,
   original accesible y edición segura. Evitar respuestas obsoletas y trabajo duplicado.
3. Conservación: invalidación al editar, caché tras reinicio, modelos dañados/incompletos, offline
   con y sin modelos. Ningún cambio de los datos de dominio por traducir.
4. Entrega: suites .NET/JVM/JS, UI y motor real Windows/emulador; paquetes coordinados nuevos,
   instalación PC con copia y conservación verificadas; límites de hardware explícitos.

## Comandos de validación

- `dotnet restore UniShare.slnx --locked-mode`, build Release, tests y format.
- `node --test tests/translation/*.test.cjs` y contratos existentes Chromium.
- Android: `test lintRelease assembleRelease` y pruebas instrumentadas API 37.
- Inferencia real y persistencia en perfil aislado, con red y sin red.
- Versión, firma Android histórica, SHA-256 y contenido de paquetes.

Fuentes primarias: [arquitectura Mozilla](https://firefox-source-docs.mozilla.org/toolkit/components/translations/resources/01_overview.html),
[Bergamot](https://firefox-source-docs.mozilla.org/toolkit/components/translations/resources/03_bergamot.html),
[fuentes](https://github.com/mozilla-firefox/firefox/tree/main/toolkit/components/translations),
[modelos publicados](https://mozilla.github.io/translations/firefox-models/).
