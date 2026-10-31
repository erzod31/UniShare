# ADR 0029: notas externas conservadoras y compilación desde fuente pública

Fecha: 2026-10-16  
Estado: aceptada

- Obsidian sólo actualiza su bloque gestionado. El frontmatter exige una identidad única y los cuatro
  marcadores deben ser únicos y estar ordenados. La exportación no sobrescribe destinos ajenos; la
  importación ignora notas ambiguas y elementos eliminados.
- La limpieza sólo retira notas gestionadas vacías y sin modificaciones externas. El contenido personal
  no importado, las propiedades, los apéndices y sus adjuntos se conservan fuera del índice activo. Es
  preferible una nota residual a perder una edición. No se añade una integración Android inexistente:
  la exportación/importación Obsidian se ofrece actualmente desde Windows.
- Android verifica la longitud final cuando el transporte la anuncia. Con gzip transparente Android
  elimina la cabecera de longitud comprimida; la prueba HTTP real asegura que no se compara contra
  los bytes descomprimidos. No se rechaza una respuesta válida de longitud desconocida.
- El ZIP fuente incluye `SOURCE_REVISION` mediante `export-subst`. El instalador acepta esa procedencia
  sin `.git`, usa `.archive` y no confunde el checkout de un directorio padre con el suyo. En Git se
  conserva la revisión exacta y `.dirty`. No se crean claves ni se modifica la seguridad aplazada.

Fuente primaria de compresión: [HttpURLConnection de Android](https://developer.android.com/reference/java/net/HttpURLConnection).
