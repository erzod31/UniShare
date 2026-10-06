# Instalación de UniShare 0.6.16

Descarga los paquetes desde **Releases** en GitHub. El archivo **Source code** sirve para desarrollar,
pero no contiene una aplicación instalada. Verifica el SHA-256 publicado antes de ejecutar un binario.

## Windows

1. Ejecuta `UniShare-Windows-Setup-0.6.16.exe`. Elige español o inglés en el asistente. Es un instalador x64 autocontenido por usuario: no pide
   permisos de administrador ni requiere instalar .NET por separado.
2. Si Windows muestra SmartScreen, comprueba primero el SHA-256 publicado. El binario aún no tiene un
   certificado comercial; usa **Más información > Ejecutar de todas formas** sólo si el hash coincide.
3. Los datos se guardan por defecto en `%LOCALAPPDATA%\UniShare`. La ubicación se puede cambiar con
   **Cambiar biblioteca…** en la barra lateral.
4. Cambia tema, tamaño de texto e idioma español/inglés desde **Opciones y herramientas**. El cambio de
   idioma reinicia sólo la interfaz y conserva la biblioteca. `Ctrl+L` o
   `Ctrl+N` abre el guardado, `Ctrl+F` enfoca la búsqueda, `Ctrl+E` edita el elemento seleccionado y
   `F6` recorre las zonas principales.
5. Al cerrar la ventana, UniShare continúa sincronizando desde el icono junto al reloj. Abre su menú para
   volver a la ventana, activar **Iniciar con Windows** o elegir **Salir completamente**. Activa el inicio
   automático si deseas que el agente se inicie después de iniciar sesión.

El instalador incluye desinstalación desde **Aplicaciones instaladas** y conserva deliberadamente la
biblioteca personal al desinstalar. Como alternativa portátil, extrae por completo
`UniShare-Windows-0.6.16.zip` y ejecuta `UniShare.Desktop.exe`.

## Android

1. Descarga `UniShare-Android-0.6.16.apk` directamente en el teléfono.
2. Permite temporalmente la instalación de aplicaciones desconocidas para el navegador o gestor de
   archivos usado, abre el APK y elige **Actualizar**. No desinstales la versión anterior: actualizarla
   conserva los datos locales.
3. La APK está firmada con clave de depuración y no es una publicación de Play Store.

Por ADB: `adb install -r UniShare-Android-0.6.16.apk`.

La pestaña **Ajustes** permite alternar entre tema de sistema, claro y oscuro y escoger **Español** o
**English**. Ambas elecciones se conservan.
La primera apertura de 0.6.8 vuelve a analizar el cuerpo de los enlaces antiguos y sustituye únicamente
las descripciones automáticas anteriores por resúmenes extractivos. Las notas editadas se conservan. La
acción **Actualizar títulos y resúmenes** permite repetirlo.

## Extensión Chrome o Edge

Hasta que Microsoft apruebe la ficha de Edge Add-ons, la instalación de prueba sigue usando el paquete
`UniShare-Chromium-0.6.16.zip`:

1. Extrae `UniShare-Chromium-0.6.16.zip`.
2. Abre la página de extensiones, activa **Modo desarrollador** y elige **Cargar descomprimida**.
3. Selecciona la carpeta extraída.
4. Mantén UniShare abierto, abre las opciones de la extensión y copia el puerto y la clave desde
   **Opciones y herramientas > Conectar teléfono** en Windows. Ambos aparecen en el panel
   **Datos de conexión para Edge**, con botones independientes para copiarlos.

El paquete `UniShare-Edge-Store-0.6.16.zip` ya está preparado para Partner Center. En las opciones de la
extensión se puede elegir español o inglés. Después de que el
propietario lo publique y Microsoft lo apruebe, Edge permitirá instalarlo con un clic y lo actualizará
automáticamente, sin modo desarrollador ni carpeta local.

## Tailscale y emparejamiento

1. Instala Tailscale desde sus canales oficiales en Windows y Android e inicia sesión en ambos con la
   misma cuenta/tailnet.
2. Mantén UniShare ejecutándose en Windows; la ventana puede estar cerrada mientras permanezca el icono
   de bandeja.
3. UniShare configura automáticamente su ruta de Tailscale Serve en el puerto HTTPS 8443. Entra en
   **Opciones y herramientas > Conectar teléfono**: debe aparecer un QR.
4. Escanea el QR con la cámara del teléfono. Android abrirá UniShare, guardará la conexión y descargará
   la biblioteca. No compartas el QR: contiene la clave privada.
5. Si la configuración automática falla, la misma ventana muestra el error, el comando de recuperación
   `tailscale serve --bg --https=8443 47831` y permite guardar manualmente la dirección `.ts.net:8443`.

Si Android indica que no puede resolver el nombre `.ts.net`:

1. Abre Tailscale en el teléfono y confirma que figure como conectado.
2. En sus preferencias, activa **Usar DNS de Tailscale**.
3. En **Túnel dividido por aplicaciones**, comprueba que UniShare no esté excluida.
4. Vuelve a UniShare y pulsa **Reintentar**. La versión 0.5.1 enlaza expresamente la petición a la red VPN
   de Android para que la resolución MagicDNS y el tráfico utilicen el túnel correcto.

Referencias: [preferencias DNS de Tailscale](https://tailscale.com/docs/features/client/manage-preferences) y
[túnel dividido en Android](https://tailscale.com/docs/features/client/android-app-split-tunneling).

No abras puertos del router. Si ya usas Tailscale Serve en 443, el puerto 8443 evita sustituir esa ruta.
Para desactivar únicamente UniShare: `tailscale serve --https=8443 off`.

## Comprobación rápida

- Guarda un enlace en Android y confirma que aparece en Windows.
- Guarda otro en Windows; con Android abierto debe aparecer en unos 15 segundos.
- Con Android cerrado, el sistema puede demorar la recepción periódica hasta 15 minutos o más por ahorro
  de batería. Al abrir la app se sincroniza de inmediato.
- Si Windows está apagado, Android conserva el cambio y lo reintenta al recuperar conectividad.

## Compilar desde el código fuente

Necesitas Git y el SDK .NET indicado en `global.json`. Un checkout nuevo se valida con:

```powershell
dotnet restore UniShare.slnx --locked-mode
dotnet build UniShare.slnx -c Release --no-restore
dotnet test UniShare.slnx -c Release --no-build
dotnet run --project src/UniShare.Desktop/UniShare.Desktop.csproj
```

Android requiere JDK 21 y Android SDK API/Build Tools 36:

```powershell
cd platforms/android
.\gradlew.bat testDebugUnitTest testReleaseUnitTest lintRelease assembleRelease
```

No necesitas ni debes reutilizar claves, perfiles o bases de datos de otra persona. El primer arranque
crea un perfil vacío y una clave de emparejamiento nueva.
