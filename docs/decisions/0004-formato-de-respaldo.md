# ADR-0004: respaldo portable versionado, no volcado SQLite

- Estado: aceptado
- Fecha: 2026-09-24

## Decisión

El respaldo completo es un ZIP con:

```text
manifest.json
checksums.sha256
metadata/items.ndjson
metadata/assets.ndjson
metadata/tags.ndjson
metadata/collections.ndjson
metadata/tag_memberships.ndjson
metadata/collection_memberships.ndjson
blobs/aa/bb/<sha256>
```

El manifest v1 contiene versión, fecha, recuentos y bytes de blobs. Cada entrada salvo el propio fichero de checksums está cubierta por SHA-256. Los metadatos son NDJSON legible y los blobs conservan direccionamiento por contenido.

## Seguridad y recuperación

- Exportar usa un archivo parcial y rename; nunca sobrescribe silenciosamente.
- Inspeccionar valida rutas, duplicados, límites, recuentos, checksums y nombre/hash de blobs.
- Restaurar sólo acepta un perfil inexistente, construye en una ruta temporal, activa claves foráneas, ejecuta `integrity_check` y mueve al destino después del commit.
- La identidad local se genera de nuevo; no se restauran credenciales ni identidad de dispositivo.
- Los UUID de items, assets, tags, colecciones y membresías se conservan.
- FTS se reconstruye por triggers durante la importación.

## Alternativas descartadas

- Copiar `library.db`: acopla el contrato al almacenamiento interno, puede capturar WAL incoherente y heredar identidad local.
- JSON monolítico: exige memoria proporcional a toda la biblioteca y dificulta rescate parcial.
- Sobrescribir el perfil activo: eleva el riesgo de pérdida y conflictos con conexiones abiertas.

El cifrado de backups queda como ampliación explícita; la versión v1 debe tratarse como contenido sensible.
