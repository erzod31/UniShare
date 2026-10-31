# Firma y continuidad de actualizaciones

La configuración admite firma de producción; los paquetes laterales continúan usando la identidad
Android histórica hasta que el propietario aporte su clave. Nunca cambies esa identidad sobre una
instalación existente sin preparar la migración: Android rechaza una actualización con otro certificado.
No desinstales la aplicación para resolver ese rechazo sin exportar y verificar primero un respaldo.

## Android

Guarda el keystore fuera del repositorio y proporciona estas variables al proceso de compilación mediante
el gestor de secretos local o de CI. Las contraseñas no deben aparecer en comandos, archivos versionados,
registros ni capturas:

- `UNISHARE_ANDROID_SIGNING_MODE=production`
- `UNISHARE_ANDROID_KEYSTORE`: ruta del keystore.
- `UNISHARE_ANDROID_STORE_PASSWORD`: contraseña del almacén.
- `UNISHARE_ANDROID_KEY_ALIAS`: alias de la clave.
- `UNISHARE_ANDROID_KEY_PASSWORD`: contraseña de la clave.

```powershell
cd platforms/android
.\gradlew.bat --no-daemon clean test lintRelease assembleRelease bundleRelease
```

El APK queda en `app/build/outputs/apk/release/app-release.apk`; el AAB en
`app/build/outputs/bundle/release/app-release.aab`. Comprueba el certificado con `apksigner verify
--verbose --print-certs` y conserva dos copias protegidas de la clave y su procedimiento de recuperación.
El modo producción falla si falta cualquier dato; nunca vuelve silenciosamente a la clave de desarrollo.
Un conjunto parcial de variables también falla aunque sólo se pida una tarea de inspección.

El modo `legacy` (predeterminado sin credenciales) conserva la firma lateral compatible con los usuarios
actuales. No es un paquete de tienda. El APK QA usa `org.unishare.app.qa` y tiene datos separados.

## Windows

Instala el certificado de firma de código y su clave privada en `CurrentUser\My` utilizando el método
del proveedor; un dispositivo criptográfico puede requerir su software y autorización del propietario.
No se importan certificados ni se modifica la confianza del sistema desde estos scripts.

- `UNISHARE_WINDOWS_REQUIRE_SIGNING=1`: exige una distribución firmada.
- `UNISHARE_WINDOWS_CERTIFICATE_THUMBPRINT`: huella SHA-1 del certificado instalado.
- `UNISHARE_WINDOWS_TIMESTAMP_URL`: autoridad RFC 3161 facilitada por el proveedor del certificado.

```powershell
.\packaging\windows\sign-artifact.ps1 -ValidateOnly
.\packaging\windows\build-installer.ps1
```

Se firman el ejecutable, el instalador y su desinstalador con SHA-256 y sello de tiempo. El empaquetado
se detiene si no hay certificado vigente con clave privada, falta SignTool, falla la firma o no se puede
verificar la cadena y el sello de tiempo. Sin estas variables, la distribución lateral sigue declarándose
explícitamente sin Authenticode. Los scripts nunca crean un certificado autofirmado para aparentar confianza.

## Alcance de la verificación

Las pruebas negativas verifican que se rechacen credenciales ausentes o incompletas. Una clave Android
temporal de pruebas puede verificar el camino de producción sin cambiar la clave lateral del usuario.
El certificado definitivo, su custodia y la aceptación de una tienda siguen siendo recursos del propietario.

Fuentes: [firma Android](https://developer.android.com/studio/publish/app-signing),
[SignTool](https://learn.microsoft.com/en-us/dotnet/framework/tools/signtool-exe) y
[firma del instalador Inno Setup](https://jrsoftware.org/ishelp/index.php?topic=setup_signtool).
