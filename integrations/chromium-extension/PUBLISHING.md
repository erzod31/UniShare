# Edge Add-ons / Chrome Web Store

Los ZIP son paquetes de envío, no extensiones ya instaladas o aprobadas por una tienda. Una versión
descomprimida no obtiene actualizaciones binarias al recargar. No se ha configurado ninguna identidad
de tienda real en el repositorio: la cuenta del propietario debe crear el producto y recibir su ID.

## Preparación local 0.6.25

```powershell
.\packaging\chromium\build-store-package.ps1 -Version 0.6.25 -Channel development
.\packaging\chromium\build-store-package.ps1 -Version 0.6.25 -Channel edge
.\packaging\chromium\build-store-package.ps1 -Version 0.6.25 -Channel chrome
```

Cada ZIP de tienda lleva `manifest.json` en la raíz, MV3, permisos mínimos, idiomas ES/EN y un
`distribution.json` con el canal previsto. No contiene claves, IDs ficticios ni `update_url` inventado.
La tienda incorpora el canal real al instalar. Cuando se conozca el ID, repetir el empaquetado con
`-ExtensionId` y el valor realmente asignado. No crear otro producto para una actualización.

Los dos expedientes `UniShare-Edge-Submission-0.6.25.zip` y
`UniShare-Chrome-Submission-0.6.25.zip` contienen su payload exacto, ficha ES/EN, privacidad ES/EN,
icono de 128px, logo de 300px, capturas reales de Ajustes 1280×800 y mosaico 440×280. Las capturas
se hicieron con un perfil QA descomprimido y muestran ese estado con honestidad, sin datos personales.
No son evidencia de instalación ni actualización real de tienda.

## Último paso del propietario, después de revisar

1. Acceder a la cuenta real de editor de Microsoft Edge Partner Center o Chrome Web Store. Si falta,
   el propietario debe crearla y aceptar personalmente acuerdos/costes aplicables. No se ha aceptado nada.
2. Crear el producto UniShare y cargar únicamente el ZIP `*-Store-*` del expediente correspondiente,
   no el expediente completo ni el paquete de desarrollo. Registrar el ID que asigne la tienda.
3. Completar la ficha y contacto del editor con datos reales, cargar las imágenes incluidas y declarar
   permisos/privacidad conforme a las fichas. No publicar una clave de emparejamiento de Windows.
4. Usar una URL HTTPS pública de la política. Tras publicar la rama pública revisada, puede utilizarse
   `https://github.com/erzod31/UniShare/blob/main/integrations/chromium-extension/privacy-policy.md`
   (o `privacy-policy-en.md`). Verificar que la versión publicada coincide con el expediente.
5. Guardar/revisar el borrador antes de enviarlo a certificación. La aprobación la decide la tienda.
6. Tras aprobarse, registrar URL/ID oficiales, instalar en un perfil de prueba independiente, emparejar
   con una biblioteca sintética y comprobar guardar, conservar ajustes y actualizar entre dos versiones
   de ese mismo producto. No declarar esta prueba superada antes de ejecutarla.

La extensión verifica su identidad con `management.getSelf()`, sin permiso adicional `management`,
el canal oficial y el tipo de instalación. Sólo habilita Aplicar tras una respuesta o evento real de
actualización pendiente del navegador, con confirmación. No descarga ni instala un ZIP por su cuenta.
No solicita comprobaciones periódicas que el navegador ya gestiona.

## English

Use the corresponding Store ZIP inside each submission dossier. All listings, privacy documents and
images are supplied; publisher account/contact, assigned product ID, owner agreements and store review
are external. No credentials or fabricated identity are supplied. An initial upload may omit the ID;
after assignment pass it with `-ExtensionId` and keep the same store product for later versions.
The browser provides native binary verification/update delivery. A development reload does not.

References: [Chrome runtime](https://developer.chrome.com/docs/extensions/reference/api/runtime),
[self identity without management permission](https://developer.chrome.com/docs/extensions/reference/api/management),
[Microsoft extension distribution](https://learn.microsoft.com/en-us/microsoft-edge/extensions/developer-guide/alternate-distribution-options).
