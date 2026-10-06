# Inventario de requisitos de UniShare

Estado inicial auditado: 2026-09-24. Este documento traduce la especificación a requisitos trazables. La fuente principal de cada requisito es el PDF `Implementacion_Universal_Library_Codex.pdf`; las decisiones concretas se registran en `docs/decisions/`.

## Requisitos funcionales

| ID | Requisito | Criterio de aceptación verificable | Prioridad |
|---|---|---|---|
| F-01 | Crear un `Item` de tipo `LINK`, `FILE` o `HYBRID`. | La operación se confirma localmente; cancelar no deja filas ni blobs huérfanos; el UUID es estable al reiniciar. | P0 |
| F-02 | Capturar desde Android Sharesheet y Windows Share Target. | URL y archivos recibidos producen un borrador durable con título provisional; el enriquecimiento posterior no bloquea el guardado. | P0 |
| F-03 | Importar en lote, arrastrar/soltar, pegar URL y recibir desde extensión. | Entradas válidas se importan; entradas inválidas se rechazan con error en español sin perder las válidas. | P0/P1 |
| F-04 | Editar metadatos, etiquetas, colecciones y favorito; eliminar con deshacer. | Los cambios persisten tras reiniciar; eliminar crea tombstone recuperable; deshacer restaura sin cambiar identidad. | P0 |
| F-05 | Buscar y filtrar. | Coincidencia por prefijo/término sobre título, URL, fuente, autor, descripción y etiquetas; filtros combinables; índice reconstruible tras importar. | P0 |
| F-06 | Descargar y gestionar archivos. | Cola visible, cancelar/reintentar; staging parcial no se marca disponible; tamaño y SHA-256 se verifican antes de promover el blob. | P0/P1 |
| F-07 | Consultar contenido offline. | Los assets presentes se abren sin red; un enlace sin copia se identifica expresamente. | P0 |
| F-08 | Exportar a Obsidian y aceptar cambios autorizados sin bucles. | Nota Markdown y adjuntos por ID estable; cuerpo editado no se sobrescribe; prueba móvil/escritorio. | P1 |
| F-09 | Emparejar, sincronizar y revocar dispositivos sin pérdida silenciosa. | Operaciones idempotentes; tags convergen; valores escalares concurrentes quedan como conflicto; blobs se verifican. | P1 |
| F-10 | Exportar y restaurar copias. | Backup versionado y verificable restaura en perfil vacío IDs, relaciones, blobs e índice; no hereda credenciales del dispositivo. | P0 |
| F-11 | Interfaz usable y accesible en español. | Tema claro/oscuro/sistema, teclado, foco visible, lector de pantalla, selección múltiple y errores accionables. | P0 |

## Requisitos de dominio y datos

| ID | Requisito | Aceptación |
|---|---|---|
| D-01 | `Item` con UUID estable y tipo `LINK`, `FILE` o `HYBRID`. | Restricciones impiden combinaciones inválidas; una URL original nunca se pierde por canonicalización. |
| D-02 | `Asset` separado del blob físico. | Varios assets pueden apuntar al mismo SHA-256; el contenido físico se almacena una sola vez. |
| D-03 | Etiquetas y colecciones con IDs estables. | Relaciones únicas, normalización documentada y eliminación sin borrar items. |
| D-04 | Historial de cambios y operaciones versionadas. | Cambio de dominio y outbox se confirman en una transacción; `op_id` y `(device_id,counter)` son únicos. |
| D-05 | Conflictos explícitos. | Ningún timestamp físico decide por sí solo; conflictos escalares concurrentes se conservan hasta resolución. |
| D-06 | Migraciones seguras. | Cada versión de esquema tiene migración y prueba desde una copia anterior; backup antes de saltos destructivos. |

## Requisitos no funcionales

| ID | Requisito | Aceptación |
|---|---|---|
| N-01 | Local-first y offline. | CRUD, búsqueda, apertura local y backup funcionan sin red, cuenta ni servidor. |
| N-02 | Conservación. | Escrituras atómicas; kill-points no producen items rotos ni blobs disponibles incompletos. |
| N-03 | Privacidad. | Sin telemetría por defecto; logs locales redactan URLs y rutas sensibles; permisos mínimos. |
| N-04 | Seguridad de entradas. | Validación de URL, nombre, tamaño, MIME, traversal, symlink y límites de expansión. |
| N-05 | Portabilidad. | Exportación abierta documentada con metadatos y archivos; formato de backup versionado. |
| N-06 | Rendimiento. | Captura local simple <1 s en mayoría de mediciones; búsqueda p95 <300 ms sobre 100 000 items en equipo de referencia, después de indexar. |
| N-07 | Flujo de archivos. | Archivos grandes se transmiten en streaming y no se cargan completos en RAM. |
| N-08 | Reproducibilidad. | SDKs fijados, restauración y build con comandos documentados, análisis estático y pruebas automatizadas. |
| N-09 | Compatibilidad. | Backups y datos de versiones soportadas migran sin pérdida; versiones futuras se rechazan claramente. |
| N-10 | Dependencias. | Cada dependencia importante tiene necesidad, versión, licencia y obligación de distribución documentadas. |

## Alcance por nivel

- P0: núcleo offline, Windows y Android, enlaces/archivos/híbridos, organización, búsqueda, backup y capturas del sistema.
- P1: extensión Chromium, captura offline, Obsidian y sincronización LAN directa.
- P2: servidor opcional, acceso remoto y captura avanzada. Nunca es requisito para el funcionamiento local.

## Secuencia acordada desde 2026-09-25

La finalización funcional del programa tiene prioridad. Los controles existentes se conservan, pero el
endurecimiento adicional —cifrado de backup, Keystore/DPAPI, rotación/revocación de claves, firma
comercial, auditoría y fuzzing especializado— queda **APLAZADO: SEGURIDAD** hasta completar los flujos
del producto en Windows y Android.

## Reglas de aceptación

- `IMPLEMENTADO` exige código y prueba reproducible.
- `VERIFICADO` exige ejecución real o evidencia equivalente registrada en `docs/verification.md`.
- `NO VERIFICADO` se usa cuando falta hardware, sistema, certificado o cuenta; debe incluir pasos exactos.
- Ningún requisito se reduce para hacer pasar las pruebas. Las limitaciones se mantienen visibles.

## Estado de sincronización 0.6.9

- F-09/D-05: **IMPLEMENTADO y VERIFICADO para transporte directo local**. Windows sirve una API
  autenticada en loopback; Android envía cambios, consulta revisiones y conserva reintentos con WorkManager.
- Windows configura Tailscale Serve HTTPS y genera un QR local `unishare://pair`; Android valida y conserva
  la configuración, sincroniza inmediatamente y muestra de forma explícita si todavía no está conectado.
  El control y relay de Tailscale no reciben
  datos en claro; la aplicación añade una clave aleatoria propia. El usuario verificó la ruta celular
  real y la ruta Wi-Fi en teléfono físico.
- La reimportación es idempotente. Un cambio remoto unilateral se aplica; un cambio local unilateral
  se conserva; la divergencia inicial o concurrente genera un conflicto que requiere decisión explícita.
- Etiquetas y colecciones usan identidad por alta: el tombstone de una membresía observada se propaga y
  una re-alta concurrente con otro UUID permanece activa.
- La importación manual de paquetes se retiró de UI y CLI. 0.6.9 registra operaciones locales, envía sólo
  los agregados modificados después del primer intercambio y avanza los cursores tras confirmación e
  importación. Los reintentos conservan el cursor anterior; una restauración incoherente fuerza un snapshot
  completo. Continúan aplazadas la revocación/rotación desde UI y las pruebas generativas extensivas.
- Windows mantiene el nodo activo al cerrar la ventana mediante un agente de bandeja por usuario. Una
  segunda apertura recupera la ventana existente y el inicio con Windows es optativo desde el icono.
