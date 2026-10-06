# ADR 0010: entrega coordinada de artefactos multiplataforma

- Estado: aceptada
- Fecha: 2026-09-24
- Origen: decisión directa del usuario

## Decisión

Una entrega de UniShare debe regenerar y validar todos los clientes soportados afectados. Los cambios
de experiencia compartida se aplicarán tanto a Windows como a Android antes de enlazar ejecutables o
APK, salvo que el usuario limite explícitamente el alcance a una plataforma.

Cada entrega indicará versión, hash, resultado de compilación/pruebas y cualquier diferencia funcional
que todavía impida la paridad. Un artefacto compilado antes del último cambio relevante se considera
obsoleto y no se entrega como versión actual.

## Consecuencias

- Las entregas tardan más, pero evitan presentar interfaces o funciones divergentes como equivalentes.
- La matriz de verificación debe distinguir paridad real, limitaciones declaradas y pruebas no realizadas.
- Una función ausente en un cliente no se oculta actualizando únicamente el número de versión.
