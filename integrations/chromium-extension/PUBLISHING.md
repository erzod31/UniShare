# Publicación en Microsoft Edge Add-ons

Microsoft Edge sólo admite una instalación normal, persistente y actualizable mediante Edge Add-ons. La
alternativa **Cargar descomprimida** es únicamente para desarrollo; un CRX privado necesita políticas de
empresa y no es apropiado para usuarios particulares.

## Preparado en el repositorio

- Paquete ZIP con `manifest.json` en la raíz y únicamente archivos de ejecución.
- Manifest V3, iconos 16/32/48/128 y apertura automática de la configuración tras instalar.
- Logotipo de ficha 300 × 300.
- Descripción, propósito único y justificación de permisos en `store-listing-es-ES.md`.
- Texto de privacidad en `privacy-policy.md`.

## Paso externo del propietario

1. Registrarse gratuitamente en el programa Microsoft Edge de Partner Center con una cuenta Microsoft.
2. Publicar `privacy-policy.md` en una URL HTTPS pública y estable.
3. Extraer `artifacts/UniShare-Edge-Submission-0.6.16.zip`.
4. Crear un producto de extensión y cargar el `UniShare-Edge-Store-0.6.16.zip` que contiene.
5. Completar la ficha en español con `store-listing-es-ES.md` y cargar `logo-300.png`.
6. Declarar las prácticas de privacidad conforme al texto preparado, enviar a certificación y esperar la
   aprobación de Microsoft.
7. Después de aprobarse, sustituir en la documentación la instalación de desarrollo por el enlace oficial
   de Edge Add-ons.

No se debe publicar la clave privada de una biblioteca, incluirla en el paquete ni pegarla en Partner
Center. Cada usuario obtiene su propia clave desde UniShare para Windows.
