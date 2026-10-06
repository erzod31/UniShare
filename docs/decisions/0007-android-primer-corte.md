# ADR 0007: primer corte Android con API 36

Fecha: 2026-09-24  
Estado: aceptada para H7 parcial

## Contexto

El host dispone de JDK 21 y SDK API 36; al inicio del hito no tenía emulador ni teléfono conectado. Compose 1.12
requiere `compileSdk 37`, que no está instalado. El producto necesita una primera entrada Android
local y un receptor Sharesheet antes de compartir formatos de datos con Windows.

## Decisión

Se fija Gradle 8.13, AGP 8.13.2, Kotlin/Compose Compiler 2.3.21, Compose BOM 2025.09.00 y
`compileSdk 36`. El wrapper verifica SHA-256. La primera vertical Android implementa sólo enlaces
con SQLite privado, UUID y `ACTION_SEND` de texto. `SQLiteOpenHelper` minimiza dependencias de este
corte; Room, esquema exportado y migraciones se incorporarán antes de completar H7.

Se deshabilitan Auto Backup y transferencias de datos del sistema hasta implementar una exportación
portable explícita. El APK se firma únicamente con clave de depuración.

## Consecuencias

El APK compila y lint/test pasan. Se instaló un emulador AOSP API 36 y se verificó Sharesheet,
persistencia tras cierre y reinicio, y apertura sin Wi-Fi/datos. Falta prueba en teléfono físico.
El formato SQLite Android todavía no es interoperable con Windows y desinstalar la app puede borrar
los datos locales. Estas limitaciones permanecen visibles en la matriz y el manual Android.
