# UniShare

UniShare es una biblioteca personal local-first para guardar enlaces, archivos, páginas capturadas y
elementos híbridos sin depender de una cuenta ni de un servidor central.

[Instalación](docs/INSTALLATION.md) · [Privacidad](docs/PRIVACY.md) · [Seguridad](SECURITY.md) ·
[Contribuir](CONTRIBUTING.md) · [Licencia MIT](LICENSE)

## Descargar la versión 0.6.16

Abre la sección **Releases** de GitHub y descarga únicamente el archivo adecuado:

- Windows x64: `UniShare-Windows-Setup-0.6.16.exe`.
- Windows portátil: `UniShare-Windows-0.6.16.zip`.
- Android 8 o posterior: `UniShare-Android-0.6.16.apk`.
- Extensión Chrome/Edge para desarrollo: `UniShare-Chromium-0.6.16.zip`.

Los paquetes se regeneran y validan conjuntamente antes de una entrega. La APK continúa firmada
para depuración y no debe publicarse como versión de Play Store.
El instalador Windows es autocontenido y no requiere instalar .NET por separado. El ZIP portátil se
genera con el mismo contenido. Comprueba siempre el SHA-256 publicado y consulta la
[guía de instalación](docs/INSTALLATION.md).

> **Estado de firma:** el instalador 0.6.16 todavía no tiene Authenticode y el APK usa una clave de
> desarrollo. Estas limitaciones son visibles y no deben confundirse con paquetes de tienda firmados.

## Qué ofrece

- Guardado y edición de enlaces, archivos y elementos híbridos.
- Colecciones, etiquetas, favoritos, búsqueda FTS y acciones por lote.
- Copia offline y captura estática segura de páginas.
- Backup portable con hashes y almacenamiento de archivos deduplicado por contenido.
- Sincronización directa Windows–Android con conflictos explícitos, sin nube obligatoria.
- Agente de bandeja Windows, Sharesheet/SAF Android, extensión Chromium y exportación a Obsidian.
- Interfaz en español o inglés, con elección persistente e independiente en cada dispositivo.

Los datos se guardan localmente. Al capturar una web se contacta con el sitio elegido; la sincronización
sólo conecta dispositivos emparejados. Lee [Privacidad](docs/PRIVACY.md) para los detalles.

## Primer arranque en Windows

Para usar la aplicación no necesitas compilar: instala el paquete de Releases. Para desarrollar desde
el código fuente necesitas el SDK fijado en `global.json`:

```powershell
dotnet restore UniShare.slnx
dotnet build UniShare.slnx -c Release --no-restore
dotnet test --solution UniShare.slnx -c Release --no-build
dotnet run --project src/UniShare.Desktop/UniShare.Desktop.csproj
```

Los datos se guardan bajo `%LOCALAPPDATA%\UniShare` salvo que se indique `--profile <ruta>`.
El idioma se elige en **Opciones y herramientas > Idioma**; Windows y Android recuerdan su propia
preferencia y el cambio no altera los datos guardados.

## Guardar un enlace desde Windows

1. Pulsa **Guardar** y pega una dirección `http://` o `https://` en **Enlace**.
2. Opcionalmente escribe una colección en **Carpeta dentro de UniShare**. Puedes usar nombres como
   `Trabajo`, `Recetas` o `Universidad/Curso`; son carpetas lógicas que no rompen los enlaces al
   mover la biblioteca.
3. Activa **Guardar también una copia offline** si quieres descargar una copia local verificable.
   Déjala desactivada para conservar sólo la dirección y sus metadatos básicos.
4. Pulsa **Guardar**. El panel se cierra y selecciona el nuevo elemento para continuar trabajando.
5. Selecciona el resultado y pulsa **Editar información** para cambiar título, fuente, autor, descripción,
   favorito, etiquetas o colecciones.

UniShare consulta el título publicado y crea un resumen extractivo del contenido real: prioriza
`articleBody`, descripciones estructuradas del elemento y después el cuerpo semántico `<article>` o
`<main>`; para vídeos de YouTube usa la descripción original del vídeo. Omite navegación, formularios,
URLs aisladas y bloques dominados por enlaces. No envía el contenido a una IA externa. Al instalar
0.6.13 vuelve a analizar los enlaces anteriores, retira los resúmenes defectuosos formados por menús y
conserva las notas editadas. La consulta puede repetirse desde **Opciones y
herramientas > Actualizar títulos y resúmenes** en Windows o desde **Ajustes** en Android.
Los resúmenes se muestran en modo lectura y sus direcciones HTTP/HTTPS son pulsables. Para cambiar el
texto deliberadamente, abre **Editar información** y pulsa **Modificar resumen**.

Para quitar un elemento, selecciónalo y pulsa **Eliminar**. Para trabajar con varios a la vez, usa
Ctrl/Mayús en la lista de Windows o **Seleccionar** en Android; UniShare muestra el recuento y permite
eliminar o restaurar el grupo. La eliminación siempre pide confirmación y mueve los elementos a la
**Papelera**, desde donde se pueden restaurar. Los cambios recuperables también se sincronizan con los
otros dispositivos.

Cuando una descarga no puede completarse, el enlace ya guardado se conserva y el trabajo queda
visible en **Opciones > Ver descargas pendientes**, desde donde se puede reintentar. El título extraído de la página sólo
reemplaza el nombre automático del dominio; un título editado por el usuario nunca se sobrescribe.

**Cambiar biblioteca…**, en la barra lateral, permite elegir la carpeta física que contiene toda la biblioteca
(`library.db`, blobs y staging). La aplicación recuerda la selección y se reinicia en esa ubicación.
Las colecciones organizan el contenido dentro de UniShare; los blobs se almacenan por hash para
deduplicar y preservar su integridad.

Consulta [DEVELOPMENT.md](DEVELOPMENT.md) para comandos y [docs/requirements.md](docs/requirements.md) para el alcance.

La aplicación permite exportar un respaldo portable desde **Opciones**. La utilidad `UniShare.Backup` documentada en `DEVELOPMENT.md` inspecciona o restaura el archivo en un perfil nuevo sin sobrescribir datos existentes.

## Sincronizar Windows y Android

Desde la versión 0.4.4 se eliminó el intercambio manual de paquetes como mecanismo de sincronización. Windows
actúa como nodo local y Android se conecta directamente, tanto en la Wi-Fi de casa como desde datos
móviles, mediante una red privada de Tailscale:

1. Instala Tailscale en Windows y Android e inicia sesión en la misma tailnet.
2. Abre UniShare Windows. La aplicación activa su ruta privada HTTPS de Tailscale sin abrir puertos.
3. Ve a **Opciones > Conectar teléfono** y escanea con la cámara del teléfono el QR que
   muestra Windows. El QR contiene la dirección y la clave; no lo compartas.
4. Android abre UniShare, conserva el emparejamiento y descarga la biblioteca inmediatamente. Este paso
   sólo se repite al reinstalar la aplicación o borrar sus datos.

Los cambios de Android se envían al guardarlos. Mientras Android está abierto, consulta cambios de
Windows cada 15 segundos sin transferir la biblioteca si no cambió; al abrirse sincroniza de inmediato.
Si está cerrado, WorkManager reintenta con conectividad y realiza una comprobación periódica cuyo mínimo
lo fija Android en 15 minutos. No hay nube de datos: Tailscale sólo aporta conectividad WireGuard y
UniShare exige además su clave propia. Si un dispositivo está apagado, el otro conserva sus cambios y
los envía al reconectar. Las ediciones simultáneas nunca se resuelven por reloj: se conservan ambas y
aparecen en **Resolver conflictos**.

Android 0.5.1 vincula las conexiones privadas a la VPN activa y diferencia claramente entre
**Sincronizado** y **Pendiente**. Un error MagicDNS ofrece reintento y acceso directo a Tailscale.

Antes de una reconciliación Android y Windows comparan el inventario de hashes. Desde 0.6.9 cada réplica
registra las entidades modificadas y, después de la primera sincronización, envía sólo sus agregados
cambiados y los blobs ausentes. Los cursores avanzan únicamente cuando el receptor confirmó el paquete y
el emisor importó la respuesta; una interrupción reenvía el mismo delta de forma idempotente. Las retiradas
de etiquetas y colecciones se aplican a la identidad concreta observada y una nueva alta concurrente permanece.

En Windows, cerrar la ventana la oculta en la bandeja y mantiene activo el nodo de sincronización. Haz
doble clic en el icono para volver a abrirla; su menú permite activar **Iniciar con Windows** o elegir
**Salir completamente**. El inicio automático es optativo y funciona después de iniciar sesión, no como
servicio del sistema previo al login.

## Primer corte Android

`platforms/android` contiene una aplicación Kotlin/Compose para enlaces locales y recepción de
texto desde Sharesheet. Compila el APK lateral optimizado con el wrapper incluido:

```powershell
cd platforms/android
.\gradlew.bat :app:testDebugUnitTest :app:assembleRelease :app:lintRelease
```

Consulta [platforms/android/README.md](platforms/android/README.md) para SDK, instalación,
evidencia en emulador y limitaciones. Android admite enlaces, archivos, híbridos, metadatos completos,
organización, copia offline, respaldo portable, sincronización directa y conflictos explícitos.
Los archivos elegidos con SAF se contrastan por firma antes de entrar al CAS; HTML/SVG activo y tipos
contradictorios se rechazan sin dejar parciales, mientras Office, HEIC/AVIF, audio y vídeo conservan un
tipo útil para abrirlos después.

## Obsidian y extensión del navegador

En Windows, **Opciones > Exportar a Obsidian** crea una carpeta `UniShare` dentro de la bóveda con
una nota estable por UUID y sus adjuntos. Las siguientes exportaciones actualizan sólo el bloque
gestionado y conservan `Notas editables`. **Importar notas desde Obsidian** trae únicamente ese bloque
después de una confirmación explícita. `UniShare/Índice.md` ofrece una entrada navegable compatible con
Obsidian de escritorio y móvil.

La extensión sin compilar de `integrations/chromium-extension` permite guardar la pestaña actual en
Chrome o Edge. Puede conservar sólo el enlace o crear una copia offline con el texto que JavaScript ya
renderizó y hasta 12 imágenes visibles. Consulta su `README.md`; se conecta sólo al nodo local autenticado
de UniShare.

## Accesibilidad

Windows admite alto contraste, tres tamaños de texto desde **Opciones y herramientas**, foco visible y
los atajos `Ctrl+L` (enlace), `Ctrl+F` (buscar), `Ctrl+N` (nuevo), `Ctrl+E` (editar) y `F6` (cambiar de
región). Android adapta la navegación a pantallas estrechas o texto ampliado, publica nombres de acción
para TalkBack y permite desplazar todos los formularios largos.

## Desarrollo y publicación

- [DEVELOPMENT.md](DEVELOPMENT.md): toolchains, compilación, pruebas y perfiles temporales.
- [docs/architecture.md](docs/architecture.md): arquitectura y límites entre componentes.
- [docs/requirements.md](docs/requirements.md): requisitos y alcance verificable.
- [docs/PUBLISHING.md](docs/PUBLISHING.md): crear un repositorio público nuevo sin copiar historial ni
  archivos privados.

No publiques bibliotecas reales, respaldos, claves de emparejamiento, nombres privados `.ts.net`, rutas
de usuario ni certificados. Las contribuciones se distribuyen bajo la [licencia MIT](LICENSE).
