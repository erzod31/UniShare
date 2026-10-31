# Dependencias y licencias

0.6.24 no añade dependencias de runtime al actualizador. Usa HttpClient/JSON/SHA-256 y
WinVerifyTrust/PKCS de .NET/Windows, y HttpURLConnection/PackageManager/FileProvider del sistema
Android y AndroidX ya presentes. El servicio nativo de Chromium verifica paquetes de tienda.
Las claves privadas y las herramientas de desarrollo no se distribuyen con el cliente.

| Dependencia | Necesidad | Versión | Licencia | Estado |
|---|---|---|---|---|
| .NET SDK | Build y runtime del núcleo/Windows | 10.0.402 fijada en `global.json` | MIT | Instalada y verificada |
| WPF | Presentación Windows inicial | Incluida en .NET 10 | MIT | Instalada |
| Microsoft.Data.Sqlite | Acceso SQLite explícito | 10.0.13 | MIT | Restaurada y bloqueada; sin avisos de vulnerabilidad |
| QRCoder | QR de emparejamiento generado localmente en Windows | 1.8.1 | MIT | Restaurada y bloqueada; sin dependencias transitivas |
| Microsoft.Web.WebView2 | Ejecutar el núcleo local WASM en Windows sin añadir otro motor de inferencia | 1.0.4314.35 estable, publicada 2026-10-23 | BSD-3-Clause, original en `licenses/WebView2-BSD.txt` | SDK mantenido por Microsoft y bloqueado en NuGet; necesita el runtime WebView2 instalado, separado del SDK |
| Bergamot/Marian Mozilla | Traducción en el dispositivo en ambas plataformas | Registro WASM 4.0/v0.6.0, código `1de4a085d3a7afb625c51a60aabb5ad298e4059f`; modelos estables 3.0 | MPL-2.0 y avisos MIT/Apache/zlib de componentes | Mismo núcleo que distribuye Firefox; glue y WASM coincidentes, hashes y URLs fijados en `shared/translation/vendor/provenance.json`; modelos bajo demanda, no incluidos en instaladores |
| franc-min / trigram-utils / n-gram / collapse-white-space | Identificar idioma sin enviar texto a red | 6.2.0 y transitivas fijadas en `tools/translation/package-lock.json` | MIT | Biblioteca estable sin release reciente (2024), dependencia acotada a un bundle local con tests de detección; no se ejecuta npm en el dispositivo |
| fzstd | Leer los modelos oficiales comprimidos | 0.1.1 | MIT | Biblioteca estable (2024), JS puro pequeño y fijado; descompresión con tamaño y hash verificados |
| esbuild | Regenerar el bundle de detección durante desarrollo | 0.28.2 | MIT, sólo desarrollo | No se incluye en instaladores; npm no se añade como requisito del usuario |
| xUnit v3 + runner | Pruebas automatizadas sobre Microsoft.Testing.Platform | 4.0.2 / 4.0.1 | Apache-2.0 | Restaurada, bloqueada y 109 pruebas ejecutadas |
| Microsoft.Testing.Extensions.CodeCoverage | Cobertura nativa en Microsoft.Testing.Platform | 18.12.0 | MIT | Restaurada, bloqueada e integrada en CI |
| Android SDK API 37 / Build Tools | Compilación Android | 37 / 36.0.0 | Licencias Android SDK aceptadas en el host; no se redistribuye | Instalado |
| Gradle / Android Gradle Plugin | Build Android | 9.8.1 / 9.4.1 | Gradle Apache-2.0; AGP sujeto a licencias Android SDK | Wrapper y distribución verificados por SHA-256; AGP 9.4.1 soporta API 37 y Gradle 9.8.1 incorpora correcciones de seguridad posteriores al mínimo 9.6.0 |
| Kotlin / Compose Compiler | Código y UI Android | 2.2.10, integrado por AGP 9 | Apache-2.0 | Build ejecutado |
| Compose BOM / Activity Compose / AndroidX Core | UI Android y `FileProvider` | 2026.09.00 / 1.13.0 / 1.19.1 | Apache-2.0 | Versiones estables; build, tests y lint ejecutados con API 37 |
| AndroidX WorkManager | Reintento durable y sincronización periódica Android | 2.12.0 | Apache-2.0 | Build, tests y lint ejecutados; versión estable 2026-09-23 |
| JUnit 4 | Tests JVM del módulo Android | 4.13.2 | EPL-1.0, sólo desarrollo | Pruebas JVM ejecutadas mediante la tarea agregada `test` |
| JSON-java | Ejecutar los contratos de `org.json` en pruebas JVM; Android usa su implementación incorporada | 20260814, sólo `testImplementation` | Public Domain | Proyecto mantenido; versión publicada en Maven Central y licencia original comprobadas. No entra en APK debug/release ni añade dependencias runtime. |
| AndroidX Test runner / JUnit extension / UIAutomator | Pruebas instrumentadas de SQLite, HTTP y selectores Android; no se incluyen en el APK release | 1.7.0 / 1.3.0 / 2.4.0 | Apache-2.0, sólo desarrollo | Versiones estables oficiales; UIAutomator evita APIs privadas de inyección de Espresso incompatibles con API 37 |
| SQLiteOpenHelper | Persistencia Android sin ORM adicional | Incluido en Android SDK | Apache-2.0 | Esquema v5 y migraciones probadas; decisión aceptada en lugar de añadir Room sin beneficio visible |
| Tailscale | Transporte WireGuard y HTTPS privado; no se redistribuye | 1.102.3 detectada | BSD-3-Clause para el cliente open source; servicio sujeto a sus términos | Opcional pero recomendado; usuario gestiona su tailnet |

Toda dependencia nueva debe añadir aquí versión, licencia, origen, mantenimiento y razón. SingleFile no se empaquetará sin decisión separada por su licencia AGPL-3.0.

Las versiones NuGet se administran una sola vez en `Directory.Packages.props`; los proyectos conservan
`packages.lock.json` para que una restauración bloqueada no cambie el grafo de forma silenciosa.

Las bibliotecas Android se obtienen de Google Maven/Maven Central. Las licencias de dependencias
transitivas del APK deben inventariarse antes de una distribución pública; el APK lateral está optimizado,
pero conserva deliberadamente la firma de depuración y no es un paquete de tienda.

Desde 0.6.18 el build Android genera el inventario de runtime en `assets/licenses/inventory-android.txt`,
incluye textos Apache-2.0/MIT/BSD y exige revisar cualquier proveedor fuera de los grupos inventariados.
Windows copia los textos originales y las atribuciones de cada NuGet/framework en `licenses/`, con un
inventario JSON. La firma de producción se configura según `docs/SIGNING.md` y no usa claves versionadas.
