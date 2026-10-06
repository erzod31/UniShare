# Changelog

Los cambios relevantes de UniShare se documentan aquí. El proyecto sigue versionado semántico mientras
permanece en la serie `0.x`.

## 0.6.16 — 2026-10-06

### Corregido

- Windows respeta las solicitudes de apagado de Restart Manager para que una actualización pueda cerrar
  limpiamente el agente de bandeja en vez de fallar por archivos bloqueados.
- La prueba WPF de edición usa un hilo STA de fondo y un margen compatible con hosts CI fríos, evitando
  un falso timeout sin omitir la comprobación visual de sólo lectura.
- El servidor de sincronización ya no puede dejar bloqueadas las operaciones posteriores si falla la
  preparación de su carpeta temporal.
- Los estados de descargas y conflictos de Windows y los principales resultados de Android respetan el
  idioma inglés seleccionado.
- Android usa las API KTX actuales para preferencias y transacciones, evita autoboxing y calcula el
  diseño compacto con el tamaño real de la ventana.

### Calidad y herramientas

- Gradle Wrapper 8.14.5 con hashes oficiales y correcciones de seguridad; se conserva Core 1.18/AGP
  8.13 porque Core 1.19 exige API 37 y AGP 9.1.
- La CI y el empaquetado validan todo el JavaScript, JSON, recursos del manifiesto y paridad de idiomas
  de la extensión Chromium.
- La CI se ejecuta en `main` y pull requests, sin duplicar toda la matriz al crear una etiqueta de
  Release para un commit ya validado.
- La instantánea pública inspecciona también HTML, CSS, JavaScript, Inno Setup y archivos Gradle al
  buscar rutas privadas, identidades Tailscale o secretos.

## 0.6.15 — 2026-10-06

### Añadido

- Interfaz completa en español e inglés, seleccionable y persistente en Windows y Android.
- La extensión Chromium permite elegir el idioma en sus opciones y publica metadatos nativos en ambos
  idiomas.
- El instalador Windows detecta español o inglés y permite escoger el idioma del asistente.

### Calidad

- La traducción se limita a la interfaz: títulos, carpetas, resúmenes, URLs y demás datos personales se
  conservan literalmente.
- Se añadieron pruebas para textos estáticos, estados dinámicos y conservación de contenido desconocido.

## 0.6.14 — 2026-10-06

### Mejorado

- Los enlaces HTTP y HTTPS incluidos en los resúmenes se muestran subrayados y pueden abrirse con el
  navegador predeterminado desde Windows y Android.
- Los resúmenes existentes se presentan en modo lectura para evitar cambios accidentales. El campo se
  habilita únicamente mediante el botón **Modificar resumen**.
- La detección conserva el texto y la puntuación originales, admite varios enlaces y no activa esquemas
  distintos de HTTP/HTTPS.

## 0.6.13 — 2026-10-06

### Corregido

- Los vídeos de YouTube usan su descripción original publicada en `shortDescription`, línea por línea,
  en vez de convertir enlaces de navegación o pie de página en un resumen.
- El fallback HTML ya no acepta todo el texto de `<body>` cuando no existe una región semántica útil.
- Los filtros de prosa rechazan menús concatenados, bloques dominados por enlaces y texto de interfaz.
- La migración de metadatos v3 reemplaza los resúmenes de navegación ya guardados y conserva notas
  manuales, incluso si mencionan términos como privacidad o copyright.
- Windows, Android y la extensión Chromium comparten el mismo orden de señales y límites.

## 0.6.12 — 2026-10-06

Primera versión preparada para publicación pública.

### Incluye

- Biblioteca local-first para enlaces, archivos, capturas y elementos híbridos.
- Clientes Windows y Android con metadatos, búsqueda, colecciones, etiquetas, favoritos y papelera.
- Selección múltiple y eliminación/restauración por lote en ambas plataformas.
- Captura offline estática, cola durable y reanudación de descargas interrumpidas.
- Backup portable, restauración verificada y almacenamiento CAS deduplicado.
- Sincronización directa Windows–Android, deltas causales y conflictos explícitos sin LWW silencioso.
- Agente de bandeja Windows, extensión Chromium, Sharesheet/SAF Android y exportación Obsidian.

### Distribución

- Windows x64 autocontenido y ZIP portátil.
- Android 8+ mediante APK lateral.
- Extensión MV3 para Chrome/Edge en modo desarrollador; paquete Edge Add-ons preparado.

### Limitaciones conocidas

- El instalador Windows no tiene firma Authenticode.
- El APK usa una clave de desarrollo y no es un paquete de Google Play.
- Publicación en Edge Add-ons, firma comercial y auditoría externa pendientes.
