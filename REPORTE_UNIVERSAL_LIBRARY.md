# Universal Library: revisión técnica, investigación y propuesta de implementación

**Fecha de revisión:** 24 de septiembre de 2026  
**Documento analizado:** `Implementacion_Universal_Library_Codex.pdf`, versión 2, 11 páginas  
**Estado observado del workspace:** no hay todavía repositorio ni código de producto; únicamente se creó material temporal para revisar el PDF. Por tanto, este informe evalúa una especificación y una estrategia, no una implementación existente.

## 1. Alcance y criterio de esta revisión

La sección "Instrucción maestra para Codex" del PDF se ha tratado como contenido de la fuente, no como una orden para construir el producto. La petición efectiva es revisar, investigar y emitir este informe. No se ha dado por implementada ni probada ninguna función.

La revisión combina:

- lectura completa y comprobación visual de las 11 páginas;
- contraste con documentación oficial actual de Android, Windows, Chromium, Obsidian, SQLite y Protocol Buffers;
- comparación con productos existentes que cubren partes del problema;
- análisis propio de arquitectura, seguridad, sincronización, pruebas, alcance y coste.

## 2. Dictamen ejecutivo

La especificación es sólida como contrato de producto: entiende bien el enfoque local-first, separa metadatos y blobs, evita sincronizar SQLite, exige operaciones idempotentes, contempla conflictos, copias recuperables, accesibilidad y pruebas destructivas. Está claramente por encima de un PRD convencional.

Sin embargo, todavía no es directamente implementable sin decisiones adicionales. Los mayores riesgos son:

1. **Sincronización:** describe propiedades correctas, pero no define con precisión la semántica convergente de cada campo, la representación del contexto causal, la reentrada de dispositivos tras compactar ni el bootstrap de un par nuevo.
2. **Ciclo de vida de procesos:** Android y Windows pueden matar o reactivar procesos en momentos incómodos. El núcleo debe funcionar con colas durables, no depender de una ventana abierta y asumir interrupción en cada límite de E/S.
3. **Obsidian móvil:** el plugin no puede depender de Node o Electron. La comunicación directa entre el plugin móvil y la app Android es más frágil que una bandeja de intercambio basada en archivos dentro del vault.
4. **Captura web completa:** SingleFile es técnicamente maduro, pero su licencia es AGPL-3.0; incorporarlo al binario exige una decisión de licencia. Además, una captura fiel de cualquier web no puede prometerse.
5. **Alcance:** Android nativo, Windows nativo, extensión, plugin, sincronización P2P segura, capturas offline, backup y empaquetado son, en conjunto, varios productos. Ejecutarlos en serie por capas crea mucho esqueleto sin valor verificable.

### Recomendación principal

Construir un producto propio solo si estas tres condiciones son innegociables:

- funcionamiento completo sin cuenta ni servidor;
- sincronización directa y selectiva entre Android y Windows;
- experiencia nativa de captura y biblioteca en ambas plataformas.

Si se puede aceptar un servidor doméstico o nube propia, **Karakeep** es la base funcional más cercana. Si el foco real son documentos académicos y metadatos, **Zotero** ya resuelve gran parte del problema. Si el centro son notas Markdown y portabilidad, **Joplin** reduce mucho el esfuerzo. Ninguno satisface a la vez el contrato local-first P2P, las dos apps nativas, el modelo URL/archivo/ambos y el control de conflictos del PDF.

Para una implementación propia, la mejor opción es mantener las tecnologías propuestas - Kotlin/Compose en Android y C#/WinUI 3 en Windows - pero reemplazar la ambigüedad del sync por un protocolo formal pequeño: registro de operaciones, contadores por origen, contexto causal, OR-Set para pertenencias, registro multivalor para campos escalares y blobs inmutables direccionados por SHA-256.

## 3. Lo que el PDF acierta

### 3.1 Local-first real

La regla de que cada dispositivo tenga su propia base y que nunca se copie SQLite es correcta. SQLite es almacenamiento interno, no protocolo. La sincronización debe transportar hechos de dominio versionados y blobs verificables.

También es correcta la separación entre:

- `Item`: identidad y metadatos;
- `Asset`: relación lógica entre item y contenido;
- blob físico: bytes inmutables identificados por hash;
- `local_assets`: disponibilidad y ubicación por dispositivo;
- `Operation`: cambio replicable e idempotente.

### 3.2 Captura rápida con enriquecimiento posterior

Confirmar una captura después del commit local mínimo y hacer título, miniatura, extracción o descarga después es la decisión adecuada. Android recomienda validar todo lo recibido por `ACTION_SEND`/`ACTION_SEND_MULTIPLE`, no confiar en MIME o tamaño y procesar binarios fuera del hilo principal ([Android: recibir datos compartidos](https://developer.android.com/develop/ui/compose/sharing/receive)).

### 3.3 Share Target de Windows es viable

Una app WinUI 3 empaquetada con MSIX puede registrarse como `windows.shareTarget`. La precisión importante es que WinUI 3 no usa el override UWP `OnShareTargetActivated`: debe leer `AppInstance.GetActivatedEventArgs()` y detectar `ExtendedActivationKind.ShareTarget` ([Microsoft: integrar una app con Windows Share](https://learn.microsoft.com/en-us/windows/apps/develop/windows-integration/integrate-sharesheet-receive)). MSIX no es un detalle cosmético: Share Target y otras extensiones necesitan identidad de paquete ([Microsoft: modelos de empaquetado](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/packaging/)).

### 3.4 Protocolos evolutivos

La exigencia de reservar números eliminados en Protobuf, mantener fixtures y no asumir actualización simultánea de ambos clientes coincide con las recomendaciones oficiales: nunca reutilizar tags, reservar campos eliminados y evitar cambios de tipo ([Protocol Buffers: buenas prácticas](https://protobuf.dev/best-practices/dos-donts/)).

### 3.5 Seguridad y recuperación

El PDF incluye amenazas que suelen olvidarse: SSRF, redirecciones, DNS, traversal, symlinks, archivos hostiles, logs sensibles, claves, revocación, disco lleno y backups incompletos. OWASP confirma que la protección SSRF debe revisar IPv4 e IPv6, todos los resultados DNS y cada redirección, no solo el hostname original ([OWASP SSRF Prevention](https://cheatsheetseries.owasp.org/cheatsheets/Server_Side_Request_Forgery_Prevention_Cheat_Sheet.html)).

## 4. Correcciones y decisiones que faltan

### 4.1 Android 17 cambia la sincronización LAN

Para apps que apunten a API 37, Android 17 bloquea por defecto el acceso a la red local. Hay dos rutas: pedir `ACCESS_LOCAL_NETWORK` en tiempo de ejecución o usar el picker de `NsdManager` para conceder acceso solo al servicio elegido ([Android: permiso de red local](https://developer.android.com/privacy-and-security/local-network-permission)).

**Recomendación:** usar el picker de NSD para el emparejamiento normal y pedir el permiso amplio solo si se habilita descubrimiento automático persistente. Mantener entrada manual/QR como respaldo. Esto reduce permisos y evita que la función dependa de multicast.

### 4.2 WorkManager no debe ser el único motor de descargas

WorkManager es adecuado para enriquecimiento corto, reintentos y sync diferible, pero las transferencias largas iniciadas por el usuario tienen una API específica desde Android 14: User-Initiated Data Transfer jobs. Se ejecutan de inmediato, requieren notificación y deben persistir estado para tolerar una terminación sin callback ([Android: UIDT jobs](https://developer.android.com/develop/background-work/background-tasks/uidt)). Además, los foreground services de `dataSync` tienen límites acumulados desde Android 15 ([Android 15: límites de dataSync](https://developer.android.com/about/versions/15/behavior-changes-15)).

**Recomendación:**

- WorkManager para miniaturas, metadatos, limpieza, sync corto y reintentos;
- UIDT para una descarga grande que el usuario acaba de iniciar;
- cada transferencia representada en DB con offsets, validador HTTP y archivo parcial;
- fallback a worker foreground en Android anterior a API 34.

### 4.3 Room/FTS debe fijarse después de un spike

Room 3 incorpora FTS5 y el driver SQLite empaquetado; el `BundledSQLiteDriver` evita depender de la versión SQLite del dispositivo ([Android: Room 3](https://developer.android.com/jetpack/androidx/releases/room3), [Android: BundledSQLiteDriver](https://developer.android.com/reference/androidx/sqlite/driver/bundled/BundledSQLiteDriver)). En Windows, el paquete principal `Microsoft.Data.Sqlite` ya incluye un SQLite consistente con FTS4, FTS5 y JSON1 ([Microsoft.Data.Sqlite: versiones personalizadas](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/custom-versions)).

**Recomendación:** hacer un spike de compatibilidad y, si Room 3 estable funciona con la matriz Android elegida, usar FTS5 en ambos lados. Si no, usar FTS4 en Android y mantener consultas/fixtures semánticamente equivalentes. No supeditar todo el proyecto a una API recién estabilizada.

### 4.4 La búsqueda parcial necesita una definición medible

"Coincidencia parcial" puede significar prefijo, tokens, subcadena o tolerancia a errores. FTS5 no ofrece automáticamente fuzzy search general.

Propuesta:

- tokens Unicode normalizados y búsqueda por prefijo para título, autor, fuente y tags;
- URL canonicalizada en una columna separada;
- trigramas solo para campos cortos si las pruebas demuestran necesidad;
- búsqueda tolerante a errores como módulo posterior, no requisito oculto del MVP;
- `integrity-check` y `rebuild` del índice tras importación. SQLite documenta explícitamente que una tabla FTS externa y su tabla de contenido pueden divergir y que deben poder reconstruirse ([SQLite FTS5](https://www.sqlite.org/fts5.html)).

### 4.5 SingleFile requiere una decisión de licencia

SingleFile está bajo AGPL-3.0 ([repositorio oficial](https://github.com/gildas-lormeau/SingleFile)). No debe copiarse o empaquetarse como una dependencia neutral sin revisar las obligaciones de distribución.

Opciones, por orden recomendado:

1. **MVP:** captura simplificada con Mozilla Readability + DOMPurify + recursos descargados con límites. Readability es Apache-2.0 y exige un sanitizador para entrada no confiable ([Mozilla Readability](https://github.com/mozilla/readability/blob/main/README.md), [DOMPurify](https://github.com/cure53/DOMPurify)).
2. **Integración externa:** permitir que SingleFile, instalado por el usuario, exporte a Universal Library.
3. **Producto AGPL compatible:** integrar SingleFile cumpliendo la licencia.
4. **Licencia comercial:** negociar con el autor si el modelo de distribución lo requiere.

No se debe presentar una captura Readability como copia fiel: es una vista de lectura sanitizada.

### 4.6 Obsidian no debe ser un cliente directo de SQLite

El PDF acierta aquí, pero falta el puente concreto. Los plugins móviles no disponen de Node/Electron; Obsidian exige usar `Vault`, `Vault.process`, `FileManager.processFrontMatter` y `normalizePath`, y desaconseja el acceso directo mediante `FileSystemAdapter` ([Obsidian: desarrollo móvil](https://docs.obsidian.md/Plugins/Getting%20started/Mobile%20development), [Obsidian: checklist de plugins](https://docs.obsidian.md/oo/plugin)).

**Recomendación:** una bandeja de intercambio basada en archivos inmutables dentro de una carpeta visible y configurable del vault, por ejemplo `_UniversalLibrary/bridge/`:

- la app exporta notas y adjuntos mediante el acceso a carpeta concedido por SAF en Android o por filesystem en Windows;
- el plugin observa cambios con la API Vault;
- para cambios de vuelta, el plugin crea pequeños mensajes `op_id.json` inmutables;
- la app importa y confirma de manera idempotente;
- los mensajes se eliminan solo después de un ACK durable;
- `origin_token` y `export_version` evitan eco;
- mover o borrar una nota nunca borra automáticamente el Item.

Este diseño funciona en móvil y escritorio sin abrir puertos ni depender de módulos Node. La desventaja es que la carpeta de bridge es visible; es preferible a usar APIs internas o inaccesibles en móvil.

## 5. Construir o adoptar una solución existente

| Alternativa | Fortalezas | Incumplimientos principales | Veredicto |
|---|---|---|---|
| **Zotero** | Windows, Android, conector de navegador, colecciones, archivos, PDF, snapshots, búsqueda y sincronización madura. Android recibe URLs desde otras apps; archivos pueden sincronizarse por Zotero Storage o WebDAV. | Modelo bibliográfico, requiere cuenta para sync de datos, no ofrece P2P LAN, no es WinUI ni tiene el modelo de conflicto solicitado. | Mejor si la biblioteca es principalmente académica. Evita construir gran parte del producto. |
| **Joplin** | Offline-first, Windows/Android/web, adjuntos, Web Clipper, varios destinos de sync y E2EE. | Es note-first, no biblioteca de assets; captura y metadatos son menos ricos; el sync usa un target, no emparejamiento directo. | Mejor si Markdown/notas pesan más que el catálogo. |
| **Karakeep** | Enlaces, assets, archivo web, OCR, búsqueda, Android, extensiones y plugin comunitario de Obsidian. Es la coincidencia funcional más cercana. | Server-first; no Windows nativo ni base local autónoma por dispositivo; el offline móvil no reemplaza un núcleo local-first P2P. | Mejor atajo si se acepta un servidor propio. |
| **Linkwarden** | Preservación web, extensión, PWA y self-hosting maduros. | Centrado en servidor y web; no satisface apps nativas autónomas ni P2P. | Bueno para archivo web, no para este contrato completo. |
| **Implementación propia** | Control total de local-first, P2P, seguridad, UX nativa y modelo URL/archivo/ambos. | Mayor coste, especialmente sync, captura web, empaquetado y matriz de pruebas. | Recomendado solo si los invariantes son reales y financiados. |

Fuentes de comparación: [Zotero Android](https://www.zotero.org/support/android), [Zotero Connector](https://www.zotero.org/support/connector), [Zotero sync](https://www.zotero.org/support/sync), [Joplin sync](https://joplinapp.org/help/apps/sync/), [Joplin E2EE](https://joplinapp.org/help/apps/sync/e2ee/), [Karakeep](https://docs.karakeep.app/), [Linkwarden](https://docs.linkwarden.app/).

## 6. Arquitectura recomendada para una implementación propia

```text
Android Compose                          Windows WinUI 3
       |                                      |
 casos de uso + repositorio             casos de uso + repositorio
       |                                      |
 Room/SQLite + CAS blobs                Microsoft.Data.Sqlite + CAS blobs
       |                                      |
       +-------- Operation log + outbox -------+
                         |
             Protobuf versionado + fixtures
                         |
        HTTPS LAN / mTLS / blobs por bloques

Browser MV3 -> Native Host -> inbox durable de Windows
Obsidian -> vault + bridge de mensajes inmutables
Backup -> manifest versionado + metadatos + blobs por hash
```

### 6.1 Estructura del repositorio

```text
android/
  app/ domain/ data/ capture/ transfers/ sync/ obsidian/
windows/
  App/ Core/ Data/ Capture/ Sync/ NativeHost/ Packaging/
browser-extension/
obsidian-plugin/
schema/
  proto/ fixtures/ compatibility/
docs/
  requirements.md architecture.md verification.md threat-model.md decisions/
tools/
  backup-inspector/ fixture-generator/ interop-tests/
```

No intentaría compartir código de dominio entre Kotlin y C#. Compartiría el contrato, los fixtures, vectores de prueba y escenarios. Forzar un core Rust/C++ común añadiría FFI, empaquetado y depuración cruzada antes de validar el producto.

### 6.2 IDs y representación

- UUIDv7 para `item_id`, `asset_id`, `operation_id` y relaciones. RFC 9562 lo normaliza, mejora localidad de índice y ordena por tiempo aproximado ([RFC 9562](https://www.rfc-editor.org/rfc/rfc9562.html)).
- En Protobuf: 16 bytes en orden de red, no string.
- En SQLite: BLOB de 16 bytes; nunca usar el layout binario peculiar de `System.Guid` sin una conversión explícita y probada.
- El orden UUIDv7 es una ayuda de índice y UX, nunca causalidad ni autoridad temporal.

### 6.3 SQLite y blobs

Usaría SQL explícito y migraciones versionadas pequeñas. En Windows prefiero `Microsoft.Data.Sqlite` directo antes que EF Core: reduce magia, facilita compartir fixtures SQL y evita limitaciones del proveedor SQLite en migraciones y tipos ([Microsoft: limitaciones EF Core SQLite](https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations)).

Configuración base:

- foreign keys activas;
- WAL para concurrencia de lectura, con checkpoint controlado;
- una cola de escritura serializada por proceso;
- transacción única para cambio de dominio + operación + outbox;
- `busy_timeout` finito y errores recuperables;
- backup previo a migraciones destructivas;
- blobs fuera de SQLite.

Layout de contenido:

```text
data/
  library.db
  blobs/ab/cd/<sha256>
  staging/<job-id>.partial
  quarantine/<job-id>
  previews/<sha256>/<variant>
```

El nombre original es metadato, no nombre físico. La promoción de `staging` a `blobs` ocurre después de cerrar el stream, comprobar tamaño y SHA-256 y hacer rename atómico cuando el filesystem lo permita.

### 6.4 Modelo de operación exacto

El envelope mínimo debería contener:

```text
op_id
origin_device_id
origin_counter
entity_id
entity_type
operation_type
payload_version
causal_context[] = (device_id, max_counter_seen)
payload_bytes
created_at_utc     // informativo
```

Invariantes:

- `UNIQUE(op_id)` y `UNIQUE(origin_device_id, origin_counter)`;
- aplicar y avanzar cursor en la misma transacción;
- un ACK solo se emite después del commit;
- repetir una operación produce el mismo estado;
- el reloj físico nunca decide concurrencia;
- cada peer conserva cursor por origen, no un cursor global.

Para esta clase de datos no recomiendo Automerge como dependencia central. Automerge tiene un protocolo excelente, pero su soporte de primera clase está concentrado en Rust/JavaScript/Swift y no ofrece una ruta igualmente madura y nativa para Kotlin y C# ([Automerge: conceptos](https://automerge.org/docs/reference/concepts/), [repositorio](https://github.com/automerge/automerge)). El dominio es pequeño y se beneficia de tipos convergentes explícitos y auditables.

### 6.5 Semántica de conflictos

| Dato | Semántica recomendada |
|---|---|
| Tags y pertenencia a colecciones | OR-Set add-wins. Cada alta tiene `membership_id`/dot propio; una baja elimina solo altas observadas. |
| Título, descripción, autor, fuente | Registro multivalor. Si dos escrituras son concurrentes se conservan ambas; resolver crea una nueva operación que domina a las elegidas. |
| Favorito y estados simples | LWW solo si el producto acepta pérdida concurrente; mejor registro multivalor con resolución automática documentada para booleanos. |
| Borrado de Item | Delete-wins para visibilidad, pero conserva versiones concurrentes y ofrece restauración. Restaurar genera una nueva operación. |
| Asset | Manifest inmutable. Una corrección crea un nuevo asset o versión; el blob por hash nunca muta. |
| Nota Obsidian | El cuerpo pertenece a Obsidian; la app no lo sobrescribe. Tags permitidos generan operaciones idempotentes. |

El OR-Set es una solución conocida para que un `remove` borre solo las altas que observó, sin borrar una alta concurrente ([paper de OR-Set](https://www.lip6.fr/Marc.Shapiro/papers/RR-8083.pdf)). Para causalidad compacta puede usarse un dot `(device,counter)` más vector de contexto; las dotted version vectors separan el evento actual de su pasado causal ([paper de DVV](https://gsd.di.uminho.pt/members/vff/dotted-version-vectors-2012.pdf)).

### 6.6 Compactación y dispositivos antiguos

El PDF dice que se compacte tras confirmación, pero falta el comportamiento de reentrada. Propuesta:

1. Cada operación conoce su origen y contador.
2. Cada peer no revocado confirma un vector por origen.
3. Una operación es estable cuando todos los peers no revocados la han confirmado.
4. Tras una retención adicional, se crea un snapshot verificable y se pueden retirar operaciones estables.
5. Un dispositivo que reaparece con cursor anterior al watermark no recibe un delta imposible: debe hacer bootstrap desde snapshot.
6. Revocar un peer lo elimina del conjunto que bloquea compactación; volver a confiar en él crea una identidad de pairing nueva.

### 6.7 Transporte y emparejamiento

Usaría HTTPS local con TLS 1.3 y certificados de dispositivo fijados durante pairing. TLS 1.3 protege el canal con cifrado autenticado ([RFC 8446](https://www.rfc-editor.org/info/rfc8446/)), pero no es E2EE frente a un servidor remoto: el PDF hace bien en separar ambas cosas.

Flujo recomendado:

1. El dispositivo que ofrece pairing genera token aleatorio de 256 bits, expiración corta y clave/certificado de dispositivo.
2. El QR contiene versión, endpoint, fingerprint público y token de un solo uso.
3. El segundo dispositivo verifica el fingerprint del QR al conectar.
4. Ambos muestran un código corto derivado del transcript y el usuario confirma.
5. Se intercambian certificados/identidades y se habilita mTLS para sesiones posteriores.
6. El token se invalida, aun si el pairing falla después de usarlo.
7. Las claves privadas se guardan en Android Keystore y con DPAPI en Windows. Android permite generar claves cuyo material privado no es exportable ([Android Keystore](https://developer.android.com/reference/android/security/keystore/KeyGenParameterSpec)); DPAPI vincula la protección al usuario/equipo y exige un plan separado de backup ([Windows DataProtectionProvider](https://learn.microsoft.com/windows/uwp/security/data-protection)).

No diseñaría criptografía propia ni usaría solo un secreto mostrado en pantalla como identidad persistente.

### 6.8 API de sincronización

Endpoints suficientes:

```text
GET  /v1/capabilities
POST /v1/pairing/exchange
POST /v1/sync/pull
POST /v1/sync/push
POST /v1/sync/ack
GET  /v1/blobs/{sha256}/manifest
GET  /v1/blobs/{sha256}?offset=&length=
```

La API debe negociar versión mínima/máxima, máximo de lote, compresión, tamaño de bloque y tipos de operación. Un tipo crítico desconocido no se confirma. Los blobs se transfieren por bloques con hash por bloque opcional y SHA-256 final obligatorio.

## 7. Implementación por superficie

### 7.1 Android

- Compose + Material 3, una Activity principal y una ruta de captura explícita.
- Al recibir un share, validar action, MIME real, número de streams y límites; crear un draft durable.
- Para URIs compartidas, no asumir una ruta de archivo. Si no hay permiso persistible, copiar inmediatamente a `staging` antes de devolver control.
- Reservar SAF para import/export y selección del vault. `takePersistableUriPermission()` solo funciona cuando el proveedor concede ese tipo de permiso ([Android SAF](https://developer.android.com/training/data-storage/shared/documents-files)).
- WorkManager con nombres únicos para evitar duplicados.
- UIDT para transferencias grandes iniciadas por el usuario.
- NSD picker en Android 17 para pairing LAN con mínimo permiso.
- Baseline inicial sugerida: `targetSdk 36` para cumplir Google Play desde agosto de 2026, preparando el salto a 37 y sus permisos de LAN. Google exige API 36 o superior para nuevas apps/updates desde el 31 de agosto de 2026 ([requisitos de Google Play](https://developer.android.com/google/play/requirements/target-sdk)).

### 7.2 Windows

- .NET 10 LTS, que tiene soporte hasta noviembre de 2028 ([política oficial de .NET](https://dotnet.microsoft.com/en-us/platform/support/policy)).
- WinUI 3 empaquetado con MSIX. Fijar la última versión estable probada; a fecha del informe Microsoft publica Windows App SDK 2.5.1 estable ([descargas oficiales](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads)).
- `AppInstance` enruta launch, protocol, file y share activation hacia una instancia principal sin perder el payload.
- El payload se persiste antes de construir UI.
- `NativeHost` debe ser mínimo: stdin/stdout reservado al protocolo, logs a fichero/ETW y delegación a una inbox durable.
- No mantener SQLite abierto simultáneamente desde app, host y worker sin un coordinador de escritura.
- Menú de Explorador después de validar P0; drag-and-drop y diálogo cubren primero el caso de archivo.
- `Microsoft.Data.Sqlite` con SQLite empaquetado y FTS5; WAL y timeout finito. Microsoft recomienda WAL para mitigar bloqueos y reintenta `busy/locked` hasta el timeout ([errores y concurrencia](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/database-errors)).

### 7.3 Extensión Chromium

Manifest V3 con:

- `activeTab`, `scripting`, `contextMenus`, `nativeMessaging` y `storage`;
- sin `<all_urls>` para la captura manual normal;
- host permissions opcionales solo para funciones que realmente las necesiten.

`activeTab` concede acceso temporal únicamente tras gesto del usuario y evita el permiso permanente de todos los sitios ([Chrome activeTab](https://developer.chrome.com/docs/extensions/develop/concepts/activeTab)). El service worker es efímero; toda sesión debe poder reiniciarse o consultar estado durable.

Native Messaging usa JSON UTF-8 con longitud de 32 bits. Edge limita los mensajes host -> extensión a 1 MB y permite mucho más en dirección inversa, pero conviene imponer un límite propio simétrico y pequeño ([Microsoft Edge Native Messaging](https://learn.microsoft.com/en-us/microsoft-edge/extensions/developer-guide/native-messaging)).

Propuesta para HTML grande:

- `connectNative`, no `sendNativeMessage`, para una sesión;
- mensajes de chunks de aproximadamente 512 KiB ya codificados;
- `request_id`, índice, total, hash de chunk y hash final;
- ACK por ventana de chunks;
- staging durable en el host y reanudación o aborto limpio;
- límite total por captura y limpieza por TTL.

Así se evita abrir un servidor localhost adicional y se mantiene el canal ligado al ID autorizado de la extensión.

### 7.4 Obsidian

Fase 1: exportación unidireccional de nota + adjuntos.  
Fase 2: lectura de tags/cuerpo permitido mediante bridge.  
Fase 3: resolución visual de conflictos y regeneración.

Frontmatter sugerido:

```yaml
universal_library_id: "..."
export_version: 3
source_url: "..."
source: "..."
item_type: "link"
asset_sha256: "..."
app_managed_fields:
  - universal_library_id
  - source_url
  - asset_sha256
```

El plugin debe usar `Vault.process()` para evitar sobrescribir una edición concurrente ([Obsidian Vault](https://docs.obsidian.md/Plugins/Vault)). El cuerpo libre nunca es propiedad de la app.

## 8. Descargas, backups y apertura segura

### 8.1 Reanudación HTTP

Para reanudar, guardar URL final, offset, ETag fuerte y/o Last-Modified. La petición usa `Range` con `If-Range`; si el servidor responde `200` en lugar de `206` o cambia el validador, se descarta el parcial y se reinicia. RFC 9110 define que Range puede ser ignorado y que `If-Range` condiciona el uso del rango ([RFC 9110](https://www.rfc-editor.org/rfc/rfc9110.html)).

### 8.2 Contenido no confiable

- Nunca abrir HTML archivado con scripts activos por defecto.
- Sanitizar y aplicar CSP estricta; impedir navegación automática, acceso a red y `file:`.
- Generar previews en proceso aislado cuando sea posible.
- No confiar en extensión ni MIME declarado.
- No extraer ZIP durante una importación sin límites de número de entradas, tamaño expandido, ratio y ruta; OWASP destaca zip bombs y traversal como amenazas directas ([OWASP File Upload](https://cheatsheetseries.owasp.org/cheatsheets/File_Upload_Cheat_Sheet.html)).

### 8.3 Formato de backup

No usar un volcado SQLite como formato contractual. Recomiendo un ZIP64 versionado:

```text
manifest.json
metadata/items.ndjson
metadata/relations.ndjson
metadata/operations.ndjson   # opcional para diagnóstico
blobs/ab/cd/<sha256>
checksums.sha256
```

`manifest.json` incluye `format_version`, versión mínima del importador, recuentos, tamaños, hashes, fecha y features. La importación se hace a un perfil temporal, valida referencias y hashes y solo después sustituye el perfil activo, conservando backup del anterior. El export de rescate JSON/CSV + archivos se mantiene separado del backup completo.

## 9. Estrategia de pruebas

### 9.1 Prueba de modelo antes de red real

Crear un simulador puro que genere operaciones en N réplicas, reordene, duplique, omita temporalmente y vuelva a entregar mensajes. Propiedades:

- convergencia al recibir el mismo conjunto de operaciones;
- idempotencia;
- no pérdida de siblings escalares;
- tags add-wins correctos;
- cursor no avanza antes del commit;
- un blob parcial nunca figura disponible;
- una restauración produce el mismo estado lógico e índice reconstruido.

Esta suite debe ejecutarse con los mismos fixtures desde Kotlin y C#.

### 9.2 Interoperabilidad

- serializar en Kotlin, leer en C# y viceversa;
- fixtures de cada versión de Protobuf;
- golden files de UUID, timestamps, URL canonicalizada, manifest y backup;
- negociación con cliente viejo y operación crítica desconocida;
- tests de byte order para UUID/GUID.

### 9.3 Fallos inyectados

- terminar proceso tras escribir blob pero antes del commit;
- terminar tras commit antes del ACK;
- repetir lote completo;
- disco lleno durante staging, backup e importación;
- cambiar ETag a mitad de descarga;
- revocar permiso SAF;
- cambiar IP del peer;
- denegar permiso de red local Android 17;
- dejar un dispositivo meses offline y compactar;
- desinstalar/reinstalar conservando o restaurando datos.

### 9.4 Rendimiento medible

El objetivo de 100.000 items / 300 ms es plausible solo si se especifica:

- dispositivo de referencia;
- consulta concreta y número de resultados;
- estado frío o caliente de caché;
- indexación terminada;
- percentil, por ejemplo p95;
- tiempo de UI y tiempo de SQL por separado.

No usar un único promedio como puerta de calidad.

## 10. Hoja de ruta revisada

El orden del PDF es razonable, pero debe ejecutarse como cortes verticales y validar el sync temprano sin construir todavía la red completa.

### Fase A - decisiones y spikes (2-3 semanas)

1. Share Target WinUI 3 empaquetado, arranque frío y app abierta.
2. Android share con URI hostil y copia durable.
3. FTS5 en Android/Windows con 100.000 items sintéticos.
4. Modelo de operaciones en memoria con convergencia aleatoria.
5. Bridge Obsidian por SAF/Vault en Android real.
6. Decisión de licencia para captura web.

**Salida:** ADRs y go/no-go; ninguna UI grande.

### Fase B - primer corte offline (6-8 semanas)

- crear Item URL/archivo/ambos;
- lista, detalle, edición y búsqueda;
- CAS de blobs;
- backup/restauración básica;
- Android share y Windows share;
- tests de migración y kill points.

**Salida:** ambas apps útiles sin red.

### Fase C - captura y transferencias (4-6 semanas)

- extensión MV3 y NativeHost;
- descargas reanudables;
- preview sanitizada;
- cuotas, progreso y errores recuperables.

### Fase D - Obsidian (3-5 semanas)

- exportación unidireccional primero;
- bridge de cambios permitido;
- pruebas escritorio/Android y prevención de bucles.

### Fase E - sincronización LAN (8-12 semanas)

- pairing y mTLS;
- pull/push/ack;
- OR-Set y conflictos escalares;
- transferencia selectiva de blobs;
- revocación, snapshots y bootstrap.

### Fase F - endurecimiento y entrega (4-8 semanas)

- accesibilidad;
- instalación/actualización/desinstalación;
- matriz de dispositivos;
- threat model ejecutable;
- SBOM/licencias;
- manual, soporte y recuperación.

## 11. Estimación y reducción de alcance

Estas cifras son una estimación de ingeniería, no datos de las fuentes:

- **P0 privado, realmente usable:** 4-6 meses-persona.
- **P1 con extensión, Obsidian y sync LAN seguro:** 9-15 meses-persona acumulados.
- **Beta pulida por un equipo de 2-3 personas:** aproximadamente 6-9 meses si los spikes no descubren bloqueos de plataforma.

El componente más incierto es sync + lifecycle, no la UI.

Si se necesita reducir riesgo, el MVP debería excluir:

- captura web fiel; mantener enlace + vista de lectura;
- bidireccionalidad Obsidian; exportar primero;
- servidor remoto y E2EE;
- menú contextual de Explorador;
- fuzzy search;
- publicación en tiendas.

No excluiría del modelo inicial el operation log: añadirlo tarde obligaría a reescribir transacciones y conflictos.

## 12. Puertas de decisión antes de escribir el repositorio completo

| Puerta | Evidencia exigida | Si falla |
|---|---|---|
| Share Target Windows | payload durable en frío y con instancia existente | reconsiderar UX Windows o empaquetado |
| Share Android | URL + PDF + múltiples URIs de 3 apps reales | limitar MIME/proveedores y documentarlo |
| FTS | p95 medido con 100k items y reconstrucción correcta | bajar objetivo o cambiar estrategia de índice |
| Sync algebra | convergencia property-based en Kotlin y C# | no implementar transporte todavía |
| Obsidian móvil | export + edición + reimportación sin bucle | dejar integración unidireccional |
| Captura web | licencia, límites y sanitización aprobados | enlace + Readability; SingleFile externo |
| Backup | restauración completa en perfil vacío | no distribuir beta |

## 13. Recomendación final

La especificación debe conservarse como contrato de aceptación, pero acompañarse de cuatro documentos antes de comenzar el desarrollo amplio:

1. `sync-semantics.md`: álgebra exacta por campo, ejemplos concurrentes, compactación y bootstrap.
2. `platform-lifecycle.md`: procesos, activaciones, colas durables y ownership de SQLite.
3. `security-protocol.md`: pairing, identidad, almacenamiento de claves, revocación, SSRF y apertura de contenido.
4. `product-scope.md`: qué significa P0/P1 en términos de una experiencia que una persona puede usar, no solo módulos compilados.

Mi elección sería:

- **Producto propio** si P2P sin servidor y UX nativa son el valor diferencial.
- **Karakeep adaptado** si se acepta servidor propio y el objetivo es llegar rápido a guardar/archivar/buscar.
- **Zotero** si predominan PDFs, metadatos académicos y anotación.
- **Joplin** si predominan Markdown, notas y portabilidad.

Para el producto propio: .NET 10 LTS + WinUI 3/MSIX en Windows; Kotlin/Compose + Room/SQLite empaquetado en Android; SQL explícito; blobs CAS; Protobuf; operation log con causalidad formal; mTLS local; extensión con `activeTab`; bridge de archivos para Obsidian; Readability como captura inicial y SingleFile solo tras decisión de licencia.

Ese diseño preserva las mejores ideas del PDF, elimina sus ambigüedades más peligrosas y permite validar valor de usuario antes de invertir en la parte más costosa: sincronización segura y convergente.
