# Matriz de verificación

Fecha de última actualización: 2026-10-06.

| Requisito/hito | Evidencia prevista | Estado | Resultado/limitación |
|---|---|---|---|
| H0 Inventario | `docs/progress.md`, listado del workspace y toolchains | VERIFICADO | Workspace sólo contenía el informe; no existía `.git`, código, tests ni build. .NET 10.0.400 y JDK 21 presentes; Android SDK ausente. |
| H0 Build | restore bloqueado, build, test, format, publish y smoke | VERIFICADO | Fuente 0.6.14 con 0 warnings/0 errores y 94/94 tests .NET sobre MTP. El ejecutable autocontenido 0.6.14 pasó smoke y cierre limpio; los artefactos coordinados se regeneraron después del último cambio funcional. |
| F-01 / D-01 | Tests de dominio + integración SQLite + UI | VERIFICADO Windows | Invariantes y persistencia pasan. UI Automation sobre el EXE final guardó, editó, eliminó, restauró y confirmó todos los metadatos después de reiniciar. Android mantiene sus flujos verificados en emulador; el recorrido físico sigue separado en F-02/F-11. |
| F-02 Android / H7 | APK, tests, lint y Sharesheet/SAF en emulador/teléfono | FUNCIONAL en emulador; sincronización 0.5.1 VERIFICADA en teléfono | Android 0.6.14 pasa 67/67 tests debug y release, lint y R8. API 36 conservó datos al actualizar y verificó selección múltiple; 0.6.10 verificó SAF real, firma de contenido y limpieza. La conexión `.ts.net` y sincronización Wi-Fi/datos fueron confirmadas en teléfono. Sharesheet/SAF físico completo continúa pendiente. |
| F-02 Windows | Test Share Target frío/caliente | APLAZADO CON MSIX/FIRMA | Windows exige identidad de paquete para Share Target. El ZIP portátil ofrece pegar, arrastrar/soltar y extensión local; no se declara Share Target nativo. |
| F-03 | Tests importación y UI drag/drop/pegar | IMPLEMENTADO; extensión Edge VERIFICADA; publicación pendiente | Planificador por lote cubre archivos, varias URL, deduplicación y rechazo parcial. Edge con perfil aislado cargó la extensión MV3, configuró la clave, abrió el popup sobre una pestaña HTTP real y persistió un híbrido offline con título, autor, resumen y colección verificados. La cuenta Partner Center y certificación siguen siendo externas. |
| F-04 | Tests CRUD/tombstone/reinicio | VERIFICADO Windows y Android en emulador | Windows 0.6.11 confirmó por UI guardar, editar título/notas/etiquetas/colecciones/favorito, mover a Papelera, restaurar y persistir tras reinicio. Android 0.6.1 verificó confirmación, desaparición de Biblioteca, Papelera y restauración; las reglas y tombstones continúan cubiertos por pruebas. |
| F-05 | Tests FTS + benchmark 100k | VERIFICADO | FTS5 v2, migración v1→v2, triggers, prefijos y filtros combinables pasan. Benchmark 0.6.5: 100 000 items, 30 medidas, p95 57,5892 ms (<300 ms). |
| F-06/F-07 | Tests de transferencia, reinicio, integridad y apertura offline | VERIFICADO | Además de la suite de streaming, reanudación, límites, integridad, CAS y HTML inerte, un arnés termina un proceso real en 256 KiB y 1536 KiB, reabre SQLite, reanuda con `Range`/`If-Range` y confirma contenido y SHA-256 exactos sin parcial residual. AOSP descargó una página HTTP pública, apagó Wi-Fi y abrió el blob local. |
| F-08 | Fixtures Markdown + prueba Obsidian escritorio/móvil | IMPLEMENTADO; clientes Obsidian NO VERIFICADOS | Las pruebas verifican nota/adjunto por UUID, índice navegable, conservación del bloque editable, importación limitada a descripción y segunda importación sin cambios. Obsidian no está instalado en el equipo de compilación. |
| F-09 | Réplicas + transporte directo | FUNCIONAL; TELÉFONO Y DATOS MÓVILES VERIFICADOS | Fusión cubre ida/vuelta, idempotencia, blobs, concurrencia, resoluciones y retirada causal. 0.6.9 añadió delta por agregados y ACK posterior al commit, con recuperación por snapshot y E2E en emulador. El usuario confirmó la ruta real por Wi-Fi y datos móviles; falta probar únicamente un cambio de red durante una operación en curso y el endurecimiento aplazado. |
| F-10 | Round-trip de backup en perfil temporal | VERIFICADO Windows y Android en emulador | ZIP v1 + NDJSON + SHA-256: round-trip Windows conserva IDs/metadatos/tombstones/relaciones/blobs, regenera device ID, reconstruye búsqueda y rechaza manipulación/destino existente. Android incorporó desde el selector una copia legacy v1 sin borrar datos (`0 nuevos, 0 actualizados, 0 conflictos`) y los elementos persistieron tras reiniciar. |
| F-11 | Tests de UI y checklist accesibilidad | PARCIAL AVANZADO | 0.6.12 implementa selección múltiple y acciones por lote en Windows/Android; API 36 confirmó dos casillas, recuento, acción `Eliminar (2)` y cancelación sin pérdida. Windows añade UI Automation, regiones vivas, texto y atajos; Android semántica TalkBack, región viva y diseño compacto. Falta escucha real con Narrador/TalkBack y recorrido físico exhaustivo. |
| N-01 | Suite con red deshabilitada | VERIFICADO Windows y Android | Windows encadena alta híbrida, búsqueda, lectura del blob, reapertura, exportación y restauración con política y transporte HTTP que rechazan cualquier intento; se observan cero accesos de red. Android AOSP abrió y conservó LINK, FILE, HYBRID y el blob verificado tras reinicio con `wifi_on=0` y `mobile_data=0`. |
| N-02 | Tests de fallos inyectados | VERIFICADO para persistencia y transferencias | Respuesta truncada y cancelación no crean assets incompletos; cortes reales antes y después del checkpoint dejan el job `Running` y un parcial reanudable, nunca un asset publicado. La recuperación verifica longitud, bytes y SHA-256 y elimina el parcial. Windows/Android recolectan sólo CAS canónico no referenciado tras siete días. |
| N-04 Android | Fixtures de firma + SAF real | VERIFICADO en emulador | PDF, PNG, JPEG, GIF, WebP, ZIP, MP3, MP4, HTML y SVG se reconocen con 512 bytes. Se rechazan firmas activas/contradictorias antes de CAS; API 36 confirmó que el rechazo no crea item, operación, blob ni parcial. |
| N-06 | Benchmark reproducible | VERIFICADO | Host documentado de 16C/32T, 31,9 GiB RAM, NVMe, Windows 11 Pro 26300 y .NET SDK 10.0.401. Repetición actual: 100 000 items, 30 consultas, p95 80,4066 ms (<300 ms). |

## Evidencia de ejecución 0.5.1 — 2026-09-26

- Estado real de Tailscale en Windows: backend `Running`, MagicDNS activo, PC y teléfono online con sus
  direcciones privadas omitidas; `tailscale ping` al teléfono respondió. Serve 8443 apunta a
  `127.0.0.1:47831`; la API HTTPS respondió 401 sin autorización, confirmando resolución, TLS y ruta.
- Android fuerza las tres operaciones de red (estado, inventario y sync) sobre una red
  `TRANSPORT_VPN`. `UnknownHostException` se conserva como reintento durable y se presenta con pasos para
  activar DNS de Tailscale o retirar UniShare de exclusiones.
- Suites: 68/68 .NET; 34/34 Android debug y 34/34 release; lint 0 errores y 13 avisos informativos;
  compilaciones Release correctas. Smoke del Windows publicado: salida 0 y versión 0.5.1.0.
- APK: `versionCode=12`, `versionName=0.5.1`, minSdk 26, targetSdk 36 y firma v2 válida.
- Instalador: Inno Setup aceptó `CloseApplications=force` y `CloseApplicationsFilter=*.*`; ProductVersion
  0.5.1. No se ejecutó una actualización silenciosa sobre los datos del usuario.
- Hashes finales: Windows Setup `EB0327EAB00AE657FB68EC38D2C2870D82825EF61C4A33912BC127C8B88AD275`;
  Windows ZIP `2AC83AD9B8F981F974E7F2C95E5B94C6F7130830CB61AA71EEC8174DDD9AE80A`; APK
  `23B9E87A92C2D90389066BB332AF643FE54138730DB511BAE7838A8ACF964B0A`; Android ZIP
  `1968BB0FC70DBDCE705A0E1E693728CEE7B93427622B45F325B62230F1CD8BF2`; Chromium ZIP
  `87A7CA6B9AAB88D6340F18ECA28556F3B4EC603BBF704EFD84A5B15EF501AD1F`.
- VERIFICADO POR EL USUARIO: instalación y sincronización de 0.5.1 en el teléfono físico afectado el
  2026-09-26. El usuario confirmó «Ya funciona» tras probar la corrección; K-021 queda cerrado. Esta
  evidencia valida el flujo real observado, aunque no sustituye una captura automatizada mediante ADB.
- VERIFICADO POR EL USUARIO: sincronización directa mediante Tailscale usando datos móviles el
  2026-09-26. Esto valida el acceso fuera de la red doméstica y cierra K-018.

## Evidencia de ejecución 0.5.0 — 2026-09-26

- `dotnet restore UniShare.slnx --locked-mode`, build Release (0 advertencias/0 errores), 68/68 pruebas
  y `dotnet format --verify-no-changes` correctos. La publicación autocontenida declara 0.5.0.0 y su
  smoke, tanto directa como extraída del ZIP, terminó con código 0.
- Android ejecutó las suites debug y release: 33/33 pruebas en cada variante. `lintRelease` terminó con
  0 errores y 13 avisos no bloqueantes (versiones disponibles y recomendaciones KTX preexistentes);
  `assembleRelease` correcto. `aapt2` confirmó `versionCode=11`, `versionName=0.5.0`, minSdk 26 y
  targetSdk 36; `apksigner` confirmó firma v2 con la misma clave lateral de desarrollo.
- El ZIP Android contiene exactamente la APK publicada (SHA-256 coincidente). El ZIP Chromium contiene
  manifest v3 y versión 0.5.0. El instalador Inno Setup declara ProductVersion 0.5.0; sigue sin firma
  comercial, limitación documentada antes de la instalación.
- Instalador Windows: 59 646 789 bytes,
  SHA-256 `F5A182C0104E1B3A4D18EC7DCDFA8EA4D7214C9B958758E26C0C0B0F029B64DD`; Windows ZIP:
  84 233 926 bytes, SHA-256 `72EC2CC67FFBBA3FB85958AC2A8AFAC97B34A49750D21504A9F0E342C154563A`;
  APK: 1 441 436 bytes, SHA-256 `A863E8A0CB31562A57A09FFB0B399F38752F6E9EC3E9D58FF7F01EBB0CF202E6`;
  Android ZIP: 1 254 847 bytes, SHA-256 `C34EE071AE7577FA65AD05C28155C3505065A269FEC73BCAAF71FC765100D115`;
  Chromium ZIP: 3 840 bytes, SHA-256 `E902750004A6EBFCA6261B508F959BEB9B3BBB5E52000200D4D98C2D75ACD717`.
- NO VERIFICADO: instalar y recorrer la nueva interfaz 0.5.0 en un teléfono físico, Wi-Fi↔datos móviles,
  Narrador y recorrido completo de teclado. No se dispone de ADB ni de dispositivo en esta sesión.

## Evidencia de ejecución 0.4.4 — 2026-09-25

- Causa reproducida: una instalación limpia de Android carecía de endpoint/clave, la UI lo ocultaba y
  por tanto reiniciar nunca podía recuperar enlaces. La 0.4.4 muestra el estado no conectado y ofrece
  emparejamiento QR `unishare://pair` con validación HTTPS `.ts.net`.
- `dotnet build` Release: 0 advertencias/0 errores; 68/68 pruebas (13 dominio, 55 infraestructura) y
  `dotnet format --verify-no-changes` correctos. La prueba nueva conserva puerto/clave al persistir el
  endpoint y rechaza HTTP, dominios ajenos y rutas adicionales.
- Android: 33/33 pruebas, lint sin errores, APK release optimizada y firmada v2. Instalada en AOSP API 36
  como `versionCode=10/versionName=0.4.4`; UI Automator observó el aviso sin emparejar, el deep link cambió
  a `Conectado a Windows` y el estado siguió presente después de `force-stop` y arranque en frío.
- Instalación real sobre 0.4.3: el perfil tenía 3 elementos/2 colecciones antes y después. Windows 0.4.4
  arrancó en bandeja, ejecutó Tailscale Serve 8443, detectó y persistió su DNS privado y la API HTTPS
  autenticada respondió 200. El secreto no se incluyó en registros ni artefactos.
- Artefactos finales desde `b2efe81`: instalador Windows, 59 635 000 bytes,
  SHA-256 `74CDA4983F25C735341D9DF626238ED7C72299B858582BC425148A565EBE7151`; Windows ZIP,
  84 230 502 bytes, SHA-256 `1732F7A185F1F3379CCD7D367319A84639F5D3E8A7FF4AF12F3649E6D922D442`;
  APK, 1 363 128 bytes, SHA-256 `8BD9062F8857D636F02F71247D0E69F6046A766402738FFB6E2216BD3F90290D`;
  Android ZIP, 1 205 798 bytes, SHA-256 `AD273E935167B41B97EDB7AA3C706333F7CED5EDF3115BFEBA42B85EC8DEB15A`;
  Chromium ZIP, 3 840 bytes, SHA-256 `F42971ACDAE1C9223DC93DC195A3F294AF230B22ED08D0E2733DDAC4A1BE2A36`.
- Limitación honesta: con Android cerrado WorkManager tiene un mínimo de 15 minutos y ejecución inexacta;
  el teléfono físico y el cambio Wi-Fi↔datos móviles siguen **NO VERIFICADOS**.

## Evidencia de ejecución 0.4.3 — 2026-09-25

- Restauración normal bloqueada correcta, build Release con 0 advertencias/0 errores y 67/67 pruebas .NET
  superadas (13 dominio y 54 infraestructura). La publicación autocontenida usa bloqueos `win-x64`
  separados y verificó una segunda restauración en modo bloqueado.
- Android limpio: 30/30 pruebas, `lintRelease` sin errores y `assembleRelease` optimizado correcto
  (86 tareas). APK firmada con esquema v2, `versionCode=9/versionName=0.4.3`, instalada mediante
  `adb install -r` sobre la versión anterior en AOSP API 36; la
  actividad reanudada fue `org.unishare.app/.MainActivity` y UI Automator encontró `UniShare`.
- Instalador Windows real: versión de producto 0.4.3, instalación aislada exit 0, ejecutable instalado
  smoke exit 0, `--background` inició oculto, la segunda ejecución exit 0 recuperó la ventana, cerrar la
  ventana mantuvo el proceso y la desinstalación exit 0 eliminó la carpeta de aplicación.
- Instalador Windows: 59 542 853 bytes, SHA-256
  `E3B6894035BBF4F8370DEFE028CAEFEC9C48C5C352266097BE4A2207563EFFFD`. No tiene firma Authenticode
  comercial (`NotSigned`), limitación comunicada en instalación.
- Windows portátil ZIP: 84 115 035 bytes, 417 entradas, único directorio de recursos `es` y sin PDB, SHA-256
  `C6409D4907166C75CA4961AF348DE7C96FC6EA39C15DA182DA6551787030C429`. EXE 0.4.3.0,
  162 304 bytes, SHA-256 `36E3331CA29F9593D79F451C955E45F830AC54361B251234F6FFF920C6363220`.
- APK lateral optimizado: 1 363 004 bytes, misma identidad y certificado de actualización, SHA-256
  `42AA1AA1139F2FA0F59C230B893F985EA83EB48A16EAE87DD03F6DE8BD0DD870`; ZIP Android:
  1 203 343 bytes, SHA-256 `311B6557E5BC574BD43DC726D57924E2AA55FAE644301625707B77F518D3257D`.
- Chromium ZIP: 3 853 bytes, 7 entradas, manifest MV3 0.4.3 y SHA-256
  `44F699071BC19943FC03A0EB42B0B7236936D9A0E92FA524A17ADCD3C77BA431`.
- Auditoría de dependencias: una dependencia de producción .NET directa (`Microsoft.Data.Sqlite`) y seis
  Android directas de Compose/Core/WorkManager, todas con uso vigente. No se retiraron librerías para no
  sustituir funcionalidad por una reducción aparente. Se eliminaron artefactos históricos, perfiles de
  prueba y cachés/salidas regenerables. Teléfono físico y Wi-Fi↔datos móviles siguen **NO VERIFICADOS**.

## Evidencia de ejecución 0.4.2 — 2026-09-25

- `dotnet restore --locked-mode`, formato, build Release y publish: correctos, 0 warnings y 0 errores;
  67/67 pruebas .NET superadas (13 dominio y 54 infraestructura).
- Android limpio y repetición final: 30/30 pruebas, lint sin errores y `assembleDebug` correcto.
  `adb install -r` conservó los datos y `dumpsys package` confirmó `versionCode=8/versionName=0.4.2`.
- Sincronización selectiva integrada: una réplica ya provista recibió 0 blobs; tras crear otro archivo,
  recibió exactamente 1. Retirada observada y re-alta concurrente convergieron sin pérdida.
- AOSP API 36 real con `adb reverse`: el protocolo 0.4.2 sincronizó hacia un perfil Windows nuevo
  12 elementos, 4 assets y 2 blobs; la siguiente reconciliación terminó con 0 nuevos/0 actualizados.
- APK: 10 636 498 bytes, firma v2 verificada, SHA-256
  `9440A9D2DE087138E50BDAFD5FDC9ADD8F9650B59BEC4F4F6812190FE80501AB`.
- Paquete Android ZIP: 10 211 575 bytes, SHA-256
  `B9E4260AC6B12F0822581B7F471E6643323D3F57355299AF7EA2CBD7DD2D9024`.
- Windows ZIP: 17 804 987 bytes, SHA-256
  `655A630A959E47A7967C291D45113242D476E528FEBBE8F507765FCF3F18B9CD`; EXE 0.4.2.0,
  162 304 bytes, SHA-256 `BD103A21B2C2EBC6E7F1EAADB54A3E7830F6FF9A573D937BD891E81BC669AB22`.
- Chromium ZIP: 3853 bytes, manifest MV3 0.4.2 y SHA-256
  `46A1F8B1553256A8FCC1FFBAEDE15A29BCCB35FF8113F00ECC69E6A13A665F1D`;
  `node --check` correcto para ambos scripts.
- Smoke publicado desde `4975b15`: exit 0; ventana real `InputIdle=True`, título `UniShare`, versión
  0.4.2.0 y cierre limpio.

## Evidencia de ejecución 0.4.1 — 2026-09-25

- `dotnet restore --locked-mode`, build Release y `dotnet format --verify-no-changes`: correctos,
  0 warnings y 0 errores; 63/63 pruebas .NET superadas.
- Android limpio: 27/27 pruebas, lint sin errores y `assembleDebug` correcto. Instalación final con
  `adb install -r` conservó los datos; `dumpsys package` confirmó `versionCode=7/versionName=0.4.1`.
- APK: 10 636 498 bytes, firma v2 verificada, SHA-256
  `7C02FEE14A9606813AC8B50963C446FA57AF7998949C82F4A6C07AE307AA44F3`.
- Paquete Android ZIP: 10 208 114 bytes, SHA-256
  `6F3BA04EA11E04F0411EE0002A2EF423D42A1F571D1D8A3FB28DBB25B9E123AD`.
- Windows ZIP: 17 799 470 bytes, SHA-256
  `1CD5FF573B28E6A505973F11E9550CD5F7569690C5942C886893E7500C6127A9`; EXE 0.4.1.0,
  162 304 bytes, SHA-256 `91531242F9494EB7325A21026F7956E69CED9DE4B8DEF0C0DE4D0749A0CF8026`.
- Chromium ZIP: 3854 bytes, manifest MV3 0.4.1 y SHA-256
  `4F7BCB44A1E892DDD06F83F98E33D0B23A42CFD10F5DA2AE8E0D1BDC5B2B7EF8`.
- Smoke publicado: exit 0; ventana real `InputIdle=True`, título `UniShare`, versión 0.4.1.0 y cierre limpio.

## Comandos de validación previstos

```powershell
dotnet restore UniShare.slnx --locked-mode
dotnet build UniShare.slnx -c Release --no-restore
dotnet test UniShare.slnx -c Release --no-build
dotnet format UniShare.slnx --verify-no-changes --no-restore
dotnet publish src/UniShare.Desktop/UniShare.Desktop.csproj -c Release
```

## Evidencia de ejecución 2026-09-24

- `dotnet restore UniShare.slnx --locked-mode`: correcto, 6 proyectos.
- `dotnet build UniShare.slnx -c Release --no-restore`: correcto, 0 warnings y 0 errores.
- `dotnet test UniShare.slnx -c Release --no-build`: 52 superadas, 0 fallidas (13 dominio, 39 infraestructura).
- `dotnet format UniShare.slnx --verify-no-changes --no-restore`: correcto después de normalizar formato.
- `dotnet publish ... -o artifacts/windows`: generó `UniShare.Desktop.exe` y dependencias.
- Smoke con perfil vacío: exit code 0 y `library.db` de 135 168 bytes.
- Arranque GUI publicado: proceso vivo, `WaitForInputIdle=true`, `MainWindowTitle=UniShare`; se cerró después de la comprobación.
- Benchmark FTS5: 100 000 items, p95 91,2185 ms, resultado `Passed=true`.
- Utilidad real de backup: export, inspect y restore finalizaron con exit 0; la app abrió el perfil restaurado en smoke con exit 0.
- Descargas: pruebas de descarga completa, persistencia tras reapertura, reanudación, cancelación,
  redirecciones, tamaño excedido y cuerpo truncado; publish y arranque GUI repetidos tras añadir la cola.
- Transporte fijado: una prueba con hostname `.invalid` sirve contenido desde la IP devuelta por la
  política, demostrando que la conexión no hace una resolución DNS independiente.
- Una respuesta `.png` declarada como imagen pero firmada `%PDF-` queda registrada como PDF. Una página
  hostil se convierte en HTML local con CSP, texto legible y sin `<script>`, `onclick` ni tracker remoto.
- Una respuesta `.html` con MIME engañoso y una `.svg` activa se convierten también en instantáneas inertes.
  Fuente, autor y descripción se extraen de `<meta>` y se guardan con el asset; un trabajo interrumpido en
  `Running` se retoma después de reabrir SQLite.
- Android: `gradlew :app:testDebugUnitTest :app:assembleDebug :app:lintDebug --offline --no-daemon`
  completó correctamente; 3 pruebas, 0 fallos, lint 0 errores y 3 avisos informativos (dos versiones
  fijadas y sugerencia KTX). APK debug SHA-256
  `570FF45EAAE94FDE146142C9816B608A0950E092653BBF69067676D8C62CE5BD`.
- Emulador AOSP API 36: `adb install -r` éxito; `am start ACTION_SEND` con texto y URL creó una fila;
  `run-as ... sqlite3` y `uiautomator dump` verificaron UUID/título/URL y error visible del payload
  `javascript:`. Recuento permaneció 1; `force-stop`, reapertura y `adb reboot` conservaron el UUID.
  `settings get global wifi_on` y `mobile_data` devolvieron 0 antes de la última reapertura.
- Migración Android real: antes de abrir el APK nuevo, `PRAGMA user_version=1`; después, versión 2
  y el UUID/URL anterior intactos. SAF importó `unishare-fixture.txt` (38 bytes): host, asset y blob
  coincidieron en SHA-256 `6315d8e4baef620c13aede67bf6cbb7e083836ad772505cea3b21e43bd85758b`.
  Un segundo import creó otro asset con la misma ruta y continuó existiendo un solo archivo CAS.
  URL válida + selector SAF creó `kind=3` con `https://example.com/hybrid-saf`; SQLite y la UI
  mostraron el híbrido. Tras `adb reboot` con red desactivada persistieron LINK, dos FILE, HYBRID y blob.
- Export Android→Windows: el selector creó `unishare-backup-v1.unishare.zip` (SHA-256
  `36C32D6A90852502C6B724EF4635034EED855B8DECA246EB4CCD1C5789FF3549`). La CLI .NET inspeccionó y
  restauró formato 1 con 4 items, 3 assets, 0 tags, 0 colecciones, 1 blob y 38 bytes. El smoke WPF del
  perfil restaurado devolvió exit 0 y el blob restaurado conservó SHA-256 `6315d8e4...d85758b`.
- Captura rápida Windows: el árbol de accesibilidad publicado expuso URL, colección, las dos acciones
  de guardado y el selector de biblioteca. UI Automation creó `https://example.com/quick-e2e` en
  `Pruebas/Automática`; exportar ese perfil confirmó 1 item, 1 colección y 0 blobs. La prueba externa
  de **Guardar offline** conservó también item/colección, pero `example.com` no completó la transferencia
  en este entorno; el job quedó `Failed` y reintentable. Por ello el HTTP externo real permanece parcial.
- Interfaz simplificada: el compilador WPF validó el nuevo diseño con una sola acción `Guardar`, opción
  offline separada, menú de opciones y acciones contextuales; build, 52/52 pruebas, formato y smoke pasan.
  La captura visual automatizada de esta revisión queda `NO VERIFICADA` porque el controlador disponible
  no expuso aplicaciones nativas durante la sesión.
- Entrega coordinada 0.1.1: Android compiló `versionCode=2`, 4/4 tests y lint sin errores; instalado
  sobre la base v2 anterior conservó cuatro items. UI Automator confirmó los encabezados, una sola acción
  `Guardar`, `+ Archivo`, `Opciones`, búsqueda y lista. Un guardado directo persistió título `example.org`
  y URL `https://example.org/android-new` después de `force-stop` y reapertura. Windows y Android se
  vuelven a empaquetar conjuntamente antes de la entrega; siguen declaradas las diferencias funcionales.
- Artefactos coordinados 0.1.1: Windows ZIP SHA-256
  `1277EE0DBAD6B493358F063FDE37DF26BA0273346BA8C5786BCD45048DB0E19E`; ejecutable Windows
  `5A6EDB0208A7AE86AB968978C7371259CDA0A591DB1DD700AFAC3C3E0F7644D2`; APK Android
  `38EAA96A20C68FBAE4DE07F8A31EA40DD6A0E4854FDF304F2385BAC6A720A667`.
- Sincronización 0.2.0: esquema Windows v4 y Android v4 migraron sin pérdida. La suite .NET pasó
  55/55 (13 dominio, 42 infraestructura), incluyendo paquete bidireccional, CAS, organización,
  cambio unilateral, conflicto concurrente y resolución local/remota. Android pasó 11/11, build y lint.
- E2E real Android→Windows: paquete con identidad `4b5f10f0-5566-4420-8768-f0647d7247e8`, 5 items,
  3 assets y 1 blob de 38 bytes; Windows importó 5 nuevos y verificó el blob.
- E2E real Windows→Android: Windows añadió un sexto item; Android lo mostró y la reimportación produjo
  `0 nuevos, 0 actualizados, 6 sin cambios y 0 conflictos`. La pantalla de conflictos mostró ambas
  versiones y las acciones local/remota; tras resolver, `Resolver conflictos (0)` quedó deshabilitado.
- Vuelta Android→Windows: paquete Android final inspeccionado con 6 items, 3 assets y 1 blob; Windows
  informó 6 sin cambios y 0 conflictos. La prueba en teléfono físico permanece `NO VERIFICADA`.
- Artefactos coordinados 0.2.0 (2026-09-24): Windows ZIP 17 757 499 bytes, SHA-256
  `446683895AA3CFF63D6890447F818AD0D803C81E4358621BB3E6E8909A822644`; EXE 0.2.0.0 SHA-256
  `FBEA1A9B9AF9CEAF448A36B0EA896C672AC400467D9CB95D1D9D21900FEB817D`, smoke exit 0. APK
  10 067 940 bytes, `versionCode=3/versionName=0.2.0`, SHA-256
  `6EB25E7998F8569A12D4213144381E51657981606B700B7F2C302E4CFED2F59B`, firma debug v2 verificada.
- Sincronización directa 0.3.0: la prueba nueva del servidor verifica 401 sin credencial y reconciliación
  bidireccional autenticada. La suite .NET pasa 56/56. Android compila con WorkManager 2.12.0, pasan
  11/11 pruebas y lint no reporta errores.
- E2E de transporte Android real: APK 0.3.0 instalada con `adb install -r` conservó seis items previos.
  Por HTTP local limitado al emulador y `adb reverse`, Android transfirió al perfil Windows 6 items,
  3 assets y 1 blob de 38 bytes. Tras importar dos cambios Windows, el sondeo activo actualizó la base
  Android a 8 filas; preferencias privadas registraron `last_result=correcto` y la revisión del servidor.
- Tailscale Windows 1.102.3: backend `Running`, nodo online y MagicDNS disponible. Ya existían handlers
  Serve en 443, por lo que UniShare usa 8443 y no los reemplaza. No se modificó la configuración externa;
  la ruta HTTPS desde teléfono y el cambio Wi-Fi↔datos móviles permanecen **NO VERIFICADOS**.
- Cierre Windows 0.3.0: el primer smoke reveló y reprodujo un deadlock al bloquear el hilo WPF durante
  `DisposeAsync`. La secuencia se corrigió; el paquete final da `SMOKE_EXIT=0` y una apertura/cierre real
  terminó con `INPUT_IDLE=True`, `CLOSE_SENT=True`, `EXITED=True`.
- Artefactos coordinados 0.3.0 (2026-09-24): Windows ZIP 17 778 597 bytes, SHA-256
  `6707FE65660E54A015589F260E541770BA9063D88997A427F489D0D900E0B3FC`; EXE 0.3.0.0 SHA-256
  `D498C2F1D62ADADA2C03F4F60AF52069F7C5BD27550896BE6B455A78D3769DBF`; APK 10 570 514 bytes,
  `versionCode=4/versionName=0.3.0`, SHA-256
  `60755C593A192A935553E1DE8319A85169479DF95B38E2F683C18AF7A2F716B1`, firma debug v2 verificada;
  ZIP Android SHA-256 `BB2441DE2E064FF9734FE567E2B9CDE77E70050ECBA65349052993A0F3548AA4`.
- Consolidación 0.3.1: contrato portable compartido por respaldo/fusión e inyección de servicios pasan
  56/56 pruebas .NET y formato; política Android añade 4 casos y la suite pasa 15/15, build y lint sin
  errores. Se comprobó que un sondeo sin cambio no transfiere y un reintento local sí fuerza transferencia.
- APK 0.3.1 instalada con `adb install -r` en AOSP API 36: conservó configuración/datos, informó
  `versionCode=5/versionName=0.3.1` y completó sincronización autenticada contra el perfil Windows con
  resultado `correcto (0 nuevos, 0 actualizados)`. El ejecutable publicado cerró con `SMOKE_EXIT=0`.
- Artefactos coordinados 0.3.1 (2026-09-24): Windows ZIP 17 774 175 bytes, SHA-256
  `4CB570738C1A52045A8767E92365DE5FF357C8A454DBBD6ADAC3DB5CA7B110F2`; EXE 0.3.1.0 SHA-256
  `F783D80EF5BB709FA44B9854B4E7EC9DE5AEA51FDEEA64C08654E56A6BB74CCC`; APK 10 679 868 bytes,
  SHA-256 `B38078426E76E9448C9B4262AC7A930D93BF8E08697C73ACF45300391DDBF92F`, firma debug v2 verificada;
  ZIP Android SHA-256 `6B4FD3193AA5DB383692460214A2A44ACD05E714268BF36C8D80638167A56864`.
- Artefactos coordinados 0.4.0 (2026-09-25): Windows ZIP 17 793 561 bytes, SHA-256
  `209A27CBBDCF44878BB696D87981BE3830C6AE2192FE2B05398CD1F567B862ED`; EXE 0.4.0.0 SHA-256
  `36381D0318A9B414DA0B9E8CB2719320B0BE6A03396018CA4477C4C29C9DA4AB`; APK 10 620 114 bytes,
  `versionCode=6/versionName=0.4.0`, SHA-256
  `3DB117B765EEE5D1164F0E195E55CE1EF460D2AD6DB1965310F3454833BF99F7`, firma debug v2 verificada;
  ZIP Android 10 195 912 bytes, SHA-256
  `AEAA6667AEAD4FF30ED08D16F8BC5AD53DC3E438F2E9E509157925AD7170EFF3`; extensión Chromium ZIP
  3 853 bytes, SHA-256 `1E6957C9887524ABC6AD75C589FFA5A5C95420BF3A3A2E958240A027087711AB`.
- La APK 0.4.0 se instaló con `adb install -r` sobre los datos 0.3.1. `dumpsys package` confirmó la
  versión y UI Automator volvió a mostrar `OfflineTests`, `Pruebas`, `android` y `etiqueta-demo`,
  además del favorito; no se borró la biblioteca existente.
- El ejecutable Windows publicado completó `--smoke-test` con exit 0. Un segundo lanzamiento real creó
  ventana `UniShare`, `MainWindowHandle` distinto de cero, `Responding=True` y cerró por `CloseMainWindow`
  dentro de 10 segundos. El binario final incorpora la revisión `d1354e5`. El controlador de captura
  nativa no enumeró apps, por lo que la imagen de esa
  ejecución queda `NO VERIFICADA`, aunque el estado de ventana sí fue observado por el proceso.

Los comandos se ajustarán al nombre real generado sin borrar evidencia anterior.

## Evidencia de ejecución 0.6.0 — 2026-09-26

- .NET: compilación Release con 0 advertencias y 0 errores; 69/69 pruebas (13 de dominio y 56 de
  infraestructura); `dotnet format --verify-no-changes` correcto.
- Captura renderizada: la prueba de integración autenticada creó un elemento HYBRID con título,
  autor, descripción, texto renderizado e imagen embebida. El HTML resultante conserva CSP inerte,
  codifica texto hostil y no contiene scripts ejecutables.
- Obsidian: las pruebas verificaron exportación estable por UUID, adjuntos, bloque editable e índice
  `UniShare/Índice.md`; la reimportación ignora ese índice administrado y no produce falsos cambios.
- Windows: el ZIP final se extrajo en un perfil aislado; `UniShare.Desktop.exe --smoke-test` devolvió
  0, creó SQLite y declara `FileVersion=0.6.0.0`. El instalador declara `ProductVersion=0.6.0`.
- Android: 34/34 pruebas debug y 34/34 release, `lintRelease` con 0 errores y 13 avisos informativos,
  y `assembleRelease` correcto. La APK final se reinstaló con éxito y abrió en AOSP API 36.
- Android adaptable: recorrido a 320 dp y fuente del 150 %; biblioteca y formulario de guardado
  permanecieron navegables, los filtros conservaron desplazamiento horizontal y el formulario vertical.
  UI Automator expuso 46 nodos con nombres accesibles. Los contrastes principales calculados fueron
  5,49:1–16,27:1.
- APK: `versionCode=13`, `versionName=0.6.0`, minSdk 26, targetSdk 36 y firma APK v2 válida. El APK
  extraído del ZIP coincide byte por byte con el APK directo.
- Extensión: `node --check` correcto para `popup.js` y `options.js`; el ZIP contiene manifiesto 0.6.0
  y sólo los siete archivos esperados. El endpoint y el HTML inerte se cubren por integración. La carga
  real en Edge/Chrome sigue **NO VERIFICADA** porque instalar una extensión requiere autorización
  explícita sobre el navegador del usuario.
- Obsidian de escritorio/móvil y la escucha auditiva con Narrador/TalkBack siguen **NO VERIFICADAS**:
  Obsidian no está instalado en el equipo y el controlador nativo no expuso la ventana WPF. No se
  infiere su aprobación a partir de tests.
- Artefactos finales (SHA-256): Windows Setup
  `865995EC43903A3E303E92FC89D68FE9605AB67F9E8C877E1A6739006E617539`; Windows ZIP
  `7AB7DD26AC52340FB611CA885A1E1472E0974F5AA1B4BFE60D341A0CB923CC00`; APK
  `D04B110F46AC75948A11FB56B1AD4D937698BB8AC9EDE707F9E556649CD12967`; Android ZIP
  `820AF9D4DE78CC4C3ECA894F8C54D90B2F11462833936758501A356101C15D43`; Chromium ZIP
  `720BA9866A733724B8BBD44748CB565BAF6B7995CADAD569078EB2FF5B217115`.

## Evidencia de ejecución 0.6.1 — 2026-09-26

- Causa del menú ilegible: el tema oscuro aplicaba el primer plano de UniShare, pero plantillas nativas
  de WPF conservaban fondos claros del sistema. Los controles ComboBox/ComboBoxItem tienen ahora una
  plantilla propia con fondo, borde, foco, hover y selección; Menu, MenuItem, ContextMenu y las claves
  nativas de color se alinean con el tema activo.
- Eliminación recuperable: Windows muestra **Eliminar** en la cabecera y el inspector, admite `Supr`,
  pide confirmación y mueve a **Papelera**. Android muestra **Eliminar** dentro de Editar información,
  pide confirmación y ofrece **Ajustes > Mostrar papelera**. Restaurar conserva el UUID y sincroniza el
  tombstone existente; no se introdujo borrado físico silencioso.
- Android AOSP API 36: el menú superior se inspeccionó en tema claro con texto oscuro sobre superficie
  clara. Un enlace sintético abrió Editar información, expuso **Eliminar**, mostró la confirmación con
  explicación de sincronización, desapareció de Biblioteca, apareció como **En la papelera** y se
  restauró correctamente.
- Validación: build .NET Release 0 advertencias/0 errores, 69/69 pruebas y formato limpio. Android pasó
  34/34 pruebas debug y 34/34 release; `lintRelease` 0 errores; `assembleRelease` correcto. Windows ZIP
  final smoke exit 0, SQLite creado, `FileVersion=0.6.1.0`; Setup `ProductVersion=0.6.1`.
- APK final: `versionCode=14`, `versionName=0.6.1`, minSdk 26, targetSdk 36 y firma v2 válida. El APK
  contenido en el ZIP coincide por SHA-256. Extensión Chromium: manifiesto 0.6.1 y sintaxis JS válida.
- El controlador Windows no enumeró la ventana WPF de la sesión, por lo que la captura visual nativa
  del ComboBox queda **NO VERIFICADA**. La plantilla explícita fue compilada y el ejecutable empaquetado
  pasó smoke, pero se mantiene esta limitación honestamente.
- Artefactos finales (SHA-256): Windows Setup
  `5036C3280291E827D1996AFEAD6CB50127609E1DF971CE1E4D59235DFE42FF32`; Windows ZIP
  `703FCA84B709BB4C4E8D48B67133B4BF7FE27A9EB785F58993E069C12920EE13`; APK
  `E3D062B4D6D9AB7BE2A1A04D23D8A9B61C240550CF1CD8866F65984C1ECDF834`; Android ZIP
  `BF5E2C49DC06173AB9B0480E08D339A9FC585D9F01ED34508AABB6F2E91FBFF7`; Chromium ZIP
  `949AE216255E4B079AD20075A51C308CFD78CA65DB70532B106773F1B46CFB00`.

## Evidencia de ejecución 0.6.2 — 2026-09-26

- Causa confirmada: `DirectSyncConfiguration.PairingKey` existía y autenticaba el servidor, pero la
  ventana sólo la codificaba dentro del QR/enlace de Android; no había ningún control que la mostrase
  para configurar Edge.
- Windows expone ahora puerto y clave en **Datos de conexión para Edge**, con campos de sólo lectura y
  acciones **Copiar puerto** y **Copiar clave**. El valor procede de la misma configuración persistida
  que verifica el encabezado Bearer, sin generar una segunda credencial.
- Build .NET Release: 0 advertencias/0 errores; 13/13 pruebas de dominio y 56/56 de infraestructura;
  `dotnet format --verify-no-changes` correcto. Windows ZIP final: smoke exit 0, SQLite creado,
  `FileVersion=0.6.2.0`; instalador `ProductVersion=0.6.2`.
- Android coordinado: 34/34 pruebas debug y 34/34 release; `lintRelease` terminó con 0 errores;
  `assembleRelease` correcto. APK `versionCode=15`, `versionName=0.6.2`, minSdk 26, targetSdk 36,
  firma v2 válida y contenido del ZIP idéntico por SHA-256.
- Extensión Chromium: manifiesto 0.6.2, JavaScript válido y ZIP con `manifest.json` en la raíz.
- **NO VERIFICADO:** recorrido visual de la ventana WPF. El controlador de interfaz disponible sólo
  enumeró navegadores y no aplicaciones nativas; la asignación de puerto/clave y los handlers se
  verificaron por compilación e inspección, pero no se declara una captura visual como superada.
- Artefactos finales (SHA-256): Windows Setup
  `F02FF6370BE3D4FF5CD9AA2D8A4B5C13483F422276870A5C7AE6C7A1A868FE0C`; Windows ZIP
  `AAA03858DE51C38F211F28E46CD65B0267F4F013289233DAF2ABC5BE61FB1FE8`; APK
  `389083CCC1729EAEBFD4A7B964EE257DF5BCF903BC4804C704A7E5F64FD10FA1`; Android ZIP
  `E477DA4FB9A7B6A608F74E1F4A878FC5E0B6902082B34DD35914AEB2EF55A1B3`; Chromium ZIP
  `1CD424718A074B65421035BB96200350064B4A982263BB9CC58718066D0943D4`.

## Evidencia de ejecución 0.6.3 — 2026-09-26

- Extensión Edge: Manifest V3 0.6.3 parseado, sintaxis correcta en `background.js`, `popup.js` y
  `options.js`. El ZIP tiene `manifest.json` en la raíz, sólo 11 archivos de ejecución y no incorpora
  documentación, fuentes gráficas ni archivos de Partner Center.
- Iconos runtime verificados en 16, 32, 48 y 128 píxeles; logotipo de ficha verificado en 300 × 300. El
  expediente contiene exactamente el ZIP de runtime, logotipo, ficha es-ES, política y guía de envío.
- Build .NET Release 0 advertencias/0 errores, 13/13 pruebas de dominio, 56/56 de infraestructura y
  formato limpio. Windows final: smoke exit 0, SQLite creado, `FileVersion=0.6.3.0`; Setup
  `ProductVersion=0.6.3`.
- Android coordinado: 34/34 debug, 34/34 release, lint 0 errores y assemble correcto. APK
  `versionCode=16`, `versionName=0.6.3`, minSdk 26, targetSdk 36 y firma v2 válida; el APK del ZIP coincide.
- **NO VERIFICADO / pendiente externo:** instalación desde Edge Add-ons. Microsoft debe certificar una
  ficha enviada por el propietario desde Partner Center; no se puede simular esa aprobación con un CRX.
- Artefactos finales (SHA-256): Windows Setup
  `374893349B14BE5708646931B608C392CA256C0DC1F2ABDCF64EB885D56E1D96`; Windows ZIP
  `4BF975D461592FBD189950E8669CD04A103B6AD7F4D518FE7619A5A0DED5C9DF`; APK
  `D47D1BFF22C14BDDA304A1388B451D944E8F37B67D249E5EBB976265D1846D25`; Android ZIP
  `E7D8228CC9C2C1389085BEB75E000CC30CE6BEB24FF925249231772BDDC98376`; Edge Store/Chromium ZIP
  `61F3B739C8F84EBC4390FCA5558636E819C2824BEA885965AAB9F3018D8A43F6`; expediente Edge
  `A0972C26E23EC63E10F93970C5E877F1C5F5219A68F2B07E4731002C049481F5`.

## Evidencia de ejecución 0.6.4 — 2026-09-30

- Fuente consolidada en Git como `af6d8a7`. El ejecutable final declara `FileVersion=0.6.4.0` y
  `ProductVersion=0.6.4+af6d8a7…`; el instalador declara `ProductVersion=0.6.4`.
- .NET Release: compilación con 0 advertencias/0 errores, 13/13 pruebas de dominio, 61/61 de
  infraestructura y `dotnet format --verify-no-changes` correcto. El ejecutable autocontenido final pasó
  smoke con exit 0 y creó SQLite; el smoke ahora falla si no arranca el servidor directo.
- Android se reconstruyó desde `clean`: 36/36 pruebas debug, 36/36 release, `lintRelease` y
  `assembleRelease` correctos. `aapt2` confirmó `versionCode=17`, `versionName=0.6.4`, minSdk 26 y
  targetSdk 36. `apksigner` confirmó firma v2; es todavía el certificado Android Debug.
- Chromium/Edge: Manifest V3 0.6.4, sintaxis válida en los tres JavaScript, 11 entradas runtime con el
  manifiesto en raíz y expediente de cinco entradas.
- **NO VERIFICADO:** recorrido visual Windows porque el controlador nativo devolvió inventario vacío y
  no expuso el lanzamiento; Sharesheet/SAF y TalkBack en teléfono físico; Narrador; cambio de red durante
  transferencia; instalación certificada desde Edge Add-ons. Windows no tiene Authenticode y Android usa
  clave debug: la firma definitiva necesita certificados del propietario.
- Artefactos finales (tamaño, SHA-256): Windows Setup 59 648 076 bytes,
  `11BA7923E2F97520D14608A45BCC8D8C4BF1195BAA9F740389EBC2766EDE7FE4`; Windows ZIP 84 250 376 bytes,
  `C0AEAB7A48416B681D3159EE8AAF8674249FFD2BB3852EBD4928D967DD2E6EA8`; APK 1 441 456 bytes,
  `859AB3CE847AE9E2D65236A839953D21FC417CBB8D248D12AB1D25260D3C60CB`; Android ZIP 1 264 677 bytes,
  `09276F0AB8E3A59786F066CE8AB26F2268DFE5F1694736C8E558DE6119F0DB7D`; Chromium/Edge Store ZIP
  19 224 bytes, `C68A97C17D0E732D1E5F00F678695979B7F74DDA0FC2A75AB9D04D566F71CFBA`; expediente Edge
  61 995 bytes, `6D9627740EDAD7CA43A1D35313CE86C39B9AE4C619A6B47A0FD5F0237BB8680D`.

## Evidencia de ejecución 0.6.5 — 2026-10-05

- Fuente de producto consolidada en `9eb6766`. El ejecutable final declara `FileVersion=0.6.5.0` y
  `ProductVersion=0.6.5+9eb6766…`; el instalador declara `ProductVersion=0.6.5`.
- Restauración normal y `win-x64` en modo bloqueado; formato limpio; compilación Release con
  0 advertencias/0 errores. Microsoft.Testing.Platform ejecutó 13 pruebas de dominio, 61 de
  infraestructura y 2 nuevas de escritorio: 76/76 correctas. Cobertura Cobertura/TRX generada.
- Benchmark limpio de 100 000 elementos: mediana 56,2635 ms, p95 57,5892 ms, límite 300 ms.
- Ejecutable autocontenido: smoke exit 0, creación real de `library.db`, servidor directo operativo y
  consulta inicial. El export/inspect/restore CLI de backup v1 creó una base restaurada; la suite cubre
  además round-trip con datos, hashes, relaciones y rechazo de manipulación.
- Android reconstruido desde `clean`: 36/36 pruebas debug, 36/36 release, `lintRelease` y R8/assemble
  correctos. APK `versionCode=18`, `versionName=0.6.5`, minSdk 26, compile/targetSdk 36 y firma v2 con el
  certificado debug compatible con las instalaciones anteriores. El APK dentro del ZIP coincide por hash.
- Chromium/Edge: Manifest V3 0.6.5 parseado, los tres JavaScript pasan `node --check`, 11 entradas de
  runtime con `manifest.json` en raíz y expediente de publicación con cinco entradas.
- Dependencias NuGet: auditorías `--vulnerable`, `--deprecated` y `--outdated` sin hallazgos después de
  migrar a xUnit v3/MTP y centralizar versiones.
- **NO VERIFICADO / externo:** ejecución del workflow en runners alojados de GitHub; recorrido visual
  Windows/Narrador; Sharesheet, SAF y TalkBack completos en teléfono; cambio de red durante transferencia;
  instalación certificada Edge Add-ons; Authenticode y firma Android de producción.
- Artefactos finales (tamaño, SHA-256): Setup Windows 59 688 409 bytes,
  `ACD7CAF52612B5BB6EA7110C7FB5D642106D5E1BBD619A48E872C0938BD3F71A`; Windows ZIP 84 325 367 bytes,
  `F774A5C74B1C6A6F408CD07B58654536C87B4956E149C7F89167933007E6B535`; APK 1 569 859 bytes,
  `946F792C9BE52B064E847D7B60EAB1C3BA326812CF08424B819C272337E1E8DB`; Android ZIP 1 353 932 bytes,
  `0B2590236B57C24DBE7E8BF14A7A5E574CA6F5173B712966A003E09C15D49FF7`; Chromium/Edge Store ZIP
  19 225 bytes, `8FD43973FCB25C71ECAB5318DD858530BE26C00626DB55DAFC9E5E1008C8DD60`; expediente Edge 61 996 bytes,
  `A992B5E99A7A33265BB9F69C1EC4A26734BABBCBABFA60E54ED1E2DFAFD352AF`.

## Evidencia de ejecución 0.6.6 — 2026-10-05

- Fuente `c2b3785`. Windows declara `FileVersion=0.6.6.0`,
  `ProductVersion=0.6.6+c2b3785…`; el instalador declara `ProductVersion=0.6.6`.
- El perfil Windows activo y su clave vigente devolvieron HTTP 200 en `/api/v1/status` por
  `127.0.0.1:47831` y por Tailscale Serve; una petición sin clave devolvió 401. El teléfono figuró
  activo y con transporte directo en `tailscale status`.
- .NET: restore normal y win-x64 bloqueados, formato limpio, Release 0 advertencias/0 errores y 76/76
  pruebas. El publicado autocontenido pasó smoke con exit 0 y `library.db` real usando 47832 para no
  interrumpir el nodo 47831 en ejecución.
- Android: 39/39 pruebas debug y 39/39 release, R8/assemble correctos y lint sin errores (15 warnings y
  1 hint no bloqueantes ya conocidos). `aapt2` confirmó versionCode 19, versionName 0.6.6, minSdk 26 y
  targetSdk 36; `apksigner` confirmó firma v2. `adb install -r` y arranque real pasaron en AOSP API 36.
- Cinco ZIP leídos completamente sin error: Windows 418 entradas, Android 1, Chromium/Edge 11 y
  expediente Edge 5. Los tres JavaScript MV3 pasan `node --check`.
- **NO VERIFICADO:** la actualización 0.6.6 y la desaparición visual del estado obsoleto en el teléfono
  físico, porque no estaba conectado por ADB; Sharesheet/SAF/TalkBack completos, cambio de red durante
  transferencia, Narrador, publicación Edge, Authenticode y firma Android de producción.
- Artefactos finales (tamaño, SHA-256): Setup Windows 59 706 777 bytes,
  `5540D0432818B32E41A18789E97A71F12841FE0035DB04C209FF5926F4DF6826`; Windows ZIP 84 325 357 bytes,
  `0EBD14CCB34370DEEE352F3B46960914D876F24CEB3FD76CE18BABDD5C4DBF31`; APK 1 569 855 bytes,
  `1AF988039451D2759245F76A8089BC9DB4E762D26D1B61A84928F95061AE0264`; Android ZIP 1 353 839 bytes,
  `10F267860CEC89EB0FDB706BC9E4620CF8973962940CE1221208E6A794839479`; Chromium/Edge Store ZIP
  19 224 bytes, `BBD1588EABFC0D9E978C2AE454A9E3A09D2FFBB49E2D5419715249D0AA36A020`; expediente Edge 61 993 bytes,
  `A2FEC179EB2533D70918F42D8B9B9A1E2B4F805BCC326C5A52FF853A3A197B50`.

## Evidencia de ejecución 0.6.7 — 2026-10-05

- Fuente de producto `a4fded6`. Windows declara `FileVersion=0.6.7.0` y
  `ProductVersion=0.6.7+a4fded6…`; el instalador declara `ProductVersion=0.6.7`.
- .NET: restauración bloqueada, formato limpio, compilación Release con 0 advertencias/0 errores y
  78/78 pruebas. El publicado autocontenido pasó smoke con exit 0, base SQLite real y servidor directo
  en el puerto aislado 47832.
- Android: 40/40 pruebas debug y 40/40 release, R8/assemble correctos y lint sin errores. `aapt2`
  confirmó versionCode 20, versionName 0.6.7, minSdk 26 y targetSdk 36; `apksigner` confirmó firma v2.
  `adb install -r` actualizó sin borrar datos y MainActivity abrió en AOSP API 36 sin excepciones.
- Pruebas nuevas cubren la prioridad Open Graph/Twitter/HTML y la descripción visible y accesible. Las
  reglas de migración sólo sustituyen el título provisional del dominio y sólo rellenan fuente, autor o
  descripción vacíos; cada cambio se registra para la sincronización directa.
- Cinco ZIP leídos completamente sin error: Windows 418 entradas, Android 1, Chromium/Edge 11 y
  expediente Edge 5. Los tres JavaScript MV3 y el manifiesto pasan validación.
- **NO VERIFICADO:** la consulta real de todos los enlaces históricos del perfil del teléfono físico,
  porque el teléfono no estaba conectado por ADB y algunos sitios pueden exigir sesión, bloquear bots o
  no publicar descripción. El reintento manual queda disponible. Siguen pendientes las pruebas físicas
  completas de Sharesheet/SAF/TalkBack, Narrador, publicación Edge, Authenticode y firma Android comercial.
- Artefactos finales (tamaño, SHA-256): Setup Windows 59 694 461 bytes,
  `E70ACAFD1DA2AE3117BC82B97E23A41CE09EF84B5EFB5609C5F583C969344841`; Windows ZIP 84 327 587 bytes,
  `7BC2007D2DB73136936AF83F03767466D88496EE4A34D17BECD79E681EFA1A57`; APK 1 569 859 bytes,
  `E2896491783F4F84C80FF0B0DDD4F3A3B2C190A6922BE9B5F232DCFB0AF8FE1D`; Android ZIP 1 355 635 bytes,
  `47EA2A8DBCCCBB281B68DB47D126BF4FA0698ABF75FA773D267302C07FDBB14E`; Chromium/Edge Store ZIP
  19 372 bytes, `98F076592565BA2FF4A7B72304AED1DBEA66B041CAA8FFC258DAEDC55EF0A5D9`; expediente Edge 62 142 bytes,
  `2247234046C2C47D5642A4CE62E211055AA92FF39B5F5EE5623CAD60C7435283`.

## Evidencia de ejecución 0.6.8 — 2026-10-05

- Fuente de producto `2ff59a5`. Windows declara `FileVersion=0.6.8.0` y
  `ProductVersion=0.6.8+2ff59a5…`; el instalador declara `ProductVersion=0.6.8`.
- .NET: restauración bloqueada, formato limpio, compilación Release con 0 advertencias/0 errores y
  81/81 pruebas. El ejecutable autocontenido pasó smoke con salida 0, creó una base SQLite real e inició
  el servidor directo en el puerto aislado 47832.
- Android: 43/43 pruebas debug y 43/43 release, `lintRelease`, R8 y `assembleRelease` correctos. `aapt2`
  confirmó versionCode 21, versionName 0.6.8, minSdk 26 y targetSdk 36; `apksigner` confirmó firma v2.
  `adb install -r` actualizó 0.6.7 a 0.6.8 y MainActivity quedó enfocada en AOSP API 36, sin excepción
  fatal de la aplicación en las últimas 400 líneas de logcat.
- Las regresiones prueban extracción literal desde el cuerpo, prioridad de Schema.org `articleBody`,
  elección del bloque semántico más sustancial, exclusión de navegación y publicidad, rechazo de una
  descripción puramente promocional y conservación de notas manuales. La revisión 2 de migración
  enumera todos los elementos LINK/HYBRID con URL tanto en Windows como en Android.
- Cinco ZIP leídos completamente: Windows 418 entradas, Android 1, Chromium/Edge 11 y expediente Edge
  5, sin rutas absolutas o ascendentes. Los tres JavaScript MV3 y el manifiesto son válidos.
- **NO VERIFICADO:** reanálisis real de todos los enlaces del perfil del teléfono físico (no conectado
  por ADB); páginas que exigen sesión, bloquean clientes nativos o renderizan todo con JavaScript pueden
  quedar sin resumen. Continúan sin verificar en dispositivo físico Sharesheet/SAF/TalkBack completos,
  Narrador, publicación Edge, Authenticode y firma Android de producción. El reintento manual permanece
  disponible y la extensión puede analizar el DOM ya renderizado.
- Artefactos finales (tamaño, SHA-256): Setup Windows 59 708 752 bytes,
  `2DA52778D81094644F6A5D3CBF87EB8B8452FC44B30E19BCA3F27693D7657752`; Windows ZIP 84 334 827 bytes,
  `7BB96AC95CA87EC6EB79258195BD22417B0BB137750D3DF249071F91F0601E19`; APK 1 569 859 bytes,
  `311DDBAF6B2F662568541341CBB2351113BE99131838550161021F3AAA3403AC`; Android ZIP 1 357 963 bytes,
  `8E2948AB9299ADD5A3E9616E124DE1E08E71748F62E6C8298A46A5720ACBBCBC`; Chromium/Edge Store ZIP
  20 623 bytes, `DDD8EFD5F41BDDDD21302D2838E0A3FDC756B5C74BB4F0D8CFB155175C8035DE`; expediente Edge
  63 390 bytes, `E678B6B8A3FC5561606EE9EFFC53FE2A6E9EBED77B588DD008CB68768BE9F2AF`.

## Evidencia de ejecución 0.6.9 — 2026-10-06

- Fuente de producto `28c77de`. Windows declara `FileVersion=0.6.9.0` y
  `ProductVersion=0.6.9+28c77de…`; el instalador declara `ProductVersion=0.6.9`.
- .NET SDK 10.0.401: formato limpio, compilación Release con 0 advertencias/0 errores y 83/83 pruebas
  (dominio, infraestructura y escritorio). Las regresiones verifican exportación de un subconjunto,
  reenvío con cursor sin confirmar, avance tras ACK y snapshot completo ante un cursor futuro.
- Android: 48/48 pruebas debug y 48/48 release; `lintRelease`, R8 y `assembleRelease` correctos. `aapt2`
  confirmó versionCode 22, versionName 0.6.9, minSdk 26 y targetSdk 36; `apksigner` confirmó firma v2.
  `adb install -r` conservó los datos 0.6.8/0.6.9 previos y MainActivity quedó enfocada en AOSP API 36,
  sin excepción fatal ni error SQLite en el arranque.
- E2E aislado instrumentable: servidor v2 con identidad/cursor; primer snapshot; enlace Android→Windows
  confirmó contadores 0→1; un enlace creado en Windows llegó automáticamente a Android con cursor 1→2 y
  apareció en la UI. El ejecutable autocontenido final pasó `--smoke-test` (exit 0), creó `library.db` e
  inició el servidor directo en 47832. El endpoint final informó protocolo 2, identidad y cursor 0.
- Cinco ZIP leídos completamente y sin rutas absolutas, invertidas o ascendentes: Windows 418 entradas,
  Android 1, Chromium/Edge 11 y expediente Edge 5. Los JavaScript MV3 y manifest 0.6.9 son válidos.
- **NO VERIFICADO:** instalación y delta 0.6.9 en el teléfono físico; cambio Wi-Fi↔datos durante una
  transferencia; recorridos físicos completos de Sharesheet, SAF y TalkBack; Narrador; publicación Edge;
  Authenticode y firma Android comercial. Wi-Fi y datos móviles sí fueron verificados por el usuario con
  el transporte directo anterior; esta entrega conserva el mismo Tailscale y cambia el envelope incremental.
- Firma actual: APK lateral con clave de desarrollo y esquema v2; Setup Windows `NotSigned`. La firma
  comercial permanece aplazada junto con el endurecimiento de seguridad por decisión del usuario.
- Artefactos finales (tamaño, SHA-256): Setup Windows 59 707 367 bytes,
  `6942A86760EBFF61A4E4295A6B0109B5382B82A51F8822D76BFE502376F5145D`; Windows ZIP 84 338 570 bytes,
  `05E06ECD59AA5CA803DB2DE0EBA47F39B5D3D02A389BE901F1147251DF8E1C04`; APK 1 569 859 bytes,
  `452453E4FA104A9CE0803B9F6EA43D528C85B58F85BD18DA2752892EED95935B`; Android ZIP 1 360 855 bytes,
  `E59264754315C246D74ADD44B0601F302BEF83AA3A3D39097170AED1B9032F83`; Chromium/Edge Store ZIP
  20 624 bytes, `8352A7BBD3FE9A229AB836E40263F9E7FB3D1A9E5593451C078B4F3D308F58CA`; expediente Edge
  63 393 bytes, `7CD9E99C6A78A2863063DBA8EFB7DBF3EE04CCC35C738334EA7F503DA3666517`.

## Evidencia de ejecución 0.6.10 — 2026-10-06

- Fuente de producto `4aa3d7d`. Windows declara `FileVersion=0.6.10.0` y
  `ProductVersion=0.6.10+4aa3d7dd2a2afdf93af86cb088304474d37978ba`; el instalador declara
  `ProductVersion=0.6.10`.
- .NET SDK 10.0.401: restauración bloqueada, formato limpio, compilación Release con 0
  advertencias/0 errores y 83/83 pruebas. El publicado autocontenido pasó `--smoke-test` con salida 0,
  creó SQLite e inició el servidor directo en el puerto aislado 47991.
- Android: 59/59 pruebas debug y 59/59 release, `lintRelease`, R8 y `assembleRelease` correctos. La
  política nueva identifica firmas en los primeros 512 bytes, conserva contenedores ZIP útiles y
  rechaza HTML/XHTML/SVG activo o contenido firmado que contradice el tipo declarado antes de CAS.
- API 36 ejercitó el selector SAF real: aceptó un PNG con MIME correcto y rechazó HTML disfrazado de
  PNG, sin crear item, operación, blob ni parcial y sin incrementar la generación local. Los mensajes
  de operación y sincronización permanecieron independientes. Todo el dato sintético se retiró después.
- `aapt2` confirmó versionCode 23, versionName 0.6.10, minSdk 26 y targetSdk 36; `apksigner` confirmó
  esquema v2. El APK final se instaló mediante `adb install -r`, mantuvo la fecha de primera instalación,
  abrió sin crash y conservó exactamente los dos elementos previos; el elemento sintético no reapareció.
- Edge real, en perfil aislado, cargó el paquete MV3, guardó su clave y capturó una pestaña HTTP como
  híbrido offline. SQLite confirmó título Open Graph, fuente, autor, resumen literal, colección y asset;
  el popup informó éxito y no emitió errores de consola. La CSP del HTML capturado quedó en
  `default-src 'none'; script-src 'none'`.
- Los cinco ZIP se leyeron completamente: Windows 418 entradas, Android 1, Chromium/Edge 11 y
  expediente Edge 5; cero rutas absolutas o ascendentes. El APK interior coincide por SHA-256 con el
  archivo suelto y Chromium coincide byte a byte con Edge Store.
- **NO VERIFICADO:** instalación y SAF/TalkBack de 0.6.10 en teléfono físico; cambio Wi-Fi↔datos durante
  una transferencia en curso; terminación exacta del proceso durante transferencia; recorrido visual
  Windows/Narrador (el ayudante de automatización falló antes de controlar la aplicación); clientes
  Obsidian reales; publicación/certificación Edge, Authenticode y firma Android de producción.
- Firma actual: APK lateral con certificado Android Debug y esquema v2; Setup Windows `NotSigned`.
  Firma comercial y endurecimiento adicional permanecen aplazados por decisión del usuario.
- Artefactos finales (tamaño, SHA-256): Setup Windows 59 716 003 bytes,
  `4F95C4BBFDC244886F53E25C6BDBC42899AD61777954F803B5BA084BD6785055`; Windows ZIP 84 339 125 bytes,
  `78DD8BCD6008FE2CFD01D79DE08866F58663DD7F4245DA2EC0A9D18C08C7C376`; APK 1 569 855 bytes,
  `A051C49DBDA6E9126A84F880B41C24EA6085131F76128181C47D6332ECF87D7F`; Android ZIP 1 364 736 bytes,
  `FC8424AC5643EB7CD6DEC59983AF365A0BB67BA1B9B2CF317F96EBF8FE1D06C3`; Chromium/Edge Store ZIP
  20 624 bytes, `45E6410782A716238BD73C6605A5CB5BF0DFA55B39DDBEEA9726B674FD53F316`; expediente Edge
  63 397 bytes, `41263BD55F2A122B5C4DF2CB1A09B407951C0375EE6AAB3906A2E28C594D9147`.

## Evidencia de ejecución 0.6.11 — 2026-10-06

- Fuente de producto `fbe9591`. Windows declara `FileVersion=0.6.11.0` y
  `ProductVersion=0.6.11+fbe9591e2e89334bcdca6396b0c3fad09211083a`; el instalador declara
  `ProductVersion=0.6.11`.
- Un recorrido real sobre el EXE final guardó un enlace con colección, editó título, notas, dos
  etiquetas/colecciones y favorito, lo eliminó, comprobó Papelera, restauró, cerró y reabrió. Todos los
  valores persistieron en SQLite y el recorrido terminó `PASS`.
- La misma prueba reveló que **Salir completamente** cerraba la ventana pero mantenía el proceso vivo
  durante más de 60 segundos. El apagado ahora espera Kestrel sin bloquear el hilo UI; el publicado final
  salió con código 0 en 79 ms durante empaquetado y en 76 ms durante la validación independiente.
  `test-clean-exit.ps1` convierte este comportamiento en puerta obligatoria del instalador.
- El empaquetado resuelve primero el SDK local fijado por `global.json`; se confirmó .NET 10.0.401.
  Formato limpio, Release con 0 advertencias/0 errores y 83/83 pruebas .NET.
- Android: 59/59 pruebas debug y 59/59 release, `lintRelease`, R8 y `assembleRelease` desde `clean`.
  `aapt2` confirmó versionCode 24, versionName 0.6.11, minSdk 26 y targetSdk 36; `apksigner` confirmó
  esquema v2. `adb install -r` conservó la primera instalación y los dos elementos previos, abrió sin
  crash y no recuperó el dato sintético retirado.
- Extensión MV3: los tres JavaScript pasan `node --check`, manifiesto 3 y versión 0.6.11. No cambió su
  comportamiento respecto al E2E real 0.6.10; los paquetes se regeneraron después del cambio de versión.
- Los cinco ZIP se leyeron completamente: Windows 418 entradas, Android 1, Chromium/Edge 11 y expediente
  Edge 5, sin rutas absolutas o ascendentes. El APK interior coincide con el suelto y Chromium coincide
  byte a byte con Edge Store.
- **NO VERIFICADO:** instalación y Sharesheet/SAF/TalkBack de 0.6.11 en teléfono físico; cambio
  Wi-Fi↔datos durante una transferencia en curso; escucha con Narrador; clientes Obsidian reales;
  publicación/certificación Edge, Authenticode y firma
  Android de producción. La seguridad adicional permanece aplazada por decisión del usuario.
- Artefactos finales (tamaño, SHA-256): Setup Windows 59 714 380 bytes,
  `228C4299668969EEF20006B62F5249C939FEA706320DB1CF3B3515BB6FC5D6E3`; Windows ZIP 84 338 998 bytes,
  `2D2AB746B3834416FEEE750AB65BFCAAE5FCEB66C3744ACAF34E9942FE8CD675`; APK 1 569 855 bytes,
  `DEAED1C93E5C855A879E758A6E3B172AAD92D31A0A3017E97701046E257062F7`; Android ZIP 1 364 755 bytes,
  `3561A6B27859E9D9267E528498C5348F69E181819DAEF72D503828BDFA22B04B`; Chromium/Edge Store ZIP
  20 624 bytes, `FC8B77FCC545B1918B3FEB127F6AE665796D15D582A91B72407D761365A51144`; expediente Edge
  63 397 bytes, `371BD638405809BF1E96E33EDC356EBF9A08028D0A9408DC4DF477470226C836`.

## Evidencia complementaria de corte de proceso — 2026-10-06

- `DurableDownloadProcessRecoveryTests` inicia un ejecutable aislado contra un servidor TCP local lento
  y termina únicamente ese proceso cuando el parcial alcanza 256 KiB y 1536 KiB. Los dos puntos cubren
  el periodo anterior y posterior a la actualización durable de progreso de 1 MiB.
- En ambos casos se observó un trabajo `Running` con ETag persistido, un parcial menor que el origen y
  cero assets. Una instancia nueva envió `Range` desde la longitud exacta e `If-Range: "crash-v1"`.
- La recuperación terminó en `Completed`, retiró el parcial y publicó un único blob de 8 MiB cuyos bytes
  y SHA-256 coincidieron con el origen. La prueba preliminar pasó cinco veces consecutivas y las dos
  variantes finales pasaron juntas.
- Release compila con 0 advertencias/0 errores. La solución completa pasa 86/86 pruebas .NET. El arnés
  está bajo `tests/` y no se incorpora a los paquetes de producto 0.6.11.

## Evidencia complementaria de funcionamiento offline Windows — 2026-10-06

- `OfflineWorkflowTests` compone el repositorio, CAS, servicio de biblioteca, cola durable y backup con
  una política de endpoint y un transporte HTTP que lanzan error ante cualquier intento de red.
- Sin invocar red creó un híbrido con PDF local, metadatos, favorito, etiqueta y colección; lo encontró
  por FTS, leyó sus bytes desde CAS, reabrió SQLite y conservó exactamente el item.
- Exportó una copia portable, la restauró en un perfil vacío y verificó búsqueda, manifiesto del asset y
  SHA-256 del blob restaurado. Los contadores finales de política y transporte fueron ambos cero.

## Evidencia complementaria de rendimiento — 2026-10-06

- Host de referencia: 16 núcleos/32 hilos; 31,9 GiB de memoria visible; Windows 11 Pro 64-bit,
  versión 10.0.26300/build 26300; workspace en SSD NVMe; .NET SDK 10.0.401.
- `UniShare.Benchmarks` generó 100 000 items en 7,4382 s y ejecutó 30 búsquedas exactas tras cinco
  calentamientos: mínimo 58,5362 ms, mediana 60,7006 ms, p95 80,4066 ms y límite 300 ms. `Passed=true`.

## Evidencia de ejecución 0.6.14 — 2026-10-06

- Código funcional: `956ede4`. Los resúmenes de Windows y Android se muestran en modo lectura; un botón
  **Modificar resumen** habilita explícitamente el campo editable. Las direcciones HTTP/HTTPS se
  subrayan y abren mediante el manejador predeterminado, sin habilitar otros esquemas.
- El parser conserva texto y puntuación, admite varios enlaces y recorta sólo puntuación final o cierres
  no equilibrados. Se añadieron tres pruebas equivalentes por plataforma.
- .NET SDK 10.0.401: formato limpio y 94/94 pruebas. Una prueba WPF STA crea la ventana real, confirma
  el `Hyperlink`, comprueba la vista fija y activa el editor mediante el evento del botón.
- Windows autocontenido pasó apertura/cierre limpio en 134 ms. El instalador 0.6.14 terminó con salida
  0, instaló `FileVersion=0.6.14.0` / `ProductVersion=0.6.14+956ede4…` y dejó la aplicación abierta.
- Android completó 67/67 pruebas debug y 67/67 release, `lintRelease`, R8 y `assembleRelease`.
  `aapt2` confirmó `versionCode=27`, `versionName=0.6.14`, minSdk 26 y targetSdk 36; `apksigner`
  confirmó firma v2. La instalación y comprobación visual en teléfono físico queda NO VERIFICADA.
- Los JavaScript MV3 pasan `node --check`; manifiesto y paquetes de extensión declaran 0.6.14.
- El sistema de control visual nativo no enumeró ventanas en esta sesión. Se conserva como evidencia
  equivalente la prueba WPF de controles, el smoke del EXE y la apertura real del ejecutable instalado.

## Evidencia de ejecución 0.6.13 — 2026-10-06

- Código funcional: `b518c13`. Build Release .NET con 0 advertencias/errores, formato limpio y 90/90
  pruebas. Windows autocontenido declara `FileVersion=0.6.13.0` y
  `ProductVersion=0.6.13+b518c1376e73fc60a8cdf6d6a1aa7d67351f9f03`; smoke y cierre limpio en
  71 ms. El instalador declara `ProductVersion=0.6.13`.
- Android completó 64/64 pruebas debug y 64/64 release, lint, R8 y `assembleRelease`. `aapt2` confirmó
  `versionCode=26`, `versionName=0.6.13`, minSdk 26 y targetSdk 36; `apksigner` confirmó firma v2.
- Los tres JavaScript MV3 pasan `node --check`. El manifiesto y los paquetes Chromium/Edge declaran
  0.6.13; todos los ZIP se abrieron y enumeraron sin error.
- Reproducción real y privada: 5/5 elementos activos contenían navegación concatenada. Las cinco páginas
  originales respondieron dentro de 2 MiB; el nuevo extractor produjo 5/5 resúmenes de 270–425 caracteres
  y 0 navegación. El reconocedor histórico clasificó 5/5 valores defectuosos.
- Antes de actualizar se exportó e inspeccionó un respaldo v1 de 7 items y 2 colecciones, SHA-256
  `96D81937AF474C680B73F8C995ACE438F0C4D873908EC9FEDA2D7A7FDDFEC5A3`. Tras instalar 0.6.13 y abrir la
  app: 5 elementos activos, 5 resúmenes presentes, 0 navegación, marcador de migración v3, SQLite
  `integrity_check=ok` y proceso ejecutándose.
- Artefactos coordinados anteriores al paquete de fuente:
  - Windows Setup, 59 718 968 bytes, SHA-256 `C3A223B258C16A1ADD9DBA67834237DC24C08CFF83ABA8168CED8D95DEB9D241`.
  - Windows ZIP, 84 346 818 bytes, SHA-256 `A098828D4DD4BC9BAEEBEA6C23D788191078E93CF4A1831626B85C01D84024B4`.
  - Android APK, 1 586 239 bytes, SHA-256 `ED5FC06D34A738F5AF3FACBD6610D5D5D03EBC7C452EFD1E40DEACF5DCF753CD`.
  - Android ZIP, 1 370 115 bytes, SHA-256 `424D0F78D031EE551F9C9576AEB54ECF24B24AC3FD88E018C1321D9525681B79`.
  - Chromium/Edge Store, 21 612 bytes, SHA-256 `51743A5B6F6ABD5C310BE148A1FBD0AE595F373CCA12090BCB05C6DB28B0B79F`.
  - Expediente Edge, 64 387 bytes, SHA-256 `BCA09462076DEDCD944EE1EE5EEA8360E5017759A5D784FC34FCB2B69E98CFB5`.

## Evidencia de ejecución 0.6.12 — 2026-10-06

- Fuente de producto `a7eb44117eb761fb5bae8d430bdb443544912abf`. Windows declara
  `FileVersion=0.6.12.0` y `ProductVersion=0.6.12+a7eb44117eb761fb5bae8d430bdb443544912abf`; el
  instalador declara `ProductVersion=0.6.12`.
- .NET SDK 10.0.401: restauración bloqueada, formato limpio, compilación Release con 0
  advertencias/0 errores y 87/87 pruebas. El ejecutable autocontenido final pasó `--smoke-test` con
  salida 0, creó SQLite en un perfil temporal y su ventana cerró con salida 0 en 65 ms.
- La recuperación durable se probó terminando un proceso real antes y después del checkpoint de 1 MiB.
  En ambos casos reanudó el parcial de 8 MiB con `Range`/`If-Range`, publicó un único blob exacto y no
  dejó un asset incompleto. El recorrido offline de Windows creó, buscó, reabrió, exportó y restauró un
  híbrido con PDF y hash intacto sin ningún intento de red.
- Android pasa 61/61 pruebas debug y 61/61 release, `lintRelease`, R8 y `assembleRelease`. API 36 mostró
  el modo de selección, dos casillas, `2 seleccionados` y `Eliminar (2)`; cancelar conservó ambos items.
  El APK final se reinstaló con `adb install -r`, conservó la fecha de primera instalación, abrió
  en frío en 450 ms, conservó los elementos `Delta` y `example` y no produjo un `FATAL EXCEPTION`.
- `aapt2` confirmó versionCode 25, versionName 0.6.12, minSdk 26 y targetSdk 36. `apksigner` confirmó
  esquema v2 y un firmante. Los JavaScript extraídos del ZIP final pasan `node --check`; el manifiesto
  es MV3 y versión 0.6.12.
- Los cinco ZIP se leyeron completamente y no contienen rutas absolutas ni ascendentes: Windows 418
  entradas/193 481 338 bytes expandidos, Android 1/1 569 859, Chromium y Edge Store 11/27 739 cada
  uno, y expediente Edge 5/66 487. El APK interior coincide por SHA-256 con el suelto, Chromium es
  idéntico a Edge Store y el ZIP Edge Store anidado en el expediente coincide con el artefacto suelto.
- Rendimiento en el host documentado: 100 000 items, mediana 60,7006 ms, p95 80,4066 ms frente a un
  límite de 300 ms. Los siete paquetes 0.6.11 y 26 directorios regenerables se retiraron; `artifacts/`
  conserva exclusivamente los siete artefactos 0.6.12.
- **NO VERIFICADO:** interacción visual de la selección múltiple en Windows durante esta sesión porque
  el controlador de aplicaciones falló antes de observar la ventana; escucha real con Narrador/TalkBack;
  Sharesheet/SAF completo en teléfono físico; cambio Wi-Fi↔datos durante una transferencia; clientes
  Obsidian reales; publicación/certificación Edge. La compilación XAML, las reglas del modelo y Android
  real en emulador sí están verificados.
- Firma actual: el APK lateral usa certificado Android Debug con esquema v2 y el Setup Windows está
  `NotSigned`. Authenticode, firma Android de producción y el endurecimiento adicional permanecen
  aplazados por decisión expresa del usuario.

## Evidencia de ejecución 0.6.15 — 2026-10-06

- Fuente de producto `bb24ac44b205c1dc2d375af845da6a77ce3f6c7f`. Windows declara
  `FileVersion=0.6.15.0` y `ProductVersion=0.6.15+bb24ac44b205c1dc2d375af845da6a77ce3f6c7f`.
  El publish autocontenido pasó cierre limpio en 80 ms; el instalador terminó y el ejecutable instalado
  quedó abierto desde `%LOCALAPPDATA%\Programs\UniShare`.
- .NET SDK 10.0.401: formato limpio, compilación Release sin advertencias/errores y 100/100 pruebas.
  Las seis pruebas nuevas cubren traducción estática/dinámica, conservación de español y contenido no
  reconocido. Android completó 69/69 pruebas debug y 69/69 release, lint debug/release, R8 y ensamblado.
- `aapt2` confirmó `versionCode=28`, `versionName=0.6.15`, minSdk 26 y targetSdk 36; `apksigner`
  confirmó esquema v2 y un firmante Android Debug. Los tres JavaScript MV3 pasan `node --check`; JSON,
  manifiesto y `_locales` son válidos, y el ZIP contiene ambos idiomas e `i18n.js`.
- **NO VERIFICADO:** recorrido visual completo español↔inglés en Windows y teléfono físico; el controlador
  de aplicaciones no devolvió ventanas nativas. Tampoco cambian las limitaciones de firma: Setup sin
  Authenticode y APK lateral con certificado de desarrollo.
- Artefactos coordinados finales:
  - Windows Setup, 59 717 707 bytes, SHA-256 `FF773F760387CE79CA63078F80FC978C9D32342AAB4D8AD9749ED8833279B577`.
  - Windows ZIP, 84 359 285 bytes, SHA-256 `26012DDA858501B2900D995D5168DF3EF7E64309E9D33A49301822AB2732727D`.
  - Android APK, 1 602 627 bytes, SHA-256 `FDAD918DA71C0A66967DC1B6E4EAE5170EA6903132A3D5ACCAD378378260A8B5`.
  - Android ZIP, 1 392 266 bytes, SHA-256 `7E36EDAA41F3E155C1690EB2A7CE8C5FBF0EA539B8E6EE9E14C758AC977080C7`.
  - Chromium/Edge Store, 23 772 bytes, SHA-256 `DC103DA93E797C5EB23894143D478AF753A34D698FE4EEBF6DDBD6C47CC9318C`.
  - Expediente Edge, 66 317 bytes, SHA-256 `0FC24E3D257038A0D65C447BE79B7164AEF6F4848457646E99A9EBD083886D35`.
  - Fuente pública, 1 039 172 bytes, SHA-256 `EFAB7D341AB383596427C6BE44B815F50A52EF27E076379B05D0CE706C0406F7`.

## Evidencia de ejecución 0.6.16 — 2026-10-06

- Fuente de producto `d755a5c6f83148e000b7549d80cf01b2cd129ccc`. Windows declara
  `FileVersion=0.6.16.0` y `ProductVersion=0.6.16+d755a5c6f83148e000b7549d80cf01b2cd129ccc`.
- .NET SDK 10.0.401: formato limpio, Release con 0 advertencias/0 errores y 105/105 pruebas. El EXE
  autocontenido cerró limpiamente en 128 ms. NuGet no informó vulnerabilidades ni paquetes obsoletos.
- Android: 69/69 pruebas debug y 69/69 release, lint debug/release con 0 hallazgos, R8 y ensamblado.
  `aapt2` confirmó `versionCode=29`, `versionName=0.6.16`, minSdk 26 y targetSdk 36; `apksigner`
  confirmó firma v2 con el certificado de desarrollo compatible con las instalaciones previas.
- El validador Chromium comprobó 4 JavaScript, 3 JSON, manifiesto MV3, recursos y paridad de 2 idiomas.
  El benchmark FTS5 de 100 000 elementos obtuvo p95 82,0413 ms frente al límite de 300 ms.
- Todos los ZIP se leyeron completamente y carecen de rutas absolutas, ascendentes o duplicadas:
  Windows 418 entradas/193 535 319 bytes; Android 2/1 610 242; Chromium y Edge Store 14/35 882;
  expediente Edge 5/69 634. La fuente pública se vuelve a inspeccionar después del último commit;
  Chromium y Edge Store son idénticos.
- Se exportó e inspeccionó un respaldo previo (4 items, 2 assets, 2 colecciones y 2 blobs). Después de
  instalar 0.6.16, una nueva exportación confirmó exactamente los mismos recuentos. La aplicación quedó
  ejecutándose desde `%LOCALAPPDATA%\Programs\UniShare`.
- La primera actualización sobre 0.6.15 confirmó el bloqueo histórico del agente. Tras corregir
  `OnSessionEnding`, una reinstalación real con 0.6.16 activa permitió a Restart Manager cerrar el
  proceso, terminó con código 0 en 9,24 s y el agente volvió a iniciar. K-042 queda cerrado.
- GitHub Actions detectó el timeout STA K-043 en su primera ejecución. El commit público
  `1fde118fe76808cd9a5507975a76bc246e80a9d3` pasó después la matriz completa: Windows con 105/105
  pruebas, publish, smoke y extensión; Android con pruebas, lint, R8, APK y subida de artefacto.
- La limpieza posterior envió a la Papelera los artefactos 0.6.14–0.6.15 y eliminó cachés, `bin`, `obj`
  y resultados regenerables; se conservaron los adjuntos de evidencia y los paquetes finales 0.6.16.
- Artefactos coordinados finales:
  - Windows Setup, 59 720 613 bytes, SHA-256 `659EF70641B1CDAEE15502748C25AB1BD2FD768CD090C2CCA017812BC5C3CF05`.
  - Windows ZIP, 84 359 436 bytes, SHA-256 `8E2B58892A202532E98741AC7D4EF09E7B6ED7B05D9EDE74865A5DA08AF7C5F4`.
  - Android APK, 1 602 627 bytes, SHA-256 `1675D46696C071179AAC27B23FB2A27F8D53BAA5715C84AA7F5344D87B894F29`.
  - Android ZIP, 1 398 683 bytes, SHA-256 `C62449D7C38989E82ED7F5094D6D857C1D715B3FA97F82AF40B4FD6F560AF342`.
  - Chromium/Edge Store, 23 771 bytes, SHA-256 `7FAB0C1D1D704D7455D60E041CB417C030421D33E9789DB61419F82E0C5700B1`.
  - Expediente Edge, 66 317 bytes, SHA-256 `2DEEE93BC560F31DAB8979FF19352610C0D445638E95CCB582EE32E3F8B7AEF4`.
  - Fuente pública: tamaño y SHA-256 finales en `SHA256SUMS-0.6.16.txt`, calculado después de cerrar
    esta evidencia para evitar una referencia circular.
- **NO VERIFICADO:** APK 0.6.16 en teléfono físico; Sharesheet/SAF y TalkBack físicos completos;
  cambio Wi-Fi↔datos durante una transferencia; Narrador; clientes Obsidian reales; publicación Edge.
  Windows continúa sin Authenticode y el APK usa la clave lateral de desarrollo por falta de
  certificados del propietario.

## Evidencia de preparación pública — 2026-10-06

- El árbol actual se recorrió como texto y binario buscando rutas de perfil, alias local, nombre e IP de
  Tailscale, claves privadas y patrones de tokens GitHub/AWS. El resultado después de la sanitización fue
  cero coincidencias. Los ZIP binarios tampoco contenían PDB, configuraciones locales ni certificados.
- El historial local conserva autores y revisiones anteriores por diseño. No se considera publicable: la
  guía obliga a usar `git archive` y un `git init` nuevo, evitando exponer `.git` y datos ya eliminados.
- La documentación pública incluye MIT, privacidad, seguridad, contribución, changelog y avisos de
  terceros. Los cuatro YAML de GitHub y el script PowerShell de publicación pasan análisis sintáctico;
  53 Markdown tienen cero enlaces locales rotos.
- Validación funcional posterior: SDK .NET 10.0.401, restore bloqueado, formato limpio, Release con 0
  advertencias/0 errores y 87/87 pruebas. Android ejecutó 61/61 pruebas debug y 61/61 release, sin fallos
  ni omisiones, `lintRelease` y `assembleRelease` correctos.
