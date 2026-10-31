# ADR 0030: pies originales, identidad y variantes TikTok

Fecha: 2026-10-26  
Estado: aceptada; complementa ADR 0022 y modifica la política multimedia de ADR 0026

## Causa comprobada

TikTok publica el pie en `itemStruct.desc`, no necesariamente en párrafos semánticos. Su HTML cambia
según cliente: el mismo post público entrega `webapp.video-detail` a Windows y
`webapp.reflow.video.detail` al agente Android. Leer sólo la primera estructura falla en el teléfono.
Además, el filtro de prosa descartaba pies cortos, hashtags, emojis y texto idéntico al título.

## Decisión

- Leer sólo rutas conocidas de los scripts `__UNIVERSAL_DATA_FOR_REHYDRATION__`, `SIGI_STATE` y
  `sigi-persisted-data`. Las variantes universal de escritorio y móvil y el antiguo `ItemModule`
  requieren coincidencia exacta del ID de publicación; no buscar `desc` indiscriminadamente.
- Un pie identificado vacío es un resultado válido, no una invitación a usar navegación o publicidad.
  Para pies de TikTok y publicaciones multimedia/sociales JSON-LD se conserva el texto literal:
  saltos, enlaces, hashtags y emojis. Hasta 4000 puntos de código; los mayores llevan elipsis.
  Los títulos conservan el límite de 500 unidades UTF-16 sin cortar un par sustituto.
- Interpretar JSON-LD como árbol JSON en los tres clientes. Tipos de artículo siguen usando prosa;
  tipos multimedia/sociales usan `description`, `caption` o `text`. Se comprueba `mainEntityOfPage`,
  `url` o `@id` cuando existen; se eliminan sólo parámetros de seguimiento, no identificadores de
  consulta. Se prefiere identidad explícita; candidatos ambiguos no se adivinan.
- Como alternativa se permite una sola consulta pública a oEmbed del mismo TikTok. La URL enviada
  elimina consulta y fragmento; no se envían cookies, claves ni sesión. Deben coincidir proveedor e ID.
  La respuesta moderna que sólo contiene iframe no proporciona una descripción. 403/404/429 y fallos
  de esa alternativa no descartan el título ya obtenido. Se mantiene la política DNS/IP por solicitud.
- La extensión puede leer un único pie visible identificado en el DOM, fuera de navegación. No se
  añade un navegador ni un servicio de scraping a Windows/Android. No se resuelven desafíos de acceso.
- `OriginDescriptionFound` sólo existe en el resultado transitorio del extractor. La migración de
  enriquecimiento v4 conserva notas manuales y reconsulta enlaces existentes, sin migración SQLite ni
  cambio de formatos persistidos/sincronizados. Se mantiene la acción manual de reintento.

## Verificación y límites

Los fixtures compartidos se ejecutan en .NET, Android JVM, Android nativo y Chromium. La variante móvil
se detectó mediante una prueba HTTP real que falló antes de incorporar su ruta, con los agentes reales
de las aplicaciones. La publicación pública usada en documentación oficial devuelve el mismo SHA-256
de pie en las variantes de escritorio y móvil. Los casos privados, retirados, regionales o bloqueados
pueden seguir sin descripción; conservar el enlace y dejar el campo vacío es preferible a inventarla.
Las pruebas no demuestran acceso universal a TikTok ni a todas las redes sociales.

## Fuentes primarias consultadas

- [TikTok Embed Videos](https://developers.tiktok.com/docs/en/embed-videos): transición del embed
  antiguo al Player; no depender exclusivamente del ejemplo oEmbed heredado.
- [TikTok Embed Player](https://developers.tiktok.com/docs/en/embed-player): respuesta oEmbed con
  iframe e ID que puede no contener título/autor.
- [Extractor original de yt-dlp](https://github.com/yt-dlp/yt-dlp/blob/master/yt_dlp/extractor/tiktok.py):
  referencia de formatos de datos publicados; no se copia código ni se añade una dependencia.
- [Schema.org VideoObject](https://schema.org/VideoObject) y
  [SocialMediaPosting](https://schema.org/SocialMediaPosting): descripción del objeto concreto.
- [JSON-java](https://github.com/stleary/JSON-java): dependencia Public Domain sólo de pruebas JVM.

Las rutas `webapp.reflow.video.detail` y el comportamiento de acceso se comprobaron directamente en
HTML público el 2026-10-26; son observaciones reproducibles, no un contrato estable del proveedor.
