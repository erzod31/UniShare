# Registro de progreso

## 2026-09-25 - Prioridad funcional

- Registrada la decisión del usuario de terminar el producto antes del endurecimiento de seguridad.
- Los controles existentes se mantienen; cifrado adicional, Keystore/DPAPI, rotación, firma comercial,
  auditoría y fuzzing especializado quedan marcados `APLAZADOS: SEGURIDAD` en ADR 0014.
- Iniciado el cierre de paridad Android: edición de metadatos, favorito, etiquetas, colecciones,
  archivado/restauración y filtros de búsqueda sobre esos campos.

## 2026-09-24 - Auditoría inicial

- Leídas las 11 páginas de la especificación PDF y el informe técnico completo.
- Inventariado el workspace, incluidos archivos ocultos: sólo existían `REPORTE_UNIVERSAL_LIBRARY.md` y una carpeta temporal vacía de revisión PDF.
- Confirmado que no había repositorio Git, código, configuración, pruebas, binarios ni cambios preexistentes de producto.
- Toolchain: .NET SDK 10.0.400, runtimes .NET Desktop 10.0.11/7.0.20, JDK 21 y Rust disponibles.
- Ausentes: Android SDK, `adb`, `sdkmanager`, Gradle y workloads .NET móviles.
- Revisadas fuentes oficiales actuales para WPF/.NET 10, Windows App SDK/Share Target, SQLite y herramientas Android.
- Creados inventario, roadmap, arquitectura, matriz de verificación, ADR inicial y registro de dependencias.

## Siguiente acción

## 2026-09-24 - H0 completado y H1/H2 iniciados

- Inicializado Git y solución .NET 10 con capas Domain, Application, Infrastructure y Desktop.
- Añadida CI Windows, SDK fijado y lockfiles NuGet.
- Implementado dominio `LINK`/`FILE`/`HYBRID`, canonicalización conservadora y tombstones.
- Implementada SQLite v1 con restricciones, WAL y operación/outbox en la misma transacción.
- Implementado CAS de blobs por SHA-256 con streaming, staging, promoción atómica y deduplicación física.
- Construida UI WPF en español para crear enlace/archivo/híbrido, buscar, editar, abrir y archivar/restaurar.
- Corregido bloqueo de perfiles temporales causado por el pool SQLite durante limpieza de tests.
- Release compila sin warnings; 19 pruebas pasan; publish y smoke ejecutados correctamente.

## Siguiente acción

## 2026-09-24 - Organización y búsqueda

- Añadidas etiquetas y colecciones normalizadas con UUID por membresía, altas add-wins y tombstones para bajas.
- Cada cambio de organización genera una operación durable y actualiza el item en la misma transacción.
- Formularios de creación/edición permiten administrar tags y colecciones.
- SQLite migró a esquema v2 con FTS5 y triggers; versiones futuras se rechazan claramente.
- Una prueba de migración detectó que el índice externo no se poblaba; corregido mediante `rebuild` FTS5.
- Añadidos filtros combinables por tipo, favorito, fecha, archivado y disponibilidad local; UI expone los principales.
- Benchmark reproducible: 100 000 items, p95 91,2185 ms sobre 30 consultas (límite 300 ms).
- Release vuelve a compilar sin warnings; 27 pruebas pasan; publish, smoke y arranque GUI siguen correctos.

## Siguiente acción

## 2026-09-24 - Backup y restauración

- Implementado ZIP portable v1 con manifest, NDJSON legible, blobs CAS y checksums SHA-256 por entrada.
- Inspección bloquea rutas peligrosas, entradas duplicadas, límites excedidos, recuentos inconsistentes y contenido alterado.
- Restauración transaccional exige perfil inexistente, conserva IDs/datos/relaciones/tombstones y regenera identidad local.
- La prueba de round-trip verifica también integridad del blob y búsqueda FTS después de restaurar.
- Exportación disponible desde la UI; utilidad `UniShare.Backup` permite exportar, inspeccionar y restaurar.
- Ejecución CLI externa export→inspect→restore y smoke del perfil restaurado completada con exit 0.
- Corregida una discrepancia de milisegundos entre fecha devuelta por export y fecha persistida en manifest.
- Release compila sin warnings y 29 pruebas pasan.

## Siguiente acción

Implementar H6: cola durable de descargas/capturas, validación de contenido, reintentos y estado offline sin corrupción.

## 2026-09-24 - H6 descargas duraderas (parcial)

- Migrado SQLite de v2 a v3 con `download_jobs` durable e índices; la migración es idempotente ante una interrupción simulada.
- Implementadas cola, consulta, reintento, pausa y cancelación con estado visible en una ventana WPF.
- La transferencia usa streaming y staging; respeta un límite configurado y no publica contenido truncado o sobredimensionado.
- La reanudación usa `Range`/`If-Range`; un ETag o Last-Modified cambiado invalida el parcial en lugar de mezclar versiones.
- Cada redirección vuelve a pasar por la política de endpoint; se rechazan loopback, rangos privados, link-local y metadata conocidos.
- La promoción al CAS calcula SHA-256; `Asset`, cambio a `HYBRID`, finalización del trabajo y outbox se confirman atómicamente.
- Se corrigió una migración v3 no idempotente descubierta por la prueba de interrupción.
- Se corrigió una advertencia CA1305 en la nueva cola haciendo explícita la cultura de formato.
- Cerrada la ventana DNS TOCTOU: `SocketsHttpHandler.ConnectCallback` conecta directamente a una IP
  validada, con proxy desactivado y revalidación por redirección. Una prueba usa un hostname `.invalid`
  y sólo funciona mediante la dirección fijada.
- Añadida detección MIME por firmas limitadas; prevalece sobre extensión y `Content-Type` no confiables.
- El HTML remoto se transforma en una instantánea estática: contenido activo eliminado, texto codificado,
  CSP sin red/scripts y límite de 16 MiB. El blob resultante conserva SHA-256 y se abre offline.
- Una prueba de ETag cambiado demuestra que no se mezclan versiones y que el parcial se elimina.
- Release compila con 0 warnings/0 errores y pasan 47/47 pruebas; publish, smoke y arranque GUI (`InputIdle=True`) son correctos.

## Siguiente acción

Cerrar H6: enriquecer título/metadata de forma no bloqueante y mejorar la extracción de lectura de páginas complejas manteniendo la instantánea inerte.

## 2026-09-24 - Enriquecimiento y recuperación H6

- Extraídos título y metadatos `<meta>` de la página capturada; el título remoto se usa en la instantánea.
- Fuente, autor y descripción se rellenan sólo si estaban vacíos y se confirman en la misma transacción
  que el asset; las ediciones explícitas del usuario no se reemplazan.
- Endurecida la detección de contenido web activo: `.html`, `.htm`, `.xhtml` y `.svg` se convierten en
  instantáneas inertes aunque la cabecera HTTP sea engañosa.
- Los trabajos persistidos en `Running` se pueden reintentar tras un cierre inesperado; se evita publicar
  un asset de un trabajo que ya fue cancelado.
- Release compila sin advertencias y pasan 51/51 pruebas, incluida la conservación explícita de
  título, fuente, autor y notas introducidos por el usuario.

## Siguiente acción

Mejorar la extracción de lectura y la experiencia de cola; después abordar Android y la integración
Windows del sistema. Las pruebas de dispositivos siguen `NO VERIFICADO` hasta disponer del entorno.

## 2026-09-24 - H7 Android iniciado

- Detectado SDK Android API 36 instalado después de la auditoría inicial; no hay emulador ni dispositivo.
- Seleccionados Gradle 8.13, AGP 8.13.2, Kotlin/Compose Compiler 2.3.21 y BOM Compose 2025.09.00
  compatibles con API 36. Descarga Gradle comprobada contra SHA-256 oficial; wrapper fijado.
- Construida app Compose inicial con enlaces locales en SQLite, búsqueda y receptor `ACTION_SEND`.
- El texto compartido puede incluir título y URL; el resultado se valida y se escribe antes de mostrar éxito.
- Desactivadas copias automáticas en nube y transferencias del sistema hasta disponer de backup portable.
- `assembleDebug` y `lintDebug` correctos; 3/3 tests Android pasan, lint 0 errores y 2 avisos
  informativos de versiones; APK debug generado.
- Instalado emulador AOSP API 36 con las licencias del SDK existentes. `ACTION_SEND` creó una fila real;
  la UI Compose mostró título y URL; un payload inválido no creó otra fila.
- `force-stop`, reapertura y `adb reboot` conservaron exactamente el mismo UUID con Wi-Fi y datos
  móviles desactivados. La prueba física sigue `NO VERIFICADO`.

## Siguiente acción

Extender H7 con archivos vía SAF, migraciones verificables y backup compatible; probar también en
teléfono físico cuando esté disponible. H7 sigue parcial por alcance funcional, no por build.

## 2026-09-24 - H7 Android: archivos e híbridos

- Migrado el esquema Android de v1 a v2 sin borrar datos: la prueba sobre la instalación existente
  conservó el UUID, título y URL del enlace previo.
- Añadidos `FILE` y `HYBRID`, tabla `assets`, claves foráneas y selector SAF sin permisos amplios de
  almacenamiento. Un archivo se transmite a staging, limita a 1 GiB, calcula SHA-256, fuerza escritura
  y sólo entonces se promociona al CAS privado.
- Dos imports del mismo archivo producen assets distintos que apuntan al mismo blob físico verificado.
- Una URL válida rellenada antes de abrir SAF crea un híbrido; una URL inválida se rechaza antes del
  selector. `ACTION_SEND` acepta texto o stream y usa la URL compartida cuando está disponible.
- Evidencia en AOSP API 36: archivo de 38 bytes, hash host/dispositivo idéntico; `kind=2` y `kind=3`
  observados en SQLite y en Compose. Tras `adb reboot` con Wi-Fi/datos apagados persistieron todas las
  filas y el hash del blob.
- Build final Android correcto; 3/3 pruebas, lint 0 errores y APK SHA-256
  `4C5E4DBB299B138C6A810DC3E0AC0CAA6DCA186A2335603580A782552D1292A6`.

## Siguiente acción

Completar H7 con exportación/restauración interoperable, CRUD/organización Android y Room; después
continuar H8. La prueba en teléfono físico sigue `NO VERIFICADO` por falta de dispositivo.

## 2026-09-24 - H7 Android: exportación portable interoperable

- Implementado exportador streaming del formato portable v1 existente: manifest, seis ficheros
  NDJSON, checksums SHA-256 y blobs CAS únicos, sin incorporar dependencias nuevas.
- La instantánea SQLite se mantiene en una transacción y se construye primero en cache privado; sólo
  después se copia al URI elegido mediante `ACTION_CREATE_DOCUMENT`.
- Verificación cruzada real: Android exportó 4 items, 3 assets y 1 blob; la CLI .NET aceptó checksums,
  fechas y JSON, restauró a un perfil Windows nuevo y el smoke WPF terminó con exit 0.
- El blob de 38 bytes conservó SHA-256
  `6315d8e4baef620c13aede67bf6cbb7e083836ad772505cea3b21e43bd85758b` después de restaurar.
- APK actualizado: SHA-256 `570FF45EAAE94FDE146142C9816B608A0950E092653BBF69067676D8C62CE5BD`;
  build, 3/3 pruebas y lint 0 errores.

## Siguiente acción

Implementar inspección y restauración Android del mismo ZIP con reemplazo transaccional y rollback;
después continuar CRUD/organización y Room. H7 sigue parcial.

## 2026-09-24 - Captura guiada de enlaces en Windows

- Añadida captura rápida en la ventana principal: URL, carpeta/colección y acciones separadas para
  guardar sólo el enlace o guardar además una copia offline.
- El título inicial se deriva del dominio. Al completar una captura HTML se sustituye por el título
  real de la página sólo mientras siga siendo el valor automático; las ediciones del usuario se conservan.
- Una descarga fallida ya no hace parecer que se perdió el alta: el enlace y su organización se
  conservan, el campo URL se limpia y el trabajo queda en la cola durable para reintentar.
- Añadido selector de ubicación física para toda la biblioteca. La ruta se guarda de forma atómica
  en la configuración local y la aplicación se reinicia con el perfil elegido.
- La automatización UI verificó los cinco controles accesibles, guardó un enlace en la colección
  `Pruebas/Automática` y una exportación posterior confirmó 1 item y 1 colección persistentes.
- Release compila con 0 warnings/0 errores; pasan 52/52 pruebas y el formato queda limpio.

## Siguiente acción

Automatizar el caso de descarga externa exitosa en un entorno con salida de red y continuar la
restauración Android. La captura y persistencia del enlace sí están verificadas aunque la red falle.

## 2026-09-24 - Simplificación de la interfaz Windows

- Reorganizada la ventana principal después de revisar una captura del uso real: se eliminaron las
  dos acciones competidoras `Guardar enlace`/`Guardar offline` y ahora existe una sola acción `Guardar`.
- La decisión offline es una casilla explícita con explicación; la carpeta se presenta como opcional
  y se indica que pertenece a UniShare y se crea automáticamente.
- Descargas, ubicación física y respaldo se agruparon bajo `Opciones`; la acción avanzada superior
  se denomina `Añadir archivo…` y deja de competir con la captura normal de enlaces.
- Búsqueda y filtros tienen encabezados visibles. La lista se identifica como `Tus elementos` y el
  estado vacío ya no duplica el botón de alta.
- Abrir, editar, descargar y archivar se ocultan hasta seleccionar un elemento.
- Añadido guardado con Enter y bloqueo del botón durante la operación para evitar altas duplicadas.
- Release compila sin advertencias, pasan 52/52 pruebas y el smoke del ejecutable publicado termina
  correctamente. La inspección por captura automatizada quedó no verificada porque el controlador de
  aplicaciones nativas no estuvo disponible en esta sesión.

## 2026-09-24 - Entrega coordinada y Android 0.1.1

- Registrada como regla permanente la decisión del usuario de actualizar y validar todos los clientes
  afectados antes de entregar artefactos; queda en `AGENTS.md` y ADR 0010.
- Alineada la pantalla Android con el flujo simplificado: URL, título automático y una sola acción
  `Guardar`; archivo y respaldo quedan como acciones secundarias y búsqueda/lista tienen jerarquía clara.
- Eliminada la falsa interacción de tocar una fila para rellenar un formulario que no guardaba ediciones.
- Android sube a 0.1.1 (`versionCode=2`) y Windows declara también versión 0.1.1.
- Añadida prueba del título derivado del dominio: pasan 4/4 pruebas Android, build y lint.
- `adb install -r` conservó los cuatro items del emulador. Un alta real creó título `example.org` para
  `https://example.org/android-new` y persistió tras cierre forzado y reapertura.

## 2026-09-24 - Sincronización bidireccional y conflictos 0.2.0

- Añadida identidad estable de origen al manifest portable y estado de observación por peer/item en
  Windows SQLite v4 y Android SQLite v4, con migraciones incrementales no destructivas.
- Implementada importación en ambos clientes con validación de rutas, cobertura de checksums, límites,
  SHA-256 de blobs, UUID, tipos, URL y timestamps antes de confirmar datos.
- La fusión usa hashes de base, no Last-Write-Wins: aplica sólo cambios unilaterales seguros y conserva
  snapshots local/remoto cuando hay divergencia o concurrencia.
- Windows incorpora diálogo de conflictos; Android muestra ambas versiones y permite conservar la local
  o usar la recibida. Android añade edición de título para completar el flujo de cambios bidireccionales.
- Tags, colecciones y membresías nuevas se conservan/fusionan; los blobs siguen deduplicados en CAS.
- 55/55 pruebas .NET y 11/11 Android pasan; build, formato y lint sin errores.
- Intercambio real en emulador API 36 y perfil Windows: Android→Windows importó 5 items y 1 blob;
  Windows→Android añadió un sexto; reimportar produjo 6 sin cambios y 0 conflictos; la vuelta a Windows
  produjo el mismo resultado. Se verificó la migración de la APK 0.1.1 a 0.2.0 conservando datos.
- El modo 0.2.0 es local y manual por archivo. Descubrimiento LAN, pairing, mTLS y revocación siguen
  pendientes y no se presentan como completados.
- Artefactos coordinados generados tras el último cambio: Windows ZIP
  `446683895AA3CFF63D6890447F818AD0D803C81E4358621BB3E6E8909A822644` y APK Android
  `6EB25E7998F8569A12D4213144381E51657981606B700B7F2C302E4CFED2F59B`; smoke Windows exit 0 y firma APK v2 válida.

## 2026-09-24 - Sincronización directa 0.3.0

- Retiradas de Windows, Android y CLI las acciones de exportar/importar paquetes para sincronizar; la
  exportación permanece únicamente como copia de seguridad.
- Añadido nodo Windows Kestrel en `127.0.0.1:47831`, autenticación bearer aleatoria de 256 bits, límite
  de 4 GiB, staging, serialización y respuestas sin caché. La configuración se conserva en el perfil.
- Seleccionado Tailscale Serve HTTPS en puerto 8443 para funcionar en casa o por datos móviles sin abrir
  puertos y sin reemplazar las rutas Serve existentes en este equipo.
- Android 0.3.0 envía al guardar/abrir, sondea revisión cada 15 segundos mientras está visible y usa
  WorkManager 2.12.0 con conectividad y backoff en segundo plano.
- La fusión segura 0.2.0 se reutiliza como envelope interno: UUID, assets, organización, tombstones y
  conflictos se conservan; timestamps nunca ganan automáticamente.
- Prueba .NET del endpoint: cliente no autenticado recibe 401; dos perfiles intercambian cambios en ambos
  sentidos. Suite completa: 56/56 (13 dominio, 43 infraestructura).
- Prueba real AOSP API 36 con `adb reverse`: Android→Windows transfirió 6 items, 3 assets y un blob de
  38 bytes; después Windows añadió 2 items y Android llegó automáticamente a 8. WorkManager registró
  resultado correcto. La actualización `adb install -r` conservó los datos anteriores.
- Tailscale 1.102.3 está activo en Windows (nombre privado omitido), pero no se alteró su Serve
  existente. Teléfono físico y cambio Wi-Fi↔datos siguen **NO VERIFICADOS**.
- El smoke publicado detectó un bloqueo de cierre por espera asíncrona en WPF. Se corrigió apagando el
  servidor de forma asíncrona antes del smoke y sin bloquear el hilo UI al cierre normal. Repetición:
  smoke exit 0; ventana publicada `InputIdle=True`, `CloseMainWindow=True`, `Exited=True`.
- Artefactos coordinados 0.3.0: Windows ZIP SHA-256
  `6707FE65660E54A015589F260E541770BA9063D88997A427F489D0D900E0B3FC`; APK SHA-256
  `60755C593A192A935553E1DE8319A85169479DF95B38E2F683C18AF7A2F716B1`.

## 2026-09-24 - Consolidación 0.3.1

- Unificado el contrato portable C# de manifest, items, assets, etiquetas, membresías y lectores
  JSON/NDJSON; respaldo y fusión ya no mantienen modelos privados duplicados.
- `PortableSyncService` y `DirectSyncServer` reciben las instancias comunes de respaldo, blobs y fusión;
  el escritorio deja de construir servicios equivalentes varias veces.
- Android reutiliza un único cliente directo por almacén y un coordinador común registra éxito/error
  tanto desde la actividad como desde WorkManager.
- Eliminado el doble envío tras cada edición. El intento inmediato sólo programa trabajo durable si
  falla; el sondeo periódico compara revisiones antes de transferir el snapshot completo.
- Añadidas cuatro pruebas de política de transferencia, además de conservar las pruebas de formato,
  fusión y transporte bidireccional existentes.
- Validación coordinada: 56/56 pruebas .NET, 15/15 Android, lint sin errores, APK actualizada en AOSP
  conservando datos, sincronización autenticada correcta y smoke Windows con salida 0.
- Artefactos 0.3.1: Windows ZIP SHA-256
  `4CB570738C1A52045A8767E92365DE5FF357C8A454DBBD6ADAC3DB5CA7B110F2`; APK SHA-256
  `B38078426E76E9448C9B4262AC7A930D93BF8E08697C73ACF45300391DDBF92F`.

## 2026-09-25 - Finalización funcional 0.4.0

- Android completa edición de título, fuente, autor, descripción, favorito, etiquetas y colecciones;
  archivo/restauración, apertura mediante `FileProvider` y restauración portable no destructiva.
- Añadida captura offline Android con límites, redirecciones acotadas, rechazo de destinos privados,
  snapshot HTML inerte, escritura atómica, SHA-256 y promoción CAS. Si la red falla, conserva el enlace
  y permite reintentar. En emulador, el fallo externo se debió a DNS ausente y mostró el detalle correcto.
- Rediseñada la pantalla Android: el alta se pliega y la biblioteca existente ocupa el espacio principal.
  La actualización final `adb install -r` conservó los datos; UI Automator mostró favoritos, tags y
  colecciones previos con `versionCode=6/versionName=0.4.0`.
- Windows incorpora importación por arrastrar/soltar en lote, restauración a un perfil nuevo sin
  sobrescribir el actual y mensajes parciales que conservan las entradas válidas.
- Obsidian exporta notas y adjuntos por UUID, conserva el cuerpo editable e importa explícitamente sólo
  la descripción. Las pruebas demuestran que reexportar no sobrescribe y reimportar dos veces no crea bucle.
- Extensión Chromium MV3 creada sin toolchain adicional. El endpoint local exige bearer, rechaza clientes
  desconocidos y una prueba real HTTP confirma URL, título, fuente y colección persistidos.
- El Share Target Windows queda aplazado con MSIX/firma: Windows exige identidad de paquete. El ZIP no se
  presenta como receptor nativo; existen pegar, lote drag/drop y extensión.
- Validación final: restore bloqueado correcto, build .NET 0 warnings/0 errores, formato limpio,
  62/62 pruebas .NET, 21/21 Android, lint sin errores, sintaxis JS/manifest válidos, smoke Windows exit 0,
  ventana publicada receptiva y cierre limpio, APK firmada v2 instalada sin pérdida en AOSP API 36.
- Artefactos coordinados: Windows ZIP SHA-256
  `209A27CBBDCF44878BB696D87981BE3830C6AE2192FE2B05398CD1F567B862ED`; APK SHA-256
  `3DB117B765EEE5D1164F0E195E55CE1EF460D2AD6DB1965310F3454833BF99F7`; extensión Chromium ZIP
  SHA-256 `1E6957C9887524ABC6AD75C589FFA5A5C95420BF3A3A2E958240A027087711AB`.

## 2026-09-25 - Pendientes funcionales 0.4.1

- Corregida la restauración Android de respaldos v1 antiguos sin `source_device_id`. Usa una identidad
  legacy estable, mantiene la fusión conservadora y rechaza identidades explícitas inválidas.
- Verificación real en AOSP: selector SAF, confirmación, incorporación sin pérdida y persistencia tras
  reiniciar. La copia anterior que fallaba terminó con 0 nuevos, 0 actualizados y 0 conflictos.
- Verificación real offline: descarga HTTP pública, snapshot HTML inerte de 3313 bytes promovido a CAS,
  Wi-Fi desactivado, búsqueda de `1.1.1.1` y apertura local mediante `HTMLViewerActivity`.
- Añadida recolección conservadora en Windows y Android: sólo blobs canónicos sin referencia y con siete
  días de antigüedad. Pruebas preservan blobs referenciados, recientes y archivos desconocidos; AOSP
  eliminó un huérfano controlado de 2020 sin tocar dos blobs reales.
- Windows y Android incorporan tema persistente sistema/claro/oscuro. Android oscuro fue inspeccionado
  visualmente y persistió tras reiniciar; Windows añade además `Ctrl+L` y `Ctrl+F`.
- Validación previa al empaquetado: 63/63 pruebas .NET, build Release sin warnings, 27/27 pruebas Android,
  lint sin errores y APK de desarrollo instalada sobre los datos existentes.
- Artefactos coordinados 0.4.1 (2026-09-25): Windows ZIP 17 799 470 bytes, SHA-256
  `1CD5FF573B28E6A505973F11E9550CD5F7569690C5942C886893E7500C6127A9`; APK 10 636 498 bytes,
  `versionCode=7/versionName=0.4.1`, firma v2 y SHA-256
  `7C02FEE14A9606813AC8B50963C446FA57AF7998949C82F4A6C07AE307AA44F3`; paquete Android ZIP
  10 208 114 bytes y SHA-256 `6F3BA04EA11E04F0411EE0002A2EF423D42A1F571D1D8A3FB28DBB25B9E123AD`;
  extensión Chromium ZIP
  3854 bytes y SHA-256 `4F7BCB44A1E892DDD06F83F98E33D0B23A42CFD10F5DA2AE8E0D1BDC5B2B7EF8`.
- El EXE publicado declara 0.4.1.0, smoke exit 0, `WaitForInputIdle=True`, título `UniShare` y cierre limpio.

## 2026-09-25 - Sincronización causal y selectiva 0.4.2

- Las retiradas de etiquetas y colecciones ahora propagan el tombstone del UUID de membresía observado.
  Una re-alta concurrente usa otro UUID y permanece activa; la integración cubre tags y colecciones.
- Android consulta `/api/v1/blobs/missing` antes de exportar. El envelope lleva un inventario SHA-256
  verificado y sólo incluye blobs ausentes; servidores 0.4.1 responden 404 y activan el envío completo
  compatible.
- Windows calcula la respuesta contra el inventario real del cliente y omite blobs que Android ya posee.
  Los metadatos continúan completos y verificables; el delta por operaciones queda pendiente.
- Prueba integrada .NET: primera réplica envía su blob, la respuesta devuelve 0; tras crear otro archivo
  en Windows, la respuesta contiene exactamente 1 blob y la otra réplica converge a 2 elementos.
- Prueba real AOSP API 36 con `adb reverse`: actualización sin borrar datos, negociación nueva y
  sincronización correcta hacia un perfil Windows nuevo con 12 elementos, 4 assets y 2 blobs físicos;
  la siguiente pasada informó 0 nuevos y 0 actualizados.
- Validación intermedia: 67/67 pruebas .NET, 30/30 Android, lint y `assembleDebug` correctos.
- Artefactos coordinados 0.4.2 (2026-09-25): Windows ZIP 17 804 987 bytes, SHA-256
  `655A630A959E47A7967C291D45113242D476E528FEBBE8F507765FCF3F18B9CD`; APK 10 636 498 bytes,
  `versionCode=8/versionName=0.4.2`, firma v2 y SHA-256
  `9440A9D2DE087138E50BDAFD5FDC9ADD8F9650B59BEC4F4F6812190FE80501AB`; paquete Android ZIP
  10 211 575 bytes y SHA-256 `B9E4260AC6B12F0822581B7F471E6643323D3F57355299AF7EA2CBD7DD2D9024`;
  extensión Chromium ZIP 3853 bytes y SHA-256
  `46A1F8B1553256A8FCC1FFBAEDE15A29BCCB35FF8113F00ECC69E6A13A665F1D`.
- El EXE publicado desde `4975b15` declara 0.4.2.0; smoke exit 0, `WaitForInputIdle=True`, título
  `UniShare` y cierre limpio.

## 2026-09-25 - Agente de bandeja Windows 0.4.3

- Cerrar la ventana ahora la oculta y mantiene activos el servidor local, SQLite y las descargas.
- El icono de bandeja abre la ventana, ofrece inicio optativo con Windows y permite salir completamente.
- Un mutex impide dos procesos por sesión; una segunda ejecución señala al agente y recupera la ventana.
- El modo `--background` inicia sin mostrar la ventana. El registro `HKCU` sólo cambia cuando el usuario
  activa o desactiva **Iniciar con Windows**.
- Prueba real: agente oculto activo, segunda instancia exit 0, ventana existente recuperada, cierre de la
  ventana dejó el proceso vivo y `--exit-on-close` produjo salida limpia 0.
- Añadido instalador autocontenido x64 por usuario con desinstalación y accesos directos opcionales. Una
  instalación silenciosa aislada devolvió 0, el smoke devolvió 0, la bandeja sobrevivió al cierre y la
  desinstalación devolvió 0 sin dejar la carpeta instalada.
- El empaquetado conserva sólo recursos en español y excluye símbolos de depuración; no se eliminó ninguna
  dependencia directa porque la auditoría confirmó que SQLite, Compose, WorkManager y las librerías de
  pruebas tienen uso vigente.
- Limpieza final: eliminados perfiles y paquetes históricos de prueba, salidas `bin`/`obj`, cachés Gradle,
  APK intermedias y la descarga local antigua de Gradle. Se conservan únicamente los cinco artefactos
  instalables/portables 0.4.3.
- Artefactos coordinados 0.4.3 (2026-09-25): instalador Windows 59 542 853 bytes, SHA-256
  `E3B6894035BBF4F8370DEFE028CAEFEC9C48C5C352266097BE4A2207563EFFFD`; Windows portátil ZIP
  84 115 035 bytes, SHA-256 `C6409D4907166C75CA4961AF348DE7C96FC6EA39C15DA182DA6551787030C429`;
  APK lateral optimizado 1 363 004 bytes, SHA-256
  `42AA1AA1139F2FA0F59C230B893F985EA83EB48A16EAE87DD03F6DE8BD0DD870`;
  Android ZIP 1 203 343 bytes, SHA-256
  `311B6557E5BC574BD43DC726D57924E2AA55FAE644301625707B77F518D3257D`; Chromium ZIP 3 853 bytes,
  SHA-256 `44F699071BC19943FC03A0EB42B0B7236936D9A0E92FA524A17ADCD3C77BA431`.

## 2026-09-25 - Emparejamiento automático Android–Windows 0.4.4

- Identificada la causa del teléfono vacío: tras una instalación limpia no existían URL ni clave y la
  interfaz confundía “sin emparejar” con una biblioteca vacía.
- Windows configura su ruta Tailscale Serve 8443 al arrancar, detecta el DNS privado, lo persiste de forma
  compatible con configuraciones 0.4.3 y genera localmente un QR sin incorporar el secreto al APK.
- Android recibe `unishare://pair`, valida HTTPS y `.ts.net`, conserva la conexión, sincroniza al instante,
  muestra estado/último resultado y mantiene WorkManager para reintentos con la app cerrada.
- Prueba AOSP API 36: la instalación limpia mostró el aviso correcto; el deep link sintético activó la
  conexión y sobrevivió a `force-stop` y arranque en frío. 33/33 pruebas y lint sin errores.
- Prueba Windows real: actualización 0.4.3→0.4.4 conservó 3 elementos y 2 colecciones; el agente configuró
  Tailscale y el endpoint HTTPS autenticado respondió 200.
- Se mantiene documentado que Android no garantiza recepción instantánea dormido sin push cloud: se
  prioriza privacidad y ausencia de una nube obligatoria.
- Artefactos coordinados finales 0.4.4: instalador Windows 59 635 000 bytes
  (`74CDA4983F25C735341D9DF626238ED7C72299B858582BC425148A565EBE7151`), Windows ZIP
  84 230 502 bytes (`1732F7A185F1F3379CCD7D367319A84639F5D3E8A7FF4AF12F3649E6D922D442`), APK
  1 363 128 bytes (`8BD9062F8857D636F02F71247D0E69F6046A766402738FFB6E2216BD3F90290D`),
  Android ZIP 1 205 798 bytes (`AD273E935167B41B97EDB7AA3C706333F7CED5EDF3115BFEBA42B85EC8DEB15A`)
  y Chromium ZIP 3 840 bytes (`F42971ACDAE1C9223DC93DC195A3F294AF230B22ED08D0E2733DDAC4A1BE2A36`).

## 2026-09-26 - Navegación y guardado unificados 0.5.0

- Windows adopta una barra lateral persistente, búsqueda superior, lista legible e inspector de detalle.
  La acción principal **Guardar** abre un único panel con enlace, carpeta y copia offline; `Ctrl+L`,
  `Ctrl+F`, foco visible, tema del sistema, claro, oscuro y alto contraste siguen disponibles.
- Android comparte el mismo vocabulario y flujo de guardado en una hoja inferior, con navegación
  Biblioteca/Colecciones/Actividad/Ajustes y estado de sincronización visible sin confundir una app sin
  emparejar con una biblioteca vacía.
- Se mantuvieron el dominio, SQLite, CAS y protocolo de sincronización: el rediseño no migra ni transforma
  datos. Los diálogos secundarios de Windows se alinearon con los pinceles dinámicos.
- Limpieza: se retiraron manejadores y propiedades sin uso, el prototipo visual temporal, perfiles de
  validación y los artefactos 0.4.4. No se añadió ninguna dependencia; se conservaron sólo los cinco
  paquetes finales 0.5.0. La publicación intermedia se regenera mediante el script de empaquetado.
- Validación final: restore bloqueado; build .NET Release con 0 advertencias/0 errores; 68/68 pruebas;
  `dotnet format --verify-no-changes`; smoke del ejecutable publicado y del ZIP portátil con salida 0.
  Android pasó 33/33 pruebas en debug y 33/33 en release, `lintRelease` con 0 errores y `assembleRelease`;
  la APK declara `versionCode=11/versionName=0.5.0` y firma v2 válida.
- Artefactos coordinados 0.5.0 (2026-09-26): instalador Windows 59 646 789 bytes, SHA-256
  `F5A182C0104E1B3A4D18EC7DCDFA8EA4D7214C9B958758E26C0C0B0F029B64DD`; Windows ZIP
  84 233 926 bytes, SHA-256 `72EC2CC67FFBBA3FB85958AC2A8AFAC97B34A49750D21504A9F0E342C154563A`;
  APK 1 441 436 bytes, SHA-256 `A863E8A0CB31562A57A09FFB0B399F38752F6E9EC3E9D58FF7F01EBB0CF202E6`;
  Android ZIP 1 254 847 bytes, SHA-256 `C34EE071AE7577FA65AD05C28155C3505065A269FEC73BCAAF71FC765100D115`;
  Chromium ZIP 3 840 bytes, SHA-256 `E902750004A6EBFCA6261B508F959BEB9B3BBB5E52000200D4D98C2D75ACD717`.
- NO VERIFICADO: instalación 0.5.0 y recorrido táctil en teléfono físico; la máquina actual no expone
  ADB ni un dispositivo. También quedan pendientes Narrador y un recorrido completo de teclado.

## 2026-09-26 - Corrección de conexión Tailscale Android 0.5.1

- Diagnóstico verificable: Windows resolvió el nombre MagicDNS privado a su IP de tailnet, Tailscale Serve
  publicó correctamente `:8443`, el endpoint respondió 401 sin clave —por tanto estaba accesible— y
  `tailscale ping` alcanzó el teléfono. El `UnknownHostException` procedía del camino DNS de Android.
- Android localiza la red con transporte VPN y abre sobre ella las conexiones de estado, negociación de
  blobs y sincronización. Si no existe VPN o MagicDNS falla, muestra una explicación accionable.
- El encabezado ya no deduce **Sincronizado** de la mera existencia del emparejamiento: refleja el último
  resultado persistido. Los fallos muestran **Pendiente**, **Reintentar** y **Abrir Tailscale**.
- Añadida prueba de regresión para traducir `UnknownHostException` y orientar sobre DNS/túnel dividido.
- El instalador Windows usa Restart Manager con `CloseApplications=force` y revisa todos los archivos
  del paquete, evitando el fallo al reemplazar `clrjit.dll` mientras el agente de bandeja está activo.
- Validación: 68/68 pruebas .NET; build Release sin advertencias/errores; formato correcto; 34/34 pruebas
  Android debug y 34/34 release; lint 0 errores; APK `versionCode=12/versionName=0.5.1`, firma v2.
- Artefactos coordinados 0.5.1: instalador Windows 59 642 582 bytes, SHA-256
  `EB0327EAB00AE657FB68EC38D2C2870D82825EF61C4A33912BC127C8B88AD275`; Windows ZIP
  84 234 302 bytes, SHA-256 `2AC83AD9B8F981F974E7F2C95E5B94C6F7130830CB61AA71EEC8174DDD9AE80A`;
  APK 1 441 436 bytes, SHA-256 `23B9E87A92C2D90389066BB332AF643FE54138730DB511BAE7838A8ACF964B0A`;
  Android ZIP 1 256 235 bytes, SHA-256 `1968BB0FC70DBDCE705A0E1E693728CEE7B93427622B45F325B62230F1CD8BF2`;
  Chromium ZIP 3 841 bytes, SHA-256 `87A7CA6B9AAB88D6340F18ECA28556F3B4EC603BBF704EFD84A5B15EF501AD1F`.
- Al generar los artefactos, la corrección todavía no estaba verificada en el teléfono porque no había
  ADB conectado; se entregó la APK 0.5.1 para realizar esa comprobación real.

### Confirmación en teléfono físico

- El 2026-09-26 el usuario instaló la entrega 0.5.1 y confirmó que la sincronización Android–Windows
  vuelve a funcionar en el teléfono afectado.
- El usuario confirmó además el funcionamiento usando datos móviles, validando el acceso fuera de la
  red doméstica mediante Tailscale y cerrando K-018.
- K-021 queda cerrado. Permanece pendiente únicamente la prueba de cambio de red durante una operación
  en curso y los recorridos físicos no relacionados con esta incidencia (Sharesheet/SAF y accesibilidad
  completa).

## 2026-09-26 - Integraciones y accesibilidad 0.6.0

- La extensión Chromium puede guardar sólo el enlace o una copia offline de la página ya renderizada:
  texto visible, metadatos y hasta 12 imágenes visibles. El servidor valida tamaños y tipos, vuelve a
  codificar el contenido y crea un HTML inerte con CSP sin scripts ni red.
- La integración con Obsidian genera un índice navegable con wikilinks estables y mantiene la
  exportación/importación conservadora por UUID sin interpretar el índice administrado como una nota.
- Windows añade nombres de UI Automation, regiones vivas, `F6`, `Ctrl+N`, `Ctrl+E`, apertura con Enter
  o doble clic, tamaños de texto persistentes, estados vacíos contextuales y panel de detalle adaptable.
- Android añade semántica para TalkBack, anuncios de estado, etiquetas accesibles, contenido centrado,
  navegación compacta y formularios desplazables para pantallas pequeñas y fuentes grandes.
- Validación final: build .NET Release limpio y 69/69 pruebas; formato limpio; Windows ZIP smoke exit 0.
  Android pasó 34/34 pruebas debug y 34/34 release, lint con 0 errores, firma APK v2, reinstalación y
  arranque correctos. La interfaz se recorrió a 320 dp con fuente 150 %.
- Limpieza final: se retiraron perfiles de prueba, capturas temporales, publicaciones intermedias y los
  cinco artefactos 0.5.1. Se conservaron únicamente los cinco paquetes coordinados 0.6.0.
- Artefactos coordinados 0.6.0 (2026-09-26): instalador Windows 59 637 009 bytes, SHA-256
  `865995EC43903A3E303E92FC89D68FE9605AB67F9E8C877E1A6739006E617539`; Windows ZIP
  84 241 854 bytes, SHA-256 `7AB7DD26AC52340FB611CA885A1E1472E0974F5AA1B4BFE60D341A0CB923CC00`;
  APK 1 441 436 bytes, SHA-256 `D04B110F46AC75948A11FB56B1AD4D937698BB8AC9EDE707F9E556649CD12967`;
  Android ZIP 1 258 132 bytes, SHA-256
  `820AF9D4DE78CC4C3ECA894F8C54D90B2F11462833936758501A356101C15D43`; Chromium ZIP 5 309 bytes,
  SHA-256 `720BA9866A733724B8BBD44748CB565BAF6B7995CADAD569078EB2FF5B217115`.
- NO VERIFICADO: carga real de la extensión en Edge/Chrome, recorrido dentro de Obsidian y escucha
  auditiva con Narrador/TalkBack. Requieren respectivamente autorización de instalación, disponer de
  Obsidian y control humano/auditivo; no se declaran superados por inferencia.

## 2026-09-26 - Menús legibles y Papelera 0.6.1

- Corregida la combinación de texto claro sobre fondo blanco observada en Windows: ComboBox y sus
  opciones usan una plantilla completamente temática; menús y contextos fijan fondo/primer plano y las
  claves de color nativas de WPF se actualizan al alternar claro, oscuro o sistema.
- **Eliminar** es ahora una acción visible en la cabecera y el detalle de Windows, también disponible
  con `Supr`. Una confirmación explica que el elemento se mueve a **Papelera** y se sincroniza; desde allí
  se restaura sin cambiar su UUID.
- Android comparte la misma terminología y seguridad: **Eliminar**, confirmación, **Mostrar papelera** y
  **Restaurar**. El menú superior fija superficie y texto contrastados para ambos temas.
- Prueba funcional Android en AOSP API 36: se creó un enlace sintético, se abrió su edición, se verificó
  la acción Eliminar y la confirmación, desapareció de Biblioteca, apareció en Papelera y se restauró.
  El menú superior claro se inspeccionó visualmente y resultó legible.
- Validación final: .NET 69/69, build sin advertencias ni errores y formato limpio; Android 34/34 debug,
  34/34 release, lint 0 errores, assemble correcto. Windows ZIP smoke exit 0; APK 0.6.1 reinstalable,
  firma v2, `versionCode=14`; extensión manifiesto 0.6.1 y JavaScript válido.
- Artefactos coordinados 0.6.1 (2026-09-26): instalador Windows 59 646 903 bytes, SHA-256
  `5036C3280291E827D1996AFEAD6CB50127609E1DF971CE1E4D59235DFE42FF32`; Windows ZIP
  84 243 805 bytes, SHA-256 `703FCA84B709BB4C4E8D48B67133B4BF7FE27A9EB785F58993E069C12920EE13`;
  APK 1 441 436 bytes, SHA-256 `E3D062B4D6D9AB7BE2A1A04D23D8A9B61C240550CF1CD8866F65984C1ECDF834`;
  Android ZIP 1 258 862 bytes, SHA-256
  `BF5E2C49DC06173AB9B0480E08D339A9FC585D9F01ED34508AABB6F2E91FBFF7`; Chromium ZIP 5 309 bytes,
  SHA-256 `949AE216255E4B079AD20075A51C308CFD78CA65DB70532B106773F1B46CFB00`.

## 2026-09-26 - Clave visible para Edge 0.6.2

- Corregida la omisión que impedía configurar manualmente la extensión: **Conectar teléfono** pasa a
  **Conectar dispositivos y Edge** y muestra un panel específico con el puerto local y la clave privada.
- Puerto y clave se presentan en campos de sólo lectura y tienen botones de copia independientes. La
  clave no se escribe en registros ni se incluye en documentación o capturas de validación.
- La ayuda de la extensión y la guía de instalación señalan la ubicación exacta del nuevo panel.
- Validación final: .NET Release sin advertencias ni errores, 69/69 pruebas y formato limpio; Android
  34/34 debug, 34/34 release, lint 0 errores y APK Release; JavaScript Chromium y manifiesto correctos.
  Windows ZIP smoke exit 0 y SQLite creado. La inspección visual WPF continúa NO VERIFICADA porque el
  controlador disponible no enumera aplicaciones nativas en esta sesión.
- Artefactos coordinados 0.6.2 (2026-09-26): instalador Windows 59 640 350 bytes, SHA-256
  `F02FF6370BE3D4FF5CD9AA2D8A4B5C13483F422276870A5C7AE6C7A1A868FE0C`; Windows ZIP
  84 244 313 bytes, SHA-256 `AAA03858DE51C38F211F28E46CD65B0267F4F013289233DAF2ABC5BE61FB1FE8`;
  APK 1 441 436 bytes, SHA-256 `389083CCC1729EAEBFD4A7B964EE257DF5BCF903BC4804C704A7E5F64FD10FA1`;
  Android ZIP 1 258 861 bytes, SHA-256
  `E477DA4FB9A7B6A608F74E1F4A878FC5E0B6902082B34DD35914AEB2EF55A1B3`; Chromium ZIP 5 380 bytes,
  SHA-256 `1CD424718A074B65421035BB96200350064B4A982263BB9CC58718066D0943D4`.

## 2026-09-26 - Distribución normal en Edge 0.6.3

- Verificada la restricción de plataforma: para usuarios particulares, Microsoft Edge sólo ofrece
  instalación persistente sin carpeta mediante Edge Add-ons. Un CRX privado exige políticas de empresa.
- La extensión usa Manifest V3, incorpora iconos 16/32/48/128 y abre sus opciones automáticamente tras
  la instalación inicial. El icono de UniShare se generó como activo raster propio y se derivaron tamaños
  exactos para el manifiesto y un logotipo de ficha 300 × 300.
- Añadidos paquete reproducible de runtime, expediente completo para Partner Center, ficha es-ES de 1 921
  caracteres, propósito único, justificación de cada permiso y política de privacidad local-first.
- Validación: paquete con `manifest.json` en raíz, 11 archivos runtime, iconos con dimensiones correctas,
  JavaScript válido y expediente con sus cinco archivos obligatorios internos. .NET 69/69; Android 34/34
  debug y 34/34 release, lint 0 errores; Windows ZIP smoke exit 0 y SQLite creado.
- Publicación **BLOQUEADA EXTERNAMENTE** hasta que el propietario use su cuenta Microsoft en Partner
  Center, acepte los términos, aloje la política en una URL HTTPS y envíe el paquete a certificación.
- Artefactos coordinados 0.6.3 (2026-09-26): Setup Windows 59 644 227 bytes,
  `374893349B14BE5708646931B608C392CA256C0DC1F2ABDCF64EB885D56E1D96`; Windows ZIP 84 244 507 bytes,
  `4BF975D461592FBD189950E8669CD04A103B6AD7F4D518FE7619A5A0DED5C9DF`; APK 1 441 436 bytes,
  `D47D1BFF22C14BDDA304A1388B451D944E8F37B67D249E5EBB976265D1846D25`; Android ZIP 1 258 879 bytes,
  `E7D8228CC9C2C1389085BEB75E000CC30CE6BEB24FF925249231772BDDC98376`; Edge Store ZIP 19 183 bytes,
  `61F3B739C8F84EBC4390FCA5558636E819C2824BEA885965AAB9F3018D8A43F6`; expediente Edge 61 944 bytes,
  `A0972C26E23EC63E10F93970C5E877F1C5F5219A68F2B07E4731002C049481F5`.

## 2026-09-30 - Corrección integral y consolidación 0.6.4

- Sincronización Android durable: toda mutación registra una generación local antes de escribir en SQLite;
  el trabajo inmediato/periódico ya no depende únicamente de que cambie la revisión de Windows y una
  edición concurrente no se confirma por error como enviada.
- Cambio de biblioteca Windows seguro: validación de integridad/esquema, espera acotada entre instancias
  y parada completa del servidor directo antes de liberar el mutex.
- Captura automática de metadatos alineada en Windows, Android y Chromium, tanto para guardar sólo el
  enlace como para la copia offline, sin sobrescribir campos modificados por el usuario.
- Colecciones navegables, filtros exactos y paginación incremental en Windows y Android. Android agrupa
  la lectura de etiquetas y descarta resultados obsoletos de búsquedas anteriores.
- Compartir en Android es idempotente frente a recreaciones de actividad y el almacén SQLite tiene ciclo
  de vida de aplicación. Los fallos de sondeo dejan de mostrar un estado de éxito antiguo.
- El smoke Windows falla si el servidor directo no arranca. CI valida .NET, ejecutable autocontenido,
  JavaScript Chromium y la matriz Android debug/release/lint/APK.
- Validación previa al empaquetado: .NET Release 0 advertencias/0 errores y 74/74 pruebas; Android 36/36
  debug y 36/36 release, lint release limpio y `assembleRelease`; JavaScript y manifiesto Chromium
  válidos; formato .NET limpio; smoke Windows exit 0 con SQLite creado.
- NO VERIFICADO: recorrido visual Windows (el controlador nativo no expuso una ventana), Sharesheet/SAF
  en teléfono físico, cambio de red durante una transferencia, Narrador/TalkBack y publicación externa
  de Edge. Firma definitiva Windows/Android pendiente de certificados del propietario.
- Artefactos coordinados 0.6.4 (2026-09-30): Setup Windows 59 648 076 bytes,
  `11BA7923E2F97520D14608A45BCC8D8C4BF1195BAA9F740389EBC2766EDE7FE4`; Windows ZIP 84 250 376 bytes,
  `C0AEAB7A48416B681D3159EE8AAF8674249FFD2BB3852EBD4928D967DD2E6EA8`; APK 1 441 456 bytes,
  `859AB3CE847AE9E2D65236A839953D21FC417CBB8D248D12AB1D25260D3C60CB`; Android ZIP 1 264 677 bytes,
  `09276F0AB8E3A59786F066CE8AB26F2268DFE5F1694736C8E558DE6119F0DB7D`; Chromium/Edge Store ZIP
  19 224 bytes, `C68A97C17D0E732D1E5F00F678695979B7F74DDA0FC2A75AB9D04D566F71CFBA`; expediente Edge
  61 995 bytes, `6D9627740EDAD7CA43A1D35313CE86C39B9AE4C619A6B47A0FD5F0237BB8680D`.

## 2026-10-05 - Auditoría, modernización y consolidación 0.6.5

- Auditoría completa documentada en `docs/audit-2026-10-05.md`: sin fallos críticos conocidos ni
  paquetes NuGet vulnerables. Se reconciliaron roadmap, dependencias, límites y evidencia.
- Corregidas dos carreras Windows: una búsqueda antigua ya no reemplaza una nueva y la paginación
  concurrente no duplica el mismo desplazamiento. Dos pruebas nuevas elevan .NET a 76/76.
- Toolchain actualizado a .NET SDK 10.0.401, SQLite 10.0.12, xUnit v3 4.0.1,
  Microsoft.Testing.Platform/cobertura 18.11.2 y versiones Android estables compatibles con API 36.
- NuGet centralizado en `Directory.Packages.props`; lockfiles y restauración reproducible conservados.
  CI usa acciones oficiales actuales, caché Gradle sin acción adicional, cobertura y smoke reforzado.
- Validación: Release 0 advertencias/0 errores, formato limpio, 76/76 .NET; Android 36/36 debug y
  36/36 release, lint y assemble desde clean; benchmark p95 57,5892 ms; JavaScript MV3 válido.
- Limpieza final: salidas temporales y artefactos 0.6.4 enviados a la Papelera de Windows; `artifacts/`
  conserva únicamente los siete paquetes 0.6.5 verificables y regenerables desde Git.
- Artefactos coordinados 0.6.5 (2026-10-05): Setup Windows 59 688 409 bytes,
  `ACD7CAF52612B5BB6EA7110C7FB5D642106D5E1BBD619A48E872C0938BD3F71A`; Windows ZIP 84 325 367 bytes,
  `F774A5C74B1C6A6F408CD07B58654536C87B4956E149C7F89167933007E6B535`; APK 1 569 859 bytes,
  `946F792C9BE52B064E847D7B60EAB1C3BA326812CF08424B819C272337E1E8DB`; Android ZIP 1 353 932 bytes,
  `0B2590236B57C24DBE7E8BF14A7A5E574CA6F5173B712966A003E09C15D49FF7`; Chromium/Edge Store ZIP
  19 225 bytes, `8FD43973FCB25C71ECAB5318DD858530BE26C00626DB55DAFC9E5E1008C8DD60`; expediente Edge 61 996 bytes,
  `A992B5E99A7A33265BB9F69C1EC4A26734BABBCBABFA60E54ED1E2DFAFD352AF`.

## 2026-10-05 - Recuperación del estado de sincronización 0.6.6

- Diagnóstico en el equipo real: Windows escucha en 47831, Tailscale conecta directamente con el
  teléfono y Serve publica el nombre MagicDNS privado en 8443. La clave temporal de prueba recibió
  HTTP 200 tanto por loopback como por Tailscale; sin credenciales ambos endpoints rechazaron con 401.
- Corregido el estado Android obsoleto: si la revisión remota coincide y no hay cambios locales, tanto
  el sondeo activo como WorkManager registran éxito sin forzar una transferencia redundante y retiran
  el aviso anterior. Los trabajos inmediatos usan reemplazo único y no se acumulan tras un reintento.
- Se añadieron tres regresiones de política. Android pasa 39/39 pruebas debug y 39/39 release, R8,
  `lintRelease` sin errores y `assembleRelease`. La APK se instaló con `adb install -r`, conservando
  datos, y abrió en AOSP API 36 como `versionCode=19/versionName=0.6.6`.
- .NET conserva 76/76 pruebas, formato limpio y compilación Release con 0 advertencias/0 errores.
  El ejecutable autocontenido pasó smoke en un puerto aislado porque el nodo real seguía atendiendo
  47831. JavaScript Chromium y la integridad de los cinco ZIP pasan.
- Fuente de producto: `c2b3785`. Artefactos coordinados 0.6.6 (2026-10-05): Setup Windows 59 706 777
  bytes, `5540D0432818B32E41A18789E97A71F12841FE0035DB04C209FF5926F4DF6826`; Windows ZIP 84 325 357
  bytes, `0EBD14CCB34370DEEE352F3B46960914D876F24CEB3FD76CE18BABDD5C4DBF31`; APK 1 569 855 bytes,
  `1AF988039451D2759245F76A8089BC9DB4E762D26D1B61A84928F95061AE0264`; Android ZIP 1 353 839 bytes,
  `10F267860CEC89EB0FDB706BC9E4620CF8973962940CE1221208E6A794839479`; Chromium/Edge Store ZIP
  19 224 bytes, `BBD1588EABFC0D9E978C2AE454A9E3A09D2FFBB49E2D5419715249D0AA36A020`; expediente Edge 61 993
  bytes, `A2FEC179EB2533D70918F42D8B9B9A1E2B4F805BCC326C5A52FF853A3A197B50`.
## 2026-10-05 - Metadatos de origen y migración 0.6.7

- Windows, Android y Chromium priorizan títulos y descripciones Open Graph, después Twitter Cards y
  finalmente HTML estándar. La fuente también admite `application-name` y el autor `article:author`.
- Las tarjetas Windows y Android muestran ahora la descripción disponible. La extensión conserva un
  título cambiado expresamente en su panel en lugar de reemplazarlo por el detectado en la página.
- Windows y Android recorren una vez los enlaces e híbridos anteriores después de abrir 0.6.7. Sólo
  sustituyen títulos provisionales y rellenan campos vacíos; las ediciones manuales se conservan. Ambos
  clientes ofrecen **Actualizar títulos y descripciones** para reintentar cuando un sitio no respondió.
- Validación previa al empaquetado: formato .NET limpio, compilación Release sin advertencias, 78/78
  pruebas .NET; Android 40/40 pruebas debug y 40/40 release, R8/assemble y lint sin errores; los tres
  JavaScript de la extensión y su manifiesto son válidos.
- Fuente de producto `a4fded6`. El ejecutable Windows pasó smoke aislado; Android se instaló con
  `adb install -r` y abrió en AOSP API 36. Los siete artefactos coordinados quedaron en `artifacts/`,
  con hashes y limitaciones registrados en `docs/verification.md`; se retiraron la entrega 0.6.6 y las
  carpetas temporales de empaquetado.
## 2026-10-05 - Resumen extractivo del contenido 0.6.8

- Sustituida la descripción social/promocional de 0.6.7 por un resumen local que conserva frases del
  cuerpo real. Prioriza Schema.org `articleBody`, después `<article>`, `<main>` y bloques sustanciales.
- Windows, Android y Chromium descartan regiones ajenas, boilerplate conocido y párrafos dominados por
  enlaces. Entre varios bloques semánticos eligen el que contiene más texto útil, evitando confundir
  una tarjeta secundaria con el contenido guardado. El resultado se limita a cuatro frases y 600
  caracteres, sin IA externa ni texto inventado.
- La descripción promocional sólo identifica valores automáticos anteriores: si coincide exactamente se
  reemplaza o elimina; una nota distinta se conserva. La migración sube a revisión 2 para volver a
  procesar todos los enlaces ya guardados después de la sincronización inicial.
- Añadidas regresiones para JSON-LD, selección del bloque principal, rechazo de publicidad sin
  contenido y conservación de notas. Validación final: 81/81 pruebas .NET, 43/43 Android debug y
  43/43 release, formato y compilaciones limpias, lint/R8 correctos y JavaScript MV3 válido.
- Fuente de producto `2ff59a5`. El EXE final pasó smoke aislado; la APK actualizó 0.6.7 mediante
  `adb install -r` y abrió como 0.6.8 en AOSP API 36. Se verificaron los siete artefactos y sus hashes,
  se retiraron los paquetes 0.6.7 y se limpiaron las carpetas temporales de empaquetado.

## 2026-10-06 - Delta por agregados y ACK causal 0.6.9

- Windows publica protocolo directo v2 con identidad y cursor monotónico. A partir del cursor confirmado
  exporta sólo items modificados con sus assets, etiquetas, colecciones y tombstones; una identidad nueva
  o un cursor futuro recibe un snapshot completo de recuperación.
- Android migra el esquema v4→v5 sin borrar datos, registra cada mutación con contador AUTOINCREMENT y
  omite operaciones generadas por una importación directa. Los cursores se guardan después de importar la
  respuesta y las operaciones confirmadas se compactan sin reutilizar contadores.
- El sondeo y WorkManager consultan el registro causal además de la generación visual, cerrando la carrera
  entre marcar un cambio y confirmarlo en SQLite. Los conflictos siguen usando baselines y nunca LWW.
- Regresiones Windows cubren subconjuntos, reenvío sin ACK, avance confirmado y recuperación por snapshot;
  Android cubre cambio de identidad, ACK, cursor futuro y rollback. Validación preliminar: 83/83 pruebas
  .NET; 48/48 Android debug y 48/48 release, lint/R8/assemble correctos.
- Prueba real aislada en AOSP API 36: actualización sobre datos 0.6.8, primer snapshot, enlace Android→Windows
  con cursores 0→1 y enlace Windows→Android con cursor 1→2; la UI mostró ambos elementos sin errores SQLite.
- Fuente de producto `28c77de`. La validación final usó .NET SDK 10.0.401: formato limpio, Release sin
  advertencias y 83/83 pruebas. Android pasó 48/48 pruebas en debug y 48/48 en release, lint, R8 y
  `assembleRelease`; la APK actualizó los datos existentes, declaró 0.6.9 y abrió enfocada en API 36.
- El EXE autocontenido final pasó smoke con SQLite y servidor v2 en puerto aislado. Se generaron y leyeron
  por completo los siete artefactos coordinados; hashes y límites quedan en `docs/verification.md`.
- Limpieza final: retiradas 21 carpetas regenerables de compilación/caché y los siete paquetes 0.6.8.
  `artifacts/` conserva exclusivamente la entrega coordinada 0.6.9 verificada.

## 2026-10-06 - Validación de contenido Android y extensión Edge 0.6.10

- Android inspecciona hasta 512 bytes mientras copia el archivo una sola vez. Reconoce PDF, imágenes,
  ZIP y derivados ofimáticos, EPUB/APK/JAR, MP3 e ISO BMFF; rechaza HTML/XHTML/SVG activo o una firma
  conocida incompatible antes de promover el parcial a CAS o insertar en SQLite.
- El estado de una operación local queda separado del estado de red. Un rechazo de archivo continúa
  visible aunque falle la sincronización y las mutaciones sólo se marcan pendientes después de completar
  su transacción; el log SQLite sigue siendo la fuente durable.
- API 36 verificó mediante SAF un PNG auténtico y HTML disfrazado de PNG. El válido se importó y el falso
  no dejó item, operación, blob, parcial ni generación; después se retiraron únicamente los datos de prueba.
- Edge se validó de extremo a extremo en un perfil real aislado: opciones, clave, popup, pestaña HTTP,
  captura híbrida offline, título, fuente, autor, resumen literal, colección, asset y CSP inerte. El popup
  terminó sin errores de consola.
- Validación final: .NET SDK 10.0.401, formato limpio, Release sin advertencias y 83/83 pruebas; Android
  59/59 debug y 59/59 release, lint/R8/assemble; JavaScript MV3 válido. El EXE final pasó smoke en 47991
  y el APK final se instaló sobre los dos elementos existentes sin pérdida ni crash.
- Fuente de producto `4aa3d7d`. Los siete artefactos coordinados 0.6.10 se generaron después del último
  cambio; tamaños, hashes, pruebas y límites constan en `docs/verification.md`.
- Limpieza final: retirados los siete paquetes 0.6.9 sustituidos y 23 directorios regenerables de
  compilación, publicación, caché, pruebas y validación. `artifacts/` contiene sólo los siete archivos
  0.6.10 comprobados; no se tocaron perfiles ni datos reales del usuario.

## 2026-10-06 - Flujos Windows y apagado del agente 0.6.11

- UI Automation controló el EXE final sobre un perfil aislado: guardó un enlace y carpeta, editó título,
  notas, etiquetas, dos colecciones y favorito, eliminó, comprobó Papelera, restauró y verificó todos los
  datos después de cerrar y reabrir. El recorrido completo terminó `PASS`.
- La prueba encontró un fallo no visible: la ventana se cerraba, pero el agente podía permanecer vivo más
  de 60 segundos por esperar sincrónicamente el servidor directo durante `OnExit`. El nuevo cierre espera
  Kestrel de forma asíncrona y después ejecuta `Shutdown`.
- El empaquetado incorpora una regresión sobre el EXE autocontenido: perfil y puerto temporales, ventana
  real y salida 0 en menos de 15 segundos. La entrega final cerró en 79 ms durante empaquetado y 76 ms en
  la segunda comprobación.
- El script Windows ahora resuelve el SDK local fijado antes de PATH; usó .NET 10.0.401. Validación:
  formato limpio, Release sin advertencias, 83/83 pruebas .NET; Android 59/59 debug y 59/59 release,
  lint/R8/assemble desde limpio; JavaScript y manifiesto MV3 válidos.
- Android final se instaló sobre API 36 sin borrar datos: versión 24/0.6.11, dos elementos existentes,
  firma v2 válida y registro de crash vacío. Los siete artefactos coordinados se leyeron y verificaron.
- Fuente de producto `fbe9591`. Se retiraron siete paquetes 0.6.10 y 24 destinos temporales/regenerables;
  `artifacts/` contiene exclusivamente los siete archivos 0.6.11 detallados en `docs/verification.md`.

## 2026-10-06 - Recuperación real tras corte de proceso

- Sustituida la última simulación pendiente de H6/N-02 por una prueba de proceso real. Un arnés aislado
  usa el transporte normal contra un servidor TCP lento y se termina de forma forzada a 256 KiB y a
  1536 KiB, antes y después del checkpoint durable de progreso.
- Tras cada corte, SQLite conserva el trabajo `Running`, el ETag y el parcial, pero no publica ningún
  asset. Una instancia nueva pide exactamente los bytes restantes con `Range` e `If-Range`, completa la
  promoción CAS, elimina el parcial y produce el mismo contenido y SHA-256 que el origen de 8 MiB.
- El arnés vive sólo en `tests/`, se compila por dependencia explícita y no forma parte de Windows,
  Android ni la extensión. Las dos variantes pasaron; la variante inicial se repitió cinco veces sin
  fallos.
- Se cerró también la verificación offline formal de Windows: con política y transporte que rechazan toda
  red, un recorrido crea un híbrido con PDF, busca, lee el CAS, reabre SQLite, exporta y restaura en otro
  perfil con hash válido. No se registró ningún intento de red.
- Registrado el host de rendimiento (16 núcleos/32 hilos, 31,9 GiB, NVMe, Windows build 26300 y .NET 10.0.401)
  y repetido el benchmark: 100 000 items, 30 medidas, p95 80,4066 ms frente al límite de 300 ms.
- La solución completa queda en 86/86 pruebas .NET y Release compila con 0 advertencias/0 errores.

## 2026-10-06 - Selección múltiple coordinada 0.6.12

- Cerrado un requisito P0 que seguía implícitamente pendiente en F-11. Windows usa selección extendida
  con Ctrl/Mayús, muestra el recuento y aplica Eliminar/Restaurar a los subconjuntos activos o en Papelera;
  Abrir, Editar y Guardar offline exigen de forma explícita una sola selección.
- Android añade un modo **Seleccionar** visible, casillas con semántica accesible, **Todos**, recuento vivo
  y eliminación/restauración por lote en una transacción SQLite. La selección se conserva durante sondeos
  de sincronización y se limpia al cambiar de sección o cuando los elementos dejan de ser visibles.
- La lógica Android de alternancia, retención y partición activo/Papelera tiene pruebas puras. API 36,
  actualizada sin borrar sus dos elementos, expuso dos casillas, `2 seleccionados` y `Eliminar (2)`; al
  cancelar conservó ambos elementos y recuperó el botón de alta.
- Validación limpia final: .NET 87/87, formato y Release sin advertencias; Android 61/61 en debug y
  61/61 en release, lint, R8 y `assembleRelease`; los tres JavaScript del ZIP MV3 y manifiesto 0.6.12
  son válidos. El EXE autocontenido pasó smoke y cerró correctamente en 65 ms.
- El recorrido visual Windows de esta función queda **NO VERIFICADO** en esta sesión: el controlador de
  aplicaciones falló dos veces antes de observar la ventana. El perfil aislado se cerró y retiró sin tocar
  la biblioteca real; la compilación XAML y la regresión del modelo de selección sí pasan.
- Fuente de producto `a7eb441`. El APK final se reinstaló en API 36 sin borrar sus dos elementos y abrió
  en frío sin crash. Los cinco ZIP se leyeron por completo, se comprobó la equivalencia de los paquetes
  anidados y quedaron registrados tamaños y hashes en `docs/verification.md`.
- Limpieza final: retirados los siete paquetes 0.6.11 y 26 directorios regenerables de build, caché,
  pruebas y publicación. `artifacts/` contiene exclusivamente los siete archivos coordinados 0.6.12.

## 2026-10-06 - Preparación para publicación pública

- Sanitizado el árbol actual: se retiraron el nombre MagicDNS, las IP privadas de la tailnet, la marca
  temporal de una instalación y las huellas específicas del equipo de validación. Una búsqueda de rutas
  de usuario, alias, claves privadas y tokens conocidos terminó sin coincidencias.
- Añadidas licencia MIT a nombre de `UniShare contributors`, privacidad, seguridad, contribución,
  changelog, avisos de terceros, formularios de incidencias y plantilla de pull request. README e
  instalación distinguen paquetes de Releases, código fuente y las firmas de desarrollo actuales.
- `.gitignore` bloquea perfiles, bases de datos, configuraciones locales, almacenes de claves y
  certificados. `.gitattributes` normaliza finales de línea y excluye las instrucciones locales del
  agente de `git archive`.
- `packaging/public/build-source-package.ps1` exige un árbol limpio, crea una instantánea desde `HEAD`
  sin historial y rechaza rutas inseguras, archivos privados y patrones de identidad/secretos. La guía
  pública indica crear un repositorio vacío y un historial nuevo: el historial de desarrollo local no
  debe subirse.
- Validación después de la sanitización: documentación PowerShell/YAML válida, 52 Markdown sin enlaces
  locales rotos, .NET 87/87 con 0 advertencias/errores, Android 61/61 debug y release, lint y APK release.

## 2026-10-06 - Resúmenes multimedia corregidos 0.6.13

- Reproducido el defecto en el perfil real sin imprimir URLs ni notas: 5/5 elementos activos eran vídeos
  de YouTube y 5/5 contenían el mismo pie de navegación neerlandés concatenado.
- La causa tenía dos partes: el extractor convertía todo `<body>` cuando no encontraba párrafos, y la
  migración protegía después ese texto automático como si fuese una nota manual.
- Windows, Android y Chromium priorizan ahora `articleBody`, la descripción del contenido estructurado y
  `videoDetails.shortDescription` para vídeos de YouTube. Las descripciones se procesan por líneas;
  enlaces aislados, regiones de navegación, texto sin prosa y CamelCase concatenado se descartan.
- El fallback global de `<body>` se retiró. Sólo se convierte texto libre cuando está contenido en una
  región semántica `article` o `main`; en caso dudoso el resumen queda vacío.
- La migración v3 sustituye únicamente descripciones promocionales previas o la firma compacta inequívoca
  del menú defectuoso. Una prueba conserva una nota natural que menciona copyright, términos, privacidad
  y contacto.
- Las cinco páginas reales respondieron dentro de 2 MiB. El extractor corregido produjo 5/5 resúmenes
  directos de 270–425 caracteres y 0 fragmentos de navegación, sin modificar todavía la base durante el
  diagnóstico.
- Validación funcional: .NET 90/90, build Release sin advertencias y formato limpio; Android 64/64 en
  debug y 64/64 en release, lint, R8 y APK; JavaScript MV3 válido. El EXE autocontenido pasó la prueba de
  cierre limpio en 71 ms.
- Se creó e inspeccionó un respaldo previo de 7 items y 2 colecciones. El instalador 0.6.13 actualizó la
  copia local; al abrir, la migración corrigió 5/5 resúmenes activos, conservó el recuento, dejó SQLite
  íntegro y la aplicación quedó ejecutándose.

## 2026-10-06 - Resúmenes fijos y enlaces pulsables 0.6.14

- Windows incorpora un `LinkifiedTextBlock` que divide texto y enlaces sin alterar el contenido; sólo
  crea hipervínculos para URI absolutas HTTP/HTTPS y usa el navegador predeterminado.
- Android aplica la misma detección a las tarjetas. El editor presenta el resumen completo en lectura y
  sólo muestra el campo de texto después de pulsar **Modificar resumen**.
- Windows usa el mismo modo de lectura en el panel lateral y en **Editar información**. La ventana de
  creación conserva el campo de notas porque todavía no existe un resumen guardado que proteger.
- Se cubren enlaces múltiples, saltos de línea, puntuación final y paréntesis equilibrados con pruebas
  unitarias equivalentes. Una prueba WPF STA confirma el estado visible/oculto de los controles.
- Validación: .NET 94/94 y formato limpio; Android 67/67 debug y 67/67 release, lint, R8 y APK release;
  extensión MV3 válida. El EXE autocontenido cerró limpiamente en 134 ms.
- Se instaló Windows 0.6.14 y quedó ejecutándose. El APK físico permanece NO VERIFICADO hasta instalarlo
  en un teléfono; la compilación declara `versionCode=27`, API 26–36 y firma v2 válida.

## 2026-10-06 - Interfaz bilingüe coordinada 0.6.15

- Windows permite cambiar entre español e inglés desde **Opciones y herramientas > Idioma**. La elección
  se guarda atómicamente, reinicia sólo la interfaz y conserva el perfil y la biblioteca.
- Android ofrece la misma elección en **Ajustes** y recompone inmediatamente todas las secciones. Las
  cadenas de interfaz se traducen sin transformar títulos, URLs, carpetas ni resúmenes del usuario.
- La extensión Chromium detecta el idioma del navegador en el primer uso, permite escogerlo en sus
  opciones y contiene metadatos nativos `_locales/es` y `_locales/en`.
- El instalador incluye los asistentes español e inglés y textos localizados para accesos directos y
  apertura final. La versión coordinada sube a Windows/Chromium 0.6.15 y Android versionCode 28.
- Validación final: .NET 100/100, formato limpio y Release sin advertencias; Android 69/69 debug y
  69/69 release, lint debug/release, R8 y APK; JavaScript/JSON MV3 válidos. Se instalaron y ejecutaron
  correctamente Windows 0.6.15 y su agente de bandeja.
- La automatización visual nativa no expuso ventanas en esta sesión; el recorrido manual de ambos idiomas
  queda **NO VERIFICADO**. La carga XAML/Compose, persistencia pura y traducción dinámica están cubiertas
  por compilación y pruebas automatizadas.

## 2026-10-06 - Auditoría integral y mantenimiento 0.6.16

- Revisados dominio, persistencia, backups, sincronización, Windows, Android, Chromium, CI, empaquetado,
  dependencias y documentación. NuGet no presenta avisos de vulnerabilidad ni paquetes obsoletos y el
  árbol versionable no contiene rutas personales, endpoints reales o secretos conocidos.
- Corregido un bloqueo del semáforo de sincronización si fallaba la preparación de `staging`; una prueba
  envía dos solicitudes bajo el fallo y comprueba que la segunda no queda colgada.
- Android adopta KTX para preferencias/transacciones, elimina autoboxing, usa el ancho de ventana real y
  termina con cero hallazgos lint. Gradle Wrapper sube de 8.13 a 8.14.5 con hashes oficiales; Core 1.19.1
  se descartó al demostrar que exige API 37/AGP 9.1.
- Completados estados bilingües dinámicos en Windows y Android. La extensión dispone de un validador único
  para sus cuatro JavaScript, tres JSON, recursos del manifiesto y paridad español/inglés; CI y empaquetado
  lo ejecutan. El saneamiento fuente cubre además HTML, CSS, JS, Inno y archivos Gradle.
- Validación previa a paquetes: formato .NET limpio, Release con 0 advertencias/errores, 105/105 pruebas;
  Android 69/69 debug y 69/69 release, lint debug/release sin hallazgos, R8 y APK; benchmark FTS5 de
  100 000 elementos con p95 82,0413 ms frente al límite de 300 ms.
- La comprobación del instalador sobre 0.6.15 expuso que Restart Manager no podía cerrar el agente de
  bandeja. Se corrigió el cierre de sesión, se reconstruyó Windows desde `d755a5c6f831` y una segunda
  instalación con 0.6.16 activa terminó con código 0, liberó los 418 archivos y permitió reiniciar el
  agente. El perfil conservó 4 elementos, 2 assets, 2 colecciones y 2 blobs antes y después.
- Windows 0.6.16 quedó instalado y ejecutándose desde `%LOCALAPPDATA%\Programs\UniShare`. Antes de la
  sustitución se creó un respaldo portable inspeccionado en el directorio `backups` del perfil.
- La limpieza final retiró a la Papelera 22 paquetes/directorios de artefactos 0.6.14–0.6.15 y 22
  directorios regenerables de compilación, pruebas y caché. Se preservaron los adjuntos de la auditoría,
  el perfil del usuario y únicamente los ocho paquetes 0.6.16 más su fichero de hashes.
- La primera ejecución pública de GitHub Actions reveló un timeout de 10 segundos en la única prueba WPF
  STA bajo instrumentación de cobertura. Se mantuvo la prueba, se convirtió su hilo en background y se
  amplió el margen a 60 segundos; la misma orden CI volvió a pasar localmente 105/105 antes del push.
- El disparador de Actions se acotó a `main` y pull requests para que etiquetar exactamente el mismo
  commit publicado no ejecute una segunda matriz idéntica.
