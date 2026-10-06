# Modelo de amenazas inicial

## Activos

- Metadatos personales y notas.
- Archivos importados y capturas web.
- Copias de seguridad.
- Identidad y claves de dispositivo futuras.

## Límites de confianza

1. UI -> casos de uso: el usuario puede pegar datos malformados accidentalmente.
2. Sistema operativo -> captura: otras aplicaciones pueden compartir MIME, tamaños o nombres falsos.
3. Filesystem externo -> CAS: rutas, reparse points y contenido son no confiables.
4. Web/extensión -> capturador: páginas y mensajes son hostiles.
5. LAN -> sync: cualquier equipo no emparejado es hostil; un peer revocado deja de ser confiable.
6. Backup/Obsidian -> importador: archivos pueden estar truncados, manipulados o ser de versión futura.

## Controles ya implementados

- URL limitada a `http`/`https`; original conservada.
- Título y combinaciones de tipo validados en dominio y SQLite.
- Importación en streaming; rechazo de reparse points; nombre reducido a basename.
- Ruta CAS relativa validada dentro del perfil.
- SHA-256 calculado antes de promoción; staging limpiado incluso al fallar.
- SQL parametrizado, claves foráneas y restricciones de esquema.
- Descargas con tamaño máximo, streaming, comprobación de longitud y promoción CAS sólo después de verificar.
- Firmas MIME conocidas prevalecen sobre extensión y cabecera declarada.
- Reanudación condicionada por ETag/Last-Modified; un recurso cambiado no se concatena al parcial anterior.
- Redirecciones manuales limitadas y validación DNS de cada destino; se bloquean loopback, redes privadas,
  link-local, multicast y endpoints de metadata conocidos. El socket conecta a la IP validada mediante
  `ConnectCallback`, evitando una segunda resolución vulnerable a DNS rebinding.
- HTML reescrito como documento propio con texto codificado, contenido activo eliminado, CSP sin red/scripts
  y límite de entrada de 16 MiB.
- Extensiones `.html`, `.htm`, `.xhtml` y `.svg` fuerzan esta conversión incluso ante MIME engañoso.
- Sin telemetría ni credenciales de servicios externos dentro de UniShare.
- El servidor de sincronización escucha sólo en `127.0.0.1`; Tailscale Serve termina HTTPS dentro de la
  tailnet. La API exige además bearer aleatorio de 256 bits y lo compara en tiempo constante.
- Peticiones limitadas a 4 GiB, staging privado, validación integral del ZIP y serialización de una sola
  reconciliación por vez. La clave no se registra ni se incluye en el repositorio.

## Pendientes antes de beta

- Cuotas, disco disponible y límites por archivo.
- Endurecer previews de formatos activos distintos de la instantánea HTML (SVG, PDF y documentos externos).
- Backup cifrado opcional y recuperación de clave.
- Rotación/revocación de la clave desde UI y almacenamiento Android respaldado por Keystore.
- Validación de mensajes Native Messaging y bridge Obsidian.
- Redacción estructurada de logs.
