# Privacidad de UniShare

UniShare está diseñado como una biblioteca personal local-first. No requiere crear una cuenta de
UniShare y no incluye telemetría, publicidad ni un servicio central del proyecto.

## Datos almacenados

Windows conserva por defecto la biblioteca en `%LOCALAPPDATA%\UniShare`. Android usa el almacenamiento
privado de la aplicación. Los elementos, notas, metadatos, blobs, miniaturas, historial de cambios y claves
de emparejamiento permanecen en esos perfiles hasta que el usuario los borra o exporta.

La desinstalación de Windows conserva deliberadamente la biblioteca. En Android, borrar los datos de la
aplicación o desinstalarla puede eliminar la copia local; crea un respaldo antes.

## Acceso a la red

- Al guardar o actualizar un enlace, UniShare contacta con el sitio indicado para obtener el título,
  contenido y recursos solicitados. El sitio puede registrar la dirección IP y datos normales de una
  conexión web.
- La copia offline sólo se crea cuando el usuario la solicita.
- La sincronización se realiza directamente entre los dispositivos emparejados. Tailscale puede aportar
  la red privada, sujeto a sus propias condiciones y política de privacidad.
- La extensión Chromium se comunica con UniShare mediante `127.0.0.1`; no envía la biblioteca al proyecto,
  a Microsoft ni a Google.
- Obsidian recibe únicamente los datos que el usuario exporta a la bóveda elegida.

## Control del usuario

La biblioteca puede cambiarse de ubicación, exportarse a un formato portable y restaurarse en otro perfil.
Los elementos pueden editarse, mover a la papelera o restaurarse. UniShare no puede borrar copias que el
usuario haya exportado, sincronizado o entregado a otras aplicaciones.

## Diagnósticos e incidencias

Antes de compartir un informe elimina URLs privadas, notas, rutas locales, nombres de dispositivo,
direcciones IP, nombres `.ts.net`, claves y códigos QR. Usa datos sintéticos para reproducir errores.
