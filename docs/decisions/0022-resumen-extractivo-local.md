# ADR 0022: resumen extractivo local del contenido

Fecha: 2026-10-05  
Estado: aceptada

## Decisión

- El resumen automático se obtiene del contenido del elemento, nunca de `meta description`,
  `og:description` ni `twitter:description`.
- La prioridad es `Article.articleBody` en JSON-LD y después el bloque `<article>` o `<main>` con mayor
  cantidad de texto útil; no se toma ciegamente la primera tarjeta de la página. Sólo como último
  recurso se examinan bloques sustanciales del cuerpo.
- Se eliminan regiones `nav`, `aside`, `header`, `footer`, formularios, diálogos y contenido activo. Se
  rechazan párrafos cortos, boilerplate conocido y bloques donde más de la mitad del texto son enlaces.
- El resultado conserva literalmente hasta cuatro frases en orden de aparición, se detiene alrededor de
  260 caracteres y nunca supera 600. No hay reformulación generativa ni llamada a una IA o nube.
- La descripción promocional se conserva sólo temporalmente en memoria para reconocer valores
  automáticos de 0.6.7. Si coincide exactamente, la migración puede sustituirla por el resumen nuevo o
  vaciarla si la página no expone cuerpo legible. Un valor distinto se considera una nota del usuario y
  se conserva.

## Motivo

`<main>` representa el contenido dominante y único de una página, mientras que `<article>` identifica
una composición autocontenida. Schema.org define `articleBody` como el cuerpo real de un artículo.
Mozilla Readability también separa `textContent` y `excerpt` del documento y elimina regiones con alta
densidad de enlaces. Estas señales describen el elemento guardado mejor que los textos preparados para
buscadores o redes sociales.

Referencias:

- https://github.com/mozilla/readability
- https://schema.org/articleBody
- https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/main
- https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/article
- https://developers.google.com/search/docs/appearance/structured-data/sd-policies

## Consecuencias

- Windows, Android y Chromium producen resúmenes comparables sin servicio externo ni dependencia nueva.
- Algunas aplicaciones web que sólo renderizan el contenido después de ejecutar JavaScript pueden dar
  resumen únicamente al guardarse desde la extensión. Las aplicaciones nativas dejan el campo vacío si
  la respuesta HTML no contiene contenido representativo.
- El algoritmo es deliberadamente extractivo: es verificable y privado, pero no pretende interpretar,
  traducir ni reescribir el texto.
