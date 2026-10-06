# 0020 — Sincronización durable y entrega segura del perfil

Fecha: 2026-09-30  
Estado: aceptada

## Contexto

La revisión remota no demuestra que un cambio local Android haya sido enviado. Además, cambiar el perfil
Windows mediante una segunda instancia puede solapar el mutex y el puerto del servidor con la instancia
que todavía se está cerrando.

## Decisión

- Android conserva dos contadores durables: generación local y última generación confirmada. Toda
  mutación incrementa el primero antes de modificar la base. Una sincronización captura esa generación y
  sólo la confirma después de completar intercambio de metadatos y blobs; una mutación posterior continúa
  pendiente.
- El trabajo periódico se ejecuta cuando cambia la revisión remota **o** existe una generación local sin
  confirmar. Los trabajos inmediatos se encadenan para no perder una solicitud durante otra ejecución.
- Windows valida `library.db` en modo sólo lectura (`quick_check`, tablas obligatorias y versión soportada)
  antes de guardar otra ubicación.
- La instancia sucesora usa `--wait-for-previous`; la anterior detiene y libera el servidor directo antes
  de liberar el mutex de instancia única.

## Consecuencias

La sincronización deja de depender de timestamps o de cambios remotos para vaciar la salida local. El
cambio de biblioteca no acepta bases arbitrarias ni compite por el puerto. No se introduce nube, broker o
servidor obligatorio: Tailscale continúa siendo sólo el transporte privado entre dispositivos.
