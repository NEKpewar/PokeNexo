using PKHeX.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace SysBot.Pokemon;

public class LegalitySettings
{
    private const string Generate = nameof(Generate);

    private const string Misc = nameof(Misc);

    private string DefaultTrainerName = "Dai";

    [Category(Generate), Description("Nombre de entrenador original predeterminado para archivos PKM que no coinciden con ninguno de los archivos PKM proporcionados."), DisplayName("OT:")]
    public string GenerateOT
    {
        get => DefaultTrainerName;
        set
        {
            if (!StringsUtil.IsSpammyString(value))
                DefaultTrainerName = value;
        }
    }

    [Category(Generate), Description("ID secreto (SID) predeterminado de 16 bits para solicitudes que no coinciden con ninguno de los archivos de datos del entrenador proporcionados. Este debería ser un número de 5 dígitos."), DisplayName("SID:")]
    public ushort GenerateSID16 { get; set; } = 54321;

    [Category(Generate), Description("ID de entrenador (TID) predeterminado de 16 bits para solicitudes que no coinciden con ninguno de los archivos de datos del entrenador proporcionados. Este debería ser un número de 5 dígitos."), DisplayName("TID:")]
    public ushort GenerateTID16 { get; set; } = 12345;

    [Category(Generate), Description("Idioma predeterminado para archivos PKM que no coinciden con ninguno de los archivos PKM proporcionados."), DisplayName("Idioma:")]
    public LanguageID GenerateLanguage { get; set; } = LanguageID.English;

    [Category(Generate), Description("Carpeta para archivos PKM con datos del entrenador para usar en archivos PKM regenerados.")]
    public string GeneratePathTrainerInfo { get; set; } = string.Empty;

    [Category(Generate), Description("Ruta del directorio MGDB para Wonder Cards."), DisplayName("Ruta de la Carpeta MGDB")]
    public string MGDBPath { get; set; } = string.Empty;

    [Category(Generate), Description("Permita que los usuarios envíen más personalizaciones con los comandos del Editor por lotes.")]
    public bool AllowBatchCommands { get; set; } = true;

    [Category(Generate), Description("Permita a los usuarios enviar OT, TID, SID y OT Gender personalizados en conjuntos de Showdown."), DisplayName("Permitir sobrescribir los datos del entrenador?")]
    public bool AllowTrainerDataOverride { get; set; } = false;

    [Category(Misc), Description("Aplicar Pokémon válidos con los datos de entrenadores OT/SID/TID (Auto OT)"), DisplayName("Utilizar la información del Entrenador?")]
    public bool UseTradePartnerInfo { get; set; } = true;

    [Category(Generate), Description("Evita el intercambio de Pokémon que requieren un HOME Tracker, incluso si el archivo ya tiene uno."), DisplayName("No permitir Pokémon no nativos")]
    public bool DisallowNonNatives { get; set; } = false;

    [Category(Generate), Description("Impide intercambiar Pokémon que ya tienen un HOME Tracker."), DisplayName("No permitir Pokémons con Home Tracker")]
    public bool DisallowTracked { get; set; } = false;

    [Category(Generate), Description("Bot creará un Pokémon Huevo de Pascua si se le proporciona un conjunto ilegal."), DisplayName("Habilitar los Huevos de Pascua?")]
    public bool EnableEasterEggs { get; set; } = false;

    [Category(Generate), Description("Requiere el rastreador HOME al intercambiar Pokémon que debieron haber viajado entre los juegos de Switch."), DisplayName("Habilitar la comprobación del rastreador HOME")]
    public bool EnableHOMETrackerCheck { get; set; } = false;

    [Category(Generate), Description("Supone que los sets de nivel 50 son sets competitivos de nivel 100")]
    public bool ForceLevel100for50 { get; set; } = true;

    [Category(Generate), Description("Fuerza la bola especificada si es legal.")]
    public bool ForceSpecifiedBall { get; set; } = true;

    [Category(Generate), Description("El orden en el que se intentan los tipos de encuentro Pokémon.")]
    public List<EncounterTypeGroup> PrioritizeEncounters { get; set; } =
    [
        EncounterTypeGroup.Slot, EncounterTypeGroup.Egg,
        EncounterTypeGroup.Static, EncounterTypeGroup.Mystery,
        EncounterTypeGroup.Trade,
    ];

    [Category(Generate), Description("Si PrioritizeGame está establecido en \"True\", se usa PriorityOrder para comenzar a buscar encuentros."), DisplayName("Priorizar Juego")]
    public bool PrioritizeGame { get; set; } = false;

    [Category(Generate), Description("El orden de las versiones de juego desde las cuales ALM intentará legalizar.")]
    public List<GameVersion> PriorityOrder { get; set; } =
        [.. Enum.GetValues<GameVersion>().Where(ver => ver > GameVersion.Any && ver <= (GameVersion)51)];

    // Misc
    [Browsable(false)]
    [Category(Misc), Description("Elimine los rastreadores HOME para archivos PKM clonados y solicitados por el usuario. Se recomienda dejar esto deshabilitado para evitar la creación de datos HOME no válidos.")]
    public bool ResetHOMETracker { get; set; } = false;

    [Category(Generate), Description("Establece todas las cintas legales posibles para cualquier Pokémon generado."), DisplayName("Establecer todas las cintas legales")]
    public bool SetAllLegalRibbons { get; set; } = false;

    [Browsable(false)]
    [Category(Generate), Description("Agrega la versión de batalla para juegos que la admiten (solo SWSH) para usar Pokémon de generaciones pasadas en juegos competitivos en línea.")]
    public bool SetBattleVersion { get; set; } = false;

    [Category(Generate), Description("Establece una pokeball del mismo color (según el color) para cualquier Pokémon generado.")]
    public bool SetMatchingBalls { get; set; } = true;

    [Category(Generate), Description("Tiempo máximo en segundos a emplear al generar un conjunto antes de cancelar. Esto evita que los sets difíciles congelen el robot."), DisplayName("Tiempo máximo de espera")]
    public int Timeout { get; set; } = 15;

    public override string ToString() => "Configuración de generación de legalidad";
}
