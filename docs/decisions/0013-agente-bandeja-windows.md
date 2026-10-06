# ADR 0013: agente de bandeja por usuario en Windows

Fecha: 2026-09-25. Estado: aceptado.

## Contexto

La sincronización directa necesita que el nodo Windows siga activo cuando se cierre la ventana. Un
servicio del sistema introduciría identidad de cuenta, permisos cruzados, instalación elevada e IPC antes
de que exista una necesidad de sincronizar sin sesión iniciada.

## Decisión

- UniShare mantiene un único proceso por sesión mediante un mutex local con nombre.
- Cerrar la ventana la oculta; no detiene SQLite, descargas ni el servidor de sincronización.
- Un icono de bandeja permite abrir la ventana, activar el inicio con Windows o salir completamente.
- Una segunda ejecución señala un evento local con nombre y muestra la ventana del proceso existente.
- El inicio automático es optativo y registra el ejecutable actual con `--background` bajo `HKCU`.
- No se instala un servicio del sistema ni se modifica el inicio automático sin acción del usuario.

## Consecuencias

La sincronización permanece disponible con la ventana cerrada y no existen dos procesos compitiendo por
la biblioteca o el puerto. El agente comienza después de iniciar sesión; un equipo apagado, suspendido o
sin sesión continúa siendo inaccesible y Android conserva sus reintentos. Si se mueve la carpeta portátil,
el usuario debe desactivar y volver a activar **Iniciar con Windows** desde la nueva ubicación.
