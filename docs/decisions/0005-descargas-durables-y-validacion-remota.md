# ADR 0005: descargas durables y validación remota

Fecha: 2026-09-24  
Estado: aceptada

## Contexto

UniShare debe conservar descargas grandes ante interrupciones sin tratar un archivo incompleto como
disponible. Las URLs y redirecciones son entradas hostiles, y la aplicación no debe convertirse en
un cliente SSRF hacia recursos locales.

## Decisión

- Persistir cada trabajo en SQLite (`download_jobs`, esquema v3) con estado, progreso y validadores HTTP.
- Escribir por streaming a un parcial estable del perfil y reanudar sólo con `Range` más `If-Range`.
- Si cambia ETag/Last-Modified o el rango no coincide, descartar el parcial y exigir un reintento limpio.
- Verificar longitud, límite y SHA-256 antes de promover al CAS.
- Confirmar `Asset`, conversión a `HYBRID`, finalización y outbox en una transacción.
- Gestionar redirecciones manualmente y validar cada URI contra rangos no públicos.
- Desactivar proxy en este cliente y conectar el socket directamente a una de las IP validadas mediante
  `SocketsHttpHandler.ConnectCallback`; el URI original se conserva para HTTP Host y TLS SNI.

## Consecuencias

La cola sobrevive a reinicios y nunca expone el parcial como `Asset`. Cancelar es terminal y elimina el
temporal; pausar o fallar permite reintentar. El backup v1 no incluye trabajos de transferencia porque
son estado operativo, no contenido ya conservado.

La prueba de transporte usa un hostname reservado `.invalid` y un servidor efímero; la descarga sólo
puede completarse mediante la IP devuelta por la política, lo que demuestra que el socket no vuelve a
resolver el hostname. Captura y sanitización HTML siguen fuera de esta decisión.
