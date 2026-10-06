# Seguridad

## Versiones compatibles

Las correcciones se preparan sobre la versión más reciente. Las versiones anteriores pueden recibir una
actualización sólo cuando sea necesario para migrar datos sin pérdida.

## Informar de una vulnerabilidad

No publiques claves, bibliotecas, respaldos ni detalles explotables en una incidencia pública. Usa una
**Private vulnerability report** en la pestaña **Security** del repositorio de GitHub. Incluye la versión,
plataforma, impacto, pasos mínimos de reproducción y, si es posible, una prueba con datos sintéticos.

Si el repositorio aún no tiene habilitados los avisos privados, abre una incidencia pública que indique
únicamente que necesitas un canal privado, sin describir la vulnerabilidad.

## Estado de los paquetes 0.6.16

- El instalador Windows no tiene todavía firma Authenticode.
- El APK lateral usa una clave Android de desarrollo; no es una publicación de Google Play.
- La sincronización directa requiere una clave adicional, pero el QR y la configuración de emparejamiento
  deben tratarse como secretos.
- Tailscale es opcional y aporta conectividad privada; no sustituye las validaciones de UniShare.

Comprueba los SHA-256 publicados antes de instalar. No abras el puerto de sincronización directamente a
Internet. El endurecimiento, la firma comercial y una auditoría externa continúan siendo trabajo futuro y
no deben darse por completados por la mera publicación del código.
