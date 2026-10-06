# ADR 0026: descripciones de contenido multimedia y fallback conservador

Fecha: 2026-10-06  
Estado: aceptada; complementa ADR 0022

## Contexto

YouTube entrega la ficha del vídeo en datos de la propia página, pero su interfaz no representa la
descripción como párrafos HTML estables. Al no encontrarlos, UniShare 0.6.12 convertía todo `<body>` en
texto. En algunas localizaciones el pie quedaba concatenado —por ejemplo copyright, contacto,
creadores, condiciones y privacidad— y superaba por longitud el filtro mínimo. Después, la migración
trataba ese valor automático como si fuera una nota manual.

## Decisión

- Para URLs de vídeo de YouTube se usa primero `videoDetails.shortDescription`, publicado en la
  respuesta HTML inicial. No se añade API key, servicio remoto adicional ni dependencia.
- JSON-LD admite también `description` únicamente para tipos de contenido conocidos como
  `Article`, `VideoObject`, `AudioObject` o `PodcastEpisode`; una descripción arbitraria del sitio no
  se confunde con el elemento guardado.
- Las descripciones estructuradas se dividen por líneas. Se descartan URLs aisladas, navegación,
  fragmentos sin prosa y transiciones CamelCase propias de etiquetas concatenadas; las frases restantes
  se conservan literalmente y en orden.
- Si no hay `articleBody`, descripción estructurada ni párrafos útiles, el resumen queda vacío. El
  texto completo de `<body>` sólo puede usarse dentro de una región semántica `article` o `main`.
- La migración v3 sólo sustituye un valor histórico cuando coincide con la descripción promocional
  anterior o con una firma compacta inequívoca de navegación. Las notas naturales se preservan.
- La política se mantiene equivalente en Windows, Android y la extensión Chromium.

## Evidencia

- Pruebas sintéticas cubren descripción de vídeo, `VideoObject`, navegación neerlandesa e inglesa,
  ausencia de fallback y conservación de una nota que menciona copyright, términos y privacidad.
- Sobre el perfil afectado, sin mostrar ni modificar sus URLs durante el diagnóstico, las cinco páginas
  respondieron dentro del límite de 2 MiB y el extractor produjo cinco resúmenes de 270–425 caracteres,
  todos derivados de la descripción del vídeo y ninguno de la navegación.

## Referencias primarias

- https://developers.google.com/youtube/v3/docs/videos — el recurso de vídeo define
  `snippet.description` como la descripción del vídeo.
- https://schema.org/VideoObject — `description` describe el objeto multimedia.
- https://html.spec.whatwg.org/dev/sections.html — `nav` representa bloques de navegación y los enlaces
  típicos de condiciones/copyright pertenecen habitualmente al pie, no al contenido principal.

## Consecuencias

- El resultado sigue siendo local, extractivo, verificable y sin reformulación generativa.
- Si YouTube cambia el nombre o formato de `shortDescription`, UniShare dejará el resumen vacío antes
  que guardar interfaz. La extensión conserva como alternativa los datos estructurados y el contenido
  semántico renderizado.
