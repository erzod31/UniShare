# Changelog

Los cambios relevantes de UniShare se documentan aquí. El proyecto sigue versionado semántico mientras
permanece en la serie `0.x`.

## 0.6.24 — 2026-10-28 (candidato sin publicar)

- Windows y Android permiten buscar y descargar una nueva versión de UniShare desde sus interfaces.
  Canal real GitHub Releases, sin credenciales; verificación de origen, versión, tamaño, SHA-256,
  identidad de producto y firmas aplicables antes de solicitar el instalador nativo con confirmación.
- Cancelación, parciales no instalables, revalidación al aplicar y rechazo de downgrades. Windows
  mantiene AppId y perfil, y prepara reapertura tras el asistente; Android conserva packageName y
  certificado y respeta permiso/confirmación del sistema y su botón Abrir.
- Extensión con consulta/aplicación nativa del navegador para paquetes de tienda. El ZIP de desarrollo
  informa que necesita publicación en tienda; recargar archivos no se presenta como actualización.
- 0.6.23 y anteriores necesitan una primera instalación manual para incorporar el actualizador.
  Ningún candidato se publica ni se aplica a instalaciones reales durante DG-0018.

## 0.6.23 — 2026-10-26

- Las descripciones se traducen automáticamente dentro de la aplicación al idioma elegido en Ajustes
  (español/inglés). Sustituye el botón que abría Google Translate en un navegador.
- Motor Bergamot de Mozilla local y compartido por Windows/Android. Los modelos se descargan por
  necesidad, con tamaño/SHA-256 fijados. Después la traducción funciona sin conexión, sin clave API,
  cuenta ni servidor de traducción y sin enviar el texto fuera del dispositivo.
- Caché derivada por texto, idioma y versión, opción para desactivar y botón para ver el original.
  Las ediciones, backups, sincronización y Obsidian siguen usando el original; los títulos no cambian.
- Descargas incompletas/dañadas y fallos no sustituyen datos; la biblioteca permanece utilizable.
  Se retiran el lanzador de navegador y sus textos/código sin uso. Licencias y reproducción incluidas.

## 0.6.22 — 2026-10-26

- La alternativa de título TikTok deja de copiar hasta 500 caracteres de la descripción: usa un
  encabezado de hasta 80 caracteres Unicode, con límite de palabra y elipsis. Prioriza un título
  específico publicado cuando existe. La descripción original se conserva completa.
- Las listas finales de hashtags/URLs permanecen en la descripción; un hashtag que identifica el tema
  de un título breve se conserva. La migración v7 reconoce la alternativa automática antigua y la acorta.
- Windows relee el elemento después de consultar la red para respetar las ediciones hechas mientras
  llegaban sus metadatos; no enriquece un elemento que se haya retirado durante esa espera.

## 0.6.21 — 2026-10-26

- Los títulos específicos de TikTok y los titulares/nombres JSON-LD se usan como título del elemento.
  La descripción permanece separada. Si TikTok sólo publica un pie, su primera línea sirve de título.
- La reconsulta v6 sustituye etiquetas TikTok genéricas anteriores que coinciden con la página de
  origen, conservando títulos personalizados y descripciones ya guardadas.
- Windows y Android ofrecen **Traducir con Google Translate**: confirmación, navegador y destino según
  el idioma de interfaz. El original permanece intacto; textos largos se copian completos para pegar.
  Es una función en línea opcional, sin SDKs ni nuevas dependencias runtime.
- El panel de detalles y el resumen del editor Windows se pueden desplazar para mantener las acciones
  accesibles con descripciones largas.

## 0.6.20 — 2026-10-26

### Corregido

- TikTok obtiene la descripción y el autor del post exacto desde datos de escritorio, la variante
  móvil `webapp.reflow.video.detail` y formatos legacy, también después de resolver un enlace corto.
- Los pies de publicaciones conservan texto breve, emojis, hashtags, saltos y enlaces; no se descartan
  por coincidir con el título ni se confunden con prosa de artículos o descripciones de recomendaciones.
- JSON-LD se interpreta como un árbol real en Android y se asocia a la URL del contenido en los tres
  clientes. Los objetos de organización y las identidades distintas no aportan el resumen del elemento.
- Una alternativa oEmbed pública y opcional sólo acepta el mismo post. Los errores y las respuestas
  modernas sin título mantienen el enlace y la información obtenida, sin inventar una descripción.
- La migración v4 vuelve a enriquecer enlaces anteriores, respetando las notas manuales. No cambia
  el esquema de datos, el formato de backup ni el protocolo de sincronización.

### Pruebas

- Contratos sintéticos compartidos por Windows, Android JVM/nativo y la extensión; límites Unicode,
  redirecciones, cancelación, respuestas incompletas y alternativa no disponible.
- Comprobación del pie original de una publicación pública real, sin claves ni sesión TikTok.

## 0.6.19 — 2026-10-16

### Corregido

- Obsidian actualiza únicamente el bloque gestionado, conservando literalmente las notas, apéndices y
  propiedades personales. No sobrescribe notas o índices sin identidad válida y marcadores inequívocos.
- Las notas de elementos retirados con contenido personal conservan sus adjuntos y permanecen fuera del
  índice activo. La importación omite elementos eliminados y notas con identidad o límites ambiguos.
- Android rechaza un cuerpo de descarga/metadatos cuya longitud no coincida con la anunciada y limpia
  el parcial. Las respuestas sin longitud y la descompresión gzip transparente siguen funcionando.
- El instalador Windows también puede compilarse desde el ZIP fuente oficial sin historial Git. La
  versión identifica la revisión del archivo y usa el sufijo `.archive`, sin afirmar que detecta ediciones.

## 0.6.18 — 2026-10-16

### Corregido

- Windows y Android reemplazan atómicamente un blob dañado sólo después de verificar la copia correcta;
  conservan los bytes anteriores en `recovery/corrupt-blobs` para recuperación.
- Los deltas incluyen los elementos propietarios de archivos solicitados aunque no exista una nueva
  operación de metadatos. Esto completa la reparación de corrupción detectada en 0.6.17.
- Android conserva borrador, carpeta, modalidad offline, URL del selector y filtros al recrear pantalla.
  La hoja se abre desplegada, las opciones se adaptan al texto ampliado y las barras contrastan en ambos temas.
- Se completa la traducción inglesa del estado vacío de una biblioteca aún no emparejada.

### Pruebas y distribución

- Aplicación QA aislada, pruebas instrumentadas reproducibles de SQLite, migraciones, SAF, compartir,
  backup, conflictos, cortes de red, confirmación causal y permisos reales de Android 17.
- Recorridos con texto al 200 % y prueba entre Android 17 y un nodo Windows real con perfiles sintéticos.
- Firma de producción APK/AAB mediante credenciales externas; Authenticode para EXE, Setup y desinstalador
  con certificado existente. Una configuración incompleta se rechaza sin degradar silenciosamente la firma.
- Paquetes con textos originales de licencias e inventario runtime, sin incluir bibliotecas de pruebas
  dentro de la aplicación de producción.

## 0.6.17 — 2026-10-15

### Corregido

- La sincronización usa una revisión lógica transaccional del contenido en vez de inferir cambios por
  tamaño y fecha de los ficheros SQLite/WAL.
- Android valida identidad, cursores y acuse causal antes de incorporar una respuesta incremental.
- Android y Windows calculan el SHA-256 antes de anunciar un blob como disponible, detectando corrupción
  aunque el archivo conserve el mismo tamaño. La recuperación completa se corrige en 0.6.18.
- Los respaldos Windows leen items, assets, organización e identidad desde una sola instantánea SQLite.
- La exportación Obsidian repara adjuntos dañados y limpia únicamente notas/adjuntos gestionados que ya
  no corresponden a elementos activos, sin tocar contenido personal ni adjuntos citados por sus notas.
- Android 17 solicita el permiso de red local necesario para sincronizar y explica el bloqueo en español
  e inglés si el usuario no lo concede.
- El empaquetador Windows descarta instalaciones de `dotnet` que no puedan resolver la versión fijada,
  en lugar de fallar por elegir primero un SDK local antiguo.
- Los binarios creados desde un árbol Git con cambios sin confirmar identifican su revisión como
  `.dirty`, evitando atribuir el ejecutable al commit base como si fuese reproducible byte a byte.

### Calidad y herramientas

- Esquema Windows v5 con migración conservadora y pruebas de revisión lógica/checkpoint WAL.
- .NET SDK 10.0.402, SQLite 10.0.13, QRCoder 1.8.1, xUnit 4.0.2 y cobertura 18.12.0.
- Android API 37, AGP 9.4.1, Gradle 9.8.1, Compose BOM 2026.09.00 y Core 1.19.1; CI usa las tareas
  vigentes de AGP 9.

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
