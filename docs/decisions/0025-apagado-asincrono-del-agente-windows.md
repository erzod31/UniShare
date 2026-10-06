# ADR 0025 - Apagado asíncrono del agente Windows

Fecha: 2026-10-06.

## Contexto

El agente de bandeja mantenía el servidor directo activo al cerrar la ventana. La ruta **Salir
completamente** y el modo de validación `--exit-on-close` llamaban a `Shutdown`, y `OnExit` esperaba de
forma síncrona `DirectSyncServer.DisposeAsync()`. En una ejecución real la ventana desaparecía, pero el
proceso permanecía vivo durante más de 60 segundos.

## Decisión

La primera solicitud de salida cancela temporalmente el cierre, oculta la ventana y espera de forma
asíncrona la detención del servidor directo. Después llama a `Shutdown`; `OnExit` conserva la liberación
idempotente de bandeja, descargas y primitivas de instancia, pero ya encuentra el servidor liberado.

El empaquetado Windows ejecuta `test-clean-exit.ps1` sobre el publicado autocontenido. La prueba usa un
perfil y puerto temporales, solicita el cierre de la ventana y exige salida 0 dentro de 15 segundos.

## Consecuencias

- Cerrar la ventana normalmente continúa ocultando el agente en la bandeja.
- **Salir completamente** y `--exit-on-close` dejan de bloquear el hilo de interfaz.
- Una excepción al detener el servidor produce código de salida 1, pero no impide que WPF termine.
- La prueba no usa ni modifica el perfil real y elimina su directorio temporal al finalizar.
