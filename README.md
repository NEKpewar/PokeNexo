# Project PokeNexo
![License](https://img.shields.io/badge/License-AGPLv3-blue.svg)
![GitHub Downloads (all assets, all releases)](https://img.shields.io/github/downloads/Daiivr/PokeNexo/total?style=flat-square&logoColor=Red&color=red)

![image](https://i.imgur.com/MCGxVgY.png)

# Screenshots
![sysbot](https://i.imgur.com/N9VZNHT.gif)



# Panel de Control de PokeNexo
- **Panel de Control Local**  
Controla todos tus bots con un panel de control fácil de usar a través de `http://localhost:8080` en tu máquina anfitriona.

## Panel de Control
<img width="1633" height="641" alt="image" src="https://github.com/user-attachments/assets/9e67d6d6-273a-4e2c-bf0e-ff7eb38b5ca8" />
<img width="1642" height="1095" alt="image" src="https://github.com/user-attachments/assets/762e41ce-0d66-4376-9019-9530a9360d80" />

## Control Remoto
Controla tus consolas directamente desde el centro de control. Simplemente abre la ventana de Control Remoto, selecciona la IP de la consola que deseas manejar ¡y comienza a usar los controles!
<img width="1405" height="1151" alt="image" src="https://github.com/user-attachments/assets/d92647c4-e177-4e19-97b2-34cfd26bb77e" />

## Visor de Registros
¡Visualiza los registros directamente desde el centro de control! Busca errores, usuarios ¡y mucho más!
<img width="1410" height="1160" alt="image" src="https://github.com/user-attachments/assets/aaf823a9-6709-49e8-8a82-52f6865cbf49" />

## Retroalimentación en Tiempo Real
¡Controla todos tus programas con un solo clic! Pon todos en espera, detén todos, inicia todos, enciende/apaga todas las pantallas de tus consolas al mismo tiempo.
<img width="1037" height="640" alt="image" src="https://github.com/user-attachments/assets/42dd0998-a759-4739-b2c7-ba96d65124a9" />

- **Actualizaciones Automáticas**
Actualiza tus bots con un solo clic para mantenerte siempre al día con las últimas versiones de PKHeX/ALM.
<img width="712" height="875" alt="image" src="https://github.com/user-attachments/assets/7fd0215b-c9a4-4d15-ac52-fb9d6a8de27c" />

# 📱 Accede a PokeBot desde cualquier dispositivo en tu red

## Configuración Rápida

### 1. Habilita el Acceso a la Red (elige una opción):
- **Opción A:** Haz clic derecho en PokeNexo.exe → Ejecutar como Administrador  
- **Opción B:** Ejecuta en cmd como administrador: `netsh http add urlacl url=http://+:8080/ user=Everyone`

### 2. Permitir a través del Firewall:
Ejecuta en cmd como administrador:
```cmd
netsh advfirewall firewall add rule name="PokeNexo Web" dir=in action=allow protocol=TCP localport=8080
```

### 3. Conéctate desde tu Teléfono:
- Obtén la IP de tu PC: ipconfig (busca Dirección IPv4)
- En tu teléfono: http://TU-IP-DE-PC:8080
- Ejemplo: http://192.168.1.100:8080

## Requisitos
- Misma red WiFi
- Regla en el Firewall de Windows (paso 2)
- Permisos de administrador (solo la primera vez)


---

# Otras Funciones del Programa

- Búsqueda en vivo de registros desde la pestaña de Registros. Busca cualquier cosa y encuentra resultados rápidamente.

![image](https://i.imgur.com/2O4xS6s.png)

- Soporte de Bandeja - Cuando presionas X para cerrar el programa, este se minimiza a la bandeja del sistema. Haz clic derecho en el ícono de PokeBot en la bandeja para salir o controlar el bot.

![image](https://i.imgur.com/1P0KMxp.png)

# Comandos del Bot de Intercambio de Pokémon

## Comandos Básicos de Intercambio

| Comando | Alias | Descripción | Uso | Permisos |
|---------|-------|-------------|-----|----------|
| `trade` | `t` | Intercambia un Pokémon desde un set de Showdown o archivo | `.trade [código] <showdown_set>` o adjuntar archivo | Rol Trade |
| `hidetrade` | `ht` | Intercambio sin mostrar detalles en el embed | `.hidetrade [código] <showdown_set>` o adjuntar archivo | Rol Trade |
| `batchTrade` | `bt` | Intercambia múltiples Pokémon (máx 3) | `.bt <sets_separados_por_--->` | Rol Trade |
| `egg` | - | Intercambia un huevo a partir del nombre del Pokémon | `.egg [código] <nombre_pokémon>` | Rol Trade |

## Comandos Especializados de Intercambio

| Comando | Alias | Descripción | Uso | Permisos |
|---------|-------|-------------|-----|----------|
| `dittoTrade` | `dt`, `ditto` | Intercambia un Ditto con estadísticas/naturaleza específicas | `.dt [código] <stats> <idioma> <naturaleza>` | Público |
| `itemTrade` | `it`, `item` | Intercambia un Pokémon con el objeto solicitado | `.it [código] <nombre_objeto>` | Público |
| `mysteryegg` | `me` | Intercambia un huevo aleatorio con IVs perfectos | `.me [código]` | Público |
| `mysterymon` | `mm` | Intercambia un Pokémon aleatorio con estadísticas perfectas | `.mm [código]` | Rol Trade |

## Comandos de Reparación y Clonado

| Comando | Alias | Descripción | Uso | Permisos |
|---------|-------|-------------|-----|----------|
| `fixOT` | `fix`, `f` | Corrige OT/apodo si se detecta publicidad | `.fix [código]` | Rol FixOT |
| `clone` | `c` | Clona el Pokémon que muestres | `.clone [código]` | Rol Clone |
| `dump` | `d` | Descarga el Pokémon que muestres | `.dump [código]` | Rol Dump |

## Comandos de Eventos y Competitivos

| Comando | Alias | Descripción | Uso | Permisos |
|---------|-------|-------------|-----|----------|
| `listevents` | `le` | Lista los archivos de eventos disponibles | `.le [filtro] [páginaX]` | Público |
| `eventrequest` | `er` | Solicita un evento específico por índice | `.er <índice>` | Rol Trade |
| `battlereadylist` | `brl` | Lista los archivos competitivos disponibles | `.brl [filtro] [páginaX]` | Público |
| `battlereadyrequest` | `brr`, `br` | Solicita un archivo competitivo por índice | `.brr <índice>` | Rol Trade |
| `specialrequestpokemon` | `srp` | Lista/solicita eventos wondercard | `.srp <gen> [filtro] [páginaX]` o `.srp <gen> <índice>` | Público/Rol Trade |
| `geteventpokemon` | `gep` | Descarga un evento como archivo pk | `.gep <gen> <índice> [idioma]` | Público |

## Comandos de Cola y Estado

| Comando | Alias | Descripción | Uso | Permisos |
|---------|-------|-------------|-----|----------|
| `tradeList` | `tl` | Muestra usuarios en la cola de intercambio | `.tl` | Admin |
| `fixOTList` | `fl`, `fq` | Muestra usuarios en la cola de FixOT | `.fl` | Admin |
| `cloneList` | `cl`, `cq` | Muestra usuarios en la cola de clonado | `.cl` | Admin |
| `dumpList` | `dl`, `dq` | Muestra usuarios en la cola de dump | `.dl` | Admin |
| `medals` | `ml` | Muestra tu cantidad de trades y medallas | `.ml` | Público |

## Comandos de Administración

| Comando | Alias | Descripción | Uso | Permisos |
|---------|-------|-------------|-----|----------|
| `tradeUser` | `tu`, `tradeOther` | Intercambia un archivo al usuario mencionado | `.tu [código] @usuario` + adjuntar archivo | Admin |

## Notas de Uso

- **Parámetro Código**: Código de intercambio opcional (8 dígitos). Si no se proporciona, se genera un código aleatorio.
- **Intercambios en Lote**: Separa múltiples sets con `---` en intercambios por lote.
- **Soporte de Archivos**: Los comandos aceptan tanto sets de Showdown como archivos .pk adjuntos.
- **Permisos**: Diferentes comandos requieren distintos roles de Discord para acceder.
- **Idiomas**: Los idiomas soportados para eventos incluyen EN, JA, FR, DE, ES, IT, KO, ZH.

## Juegos Compatibles

- Sword/Shield (SWSH)
- Brilliant Diamond/Shining Pearl (BDSP) 
- Leyendas Arceus (PLA)
- Escarlata/Púrpura (SV)
- Let's Go Pikachu/Eevee (LGPE)
  

# Licencia
Consulta el archivo `License.md` para más detalles sobre la licencia.
