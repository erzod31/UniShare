# ADR 0011: sincronización portable y conflictos explícitos

Fecha: 2026-09-24. Estado: reemplazado por ADR 0012.

## Contexto

UniShare debe intercambiar datos entre Android y Windows sin cuenta, nube ni servidor obligatorio. Los
timestamps físicos no permiten distinguir de forma segura cambios concurrentes y no pueden usarse como
Last-Write-Wins. Ya existe un formato de backup ZIP v1 verificado e interoperable.

## Decisión

La primera sincronización bidireccional reutiliza el formato portable y añade `source_device_id` estable
al manifest. Cada receptor valida rutas, cobertura de checksums, tamaños y SHA-256 antes de fusionar.
Mantiene una base por `(peer_device_id,item_id)` con los hashes local/remoto observados:

- elemento ausente: importar;
- contenido idéntico: no hacer nada;
- sólo cambió el remoto: aplicar remoto;
- sólo cambió el local: conservar local;
- primera divergencia, cambios en ambas ramas o conflicto pendiente: conservar ambos snapshots.

La resolución requiere escoger expresamente la versión local o recibida. Las etiquetas y colecciones se
fusionan por nombre normalizado; las membresías nuevas son aditivas y una retirada ambigua no borra datos.
Los blobs sólo se publican en CAS después de verificar su hash.

## Consecuencias

El modo funciona offline y el usuario controla el canal de transporte. Reimportar es idempotente y no
duplica blobs. No existe pérdida silenciosa por reloj. A cambio, en 0.2.0 el intercambio es manual y el
estado de conflictos vive en cada réplica; pairing LAN, mTLS, ACK y revocación siguen siendo un hito posterior.
