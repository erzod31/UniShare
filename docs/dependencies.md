# Dependencias y licencias

| Dependencia | Necesidad | Versión | Licencia | Estado |
|---|---|---|---|---|
| .NET SDK | Build y runtime del núcleo/Windows | 10.0.401 fijada en `global.json` | MIT | Instalada y verificada |
| WPF | Presentación Windows inicial | Incluida en .NET 10 | MIT | Instalada |
| Microsoft.Data.Sqlite | Acceso SQLite explícito | 10.0.12 | MIT | Restaurada y bloqueada |
| QRCoder | QR de emparejamiento generado localmente en Windows | 1.8.0 | MIT | Restaurada y bloqueada; sin dependencias transitivas |
| xUnit v3 + runner | Pruebas automatizadas sobre Microsoft.Testing.Platform | 4.0.1 / 4.0.0 | Apache-2.0 | Restaurada, bloqueada y 87 pruebas ejecutadas |
| Microsoft.Testing.Extensions.CodeCoverage | Cobertura nativa en Microsoft.Testing.Platform | 18.11.2 | MIT | Restaurada, bloqueada e integrada en CI |
| Android SDK API 36 / Build Tools | Compilación Android | 36 / 36.0.0 | Licencias Android SDK aceptadas en el host; no se redistribuye | Instalado |
| Gradle / Android Gradle Plugin | Build Android | 8.14.5 / 8.13.2 | Gradle Apache-2.0; AGP sujeto a licencias Android SDK | Wrapper y distribución verificados por SHA-256; build ejecutado |
| Kotlin / Compose Compiler | Código y UI Android | 2.3.21 | Apache-2.0 | Build ejecutado |
| Compose BOM / Activity Compose / AndroidX Core | UI Android y `FileProvider` | 2026.06.01 / 1.13.0 / 1.18.0 | Apache-2.0 | Últimas versiones estables compatibles con API 36/AGP 8.13; build, tests y lint ejecutados |
| AndroidX WorkManager | Reintento durable y sincronización periódica Android | 2.12.0 | Apache-2.0 | Build, tests y lint ejecutados; versión estable 2026-09-23 |
| JUnit 4 | Tests JVM del módulo Android | 4.13.2 | EPL-1.0, sólo desarrollo | 61 pruebas debug y 61 release ejecutadas |
| SQLiteOpenHelper | Persistencia Android sin ORM adicional | Incluido en Android SDK | Apache-2.0 | Esquema v5 y migraciones probadas; decisión aceptada en lugar de añadir Room sin beneficio visible |
| Tailscale | Transporte WireGuard y HTTPS privado; no se redistribuye | 1.102.3 detectada | BSD-3-Clause para el cliente open source; servicio sujeto a sus términos | Opcional pero recomendado; usuario gestiona su tailnet |

Toda dependencia nueva debe añadir aquí versión, licencia, origen, mantenimiento y razón. SingleFile no se empaquetará sin decisión separada por su licencia AGPL-3.0.

Las versiones NuGet se administran una sola vez en `Directory.Packages.props`; los proyectos conservan
`packages.lock.json` para que una restauración bloqueada no cambie el grafo de forma silenciosa.

Las bibliotecas Android se obtienen de Google Maven/Maven Central. Las licencias de dependencias
transitivas del APK deben inventariarse antes de una distribución pública; el APK lateral está optimizado,
pero conserva deliberadamente la firma de depuración y no es un paquete de tienda.
