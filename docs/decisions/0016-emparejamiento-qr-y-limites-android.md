# ADR 0016: emparejamiento QR y sincronización automática en Android

Fecha: 2026-09-25. Estado: aceptado.

## Contexto

Una instalación nueva de Android no conocía la URL ni la clave del nodo Windows. La interfaz ocultaba
esa ausencia y mostraba una biblioteca vacía, por lo que cerrar y abrir la APK no podía recuperar nada.
Copiar manualmente URL y clave era propenso a errores. Android tampoco permite prometer recepción
instantánea indefinida con la app cerrada sin un servicio push externo.

## Decisión

- Windows configura su ruta HTTPS 8443 con la CLI local de Tailscale y obtiene su DNS privado.
- Windows persiste esa URL y genera un QR `unishare://pair` con URL y clave aleatoria de 256 bits.
- Android registra un deep link acotado, valida HTTPS y el dominio `.ts.net`, guarda el emparejamiento,
  sincroniza de inmediato y mantiene reintentos con WorkManager.
- Android muestra de forma prominente los estados no conectado, sincronizando, pendiente y conectado.
- No se incorpora FCM ni otra nube obligatoria. En primer plano se sondea la revisión cada 15 segundos;
  en segundo plano se respeta el trabajo periódico inexacto que permite el sistema.

## Consecuencias

El usuario realiza una única acción explícita y verificable: escanear el QR. Reinstalar o borrar los datos
de Android exige emparejar de nuevo, porque insertar la clave en el APK impediría revocarla y la expondría.
El QR es un secreto y no debe publicarse. Con Android dormido puede haber latencia; al abrir la aplicación,
guardar un cambio o escanear el QR el intento es inmediato y los fallos quedan visibles y reintentables.

