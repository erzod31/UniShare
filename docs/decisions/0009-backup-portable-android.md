# ADR 0009: exportación Android mediante el contrato portable v1

Fecha: 2026-09-24  
Estado: aceptada para H7 parcial

## Contexto

Android guarda datos privados que se perderían al desinstalar. Windows ya define un ZIP v1 abierto,
versionado y verificable; crear otro formato aumentaría el riesgo de divergencia y bloquearía la
portabilidad entre plataformas.

## Decisión

Android exporta exactamente el contrato v1: `manifest.json`, seis entradas NDJSON, blobs canónicos y
`checksums.sha256`. Los campos todavía no soportados en Android se representan sin inventar datos:
metadatos nulos, favorito falso y colecciones/etiquetas vacías. Los timestamps se normalizan al formato
UTC de siete decimales que acepta el importador .NET.

La exportación se genera en cache privado dentro de una instantánea SQLite. Cada entrada se hashea
mientras se escribe y cada blob debe coincidir con su nombre SHA-256. Tras cerrar y sincronizar el ZIP,
se copia al URI creado por el selector SAF. No se piden permisos globales de almacenamiento.

## Consecuencias

Un backup creado en Android puede inspeccionarse y restaurarse directamente con UniShare Windows; la
prueba real Android→Windows confirma 4 items, 3 assets y un blob deduplicado. La exportación protege
contra pérdida si el usuario conserva el ZIP fuera del sandbox de la app.

Android aún no restaura el formato y el proveedor SAF puede fallar después de crear el documento,
dejando un archivo parcial visible; el original local nunca se modifica. La restauración deberá validar
todo el ZIP y preparar una base/CAS nuevos antes de cualquier reemplazo.
