# ADR-0028: recuperación verificable y pruebas de dispositivo aisladas

- Estado: aceptado
- Fecha: 2026-10-16

Detectar corrupción por SHA-256 no garantizaba repararla: los importadores rechazaban el destino dañado
y los exportadores incrementales podían omitir un archivo solicitado sin operaciones nuevas de su item.

Se amplían los deltas con los agregados propietarios de hashes solicitados, conservando metadatos y
organización coherentes en la misma instantánea del exportador. La promoción requiere SHA-256/longitud
correctos y reemplazo atómico; el archivo sustituido queda en `recovery/corrupt-blobs`. Esta carpeta no
se considera CAS válido, no se elimina automáticamente y no sustituye un backup completo.

Las pruebas Android de dispositivo usan `org.unishare.app.qa`, perfiles por prueba y documentos sintéticos.
UIAutomator 2.4 usa controles del sistema para permiso/SAF. Se descartó Espresso en este hito porque su
versión estable resolvía mediante reflexión `InputManager.getInstance`, retirado en Android 17; no se
relajaron las comprobaciones ni se excluyeron los escenarios visuales.

La hoja de guardado abre desplegada y conserva el borrador/URL de SAF al recrear la actividad. Los fallos
de conexión mantienen outbox y cursores, y los ACK inválidos se rechazan antes de importar.

La firma definitiva permanece externa. Android permite credenciales explícitas sin cambiar la identidad
lateral histórica; Windows usa un certificado instalado y sello RFC 3161 sin modificar la confianza del
sistema. Los paquetes incorporan inventarios y textos de licencia, y las credenciales quedan fuera del árbol.
