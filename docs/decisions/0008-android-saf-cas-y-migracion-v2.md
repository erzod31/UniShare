# ADR 0008: SAF, CAS y migración Android v2

Fecha: 2026-09-24  
Estado: aceptada para H7 parcial

## Contexto

El primer corte Android sólo aceptaba enlaces y tenía un esquema v1. UniShare debe importar archivos
sin permisos amplios, deduplicarlos y crear elementos híbridos sin perder instalaciones existentes.
El formato Android aún no es interoperable con el núcleo Windows y H7 sigue en desarrollo.

## Decisión

Se usa `ACTION_OPEN_DOCUMENT` para recibir un URI concedido por el selector del sistema y copiar su
contenido inmediatamente al almacenamiento privado. La copia se transmite a un archivo `.partial`,
se limita a 1 GiB, calcula SHA-256 mientras escribe y fuerza el descriptor antes de promover al layout
CAS `blobs/aa/bb/<sha256>`. Si el hash ya existe, se verifica y se reutiliza.

SQLite v2 amplía `items` a `LINK=1`, `FILE=2` y `HYBRID=3`, permite URL nula sólo para `FILE` y añade
`assets` con clave foránea. La migración crea una tabla nueva, copia las filas v1 y sólo después
reemplaza la anterior. Una URL válida presente al abrir SAF produce `HYBRID`; sin URL produce `FILE`.
La escritura de item y asset es transaccional, pero el CAS físico permanece deliberadamente fuera de
SQLite.

## Consecuencias

No se solicita permiso global de almacenamiento y los documentos siguen disponibles después de que
expire la concesión SAF. Dos assets iguales ocupan una sola copia física. La migración real v1→v2 y
la persistencia offline tras reinicio están verificadas en AOSP API 36.

Un fallo entre promoción CAS y commit puede dejar un blob huérfano; se trata igual que K-007 y no se
borra automáticamente hasta diseñar recolección conservadora. El MIME aún procede del proveedor y
requiere detección por firma (K-014). Room, backup interoperable y prueba física siguen pendientes.
