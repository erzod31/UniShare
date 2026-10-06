# 0023 - Delta por agregados y ACK causal

Fecha: 2026-10-06

Estado: aceptada

## Contexto

La sincronización directa ya evitaba transferir blobs presentes, pero cada cambio seguía enviando todos
los metadatos. Además, la revisión de archivo indicaba que algo había cambiado, no qué operaciones habían
sido confirmadas. Avanzar por timestamps o antes del commit podía perder un cambio tras una interrupción.

## Decisión

- Cada réplica mantiene un contador monotónico y registra el UUID del item afectado.
- El delta transmite el agregado completo de cada UUID cambiado —item, assets, membresías y etiquetas
  referenciadas— usando el formato portable v1 existente. No se introduce un segundo serializador.
- La primera sincronización, un cambio de identidad o un cursor futuro usa un snapshot completo.
- Windows devuelve su identidad, el cursor incluido y el contador Android incorporado. Android persiste
  esos valores sólo después de importar y confirmar la respuesta.
- Una petición fallida no avanza cursores. El mismo delta puede repetirse porque la fusión es idempotente.
- Las importaciones directas no generan nuevas operaciones locales; una restauración elegida por el usuario
  sí las genera. Android compacta operaciones ya confirmadas conservando el contador AUTOINCREMENT.
- La detección y resolución de conflictos existente se mantiene: no se decide por `updated_at`.

## Alternativas descartadas

- Enviar el log interno completo: acoplaría los esquemas Windows y Android y duplicaría el contrato portable.
- Last-Write-Wins por timestamp: puede descartar ediciones concurrentes y contradice los requisitos.
- Avanzar el cursor al enviar: una caída entre envío y commit produciría pérdida silenciosa.

## Consecuencias

El tráfico de metadatos pasa de O(biblioteca) a O(items modificados) después del primer intercambio. El
receptor sigue validando checksums y relaciones del paquete. Quedan para el endurecimiento posterior la
rotación/revocación de credenciales y pruebas generativas extensivas con muchas réplicas.
