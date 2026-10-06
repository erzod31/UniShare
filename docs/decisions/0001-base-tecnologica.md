# ADR-0001: base tecnológica y primer corte ejecutable

- Estado: aceptado, reversible
- Fecha: 2026-09-24

## Contexto

El workspace no contiene aplicación. El equipo tiene .NET SDK 10.0.400 y JDK 21, pero carece de Android SDK, Gradle, plantilla WinUI y workloads móviles. La especificación prefiere Kotlin/Compose en Android y C#/WinUI 3 en Windows.

## Decisión

1. Crear un núcleo .NET 10 sin dependencia de UI para dominio, casos de uso e infraestructura Windows.
2. Usar SQLite mediante `Microsoft.Data.Sqlite` y SQL explícito.
3. Entregar el primer corte Windows en WPF sobre .NET 10 porque compila con el SDK instalado y permite validar datos/UX inmediatamente.
4. Mantener integración Windows detrás de adaptadores y añadir Windows App SDK/MSIX para Share Target. La documentación oficial admite Share Target en aplicaciones WPF empaquetadas.
5. Implementar Android posteriormente como Kotlin/Compose/Room, compartiendo contratos y fixtures, no binarios ni una base de datos.

## Alternativas consideradas

- WinUI 3 desde el primer commit: mayor fidelidad al PDF, pero exige toolchain/paquetes no instalados antes de validar el núcleo.
- Kotlin Multiplatform/Compose Desktop: reduce duplicación, pero se aleja de la integración nativa Windows y no elimina la necesidad de Android SDK.
- Web/PWA/Electron: arranque rápido, pero degrada filesystem, Share Target, empaquetado y experiencia offline nativa.
- Núcleo Rust compartido: potente para interoperabilidad, pero introduce FFI y empaquetado antes de demostrar valor.

## Consecuencias y migración

- WPF es una desviación visible y temporal respecto a WinUI, no una omisión de Windows nativo.
- Dominio y casos de uso no referencian WPF; migrar presentación a WinUI no cambia datos ni contratos.
- Antes de H8 se reevaluará WPF + Windows App SDK frente a una presentación WinUI completa con evidencia de build y Share Target.
- Android queda `NO VERIFICADO` hasta instalar SDK/emulador o disponer de dispositivo.
