# Extensión Chromium de UniShare 0.6.24

Las opciones incluyen **Actualizar extensión**. En una distribución de tienda, consulta y aplica el
paquete mediante el servicio nativo del navegador. El ZIP de desarrollo no tiene ese canal y lo
indica expresamente; recargar no descarga un binario nuevo. Ver [APP_UPDATES](../../docs/APP_UPDATES.md).

La captura obtiene resúmenes del contenido semántico. En vídeos de YouTube usa la descripción original
publicada por la página y nunca convierte el pie de navegación completo en resumen.
Desde 0.6.20 también lee el pie del post TikTok exacto en datos actuales de escritorio/móvil y legacy.
Si faltan, puede usar un único pie visible identificado por la página, fuera de menús y pies de navegación.
Conserva hashtags, emojis, enlaces y textos cortos. Los posts multimedia/sociales JSON-LD se asocian a
su URL; una recomendación o la descripción comercial del sitio no sustituye el contenido.
0.6.21 prioriza también el título específico de TikTok y `headline`/`name` del objeto JSON-LD exacto.
La traducción de descripciones guardadas se ofrece en los clientes Windows y Android, no en el popup
de captura de la extensión.
0.6.22 usa un encabezado de hasta 80 caracteres para el título TikTok cuando no hay uno específico;
el pie completo, sus enlaces y hashtags permanecen en la descripción enviada a UniShare.

## Instalación normal

La distribución definitiva es Microsoft Edge Add-ons: Edge guarda y actualiza internamente la extensión,
sin depender de una carpeta descomprimida. El paquete de envío, la ficha, los iconos y la política de
privacidad están descritos en `PUBLISHING.md`. La publicación requiere la cuenta Partner Center del
propietario y la revisión de Microsoft.

## Prueba local antes de publicar

1. Mantén abierta la aplicación UniShare para Windows.
2. En Chrome o Edge abre la página de extensiones, activa el modo desarrollador y elige
   **Cargar descomprimida**.
3. Selecciona esta carpeta `integrations/chromium-extension`.
4. Abre las opciones de la extensión y pega el puerto y la clave que muestra UniShare en
   **Opciones y herramientas → Conectar teléfono**, dentro de **Datos de conexión para Edge**.
   En esa misma pantalla puedes elegir español o inglés.
5. En cualquier página HTTP/HTTPS, pulsa el icono de UniShare, revisa el título y la carpeta, elige si
   quieres una copia offline y guarda.

El título se obtiene del post original cuando existe y de Open Graph, Twitter Cards o HTML como alternativa.
En artículos el resumen se extrae del contenido semántico; los pies de publicaciones conservan su texto
literal, incluidos enlaces. Si el sitio restringe el acceso o no publica un pie verificable, queda vacío.
Si cambias el
título en el panel antes de guardar, tu edición tiene prioridad y no se sustituye por el valor de la
página.

La extensión se comunica sólo con `127.0.0.1`: el enlace queda almacenado por la aplicación local y no
se envía a un servicio externo. La clave se guarda en el almacenamiento local del perfil del navegador.

La copia offline toma el texto que la página ya renderizó mediante JavaScript y hasta 12 imágenes visibles.
Las imágenes incompatibles con la política de origen del navegador se omiten. Windows vuelve a validar el
contenido y genera un documento estático con CSP sin scripts, conexiones, marcos, vídeo ni formularios.

La versión 0.6.10 se validó en Microsoft Edge con un perfil aislado: opciones, popup sobre una pestaña HTTP
real y guardado offline contra un nodo UniShare local. SQLite conservó título de origen, fuente, autor,
resumen literal, colección y el snapshot inerte. La instalación estable continúa requiriendo Edge Add-ons.
Si sólo quieres un marcador, desactiva **Guardar también una copia offline**.

La validación `packaging/chromium/test-extension.ps1` ejecuta además los contratos comunes de metadatos
con `node --test tests/chromium/metadata.test.cjs`; Node sólo es necesario para desarrollo y empaquetado.
