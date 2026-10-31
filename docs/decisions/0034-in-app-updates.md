# ADR 0034 — actualización propia por plataforma

Estado: implementación candidata validada con adaptadores; aplicación real pendiente (DG-0018,
2026-10-28).

El repositorio público real es `erzod31/UniShare`. Se consulta su última release estable mediante
GitHub REST sin credenciales, con límites y sin enviar datos de la biblioteca. El 28/10/2026 la
release existente es 0.6.16; no se simula la publicación de un candidato posterior.

Windows y Android seleccionan sólo el nombre exacto del paquete de su producto/plataforma. Exigen
versión estrictamente mayor, origen HTTPS de ese repositorio, digest SHA-256 de la API, tamaño
acotado y verificación del paquete antes de ofrecerlo al instalador. Se verifica otra vez al aplicar.
La descarga es cancelable y un parcial no se ofrece como paquete. No se permiten downgrades.

Android compara packageName, versionName/versionCode y certificados con su instalación; respeta
el permiso para instalar desde esta fuente y el instalador del sistema, que verifica finalmente
el APK y ofrece abrirlo. Windows comprueba metadatos PE y Authenticode: una firma presente debe
ser válida y, si el cliente ya está firmado, conservar su certificado. Los clientes Windows
históricos no están firmados; en ese caso se informa expresamente y se conserva la confirmación
del asistente Inno. No se presenta un hash como sustituto de una firma Authenticode.
Un directorio PE de certificados presente implica firma que debe ser válida: NOSIGNATURE no puede
convertir un bloque de firma corrupto en un paquete histórico sin firma.

Windows entrega al Inno existente sin cambiar AppId y cierra sólo su propia instancia después
de aceptar el lanzamiento. El instalador reabre la app tras una actualización aceptada conservando
el perfil. Cancelar el asistente puede requerir reabrir la app manualmente. El ZIP portátil puede
instalar la edición por usuario; no modifica por sí mismo su carpeta portátil.

La extensión utiliza exclusivamente la actualización nativa del navegador para paquetes de tienda.
El ZIP de desarrollo no puede actualizar su binario descargando un ZIP o recargando sus archivos.
Sin publicación/identidad de tienda, esa limitación se mantiene visible y documentada.

No hay clientes macOS/Linux/iOS/webOS distribuidos que este cambio pueda declarar disponibles.
La entrega inicial 0.6.23 carece de actualizador: requiere una primera instalación manual del nuevo
candidato. Los candidatos no se publican ni se instalan en equipos reales durante DG-0018.
