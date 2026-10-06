# Plan de implementación por hitos

Los hitos son cortes verificables. Cada uno termina con build, análisis, pruebas, evidencia y actualización de `progress.md` y `verification.md`.

## H0 - Repositorio y build reproducible

Entregables: solución, configuración común, CI, aplicación mínima ejecutable, pruebas y documentación de desarrollo.

Criterios:

- `dotnet restore`, `dotnet build` y `dotnet test` funcionan desde un checkout limpio.
- La aplicación Windows abre y muestra un estado explícito de biblioteca vacía.
- Dependencias y versiones están fijadas.

## H1 - Dominio, almacenamiento y migraciones

Entregables: `Item`, `Asset`, tags, colecciones, operaciones, conflictos, SQLite y almacén CAS.

Criterios:

- Tests de invariantes de los tres tipos de item.
- Migración inicial idempotente y claves foráneas activas.
- Cambio de dominio y operación persisten atómicamente.
- Blob duplicado ocupa una sola copia física.

## H2 - CRUD y primer flujo Windows

Entregables: crear, editar, archivar, restaurar y eliminar definitivamente según política; lista y detalle.

Criterios:

- Flujo real URL/archivo/híbrido desde UI.
- Persistencia demostrada tras cerrar y reabrir.
- Cancelar no deja datos huérfanos.

Estado: verificado en 0.6.11 mediante UI Automation sobre el EXE autocontenido. El recorrido guardó un
enlace con colección, editó título/notas/etiquetas/colecciones/favorito, eliminó, comprobó Papelera,
restauró, cerró limpiamente y confirmó todos los datos después de reiniciar.

## H3 - Organización

Entregables: etiquetas, colecciones, favoritos y acciones masivas.

Criterios: relaciones únicas, edición persistente y deshacer probado.

Estado: verificado. El recorrido UI Windows 0.6.11 confirmó etiquetas, dos colecciones y favorito después
de cerrar y reabrir. 0.6.12 añade selección extendida con Ctrl/Mayús y acciones masivas en Windows, y un
modo explícito con casillas, seleccionar todo y transacción por lote en Android. El emulador confirmó dos
selecciones simultáneas, recuento, acción `Eliminar (2)`, cancelación y conservación de los elementos.

## H4 - Búsqueda y filtros

Entregables: FTS, canonicalización conservadora, filtros y benchmark.

Criterios: reconstrucción del índice, consultas combinadas y medición con 100 000 items.

Estado: verificado. Migración v1→v2, reconstrucción FTS5, filtros y benchmark de 100 000 elementos
con p95 63,681 ms en la auditoría del 2026-10-05.

## H5 - Importación, exportación y backup

Entregables: formato versionado, exportación abierta y restauración transaccional.

Criterios: round-trip sin pérdida, hashes verificados, backup futuro rechazado claramente y restauración en perfil vacío.

Estado: verificado para formato v1 y restauración en perfil nuevo; cifrado opcional pendiente.

## H6 - Captura y descargas web

Entregables: enriquecimiento, descargas reanudables, vista de lectura sanitizada y cola durable.

Criterios: interrupción sin corrupción, cambio de ETag reinicia descarga y contenido archivado no ejecuta scripts.

Estado: verificado dentro del alcance de captura estática. Cola SQLite v3, descarga por streaming, pausa/reintento/cancelación,
reanudación condicionada por ETag/Last-Modified, límite de tamaño, promoción CAS y UI de cola
están implementados y probados. La conexión se fija a una IP validada en cada petición y
redirección. La detección MIME por firma y una captura HTML estática con CSP están probadas.
Un arnés termina el proceso de descarga en dos puntos reales —antes y después del checkpoint de
progreso— y demuestra reapertura, `Range`/`If-Range`, hash final exacto y limpieza del parcial.
Fuente y autor se enriquecen cuando están vacíos; el título del usuario se conserva y el título remoto
aparece en la instantánea. El resumen extractivo local usa contenido estructurado o semántico, rechaza
texto promocional y migra los valores automáticos anteriores sin reemplazar notas distintas. Una vista de
lectura con fidelidad completa superior al snapshot estático continúa fuera de este hito.

## H7 - Android

Entregables: app Kotlin/Compose, persistencia SQLite versionada, Sharesheet, SAF, búsqueda y backup compatibles.

Criterios: APK reproducible; pruebas en emulador; pruebas físicas marcadas con fecha o `NO VERIFICADO`.

Estado: funcional en emulador. El APK Kotlin/Compose crea enlaces, archivos e híbridos; permite buscar,
editar todos los metadatos, etiquetas, colecciones y favorito, archivar/restaurar, abrir el contenido y
crear/reintentar una copia offline. Sharesheet, SAF, persistencia, migraciones v1→v4, respaldo portable,
restauración no destructiva y sincronización directa comparten UUID, organización y blobs. Pasan build,
lint y pruebas en debug y release. 0.6.10 valida por firma los archivos SAF antes de promover el CAS,
rechaza contenido web activo o discordante y conserva visibles por separado los errores locales y de
sincronización. La persistencia usa `SQLiteOpenHelper` con migraciones
verificadas; migrar a Room no aporta una capacidad visible y no es un requisito pendiente. La
sincronización en teléfono fue verificada por el usuario tanto en Wi-Fi como con datos móviles; quedan
NO VERIFICADOS los recorridos físicos completos de Sharesheet, SAF y TalkBack.

## H8 - Integración Windows

Entregables: MSIX, Share Target, activación de instancia y drag-and-drop.

Criterios: instalación limpia; share en arranque frío y app abierta; payload durable antes de UI.

Estado: arrastrar/soltar por lote está implementado y probado en el planificador. El Share Target nativo
queda ligado al paquete MSIX firmado: la documentación oficial de Windows exige identidad de paquete.
Se mantiene el ZIP portátil y se aplaza MSIX/Share Target junto con la firma; no se presenta como completo.

## H9 - Sincronización y conflictos

Entregables: operación causal, OR-Set, MV-register, pairing, mTLS, pull/push/ack y blobs por bloques.

Criterios: property tests con reordenación/duplicación; dos réplicas convergen; conflicto de título se conserva; cursor avanza tras commit.

Estado: funcional, con endurecimiento aplazado. El transporte directo Android–Windows está implementado sobre HTTP local
autenticado y preparado para Tailscale Serve HTTPS; el emulador verificó ambos sentidos, blobs y
reintentos. La versión 0.4.3 evita transferir durante sondeos sin cambios, negocia SHA-256 y sólo incluye
blobs ausentes. Las retiradas de membresía usan observed-remove por UUID; una re-alta concurrente sobrevive.
Conflictos explícitos y baselines evitan LWW. En 0.6.9 un registro de operaciones selecciona agregados
incrementales, los cursores se confirman después del commit y los reintentos no avanzan el ACK. Un cursor
restaurado o una identidad nueva fuerza un snapshot de recuperación. Faltan revocación/rotación en UI y
property tests extensivos; corresponden al endurecimiento aplazado. El usuario verificó el transporte real
por Wi-Fi y datos móviles el 2026-09-26.

## H10 - Extensión y Obsidian

Entregables: extensión MV3, host nativo, bridge de Obsidian y notas portables.

Criterios: mensajes validados y acotados; cuerpo de nota no se sobrescribe; ausencia de bucle demostrada.

Estado: implementado y validado en Edge. La extensión MV3 usa la API local autenticada y persiste URL, título y colección;
el endpoint tiene prueba de autorización e integración. Obsidian exporta notas/adjuntos por UUID, conserva
el bloque editable e importa explícitamente sólo ese bloque; la segunda importación es un no-op probado.
En 0.6.10 un perfil Edge aislado verificó opciones, popup, pestaña HTTP, resumen y copia offline inerte.
Obsidian móvil permanece como prueba manual.

## H11 - Endurecimiento y entrega

Entregables funcionales actuales: accesibilidad, instaladores y manual.

Entregables **APLAZADOS: SEGURIDAD**: threat model ejecutable, firma comercial, cifrado adicional,
SBOM de publicación, fuzzing especializado y auditoría externa.

Criterios: matriz final completa, cero errores críticos conocidos y todas las pruebas de hardware verificadas o marcadas honestamente.

## Orden inmediato

1. Completar en hardware real Sharesheet, SAF, TalkBack y el recorrido visual de la extensión.
2. Publicar la extensión en Edge Add-ons cuando el propietario disponga de la cuenta y la URL de privacidad.
3. Retomar el hito de seguridad aplazado sólo después de aceptar la entrega funcional.
