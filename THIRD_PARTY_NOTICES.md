# Avisos de terceros

UniShare se distribuye bajo MIT, pero utiliza herramientas y bibliotecas de terceros con sus propias
licencias. Este resumen no reemplaza los textos incluidos por cada proveedor en sus paquetes oficiales.

| Componente | Uso | Licencia principal |
|---|---|---|
| .NET, WPF y bibliotecas Microsoft `System.*` | Runtime y aplicación Windows | MIT |
| Microsoft.Data.Sqlite y Microsoft Testing Platform | SQLite y pruebas .NET | MIT |
| SQLite / `e_sqlite3` | Motor de base de datos | Dominio público |
| SQLitePCLRaw | Enlace nativo SQLite para .NET | Apache-2.0 |
| QRCoder | QR de emparejamiento | MIT |
| xUnit.net | Pruebas .NET | Apache-2.0 |
| Kotlin y Compose Compiler | Código y compilación Android | Apache-2.0 |
| AndroidX, Compose, Activity, Core y WorkManager | UI y servicios Android | Apache-2.0 |
| Gradle | Sistema de compilación Android | Apache-2.0 |
| JUnit 4 | Pruebas JVM | EPL-1.0 |
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
- Tailscale: <https://github.com/tailscale/tailscale/blob/main/LICENSE>
- Inno Setup: <https://jrsoftware.org/files/is/license.txt>
