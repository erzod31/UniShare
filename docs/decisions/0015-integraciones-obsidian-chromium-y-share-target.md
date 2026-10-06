# ADR 0015: integraciones funcionales y límite del Share Target

Fecha: 2026-09-25  
Estado: aceptada

## Decisión

- Obsidian se integra mediante una carpeta `UniShare` portable: el bloque gestionado se regenera y el
  bloque `Notas editables` se conserva. La importación sólo ocurre por acción explícita y sólo modifica
  la descripción.
- Chrome/Edge usan una extensión Manifest V3 sin proceso nativo adicional. Envía URL, título y carpeta
  al endpoint loopback autenticado ya mantenido por UniShare Windows.
- El ZIP Windows 0.4.0 no se anunciará como Share Target nativo. Se mantienen pegar, arrastrar/soltar y
  la extensión como capturas funcionales.
- MSIX/Share Target se aplaza junto con la firma de distribución. No se añadirá una dependencia grande
  de Windows App SDK ni un paquete sin una estrategia de firma e instalación aceptada.

## Motivo

La documentación oficial vigente indica que WPF/WinForms necesitan identidad de paquete y activación
de Windows App SDK para recibir desde la hoja Compartir. Un MSIX o sparse package distribuible debe
estar firmado. Introducirlo durante una fase que aplaza firma/endurecimiento crearía un artefacto difícil
de instalar y una falsa sensación de entrega completa.

Referencias:

- https://learn.microsoft.com/windows/apps/develop/windows-integration/integrate-sharesheet-receive
- https://learn.microsoft.com/windows/apps/package-and-deploy/packaging/

## Consecuencias

- La captura cotidiana queda cubierta sin nube ni instalador adicional.
- El endpoint Chromium reutiliza autenticación y almacenamiento del programa; no duplica una base.
- El Share Target permanece visible como limitación verificable hasta la fase de empaquetado firmado.
