# ADR 0012: sincronización directa sobre Tailscale

Fecha: 2026-09-24. Estado: aceptado.

## Contexto

El usuario rechazó el intercambio manual de archivos y pidió que cada cambio se sincronice directamente
entre Android y Windows dentro y fuera de casa. Abrir puertos, mantener DDNS y gestionar TLS en el router
incrementa el riesgo; un servidor cloud obligatorio contradice el objetivo local-first. Android tampoco
permite mantener indefinidamente un socket de fondo sin costes y restricciones visibles.

## Decisión

- Windows es el nodo de convergencia y escucha exclusivamente en `127.0.0.1:47831`.
- Tailscale Serve publica ese loopback dentro de la tailnet por HTTPS en el puerto 8443. Se elige un
  puerto separado para no reemplazar rutas Serve preexistentes.
- UniShare añade una clave aleatoria de 256 bits y comparación en tiempo constante; pertenecer a la
  tailnet no basta para usar la API.
- Android envía una reconciliación al guardar, al abrir la app y mediante trabajo durable. Negocia primero
  los SHA-256 disponibles; conserva metadatos completos, pero sólo envía y recibe blobs ausentes.
  Mientras está visible sondea sólo una revisión ligera cada 15 segundos y descarga si cambió.
- WorkManager exige conectividad, aplica backoff y conserva reintentos. El intervalo periódico mínimo
  de Android es 15 minutos; no se usa servicio permanente ni FCM.
- El formato portable verificado sigue existiendo internamente como envelope de transporte y para
  respaldos, pero se eliminan de las interfaces y CLI la importación/exportación manual para sincronizar.
- La fusión mantiene baselines por peer: sólo aplica automáticamente cambios unilaterales; concurrencia
  conserva ambos snapshots. `updated_at` nunca elige un ganador. Los blobs se aceptan sólo tras SHA-256.
- Una retirada de etiqueta o colección sólo tombstonea el UUID de membresía que el emisor observó. Una
  re-alta concurrente recibe otro UUID y permanece, sin usar timestamps como causalidad.

## Consecuencias

Los datos viajan cifrados punto a punto por WireGuard cuando Tailscale logra ruta directa y siguen
cifrados si usa un relay. Windows debe estar encendido y UniShare abierto; si no, Android conserva el
cambio y reintenta. Con Android cerrado, la recepción de un cambio Windows puede demorarse por las
políticas del sistema. Inmediatez absoluta exigiría una notificación persistente o un servicio push
externo, opciones rechazadas para esta versión.
