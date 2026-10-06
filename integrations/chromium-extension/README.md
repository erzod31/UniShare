# Extensión Chromium de UniShare 0.6.16

La captura obtiene resúmenes del contenido semántico. En vídeos de YouTube usa la descripción original
publicada por la página y nunca convierte el pie de navegación completo en resumen.

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

El título se obtiene de Open Graph, Twitter Cards o HTML. El resumen se extrae del contenido semántico;
en YouTube se usa la descripción original del vídeo y se descartan URLs aisladas y menús. Si cambias el
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
