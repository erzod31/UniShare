# ADR-0027: revisión lógica, integridad de réplicas y Android 17

- Estado: aceptado
- Fecha: 2026-10-15

## Contexto

La revisión profunda de 0.6.16 encontró cuatro puntos que podían superar una compilación correcta y
fallar sólo con datos reales: la revisión del servidor dependía del tamaño y fecha de SQLite/WAL; un
respaldo leía sus tablas sin una instantánea común; la negociación de blobs trataba existencia o tamaño
como prueba de integridad; y Android 17 bloquea conexiones locales de aplicaciones dirigidas a API 37
si el usuario no concede `ACCESS_LOCAL_NETWORK`.

También estaban disponibles versiones estables conjuntas de API 37, AGP 9.4.1, Gradle 9.8.1 y Compose,
por lo que la restricción temporal registrada en ADR-0021 ya no era vigente.

## Decisión

- SQLite Windows pasa al esquema v5. Triggers transaccionales incrementan `content_revision` al cambiar
  items, assets o membresías; la API publica un hash de identidad de dispositivo y revisión lógica.
- La lectura de un respaldo usa una única transacción de lectura para que metadatos y relaciones
  pertenezcan al mismo estado confirmado.
- Android y Windows calculan SHA-256 antes de anunciar un blob disponible. Una copia dañada se declara
  ausente y puede repararse desde la otra réplica.
- Android valida encabezados causales antes de importar la respuesta incremental.
- Android adopta compile/target SDK 37, AGP 9.4.1, Gradle 9.8.1 y Kotlin incorporado. Solicita el nuevo
  permiso de red local sólo en Android 17 o posterior y conserva el flujo anterior en sistemas previos.
- El exportador de Obsidian verifica adjuntos por hash, repara copias dañadas y retira únicamente notas y
  adjuntos gestionados por UniShare que dejaron de existir, preservando archivos del usuario.

## Consecuencias

Los checkpoints del WAL ya no provocan sincronizaciones ficticias ni pueden ocultar cambios lógicos. La
negociación cuesta una lectura SHA-256 de los blobs candidatos, elegida deliberadamente para priorizar
conservación sobre una optimización basada sólo en tamaño. Android 17 muestra un diálogo adicional al
primer uso de la sincronización. La interacción física con ese diálogo queda marcada como no verificada
hasta disponer de un dispositivo Android 17; compilación, manifest, política de versión y límites están
cubiertos automáticamente.
