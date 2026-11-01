# Actualizar la aplicación / Update the application

## Español

El candidato 0.6.25 incorpora **Opciones y herramientas > Actualizar UniShare…** en Windows y una
tarjeta **Actualizar UniShare** en **Ajustes** de Android. Muestra la versión instalada, el canal,
el resultado de buscar y el progreso de descarga. Descarga sólo después de pulsar el botón.

El canal estable real es `https://api.github.com/repos/erzod31/UniShare/releases/latest`, configurado
en `shared/updates/channel.json`. El 02/11/2026 sigue publicando 0.6.16; los candidatos locales posteriores
no se ofrecen como publicados y nunca se instala una versión anterior. Si no hay una nueva release,
la app lo indica. Red ausente, rate limit, manifiesto incompleto o verificación fallida permiten
reintentar sin instalar nada. No se envían elementos, claves de emparejamiento ni credenciales.

Se exige versión X.Y.Z mayor, un único activo con nombre exacto, tamaño limitado y SHA-256 de la
respuesta HTTPS autenticada de GitHub. La descarga rechaza HTTP y redirecciones ajenas a los hosts
de activos de GitHub. Se compara otra vez archivo/hash/identidad antes de aplicar.

Windows comprueba el PE del instalador, nombre de producto/versión y cualquier Authenticode presente.
Un cliente ya firmado exige el mismo certificado. La cadena histórica no está firmada: la UI lo
advierte expresamente y el hash no se presenta como firma. Al confirmar, abre el asistente Inno
normal y cierra sólo su instancia. El asistente conserva AppId/datos/perfil y reabre tras terminar.
Si se cancela el asistente, puede ser necesario abrir UniShare manualmente. Desde un ZIP portátil
se instala la edición por usuario con el perfil actual; la carpeta portátil no se sobrescribe.

Android compara packageName, versionName, versionCode creciente y certificado con la instalación.
Si falta permiso para instalar desde UniShare, abre la pantalla del sistema: vuelve a la app y
pulsa Instalar de nuevo tras concederlo. El instalador Android realiza la verificación final de la
firma y exige confirmar la actualización; al terminar ofrece **Abrir**. No desinstales antes.
Las compilaciones `.qa` no instalan paquetes de producción. La caché de actualización es privada
y el FileProvider expone sólo su subcarpeta de APK con permiso temporal de lectura.

La extensión ofrece en sus opciones el mecanismo nativo del navegador, que descarga y verifica el
paquete de tienda. La aplicación respeta sus límites de consulta y exige confirmar antes de aplicar
el paquete pendiente. **El ZIP de desarrollo carece de canal binario**: necesita una publicación e
instalación desde Edge Add-ons/Chrome Web Store; recargar no descarga un paquete nuevo. No se inventa
una URL/identidad de tienda ni se anuncia esa publicación como terminada.

**Transición inicial:** 0.6.23 y anteriores carecen de este código. Hay que instalar manualmente el
primer candidato que lo incorpora. Esto no actualiza retroactivamente los binarios ya instalados.
Los candidatos DG-0034 no están publicados y las pruebas nunca ejecutan instaladores/desinstaladores
reales en el Windows compartido. La aplicación real del cambio, UAC, permisos/instalación Android y
reapertura tras sustituir el binario requieren validación posterior en VM/dispositivo independiente.

## English

Candidate 0.6.25 adds **Options and tools > Update UniShare…** on Windows and an **Update UniShare**
card in Android **Settings**. Check, review the available version, download, then confirm installation.
The real GitHub Releases channel currently publishes 0.6.16; unpublished candidates are not advertised
as live updates. Older versions, wrong packages, invalid hashes, truncated downloads and mismatched
signatures are rejected. Canceling a download does not install anything.

Windows hands the verified package to the ordinary Inno wizard, preserving its identity and profile;
successful updates reopen the application. If the wizard is canceled, reopen manually. Android
requires the system's permission and update confirmation and offers **Open** after installation.
The original unsigned Windows lineage remains explicitly disclosed; a signed client requires its
existing signing certificate. No private keys or library data are sent to the channel.

Browser extensions use their browser-managed store update mechanism. The unpacked development ZIP
cannot update its binary through a reload and still needs a real store publication/installation.
Versions up to 0.6.23 require one initial manual installation to gain this updater. No candidates
are published or applied to the user's installations by DG-0034.

## Mantenimiento / Maintenance

- Mantener el repositorio público real en `shared/updates/channel.json`; no poner tokens en él.
- Publicar sólo tras aprobación externa una release estable `vX.Y.Z` con los nombres exactos
  `UniShare-Windows-Setup-X.Y.Z.exe` y `UniShare-Android-X.Y.Z.apk`. GitHub debe aportar su digest
  `sha256:` y tamaño; un activo sin ellos se rechaza, aunque exista un archivo de checksums aparte.
- Conservar AppId/packageName/certificados y aumentar versión y versionCode. Preparar rotaciones de
  firma como una migración explícita; no retirar validaciones para admitir otra clave.
- La muestra `packaging/updates/release.example.json` contiene marcadores y es deliberadamente
  inválida hasta una publicación real. Nunca sirve como canal de producción.
- Pruebas .NET/JVM/JS cubren contratos, errores, cancelación y lanzadores falsos; no equivalen a
  instalar/reemplazar realmente un binario. Se requiere VM independiente para esa última prueba.

## Preparación 0.6.25 / Release preparation

La extensión comprueba identidad propia, tipo normal de instalación y endpoint oficial de Edge o Chrome.
Aplicar requiere una actualización pendiente del navegador; una URL HTTPS arbitraria ya no basta.
Se preparan expedientes separados de ambas tiendas, sin IDs inventados. El canal estable real sigue
en 0.6.16 y no se ha activado una publicación durante esta preparación.

Procedimiento y límites: [RELEASE_PREPARATION](RELEASE_PREPARATION.md).
