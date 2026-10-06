# ADR 0014: terminar el producto antes del endurecimiento de seguridad

Fecha: 2026-09-25. Estado: aceptado.

## Contexto

UniShare todavía tiene diferencias funcionales entre Windows y Android y carece de varias integraciones
del producto. El usuario ha indicado expresamente que se termine primero el programa y que la seguridad
avanzada se complete después.

## Decisión

- Conservar todos los controles de seguridad existentes y no debilitarlos para acelerar funciones.
- Priorizar CRUD completo, organización, consulta offline, restauración, integración Windows,
  extensión/Obsidian, accesibilidad, instalación y documentación de uso.
- Aplazar cifrado adicional, Keystore/DPAPI, rotación de claves, firma comercial, auditoría externa,
  fuzzing especializado y demás endurecimiento no imprescindible para ejecutar la función.
- Mantener esas tareas visibles como `APLAZADAS: SEGURIDAD`, nunca como implementadas.

## Consecuencias

Una función no se considera terminada sin pruebas y evidencia, pero la finalización funcional no implicará
que UniShare esté listo para distribución pública o datos altamente sensibles. Después del cierre del
producto se abrirá un hito de seguridad independiente.
