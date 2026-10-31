# Avisos de terceros

UniShare se distribuye bajo MIT, pero utiliza herramientas y bibliotecas de terceros con sus propias
licencias. Este resumen no reemplaza los textos incluidos por cada proveedor en sus paquetes oficiales.

Los paquetes Windows incluyen `licenses/` con los textos originales disponibles en NuGet, avisos de los
frameworks autocontenidos e inventario de versiones y atribuciones. Android incorpora en sus assets los
textos comunes y el inventario de dependencias runtime; las bibliotecas de prueba quedan excluidas.
Los textos Apache-2.0, MIT de QRCoder y BSD de Skia también están en el directorio fuente `licenses/`.
No se han modificado las bibliotecas de terceros.

| Componente | Uso | Licencia principal |
|---|---|---|
| .NET, WPF y bibliotecas Microsoft `System.*` | Runtime y aplicación Windows | MIT |
| Microsoft.Data.Sqlite y Microsoft Testing Platform | SQLite y pruebas .NET | MIT |
| SQLite / `e_sqlite3` | Motor de base de datos | Dominio público |
| SQLitePCLRaw | Enlace nativo SQLite para .NET | Apache-2.0 |
| QRCoder | QR de emparejamiento | MIT |
| Microsoft.Web.WebView2 | Host local del motor de traducción Windows | BSD-3-Clause; runtime del sistema separado |
| Bergamot/Marian y modelos Mozilla | Traducción local compartida Windows/Android | MPL-2.0 para Bergamot/modelos; componentes MIT, Apache-2.0 y zlib en `licenses/Bergamot-third-party.txt` |
| franc-min, trigram-utils, n-gram y collapse-white-space | Detección de idioma local | MIT; textos en `licenses/franc-MIT.txt` |
| fzstd | Descompresión local de modelos verificados | MIT |
| esbuild | Sólo regeneración de assets en desarrollo; no entra en los clientes | MIT |
| xUnit.net | Pruebas .NET | Apache-2.0 |
| Kotlin y Compose Compiler | Código y compilación Android | Apache-2.0 |
| AndroidX, Compose, Activity, Core y WorkManager | UI y servicios Android | Apache-2.0 |
| AndroidX Test, JUnit extension y UIAutomator | Sólo pruebas instrumentadas; no se incluyen en el APK release | Apache-2.0 |
| Gradle | Sistema de compilación Android | Apache-2.0 |
| JUnit 4 | Pruebas JVM | EPL-1.0 |
| JSON-java | Sólo pruebas JVM de extracción estructurada; no se incluye en el APK | Public Domain |
| Tailscale | Transporte privado opcional, no redistribuido | BSD-3-Clause; servicio sujeto a sus términos |
| Inno Setup | Creación del instalador, no incluido como código fuente del proyecto | Licencia de Inno Setup |

Las versiones exactas se encuentran en `Directory.Packages.props`, los `packages.lock.json`,
`platforms/android/build.gradle.kts`, `platforms/android/app/build.gradle.kts` y el wrapper Gradle.

Fuentes de licencias:

- .NET y componentes Microsoft: <https://github.com/dotnet/runtime/blob/main/LICENSE.TXT>
- SQLite: <https://sqlite.org/copyright.html>
- SQLitePCLRaw: <https://github.com/ericsink/SQLitePCL.raw/blob/master/LICENSE>
- QRCoder: <https://github.com/codebude/QRCoder/blob/master/LICENSE.txt>
- xUnit.net: <https://github.com/xunit/xunit/blob/main/LICENSE>
- AndroidX: <https://source.android.com/docs/setup/about/licenses>
- Kotlin: <https://github.com/JetBrains/kotlin/blob/master/license/LICENSE.txt>
- Gradle: <https://github.com/gradle/gradle/blob/master/LICENSE>
- JUnit 4: <https://github.com/junit-team/junit4/blob/main/LICENSE-junit.txt>
- JSON-java: <https://github.com/stleary/JSON-java/blob/master/LICENSE>
- Tailscale: <https://github.com/tailscale/tailscale/blob/main/LICENSE>
- Inno Setup: <https://jrsoftware.org/files/is/license.txt>
