# ADR 0018: captura renderizada acotada y accesibilidad adaptativa

Fecha: 2026-09-26  
Estado: aceptada

## Contexto

La descarga HTTP segura conserva páginas estáticas, pero no ve contenido generado por JavaScript y su
vista de lectura básica elimina imágenes. Además, Windows y Android necesitaban funcionar con lector de
pantalla, texto ampliado y superficies estrechas sin crear una interfaz paralela.

## Decisión

- La extensión Chromium obtiene de la pestaña activa únicamente texto ya renderizado, metadatos y hasta
  12 imágenes visibles convertidas localmente a JPEG. No transmite scripts, formularios, cookies ni el
  DOM ejecutable.
- El servidor local vuelve a validar límites, formatos `data:image` y base64, codifica todo texto y crea
  un HTML inerte con CSP sin scripts, red ni marcos. El límite es 1 000 000 de caracteres, 1,5 MB por
  imagen y 8 MB para el conjunto de imágenes.
- La captura HTTP normal se mantiene como alternativa independiente y no incorpora un navegador
  embebido.
- Windows expone nombres y regiones vivas a UI Automation, atajos de teclado, diseño compacto y tres
  tamaños de texto persistentes. Conserva los colores del sistema en alto contraste.
- Android usa semántica explícita para acciones e iconos, regiones vivas, controles desplazables y una
  navegación compacta cuando la anchura o el escalado de fuente lo requieren.

## Consecuencias

- Las aplicaciones web y páginas con hidratación JavaScript pueden conservar su texto visible.
- Las imágenes que prohíben lectura mediante canvas por política de origen se omiten; nunca se debilita
  la protección del navegador para capturarlas.
- Vídeo, audio, canvas interactivo y estados que requieren sesión no se reproducen dentro de la copia.
- No se añade una dependencia de Chromium, Playwright o un servicio externo al ejecutable principal.
