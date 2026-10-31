# ADR 0031: títulos de contenido y traducción voluntaria

Fecha: 2026-10-26  
Estado: aceptada para títulos; traducción en navegador sustituida por ADR 0033 desde 0.6.23.

- Título y descripción cumplen funciones distintas. Un título TikTok específico o el `headline`/`name`
  JSON-LD del elemento identificado se conserva como título. Si TikTok no ofrece un título separado,
  la primera línea del pie original es una alternativa verificable, sin generar texto artificial.
- `PageTitle` es un dato transitorio. La migración v6 sólo sustituye un título provisional de dominio
  o una etiqueta genérica TikTok que coincide exactamente con el título publicado por la página.
  Incluye la etiqueta global `TikTok - Make Your Day`. Un título personalizado, incluso parecido a
  la marca, se conserva. No cambia el esquema ni sync.
- Se incorpora una acción opcional **Traducir con Google Translate** en Windows y Android. La
  confirmación identifica proveedor y envío. El navegador recibe únicamente la descripción elegida
  y el destino del idioma de interfaz; el sitio permite escoger otros idiomas. El original no se
  reemplaza ni se guarda automáticamente el resultado como una edición del usuario.
- Si la URL resultante no es fiable por longitud, se ofrece copiar el texto completo y abrir el
  traductor para pegarlo. No se envía silenciosamente un texto truncado. Esta vía puede requerir
  fragmentar manualmente un texto que supere los límites propios del proveedor.
- La función sigue siendo opcional y requiere Internet. No es un motor integrado ni traducción offline.
  No se usa una API de Google sin documentar ni se extraen traducciones de páginas por scraping ni se
  añaden credenciales.
  El texto puede quedar en el historial del navegador y está sujeto a la privacidad del proveedor.
- Se consideraron ML Kit (modelos Android descargables de unos 30 MB y sin paridad directa Windows)
  y LibreTranslate (servicio/configuración adicional, con clave/coste en el hosting gestionado). No
  se incorporan esas dependencias para esta acción sencilla y reversible.

Fuentes primarias:

- [TikTok Video Object](https://developers.tiktok.com/docs/en/tiktok-api-v2-video-object): distingue
  `title` y `video_description` y advierte la diferencia del sitio entre móvil y escritorio.
- [Google Translate: traducir texto](https://support.google.com/translate/answer/6142478) y
  [privacidad de Google](https://policies.google.com/privacy). La URL con texto y destino se verificó
  realmente en un navegador aislado con contenido sintético y sin cuenta.
- [ML Kit Android](https://developers.google.com/ml-kit/language/translation/android) y
  [LibreTranslate](https://docs.libretranslate.com/): alternativas estudiadas, no redistribuidas.
