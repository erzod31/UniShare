# ADR 0017: navegación y guardado unificados

Fecha: 2026-09-26  
Estado: aceptada

## Contexto

La ventana anterior exponía demasiadas acciones con el mismo peso visual. Guardar un enlace podía
interpretarse como varios flujos distintos y Android no agrupaba claramente biblioteca, organización,
sincronización y preferencias.

## Decisión

- Usar una interfaz «serena futurista»: superficies neutras, violeta como acción primaria, cian sólo
  como acento de estado, esquinas moderadas y jerarquía tipográfica clara.
- Mantener una única acción principal **Guardar** en ambos clientes.
- Pedir en el mismo flujo el enlace, la carpeta lógica y la elección **Sólo enlace / Copia offline**.
- Mostrar la selección y las acciones posteriores en un inspector en Windows; en Android, mantener las
  acciones dentro de cada tarjeta y diálogo para no perder espacio útil.
- Separar la navegación Android en Biblioteca, Colecciones, Actividad y Ajustes. En Windows se conserva
  una barra lateral persistente por disponer de más anchura.
- No cambiar el modelo de datos ni introducir una dependencia de interfaz adicional: WPF y Compose
  Material 3 siguen siendo las tecnologías nativas ya utilizadas.

## Consecuencias

Los dos clientes comparten vocabulario y recorrido de guardado, pero adaptan la navegación a su tamaño.
El cambio no migra datos y conserva accesibles las funciones avanzadas. La accesibilidad depende de los
controles nativos, nombres de automatización, foco visible, teclado y contraste de los temas claro,
oscuro y alto contraste.
