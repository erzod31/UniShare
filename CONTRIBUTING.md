# Contribuir a UniShare

Gracias por ayudar a mejorar UniShare. El proyecto acepta informes de errores, propuestas de experiencia
de usuario, documentación y cambios de código.

## Antes de abrir una incidencia

1. Comprueba que utilizas la versión más reciente publicada.
2. Busca incidencias existentes para evitar duplicados.
3. No adjuntes bibliotecas reales, claves de emparejamiento, nombres privados de Tailscale, direcciones
   IP, rutas con tu nombre de usuario ni capturas que contengan información personal.
4. Para una vulnerabilidad, usa el procedimiento privado de [SECURITY.md](SECURITY.md).

## Desarrollo

Consulta [DEVELOPMENT.md](DEVELOPMENT.md) para preparar .NET y Android. Antes de enviar un cambio:

```powershell
dotnet restore UniShare.slnx --locked-mode
dotnet format UniShare.slnx --verify-no-changes --no-restore
dotnet build UniShare.slnx -c Release --no-restore
dotnet test UniShare.slnx -c Release --no-build
```

Para Android:

```powershell
cd platforms/android
.\gradlew.bat testDebugUnitTest testReleaseUnitTest lintRelease assembleRelease
```

Mantén los cambios pequeños, añade pruebas que demuestren el comportamiento y actualiza la documentación
cuando cambie un flujo visible. No reduzcas validaciones para hacer pasar una prueba.

## Datos de prueba

Usa perfiles temporales mediante `--profile <ruta-temporal>`. No incorpores bases de datos, blobs,
respaldos, APK firmados con claves privadas, certificados ni configuración de dispositivos reales.

Al contribuir aceptas que tu aportación se distribuya bajo la licencia MIT del proyecto.
