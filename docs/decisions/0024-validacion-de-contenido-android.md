# 0024 - Validación acotada de archivos Android

Fecha: 2026-10-06

Estado: aceptada

## Contexto

SAF entrega un nombre, un flujo y un MIME declarado por otro proveedor. Conservar ese MIME sin mirar el
contenido podía etiquetar mal un archivo, abrir HTML/SVG activo como un asset ordinario o promover al CAS
un contenido contradictorio. A la vez, leer el archivo completo una segunda vez o incorporar una biblioteca
de detección pesada perjudicaría el flujo local de archivos grandes.

## Decisión

- Mientras copia y calcula SHA-256, Android conserva como máximo los primeros 512 bytes.
- Antes de promover el parcial reconoce firmas de PDF, PNG, JPEG, GIF, WebP, ZIP, MP3 y contenedores
  `ftyp` (MP4/M4A/QuickTime/3GP/HEIF/AVIF).
- Una firma conocida contradicha por otro tipo firmado se rechaza con un error accionable. HTML, XHTML y
  SVG se rechazan por firma, MIME o extensión; las páginas offline usan el capturador que las vuelve inertes.
- El tipo detectado prevalece sobre `application/octet-stream`. Los contenedores ZIP de Office,
  OpenDocument, EPUB, APK y JAR conservan su tipo útil declarado o inferido por extensión.
- La decisión ocurre antes de mover el parcial al CAS o abrir la transacción del item. El `finally` elimina
  el staging y el marcador visual de sincronización sólo se incrementa después de una mutación confirmada.
- Los mensajes de operación y de sincronización se representan por separado para que un fallo de red no
  oculte por qué se rechazó un archivo.

## Alternativas descartadas

- Confiar sólo en `ContentResolver.getType`: el proveedor es una frontera no confiable y puede devolver un
  tipo genérico o incorrecto.
- Inferir sólo por extensión: un nombre se puede cambiar y no demuestra el contenido.
- Apache Tika u otra dependencia amplia: aumenta mucho el APK y la superficie de mantenimiento para un
  conjunto pequeño de formatos necesarios.
- Aceptar HTML/SVG directo y abrirlo externamente: evita la sanitización ya exigida a las capturas offline.

## Consecuencias

La inspección usa memoria constante y no añade una segunda pasada sobre archivos de hasta 1 GiB. Los tipos
no reconocidos siguen siendo importables con un MIME declarado acotado o `application/octet-stream`.
Añadir formatos futuros requiere un fixture de firma y una regla explícita; no se intenta identificar cada
formato existente ni sustituir el endurecimiento de seguridad pospuesto.
