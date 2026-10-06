# ADR 0006: captura HTML inerte

Fecha: 2026-09-24  
Estado: aceptada

## Contexto

Guardar una página HTML original y abrirla desde disco puede ejecutar scripts, cargar trackers o resolver
recursos remotos. UniShare necesita lectura offline sin convertir el archivo capturado en contenido activo.

## Decisión

Para la primera implementación, UniShare no conserva el DOM ejecutable como vista offline. Elimina bloques
activos conocidos, convierte etiquetas de bloque en saltos, retira el resto de etiquetas, decodifica entidades
y vuelve a codificar todo el texto dentro de un documento generado por la aplicación. El documento incluye
CSP `default-src 'none'`, no contiene enlaces activos ni recursos externos y tiene extensión `.offline.html`.
La conversión se aplica asimismo si la extensión sugiere HTML/XHTML/SVG aunque la cabecera MIME no coincida.

La entrada HTML se limita a 16 MiB antes de cargarla para extracción. El SHA-256 se calcula sobre la instantánea
inerte que realmente se conserva. La URL original permanece en el `Item` y aparece sólo como texto codificado.
El título remoto se conserva en la instantánea; fuente, autor y descripción se incorporan sólo a campos
vacíos del `Item`, junto con el asset en una transacción.

## Consecuencias

La captura es segura, portable y legible sin red, pero pierde diseño, imágenes y contenido generado por
JavaScript. Una extracción de mayor fidelidad podrá sustituir esta etapa si mantiene los mismos límites de
seguridad y una licencia compatible; el HTML original nunca se abrirá silenciosamente como snapshot confiable.
