# Ficha de Microsoft Edge Add-ons — es-ES

## Nombre

UniShare

## Descripción breve

Guarda enlaces y copias offline directamente en tu biblioteca privada UniShare.

## Descripción

UniShare permite guardar la pestaña actual de Microsoft Edge en tu biblioteca personal local. Puedes
revisar el título, elegir una carpeta y decidir entre conservar únicamente el enlace o añadir una copia
offline con el texto renderizado y las imágenes visibles compatibles. La extensión se comunica
exclusivamente con la aplicación UniShare instalada en el mismo ordenador mediante `127.0.0.1`; no usa
una cuenta en la nube, no incorpora publicidad, no vende datos y no envía el historial de navegación a
terceros. Sólo accede a la pestaña activa después de que pulses el icono y confirmes el guardado.

Requiere UniShare para Windows abierto y una configuración inicial con el puerto y la clave privada que
muestra la propia aplicación. El contenido guardado queda bajo el control del usuario en su biblioteca
UniShare y puede sincronizarse directamente con Android mediante su red privada configurada.

## Propósito único

Guardar, a petición del usuario, la pestaña activa de Edge en una biblioteca local UniShare.

## Términos de búsqueda

marcadores, enlaces, biblioteca, offline, privacidad, lectura, archivo

## Justificación de permisos

- `activeTab`: leer URL y título únicamente de la pestaña que el usuario decide guardar.
- `scripting`: extraer texto renderizado e imágenes visibles sólo cuando se solicita una copia offline.
- `storage`: conservar localmente el puerto y la clave de conexión introducidos por el usuario.
- `http://127.0.0.1/*`: enviar el elemento exclusivamente a la aplicación UniShare del mismo equipo.

## Activos para la ficha

- Logotipo obligatorio: `store-assets/logo-300.png` (300 × 300 PNG).
- Capturas y mosaicos promocionales: opcionales; se pueden añadir después de validar la ficha publicada.
