# Arquitectura de UniShare

## Principios

- El estado autoritativo vive localmente en cada dispositivo.
- UI, almacenamiento, captura, búsqueda, importación, sync e integración del sistema dependen de contratos, no entre sí.
- SQLite es una implementación local, nunca el protocolo de sincronización.
- Los blobs son inmutables y se identifican por SHA-256.
- Toda entrada externa es no confiable.
- Los relojes físicos son informativos, no una regla de resolución causal.

## Componentes previstos

```text
Windows WPF/.NET 10       Android Compose/Kotlin
          |                         |
          +--- casos de uso --------+
                    |
           contratos de dominio
          /    |       |       \
 repositorio  CAS   búsqueda  operación/outbox
      |                          |
   SQLite                 motor causal/sync
                                  |
                         HTTPS LAN + mTLS

Chromium -> Native Host -> inbox durable
Obsidian -> bridge de archivos inmutables
Backup   -> manifest + NDJSON + blobs por hash
```

El código no se comparte entre Kotlin y C#. Se comparten contratos, fixtures y escenarios para evitar FFI prematuro.

## Modelo inicial

- `Item`: UUID, tipo, título, URLs opcionales, metadatos, fechas y tombstone.
- `Asset`: UUID, relación a item, hash, tamaño, MIME, nombre original y origen.
- `LocalAsset`: disponibilidad y ruta relativa por dispositivo.
- `Tag`, `Collection` y relaciones con identidad estable.
- `Operation`: UUID, dispositivo, contador, entidad, versión, payload y contexto causal.
- `Conflict`: versiones concurrentes sin resolver.
- `DownloadJob`: estado durable de transferencia.

## Límites e interfaces

- Dominio: tipos y reglas puras, sin referencias de UI o SQLite.
- Aplicación: casos de uso y puertos (`IItemRepository`, `IBlobStore`, `ISearchIndex`, `IBackupService`, `ISyncEngine`).
- Infraestructura: SQLite, filesystem, red, serialización y migraciones.
- Presentación: WPF y Compose; transforma estado, no ejecuta SQL.
- Integraciones: adaptadores aislados para Share Target, Sharesheet, extensión y Obsidian.

## Persistencia

El cambio de entidad, las relaciones y la operación de outbox se escriben en una transacción. SQLite usa claves foráneas, WAL y timeout finito. Las rutas almacenadas son relativas a una raíz del perfil y se revalidan al resolverlas.

El esquema Windows v4 añade `sync_item_state`; v3 añadió `download_jobs`. Una transferencia escribe primero en
`staging/download-<uuid>.partial`; sólo tras comprobar longitud y calcular SHA-256 se promueve al
CAS. La creación del `Asset`, el cambio `LINK` a `HYBRID`, la finalización del trabajo y su operación
de outbox comparten una transacción SQLite. Los trabajos pausados o fallidos conservan validadores
HTTP para reanudarse de forma segura; cancelar elimina el parcial.

Las firmas conocidas prevalecen sobre extensiones o cabeceras MIME no confiables. El HTML remoto no
se publica tal cual: se elimina contenido activo, se extrae texto, se codifica de nuevo y se genera un
documento local con CSP `default-src 'none'`. La captura básica está limitada a 16 MiB para acotar memoria.
El título remoto se usa dentro de la instantánea; fuente y autor sólo rellenan campos vacíos. El resumen
extractivo procede de `articleBody`, `<article>` o `<main>` y no de metadatos promocionales. Una migración
puede reemplazar descripciones automáticas anteriores cuando coinciden exactamente, pero conserva notas
distintas. La publicación del asset y los metadatos es una única transacción. Trabajos persistidos en
`Running` pueden retomarse al arrancar de nuevo.

Layout previsto:

```text
profile/
  library.db
  blobs/ab/cd/<sha256>
  staging/<job-id>.partial
  quarantine/<job-id>/
  previews/<sha256>/<variant>
```

Android mantiene un esquema v5 separado. Recibe documentos mediante SAF, los transmite a
`files/staging/<uuid>.partial` y los promueve a `files/blobs/aa/bb/<sha256>` después de validar tamaño
y hash. Durante esa misma copia inspecciona 512 bytes: las firmas conocidas determinan el MIME,
los contenedores Office/HEIF/AVIF conservan un tipo útil y HTML/SVG o firmas contradictorias se rechazan
antes del CAS. `items` admite LINK/FILE/HYBRID y `assets` referencia el contenido; varias filas pueden
compartir el mismo blob. La migración v1→v2 se comprobó sobre datos reales. Este diseño es un corte
local provisional: el contrato de backup será la frontera interoperable y no el archivo SQLite.
La exportación Android materializa ese contrato como ZIP portable v1 (manifest, identidad del dispositivo,
NDJSON, checksums y blobs CAS) en un temporal privado antes de copiarlo al URI SAF elegido. Ambos clientes
validan e importan el archivo sin copiar bases SQLite entre plataformas.

## Conflictos y sincronización

- Duplicados: `op_id` y `(device_id,counter)` hacen la aplicación idempotente.
- Tags/colecciones: OR-Set add-wins con identidad por alta.
- Campos escalares: registro multivalor; concurrencia conserva siblings.
- Borrado: tombstone visible con versiones recuperables.
- Blobs: manifest inmutable; disponibilidad local sólo tras hash final válido.

El envelope incluye `op_id`, origen, contador, entidad, tipo, versión, payload y dependencias causales. Los ACK sólo se emiten después del commit.

La implementación transporta directamente paquetes portables verificados por una API local que
sólo escucha en loopback. Tailscale Serve aporta HTTPS privado dentro de la tailnet; un bearer aleatorio
de 256 bits añade autenticación de aplicación. Cada réplica guarda, por dispositivo remoto e item, los
hashes local y remoto de la última observación. La igualdad es
idempotente; sólo una rama modificada se acepta automáticamente; dos ramas modificadas se almacenan como
conflicto con ambos snapshots. `updated_at` nunca decide el ganador. Los blobs se copian sólo después de
validar checksum, longitud y SHA-256. Antes del intercambio se negocia el inventario de hashes y cada
envelope incluye sólo blobs ausentes. Desde 0.6.9 Windows y Android mantienen un contador causal local:
la primera reconciliación o un cambio de identidad usa un snapshot completo; las siguientes seleccionan
en el registro de operaciones únicamente los items modificados y materializan sus agregados (item, assets,
etiquetas, colecciones y tombstones) en el mismo formato portable v1. El servidor confirma hasta qué
contador del teléfono incorporó y expone su propio cursor; Android persiste ambos sólo después de importar
la respuesta. Un reintento conserva los cursores anteriores, por lo que reenvía el mismo delta sin pérdida.
Si un cursor retrocede, el servidor responde con snapshot completo de recuperación. Etiquetas y colecciones
se unen por nombre normalizado. Cada membresía tiene UUID: retirar una alta observada propaga su tombstone,
mientras una re-alta concurrente con otra identidad no se borra.

Android envía al editar y abrir, sondea una revisión ligera mientras está visible y usa WorkManager con
backoff al estar cerrado. El trabajo periódico sólo transfiere cuando cambia la revisión remota o existen
operaciones sin acuse; un reintento local pendiente fuerza el envío. El mínimo periódico del sistema es 15 minutos. Windows debe estar encendido y
la app abierta; la cola Android mantiene los cambios cuando el peer no está disponible.

En Windows el host vive en un único proceso por sesión. Cerrar la ventana WPF la oculta, pero conserva
el servidor, las descargas y el icono de bandeja. Una segunda ejecución señala un evento local con nombre
y reactiva la ventana del proceso existente; así no compite por SQLite ni por el puerto 47831. El inicio
automático, si el usuario lo activa, registra el mismo ejecutable con `--background` en `HKCU\...\Run`.

## Seguridad

Los controles detallados vivirán en `docs/threat-model.md`. El mínimo incluye límites de tamaño, canonicalización de rutas, bloqueo de traversal/symlink escape, MIME detectado, capturas web sin scripts, secretos fuera del repositorio y logs redactados.
