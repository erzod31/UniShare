# ADR 0013: consolidar contrato portable y planificación de sincronización

Fecha: 2026-09-24. Estado: aceptado.

## Contexto

Respaldo y sincronización C# declaraban por separado los mismos registros y lectores JSON/NDJSON.
Android ejecutaba a la vez un envío inmediato y otro trabajo durable por cada cambio, y el trabajo
periódico transfería el snapshot incluso cuando la revisión del servidor no había cambiado.

## Decisión

- Mantener un único contrato portable interno en `PortablePackageContract`.
- Inyectar respaldo, blobs y fusión en vez de reconstruirlos dentro de cada servicio.
- Centralizar en Android el registro de resultados mediante `DirectSyncCoordinator`.
- Hacer un intento inmediato tras editar y crear un reintento forzado sólo si falla.
- En trabajo periódico, consultar primero la revisión ligera y no transferir si es igual a la última.

## Consecuencias

El formato de archivo y la semántica de conflictos no cambian. Se reduce la posibilidad de que respaldo
y sincronización diverjan, y se evitan transferencias completas duplicadas. La transferencia necesaria
sigue siendo un snapshot completo hasta implementar el protocolo incremental por operaciones y blobs.
