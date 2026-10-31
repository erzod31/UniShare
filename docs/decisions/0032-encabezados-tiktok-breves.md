# ADR 0032: encabezados TikTok breves

Fecha: 2026-10-26  
Estado: aceptada; sustituye la alternativa larga de ADR 0031

La primera alternativa usaba hasta 500 unidades UTF-16 de la primera línea del pie. Aunque procedía del
contenido, el usuario confirmó que resultaba demasiado larga como título. Se mantiene la prioridad de
un título específico publicado; cuando falta, la alternativa pasa a ser un encabezado de **hasta 80
puntos de código**, con corte preferente por palabra y elipsis si hace falta. No se generan frases ni
se resume con un modelo externo.

Las listas finales de hashtags/URLs se conservan en la descripción completa; no se elimina un hashtag
único que identifica el tema del encabezado. Se respetan Unicode y títulos breves existentes. La regla
está en Application para Windows y se reproduce en Android/Chromium con fixtures compartidos.

La migración v7 sólo sustituye títulos genéricos conocidos o aquellos que coinciden exactamente con
la alternativa antigua calculada a partir del pie de origen. Un título personalizado distinto, incluso
largo, permanece intacto. No cambia SQLite, el formato portable ni el protocolo de sincronización.

Windows vuelve a leer el elemento después de la petición HTTP, para combinar con sus valores actuales
y no aplicar información a un elemento retirado durante la espera. Las descripciones se mantienen
con la política de conservación anterior.

Evidencia: los 16 títulos antiguos del perfil medían más de 80 caracteres y llegaban a 500; tras aplicar
la migración, los 16 quedaron por debajo del límite, con máximo observado 79. Backups comparados y
fixtures demuestran conservación de descripciones, URLs, identidades, organización y títulos manuales.
