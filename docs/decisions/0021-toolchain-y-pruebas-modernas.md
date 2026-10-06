# ADR-0021: toolchain estable, paquetes centralizados y Microsoft.Testing.Platform

- Estado: aceptado
- Fecha: 2026-10-05

## Contexto

La auditoría encontró versiones NuGet repetidas entre proyectos, xUnit 2 marcado como legado, acciones
de CI antiguas y bibliotecas Android retrasadas. Actualizar Compose a 1.12 exige API 37 y AGP 9.1,
pero esa plataforma todavía no está disponible en el SDK estable instalado. El producto no necesita
optar por los cambios de comportamiento de `targetSdk 37` para esta entrega.

## Decisión

- Fijar .NET SDK 10.0.401 y administrar NuGet desde `Directory.Packages.props`, manteniendo los lockfiles.
- Migrar las 76 pruebas .NET a xUnit v3 4.0.1 sobre Microsoft.Testing.Platform y usar la extensión de
  cobertura de Microsoft 18.11.2.
- Actualizar SQLite a 10.0.12 y las acciones oficiales de GitHub a sus revisiones estables actuales.
- Mantener AGP 8.13.2, Gradle 8.13, Kotlin 2.3.21 y `compileSdk 36`; usar las últimas bibliotecas que
  declaran compatibilidad con esa base: Compose BOM 2026.06.01, Activity 1.13.0 y Core 1.18.0.
- No adoptar versiones alpha, ni AGP 9 con el modo Kotlin heredado, ni aceptar nuevos términos de una
  acción de caché. La caché Gradle de CI se gestiona mediante `setup-java`.

## Consecuencias

El grafo NuGet tiene una sola fuente de versiones y no contiene paquetes vulnerables, obsoletos o con
actualizaciones pendientes según NuGet al 2026-10-05. CI produce cobertura y usa ejecuciones cancelables.
Android recibe correcciones estables sin requerir una plataforma inexistente ni una migración mayor
incompleta. Permanece un aviso no bloqueante del lector XML del SDK hasta que API 37 y AGP 9 puedan
migrarse conjuntamente.
