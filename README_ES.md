# TerraGuide

> Traduccion al Español por [FrankV22](github.com/itsFrankV22)

Un plugin útil para servidores de Terraria que ejecutan TShock, el cual proporciona acceso dentro del juego a recetas de fabricación e información de la wiki.

## Características

- `/recipe <nombre del objeto>` - Muestra información sobre la fabricación de objetos
  - Muestra la estación de fabricación requerida
  - Lista todos los ingredientes necesarios
  - Muestra condiciones especiales (biomas, líquidos, etc.)
  - Admite búsqueda difusa para nombres de objetos
- `/wiki <término de búsqueda>` - Busca y muestra información de la Wiki oficial de Terraria
  - Muestra descripciones de objetos
  - Proporciona acceso rápido a información del juego
  - Admite coincidencias parciales

## Instalación

1. Descarga la última versión desde la página de lanzamientos
2. Coloca `TerraGuide.dll` en la carpeta `ServerPlugins` de tu servidor
3. Reinicia tu servidor TShock

## Comandos

| Comando   | Permiso          | Descripción                                  |
| --------- | ---------------- | -------------------------------------------- |
| `/recipe` | `terraguide.use` | Muestra información sobre la fabricación de objetos |
| `/wiki`   | `terraguide.use` | Busca y muestra información de la wiki       |

## Permisos

| Permiso          | Descripción                                            |
| ---------------- | ------------------------------------------------------ |
| `terraguide.use` | Permite el uso de los comandos `/recipe` y `/wiki`     |

## Configuración

¡No se necesita configuración! Simplemente instala y usa.

## Construcción desde el código fuente

1. Clona el repositorio
2. Abre la solución en Visual Studio
3. Restaura los paquetes NuGet
4. Construye la solución

## Dependencias

- TShock 5.0 o posterior
- .NET Framework 4.7.2 o posterior

## Contribuciones

¡Las pull requests son bienvenidas! Para cambios importantes, por favor, abre un "issue" primero para discutir qué te gustaría cambiar.

## Autor

jgranserver

## Créditos

- Equipo de TShock por el increíble mod de servidor.
- Colaboradores de la Wiki de Terraria por la información de recetas y objetos.
- [FrankV22](github.com/itsFrankV22) por soporte a español.

**README** `v1`
