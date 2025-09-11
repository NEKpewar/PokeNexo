using Discord;
using Discord.WebSocket;
using PKHeX.Core;
using SysBot.Pokemon.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using static SysBot.Pokemon.TradeSettings;
using static MovesTranslationDictionary;

namespace SysBot.Pokemon.Discord;

/// <summary>
/// Extracts and formats details from Pokémon data for Discord embed displays.
/// </summary>
/// <typeparam name="T">Type of Pokémon data structure.</typeparam>
public static class DetailsExtractor<T> where T : PKM, new()
{
    /// <summary>
    /// Adds additional text to the embed as configured in settings.
    /// </summary>
    /// <param name="embedBuilder">Discord embed builder to modify.</param>
    public static void AddAdditionalText(EmbedBuilder embedBuilder)
    {
        string additionalText = string.Join("\n", SysCordSettings.Settings.AdditionalEmbedText);
        if (!string.IsNullOrEmpty(additionalText))
        {
            embedBuilder.AddField("\u200B", additionalText, inline: false);
        }
    }

    /// <summary>
    /// Adds normal trade information fields to the embed.
    /// </summary>
    /// <param name="embedBuilder">Discord embed builder to modify.</param>
    /// <param name="embedData">Extracted Pokémon data.</param>
    /// <param name="trainerMention">Discord mention for the trainer.</param>
    /// <param name="pk">Pokémon data.</param>
    public static void AddNormalTradeFields(EmbedBuilder embedBuilder, EmbedData embedData, string trainerMention, T pk)
    {
        string leftSideContent = (SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.ShowLevel ? $"**Nivel:** {embedData.Level}\n" : "");
        leftSideContent +=
            (SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.ShowTeraType && !string.IsNullOrWhiteSpace(embedData.TeraType) ? $"**Tera Tipo:** {embedData.TeraType}\n" : "") +
                        (SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.ShowAbility ? $"**Habilidad:** {embedData.Ability}\n" : "") +
            (SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.ShowScale && !string.IsNullOrWhiteSpace(embedData.Scale.Item1) ? $"**Tamaño:** {embedData.Scale.Item1}\n" : "") +
                        (SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.ShowNature ? $"**Naturaleza:** {embedData.Nature}\n" : "") +
            (SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.ShowMetDate ? $"{embedData.MetDate}\n" : "") +
            (SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.ShowLanguage ? $"**Idioma**: {embedData.Language}\n" : "") +
            (SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.ShowIVs ? $"**IVs**: {embedData.IVsDisplay}\n" : "") +
            (SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.ShowEVs && !string.IsNullOrWhiteSpace(embedData.EVsDisplay) ? $"**EVs**: {embedData.EVsDisplay}\n" : "") +
            (SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.ShowGVs && !string.IsNullOrWhiteSpace(embedData.GVsDisplay) ? $"**GVs**: {embedData.GVsDisplay}\n" : "") +
            (SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.ShowAVs && !string.IsNullOrWhiteSpace(embedData.AVsDisplay) ? $"**AVs**: {embedData.AVsDisplay}\n" : "");
        leftSideContent += $"\n{trainerMention}\nAgregado a la cola de tradeo.";

        leftSideContent = leftSideContent.TrimEnd('\n');
        string shinySymbol = GetShinySymbol(pk);
        embedBuilder.AddField($"**{shinySymbol}{embedData.SpeciesName}{(string.IsNullOrEmpty(embedData.FormName) ? "" : $"-{embedData.FormName}")} {embedData.SpecialSymbols}**", leftSideContent, inline: true);
        embedBuilder.AddField("\u200B", "\u200B", inline: true); // Spacer
        embedBuilder.AddField("**Movimientos:**", embedData.MovesDisplay, inline: true);
    }

    /// <summary>
    /// Adds special trade information fields to the embed.
    /// </summary>
    /// <param name="embedBuilder">Discord embed builder to modify.</param>
    /// <param name="isMysteryEgg">Whether this is a mystery egg trade.</param>
    /// /// <param name="isMysteryMon">Whether this is a mystery trade.</param>
    /// <param name="isSpecialRequest">Whether this is a special request trade.</param>
    /// <param name="isCloneRequest">Whether this is a clone request trade.</param>
    /// <param name="isFixOTRequest">Whether this is a fix OT request trade.</param>
    /// <param name="trainerMention">Discord mention for the trainer.</param>
    public static void AddSpecialTradeFields(EmbedBuilder embedBuilder, bool isMysteryTrade, bool isMysteryEgg, bool isSpecialRequest, bool isCloneRequest, bool isFixOTRequest, string trainerMention)
    {
        string specialDescription = $"**Entrenador:** {trainerMention}\n";

        if (isMysteryTrade)
        {
            specialDescription += "Pokemon Misterioso";
        }
        else if (isMysteryEgg)
        {
            specialDescription += "Huevo Misterioso";
        }
        else if (isSpecialRequest)
        {
            specialDescription += "Solicitud Especial";
        }
        else if (isCloneRequest)
        {
            specialDescription += "Solicitud de clonación";
        }
        else if (isFixOTRequest)
        {
            specialDescription += "Solicitud de FixOT";
        }
        else
        {
            specialDescription += "Solicitud de Dump";
        }

        embedBuilder.AddField("\u200B", specialDescription, inline: false);
    }

    /// <summary>
    /// Adds thumbnails to the embed based on trade type.
    /// </summary>
    /// <param name="embedBuilder">Discord embed builder to modify.</param>
    /// <param name="isCloneRequest">Whether this is a clone request trade.</param>
    /// <param name="isSpecialRequest">Whether this is a special request trade.</param>
    /// <param name="heldItemUrl">URL for the held item image.</param>
    public static void AddThumbnails(EmbedBuilder embedBuilder, bool isCloneRequest, bool isSpecialRequest, bool isDumpRequest, bool isFixOTRequest, string heldItemUrl, T pk, PokeTradeType tradeType)
    {
        if (isCloneRequest || isSpecialRequest || isDumpRequest || isFixOTRequest)
        {
            embedBuilder.WithThumbnailUrl("https://raw.githubusercontent.com/hexbyt3/sprites/main/profoak.png");
        }
        else if (tradeType == PokeTradeType.Item)
        {
            // Usa la imagen del Pokémon como thumbnail cuando el tipo de intercambio es 'Item'
            var speciesImageUrl = TradeExtensions<T>.PokeImg(pk, false, true, null); // Asume que tienes acceso a 'pk' aquí
            embedBuilder.WithThumbnailUrl(speciesImageUrl);
        }
        else if (!string.IsNullOrEmpty(heldItemUrl))
        {
            embedBuilder.WithThumbnailUrl(heldItemUrl);
        }
    }

    /// <summary>
    /// Extracts detailed information from a Pokémon for display.
    /// </summary>
    /// <param name="pk">Pokémon data.</param>
    /// <param name="user">Discord user initiating the trade.</param>
    /// <param name="isMysteryEgg">Whether this is a mystery egg trade.</param>
    /// /// <param name="isMysteryMon">Whether this is a mystery trade.</param>
    /// <param name="isCloneRequest">Whether this is a clone request trade.</param>
    /// <param name="isDumpRequest">Whether this is a dump request trade.</param>
    /// <param name="isFixOTRequest">Whether this is a fix OT request trade.</param>
    /// <param name="isSpecialRequest">Whether this is a special request trade.</param>
    /// <param name="isBatchTrade">Whether this is part of a batch trade.</param>
    /// <param name="batchTradeNumber">The number of this trade in the batch sequence.</param>
    /// <param name="totalBatchTrades">Total number of trades in the batch.</param>
    /// <returns>Structured Pokémon data for embed display.</returns>
    public static EmbedData ExtractPokemonDetails(T pk, SocketUser user, bool isMysteryTrade, bool isMysteryEgg, bool isCloneRequest, bool isDumpRequest, bool isFixOTRequest, bool isSpecialRequest, bool isBatchTrade, int batchTradeNumber, int totalBatchTrades, PokeTradeType type)
    {
        string langCode = ((LanguageID)pk.Language).GetLanguageCode();
        GameStrings strings = GameInfo.GetStrings(langCode);

        var originalLanguage = GameInfo.CurrentLanguage;
        GameInfo.CurrentLanguage = langCode;

        var embedData = new EmbedData
        {
            // Basic Pokémon details
            Moves = GetMoveNames(pk),
            Level = pk.CurrentLevel
        };

        int languageId = pk.Language;
        string languageDisplay = GetLanguageDisplay(pk);
        embedData.Language = languageDisplay;

        // Pokémon appearance and type details
        if (pk is PK9 pk9)
        {
            embedData.TeraType = GetTeraTypeString(pk9);
            embedData.Scale = GetScaleDetails(pk9);
        }

        // Pokémon identity and special attributes
        embedData.Ability = GetTranslatedAbilityName(pk);
        embedData.Nature = GetTranslatedNatureName(pk);
        embedData.SpeciesName = GameInfo.GetStrings("en").Species[pk.Species];
        embedData.SpecialSymbols = GetSpecialSymbols(pk);
        embedData.FormName = ShowdownParsing.GetStringFromForm(pk.Form, strings, pk.Species, pk.Context);
        embedData.HeldItem = strings.itemlist[pk.HeldItem];
        embedData.Ball = strings.balllist[pk.Ball];

        // Display elements
        Span<int> ivs = stackalloc int[6];
        pk.GetIVs(ivs);
        string ivsDisplay;
        if (ivs.ToArray().All(iv => iv == 31))
        {
            ivsDisplay = "Máximos";
        }
        else
        {
            ivsDisplay = string.Join("/", new[]
              {
                ivs[0].ToString(),
                ivs[1].ToString(),
                ivs[2].ToString(),
                ivs[4].ToString(),
                ivs[5].ToString(),
                ivs[3].ToString()
            });
        }
        embedData.IVsDisplay = ivsDisplay;

        int[] evs = GetEVs(pk);
        embedData.EVsDisplay = string.Join(" / ", new[] {
            (evs[0] != 0 ? $"{evs[0]} HP" : ""),
            (evs[1] != 0 ? $"{evs[1]} Atk" : ""),
            (evs[2] != 0 ? $"{evs[2]} Def" : ""),
            (evs[4] != 0 ? $"{evs[4]} SpA" : ""),
            (evs[5] != 0 ? $"{evs[5]} SpD" : ""),
            (evs[3] != 0 ? $"{evs[3]} Spe" : "") // correct pkhex/ALM ordering of stats
        }.Where(s => !string.IsNullOrEmpty(s)));

        int[] gvs = GetGVs(pk);
        embedData.GVsDisplay = string.Join(" / ", new[] {
            (gvs[0] != 0 ? $"{gvs[0]} HP" : ""),
            (gvs[1] != 0 ? $"{gvs[1]} Atk" : ""),
            (gvs[2] != 0 ? $"{gvs[2]} Def" : ""),
            (gvs[4] != 0 ? $"{gvs[4]} SpA" : ""),
            (gvs[5] != 0 ? $"{gvs[5]} SpD" : ""),
            (gvs[3] != 0 ? $"{gvs[3]} Spe" : "") // correct pkhex/ALM ordering of stats
        }.Where(s => !string.IsNullOrEmpty(s)));

        int[] avs = GetAVs(pk);
        embedData.AVsDisplay = string.Join(" / ", new[] {
            (avs[0] != 0 ? $"{avs[0]} HP" : ""),
            (avs[1] != 0 ? $"{avs[1]} Atk" : ""),
            (avs[2] != 0 ? $"{avs[2]} Def" : ""),
            (avs[4] != 0 ? $"{avs[4]} SpA" : ""),
            (avs[5] != 0 ? $"{avs[5]} SpD" : ""),
            (avs[3] != 0 ? $"{avs[3]} Spe" : "") // correct pkhex/ALM ordering of stats
        }.Where(s => !string.IsNullOrEmpty(s)));

        if (pk.IsEgg)
        {
            // Para huevos usa la fecha del sistema como marcador
            embedData.MetDate = "**Obtenido:** " + DateTime.Now.ToString("dd/MM/yyyy");
        }
        else if (pk.FatefulEncounter)
        {
            embedData.MetDate = "**Obtenido:** " + pk.MetDate.ToString();
        }
        else
        {
            embedData.MetDate = "**Atrapado:** " + pk.MetDate.ToString();
        }
        embedData.MovesDisplay = string.Join("\n", embedData.Moves);
        embedData.PokemonDisplayName = pk.IsNicknamed ? pk.Nickname : embedData.SpeciesName;

        // Trade title
        embedData.TradeTitle = GetTradeTitle(isMysteryTrade, isMysteryEgg, isCloneRequest, isDumpRequest, isFixOTRequest, isSpecialRequest, isBatchTrade, batchTradeNumber, embedData.PokemonDisplayName, pk.IsShiny);

        // Author name
#pragma warning disable CS8604 // Possible null reference argument.
        embedData.AuthorName = GetAuthorName(user.Username, user.GlobalName, embedData.TradeTitle, isMysteryTrade, isMysteryEgg, isFixOTRequest, isCloneRequest, isDumpRequest, isSpecialRequest, isBatchTrade, embedData.NickDisplay, pk.IsShiny, type);
#pragma warning restore CS8604 // Possible null reference argument.

        return embedData;
    }

    /// <summary>
    /// Gets user details for display.
    /// </summary>
    /// <param name="totalTradeCount">Total number of trades for this user.</param>
    /// <param name="tradeDetails">Trade code details if available.</param>
    /// <returns>Formatted user details string.</returns>
    public static string GetUserDetails(int totalTradeCount, TradeCodeStorage.TradeCodeDetails? tradeDetails)
    {
        string userDetailsText = "";
        if (SysCord<T>.Runner.Config.Trade.TradeConfiguration.StoreTradeCodes && tradeDetails != null)
        {
            if (!string.IsNullOrEmpty(tradeDetails?.OT))
            {
                userDetailsText += $"OT: {tradeDetails?.OT}";
            }
            if (tradeDetails?.TID != null)
            {
                userDetailsText += $" | SID: {tradeDetails?.SID}";
            }
            if (tradeDetails?.TID != null)
            {
                userDetailsText += $" | TID: {tradeDetails?.TID}";
            }
        }
        return userDetailsText;
    }

    private static string GetLanguageDisplay(T pk)
    {
        int safeLanguage = (int)Language.GetSafeLanguage(pk.Generation, (LanguageID)pk.Language, (GameVersion)pk.Version);

        string languageName = "Unknown";
        var languageList = GameInfo.LanguageDataSource(pk.Format);
        var languageEntry = languageList.FirstOrDefault(l => l.Value == pk.Language);

        if (languageEntry != null)
        {
            languageName = languageEntry.Text;
        }
        else
        {
            languageName = ((LanguageID)pk.Language).GetLanguageCode();
        }

        if (safeLanguage != pk.Language)
        {
            string safeLanguageName = languageList.FirstOrDefault(l => l.Value == safeLanguage)?.Text ?? ((LanguageID)safeLanguage).GetLanguageCode();
            return $"{languageName} (Safe: {safeLanguageName})";
        }

        return languageName;
    }

    private static string GetTranslatedAbilityName(T pk)
    {
        GameStrings strings = GameInfo.GetStrings("en"); // or pk.Language if you want localized ability names
        string abilityName = strings.abilitylist[pk.Ability];
        return AbilityTranslationDictionary.AbilityTranslation.TryGetValue(abilityName, out var translatedName)
            ? translatedName
            : abilityName;
    }

    private static string GetAuthorName(string username, string globalname, string tradeTitle, bool isMysteryTrade, bool isMysteryEgg, bool isFixOTRequest, bool isCloneRequest, bool isDumpRequest, bool isSpecialRequest, bool isBatchTrade, string NickDisplay, bool isShiny, PokeTradeType tradeType)
    {
        string userName = string.IsNullOrEmpty(globalname) ? username : globalname;
        string isPkmShiny = isShiny ? " Shiny" : "";

        // Agregar manejo para el caso de PokeTradeType es Item
        if (tradeType == PokeTradeType.Item)
        {
            return $"Item solicitado por {userName}";
        }

        if (isMysteryTrade || isMysteryEgg || isFixOTRequest || isCloneRequest || isDumpRequest || isSpecialRequest || isBatchTrade)
        {
            return $"{tradeTitle} {username}";
        }
        else
        {
            // Verifica si el Pokémon tiene un apodo para usarlo, de lo contrario mantiene el formato estándar.
            return !string.IsNullOrEmpty(NickDisplay) ?
                   $"{NickDisplay} solicitado por {userName}" :
                   $"Pokémon{isPkmShiny} solicitado por {userName}";
        }
    }

    private static int[] GetEVs(T pk)
    {
        int[] evs = new int[6];
        pk.GetEVs(evs);
        return evs;
    }

    private static int[] GetGVs(T pk)
    {
        if (pk is IGanbaru ganbaru)
        {
            Span<byte> gvs = stackalloc byte[6];
            ganbaru.GetGVs(gvs);
            return gvs.ToArray().Select(x => (int)x).ToArray();
        }
        return new int[6];
    }
    private static int[] GetAVs(T pk)
    {
        if (pk is IAwakened awakened)
        {
            Span<byte> avs = stackalloc byte[6];
            AwakeningUtil.GetAVs(awakened, avs);
            return avs.ToArray().Select(x => (int)x).ToArray();
        }
        return new int[6]; // Default if not applicable
    }

    private static List<string> GetMoveNames(T pk)
    {
        ushort[] moves = new ushort[4];
        pk.GetMoves(moves.AsSpan());
        List<int> movePPs = [pk.Move1_PP, pk.Move2_PP, pk.Move3_PP, pk.Move4_PP];
        var moveNames = new List<string>();

        var typeEmojis = SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.CustomTypeEmojis
            .Where(e => !string.IsNullOrEmpty(e.EmojiCode))
            .ToDictionary(e => (PKHeX.Core.MoveType)e.MoveType, e => $"{e.EmojiCode}");

        for (int i = 0; i < moves.Length; i++)
        {
            if (moves[i] == 0) continue;
            var stringsEN = GameInfo.GetStrings("en"); // keep EN names if you translate later
            string moveName = (moves[i] < stringsEN.movelist.Length) ? stringsEN.movelist[moves[i]] : "Desconocido";
            string translatedMoveName = MovesTranslation.ContainsKey(moveName) ? MovesTranslation[moveName] : moveName;
            byte moveTypeId = MoveInfo.GetType(moves[i], default);
            PKHeX.Core.MoveType moveType = (PKHeX.Core.MoveType)moveTypeId;
            string formattedMove = $"{translatedMoveName}";
            if (SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.MoveTypeEmojis && typeEmojis.TryGetValue(moveType, out var moveEmoji))
            {
                formattedMove = $"{moveEmoji} {formattedMove}";
            }
            moveNames.Add($"\u200B{formattedMove}");
        }

        return moveNames;
    }

    private static string GetTranslatedNatureName(T pk)
    {
        var stringsEN = GameInfo.GetStrings("en"); // use EN for mapping
        string natureName = (int)pk.Nature < stringsEN.natures.Length
            ? stringsEN.natures[(int)pk.Nature]
            : "Unknown";

        return NatureTranslations.TraduccionesNaturalezas.TryGetValue(natureName, out var translatedName)
            ? translatedName
            : natureName;
    }

    private static (string, byte) GetScaleDetails(PK9 pk9)
    {
        string scaleText = $"{PokeSizeDetailedUtil.GetSizeRating(pk9.Scale)}";
        byte scaleNumber = pk9.Scale;

        // Formato inicial que incluye siempre el número de escala
        string scaleTextWithNumber = $"{scaleText} ({scaleNumber})";

        // Aplica los emojis si están habilitados
        if (SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.UseScaleEmojis)
        {
            var scaleXXXSEmoji = SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.ScaleEmojis.ScaleXXXSEmoji.EmojiString;
            var scaleXXXLEmoji = SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.ScaleEmojis.ScaleXXXLEmoji.EmojiString;

            if (scaleText == "XXXS" && !string.IsNullOrEmpty(scaleXXXSEmoji))
            {
                scaleTextWithNumber = $"{scaleXXXSEmoji} {scaleTextWithNumber}";
            }
            else if (scaleText == "XXXL" && !string.IsNullOrEmpty(scaleXXXLEmoji))
            {
                scaleTextWithNumber = $"{scaleXXXLEmoji} {scaleTextWithNumber}";
            }
        }

        // Retorna el texto completo de la escala y el número de escala como un tuple
        return (scaleTextWithNumber, scaleNumber);
    }

    private static string GetShinySymbol(T pk)
    {
        var shinySettings = SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.ShinyEmojis;

        if (pk.ShinyXor == 0)
        {
            string shinySquareEmoji = string.IsNullOrEmpty(shinySettings.ShinySquareEmoji.EmojiString) ? "◼ " : shinySettings.ShinySquareEmoji.EmojiString + " ";
            return shinySquareEmoji;
        }
        else if (pk.IsShiny)
        {
            string shinyNormalEmoji = string.IsNullOrEmpty(shinySettings.ShinyNormalEmoji.EmojiString) ? "★ " : shinySettings.ShinyNormalEmoji.EmojiString + " ";
            return shinyNormalEmoji;
        }
        return string.Empty;
    }

    private static string GetSpecialSymbols(T pk)
    {
        string alphaMarkSymbol = string.Empty;
        string mightyMarkSymbol = string.Empty;
        string markTitle = string.Empty;
        if (pk is IRibbonSetMark9 ribbonSetMark)
        {
            alphaMarkSymbol = ribbonSetMark.RibbonMarkAlpha ? SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.SpecialMarksEmojis.AlphaMarkEmoji.EmojiString + " " : string.Empty;
            mightyMarkSymbol = ribbonSetMark.RibbonMarkMightiest ? SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.SpecialMarksEmojis.MightiestMarkEmoji.EmojiString + " " : string.Empty;
        }
        if (pk is IRibbonIndex ribbonIndex)
        {
            TradeExtensions<T>.HasMark(ribbonIndex, out RibbonIndex result, out markTitle);
        }
        string alphaSymbol = (pk is IAlpha alpha && alpha.IsAlpha) ? SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.SpecialMarksEmojis.AlphaPLAEmoji.EmojiString + " " : string.Empty;
        string GigantamaxSymbol = (pk is IGigantamax gigantamax && gigantamax.CanGigantamax) ? SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.SpecialMarksEmojis.GigantamaxEmoji.EmojiString + " " : string.Empty;
        string genderSymbol = GameInfo.GenderSymbolASCII[pk.Gender];
        string maleEmojiString = SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.GenderEmojis.MaleEmoji.EmojiString;
        string femaleEmojiString = SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.GenderEmojis.FemaleEmoji.EmojiString;
        string displayGender = genderSymbol switch
        {
            "M" => !string.IsNullOrEmpty(maleEmojiString) ? maleEmojiString : "(M) ",
            "F" => !string.IsNullOrEmpty(femaleEmojiString) ? femaleEmojiString : "(F) ",
            _ => ""
        };
        string mysteryGiftEmoji = pk.FatefulEncounter ? SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.SpecialMarksEmojis.MysteryGiftEmoji.EmojiString : "";

        return (!string.IsNullOrEmpty(markTitle) ? $"{markTitle} " : "") + displayGender + alphaSymbol + mightyMarkSymbol + alphaMarkSymbol + GigantamaxSymbol + mysteryGiftEmoji;
    }

    private static string GetTeraTypeString(PK9 pk9)
    {
        // Determinar si el tipo Tera es 'Stellar' o un tipo regular usando el nombre completo del namespace para MoveType
        var isStellar = pk9.TeraTypeOverride == (PKHeX.Core.MoveType)TeraTypeUtil.Stellar || (int)pk9.TeraType == 99;
        var teraType = isStellar ? SysBot.Pokemon.TradeSettings.MoveType.Stellar : (SysBot.Pokemon.TradeSettings.MoveType)pk9.TeraType;
        string teraTypeEmoji = "";
        string teraTypeName = teraType.ToString();  // Convierte el tipo Tera a string usando el enum de TradeSettings

        // Obtener el emoji si está habilitado, especificando el namespace completo para evitar conflictos
        if (SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.UseTeraEmojis)
        {
            var emojiInfo = SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.TeraTypeEmojis.Find(e => e.MoveType == teraType);
            if (emojiInfo != null && !string.IsNullOrEmpty(emojiInfo.EmojiCode))
            {
                teraTypeEmoji = emojiInfo.EmojiCode + " ";  // Añade un espacio después del emoji para separarlo del nombre
            }
        }

        // Utiliza el diccionario de traducciones para obtener la cadena traducida del tipo Tera (si aplica)
        if (TeraTypeDictionaries.TeraTranslations.TryGetValue(teraTypeName, out var translatedType))
        {
            teraTypeName = translatedType;
        }

        // Devuelve la combinación del emoji (si existe) y el nombre traducido del tipo Tera
        return teraTypeEmoji + teraTypeName;
    }

    private static string GetTradeTitle(bool isMysteryTrade, bool isMysteryEgg, bool isCloneRequest, bool isDumpRequest, bool isFixOTRequest, bool isSpecialRequest, bool isBatchTrade, int batchTradeNumber, string pokemonDisplayName, bool isShiny)
    {
        string shinyEmoji = isShiny ? "✨ " : "";
        return isMysteryTrade ? "✨ Pokemon Misterioso Shiny ✨ de" :
               isMysteryEgg ? "✨ Huevo Misterioso Shiny ✨ de" :
               isBatchTrade ? $"Comercio por lotes #{batchTradeNumber} - {shinyEmoji}{pokemonDisplayName} de" :
               isFixOTRequest ? "Solicitud de FixOT de" :
               isSpecialRequest ? "Solicitud Especial de" :
               isCloneRequest ? "Capsula de Clonación activada para" :
               isDumpRequest ? "Solicitud de Dump de" :
               "";
    }
}

/// <summary>
/// Container for Pokémon data formatted for Discord embed display.
/// </summary>
public class EmbedData
{
    public string? Ability { get; set; }

    public string? AuthorName { get; set; }

    public string? Ball { get; set; }

    public string? EmbedImageUrl { get; set; }

    public string? EVsDisplay { get; set; }

    public string? FormName { get; set; }

    public string? HeldItem { get; set; }

    public string? HeldItemUrl { get; set; }

    public bool IsLocalFile { get; set; }

    public string? IVsDisplay { get; set; }

    public string? GVsDisplay { get; set; }
    public string? AVsDisplay { get; set; }

    public int Level { get; set; }

    public int Tracker { get; set; }

    public string? MetDate { get; set; }

    public List<string>? Moves { get; set; }

    public string? MovesDisplay { get; set; }

    public string? Nature { get; set; }

    public string? PokemonDisplayName { get; set; }

    public string? NickDisplay { get; set; }

    public (string, byte) Scale { get; set; }

    public string? SpecialSymbols { get; set; }

    public string? SpeciesName { get; set; }

    public string? TeraType { get; set; }

    public string? TradeTitle { get; set; }

    /// <summary>Pokémon language.</summary>
    public string? Language { get; set; }
}
