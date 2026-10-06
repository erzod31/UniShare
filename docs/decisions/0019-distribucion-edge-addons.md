# ADR 0019: distribución de la extensión mediante Microsoft Edge Add-ons

Fecha: 2026-09-26

## Contexto

La carga descomprimida de una extensión necesita modo desarrollador y conservar una carpeta. El usuario
quiere el comportamiento normal de otras extensiones: instalación con un clic, almacenamiento gestionado
por Edge y actualizaciones automáticas.

## Decisión

- Publicar la extensión en Microsoft Edge Add-ons mediante Partner Center.
- Mantener el ZIP descomprimible sólo como canal de prueba anterior a la publicación.
- No distribuir un CRX privado: Microsoft limita ese canal a equipos administrados mediante políticas de
  empresa y no resuelve el caso de un usuario particular.
- Generar un ZIP de ejecución mínimo y otro expediente con logotipo, ficha y política de privacidad.
- Abrir automáticamente las opciones la primera vez que se instala para explicar la conexión local.

## Consecuencias

Después de la aprobación, Edge instalará y actualizará la extensión sin depender de una carpeta. La
publicación inicial necesita intervención del propietario: cuenta Microsoft/Partner Center, aceptación de
los términos, URL HTTPS pública para la política y envío a certificación. Esos actos no pueden realizarse
ni darse por aprobados desde el repositorio.

Documentación oficial consultada:

- https://learn.microsoft.com/en-us/microsoft-edge/extensions/publish/hosting-and-updating
- https://learn.microsoft.com/en-us/microsoft-edge/extensions/publish/create-dev-account
- https://learn.microsoft.com/es-es/microsoft-edge/extensions/publish/publish-extension
- https://learn.microsoft.com/en-us/deployedge/microsoft-edge-manage-extensions-webstore
