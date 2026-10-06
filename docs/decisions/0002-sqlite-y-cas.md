# ADR-0002: SQLite explícito, outbox transaccional y blobs CAS

- Estado: aceptado
- Fecha: 2026-09-24

## Contexto

UniShare debe sobrevivir sin red, deduplicar archivos, permitir migraciones y no sincronizar bases SQLite. El registro de operaciones no puede quedar desalineado del estado local.

## Decisión

- Usar `Microsoft.Data.Sqlite` 10.0.x fijado centralmente (10.0.12 en la entrega 0.6.5) con SQL explícito,
  claves foráneas, WAL, `synchronous=FULL` y timeout finito.
- Escribir `Item`, `Asset` y `Operation` en una misma transacción.
- Mantener UUID en texto canónico durante el primer esquema para facilitar inspección y evitar diferencias binarias de `Guid`; una optimización futura a BLOB exige migración y fixtures.
- Guardar bytes fuera de SQLite bajo `blobs/aa/bb/<sha256>`.
- Copiar en streaming a `staging`, sincronizar el archivo, calcular SHA-256 y promover mediante rename antes de referenciarlo.
- Permitir varios `Asset` lógicos con el mismo hash, pero una sola copia física.

## Consecuencias

- El formato es inspeccionable y la UI no depende de un ORM.
- Copiar el archivo antes de la transacción puede dejar un blob CAS no referenciado si el commit falla; nunca deja un item apuntando a bytes ausentes. Se añadirá recolección segura de blobs sin referencias y test de fallo inyectado antes de distribución.
- Las operaciones actuales ya tienen identidad/origen/contador/payload/contexto, pero el merge causal completo pertenece a H9.
