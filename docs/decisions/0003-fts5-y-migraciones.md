# ADR-0003: FTS5, prefijos y migraciones incrementales

- Estado: aceptado
- Fecha: 2026-09-24

## Contexto

La búsqueda debe responder sobre 100 000 items y sobrevivir a importaciones/migraciones. `LIKE '%texto%'` era correcto funcionalmente, pero obliga a escanear filas y no ofrece un índice reconstruible.

## Decisión

- Esquema v2 añade FTS5 con tokenizer Unicode y eliminación de diacríticos.
- Indexar título, URL, fuente, autor y descripción; tags/colecciones se consultan por relaciones activas.
- Usar búsqueda de prefijos por token y limitar resultados a 200 por defecto (máximo 500).
- Mantener triggers de insert/update/delete y usar el comando FTS5 `rebuild` durante migración/importación.
- Rechazar una versión de esquema futura con un error claro.
- Exponer filtros combinables de tipo, favorito, rango temporal, tombstone y asset local.

## Evidencia y consecuencia

El primer intento de migración trató `SELECT rowid FROM items_fts` como prueba de que el índice tenía contenido. En una tabla FTS de contenido externo ese `SELECT` puede reflejar la tabla base aun con índice vacío. Una prueba v1→v2 reprodujo el fallo; se sustituyó por `INSERT INTO items_fts(items_fts) VALUES('rebuild')` dentro de la transacción.

Benchmark local: 100 000 items, 30 consultas tras warmup, p95 91,2185 ms, límite 300 ms. La generación tardó 8,307 s y no forma parte de la latencia consultada.
