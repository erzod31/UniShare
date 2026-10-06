# UniShare para Android 0.6.16

La aplicación Android es local-first: guarda enlaces, archivos e híbridos en SQLite privado y conserva
blobs en un CAS SHA-256. Recibe contenido con Sharesheet/SAF, busca, edita metadatos y organización,
crea copias offline, exporta/restaura respaldos y
sincroniza directamente con UniShare Windows. No usa una nube de datos ni copia automática del sistema.

Desde la navegación introducida en 0.5.0, la app separa **Biblioteca**, **Colecciones**, **Actividad** y **Ajustes**. El botón flotante
**Guardar** abre una única hoja en la que se pega el enlace, se elige la carpeta y se decide entre
conservar sólo el enlace o crear también una copia offline.

Al guardar, Android obtiene el título, el sitio y el autor, y genera localmente un resumen extractivo del
cuerpo real. Usa `articleBody`, `<article>` o `<main>` y descarta navegación y bloques dominados por enlaces;
la descripción comercial de la página nunca se usa como resumen. La primera apertura de 0.6.8 vuelve a
analizar los enlaces anteriores después de sincronizar, sustituye los valores automáticos de 0.6.7 y
respeta las notas editadas. **Ajustes > Actualizar títulos y resúmenes** permite reintentar.
Desde 0.6.13, los vídeos de YouTube usan su descripción original y la migración v3 retira resúmenes
históricos formados por menús sin sobrescribir notas manuales.
Desde 0.6.14 los resúmenes se presentan en modo lectura, las direcciones HTTP/HTTPS son pulsables y el
campo de texto sólo se habilita al elegir **Modificar resumen**.
Desde 0.6.15 la interfaz puede cambiarse entre español e inglés desde **Ajustes**. La preferencia se
guarda en el dispositivo y nunca modifica el contenido de la biblioteca.

## Requisitos de compilación

- JDK 17 o 21.
- Android SDK con `platforms;android-36` y Build Tools 36.0.0.
- Gradle 8.14.5, AGP 8.13.2, Kotlin 2.3.21, Compose BOM 2026.06.01 y AndroidX Core 1.18.0
  fijados por el proyecto. Son las últimas revisiones estables cuyos metadatos admiten API 36.
- Conexión inicial para resolver dependencias de Google Maven/Maven Central.

```powershell
cd platforms/android
.\gradlew.bat :app:testDebugUnitTest :app:lintRelease :app:assembleRelease
```

El APK optimizado queda en `app/build/outputs/apk/release/app-release.apk`. La entrega lateral conserva
la misma firma de depuración de las versiones anteriores para permitir `adb install -r`; no debe
publicarse en una tienda.

## Configurar sincronización directa

1. Instala Tailscale en el ordenador y el teléfono e inicia sesión en la misma tailnet.
2. Abre UniShare Windows y entra en **Opciones y herramientas > Conectar teléfono**. Windows configura
   automáticamente Tailscale Serve y muestra un QR.
3. Escanea el QR con la cámara. El enlace `unishare://pair` abre Android, valida la dirección privada,
   guarda el emparejamiento y sincroniza de inmediato. La entrada manual queda como recuperación.

Android envía el cambio justo después de guardar. Mientras la app está visible consulta cada 15 segundos
una revisión pequeña y sólo reconcilia la biblioteca cuando cambió. También sincroniza al abrir y usa
WorkManager con conectividad y backoff al estar cerrada. Android impone un mínimo de 15 minutos para el
trabajo periódico y puede aplazarlo; no existe garantía de recepción instantánea con la app dormida.

Desde 0.5.1 las peticiones `.ts.net` se abren explícitamente sobre la red VPN activa de Android. El
encabezado sólo muestra **Sincronizado** cuando el último intento terminó correctamente; los fallos de
VPN o MagicDNS se explican en español y ofrecen **Reintentar** y **Abrir Tailscale**. Si persiste un fallo
DNS, activa **Usar DNS de Tailscale** y elimina UniShare de las exclusiones del túnel dividido.

La versión 0.6.0 añadió nombres de acción para TalkBack, anuncios de estado, navegación compacta sin
etiquetas visuales redundantes cuando falta anchura, filtros desplazables y formularios que siguen
alcanzables con texto ampliado y teclado en pantalla. Compose conserva el escalado `sp` del sistema.

Desde 0.6.1, **Eliminar** muestra una confirmación y mueve el elemento a la Papelera recuperable. En
**Ajustes > Mostrar papelera** se puede revisar y restaurar. Los menús fijan colores de superficie y
texto compatibles con los temas claro y oscuro.

Desde 0.6.12, **Seleccionar** activa casillas accesibles en la biblioteca. Se puede marcar un grupo,
usar **Todos** y eliminar o restaurar el lote en una única transacción local; el modo muestra el recuento,
oculta el botón de alta para evitar acciones ambiguas y se cancela sin modificar los elementos.

Windows debe estar encendido, UniShare abierto y Tailscale conectado. Si no lo está, el dato permanece
guardado en Android y el trabajo se reintenta. La API Windows sólo escucha en loopback; Tailscale Serve
termina HTTPS y UniShare exige además una clave de 256 bits. No abras el puerto 47831 en el router.

## Prueba funcional

1. Instala sin borrar datos: `adb install -r app/build/outputs/apk/release/app-release.apk`.
2. Comprueba que los elementos anteriores siguen visibles tras la actualización.
3. Configura Tailscale como se indicó y guarda un enlace en Android; debe aparecer en Windows.
4. Modifica o crea otro en Windows; con Android visible debe llegar en unos 15 segundos.
5. Apaga Windows, guarda en Android y verifica el mensaje de pendiente; enciende Windows y abre UniShare
   para comprobar el reintento.
6. Edita el mismo título sin conexión en ambos lados. Tras reconectar deben conservarse las dos versiones
   y **Resolver conflictos** debe permitir elegir explícitamente.
7. Repite una vez con Wi-Fi y otra con datos móviles. El usuario verificó ambos transportes el
   2026-09-26; repítelo después de cambios de protocolo.
8. Exporta una copia de seguridad y valida en Windows con `UniShare.Backup inspect`. El respaldo no es
   un mecanismo de sincronización.

Para el emulador de desarrollo se admite únicamente en build `debug` HTTP hacia `10.0.2.2` o
`127.0.0.1`; el manifest release no habilita tráfico claro. La prueba automatizada usa `adb reverse`.

## Estado y límites

- Esquema Android v5; la migración v4→v5 conserva los datos y crea el registro causal de operaciones.
- La reconciliación conserva UUID, metadatos, organización, tombstones y blobs verificados.
- La descarga offline conserva primero el enlace; si falla la red, muestra el error y permite reintentar.
  Las páginas HTML se convierten en una instantánea estática sin scripts ni recursos remotos.
- La restauración fusiona una copia verificada sin borrar datos y conserva divergencias como conflictos.
- Android contrasta los primeros 512 bytes con el tipo declarado por SAF. Rechaza firmas contradictorias
  y HTML/SVG activo, conserva tipos de contenedores ZIP conocidos y no deja parciales ni cambios ficticios
  de sincronización tras un rechazo. Usa `SQLiteOpenHelper` con migraciones probadas en lugar de Room.
- Wi-Fi y datos móviles sobre Tailscale fueron verificados en teléfono. Sharesheet, SAF y TalkBack
  completos en hardware permanecen **NO VERIFICADOS**.
- La versión 0.6.9 negocia hashes y transfiere sólo los blobs ausentes y los agregados de items modificados.
  Los ACK y cursores se guardan después del commit; un fallo reenvía el delta y un cursor inválido fuerza
  un snapshot completo de recuperación.
- El emulador API 36 verificó actualización sin pérdida, persistencia offline y sincronización HTTP real
  Android→Windows (6 elementos, 3 assets, un blob) y Windows→Android (8 elementos finales).
