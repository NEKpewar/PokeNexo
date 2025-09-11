using Discord;
using Discord.Commands;
using Discord.Net;
using Discord.WebSocket;
using PKHeX.Core;
using PKHeX.Core.AutoMod;
using SysBot.Base;
using SysBot.Pokemon.TradeModule;
using SysBot.Pokemon.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static SysBot.Pokemon.TradeSettings.TradeSettingsCategory;

namespace SysBot.Pokemon.Discord;

public static class Helpers<T> where T : PKM, new()
{
    private static TradeQueueInfo<T> Info => SysCord<T>.Runner.Hub.Queues.Info;

    public static Task<bool> EnsureUserNotInQueueAsync(ulong userID, int deleteDelay = 2)
    {
        // Si el usuario ya está en la cola, devuelve false → bloquea nuevas solicitudes
        if (Info.IsUserInQueue(userID))
            return Task.FromResult(false);

        // Si no está en cola, permite continuar
        return Task.FromResult(true);
    }

    public static async Task SendAlreadyInQueueEmbedAsync(SocketCommandContext context)
    {
        var currentTime = DateTime.UtcNow;
        var formattedTime = currentTime.ToString("hh:mm tt");

        var queueEmbed = new EmbedBuilder
        {
            Color = Color.Red,
            ImageUrl = "https://c.tenor.com/rDzirQgBPwcAAAAd/tenor.gif",
            ThumbnailUrl = "https://i.imgur.com/DWLEXyu.png"
        };

        queueEmbed.WithAuthor("Error al intentar agregarte a la lista", "https://i.imgur.com/0R7Yvok.gif");

        // Igual que tu ejemplo original
        queueEmbed.AddField("__**Error**__:", $"❌ {context.User.Mention} No pude agregarte a la cola", true);
        queueEmbed.AddField("__**Razón**__:", "No puedes agregar más operaciones hasta que la actual se procese.", true);
        queueEmbed.AddField("__**Solución**__:", "Espera un poco hasta que la operación existente se termine e intentalo de nuevo.");

        queueEmbed.Footer = new EmbedFooterBuilder
        {
            Text = $"{context.User.Username} • {formattedTime}",
            IconUrl = context.User.GetAvatarUrl() ?? context.User.GetDefaultAvatarUrl()
        };

        await context.Channel.SendMessageAsync(embed: queueEmbed.Build()).ConfigureAwait(false);
    }

    public static async Task ReplyAndDeleteAsync(SocketCommandContext context, string message, int delaySeconds, IMessage? messageToDelete = null)
    {
        try
        {
            var sentMessage = await context.Channel.SendMessageAsync(message).ConfigureAwait(false);
            _ = DeleteMessagesAfterDelayAsync(sentMessage, messageToDelete ?? context.Message, delaySeconds);
        }
        catch (Exception ex)
        {
            LogUtil.LogSafe(ex, nameof(TradeModule<T>));
        }
    }

    public static async Task DeleteMessagesAfterDelayAsync(IMessage? sentMessage, IMessage? messageToDelete, int delaySeconds)
    {
        try
        {
            await Task.Delay(delaySeconds * 1000);

            var tasks = new List<Task>();

            if (sentMessage != null)
                tasks.Add(TryDeleteMessageAsync(sentMessage));

            if (messageToDelete != null)
                tasks.Add(TryDeleteMessageAsync(messageToDelete));

            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            LogUtil.LogSafe(ex, nameof(TradeModule<T>));
        }
    }

    private static async Task TryDeleteMessageAsync(IMessage message)
    {
        try
        {
            await message.DeleteAsync();
        }
        catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.UnknownMessage)
        {
            // Ignore Unknown Message exception
        }
    }

    public static async Task<ProcessedPokemonResult<T>> ProcessShowdownSetAsync(
    SocketCommandContext context, string content, bool ignoreAutoOT = false)
    {
        content = ReusableActions.StripCodeBlock(content);
        bool isEgg = TradeExtensions<T>.IsEggCheck(content);

        if (!ShowdownParsing.TryParseAnyLanguage(content, out ShowdownSet? set) || set == null || set.Species == 0)
        {
            var firstLine = content.Split('\n').FirstOrDefault()?.Trim() ?? "";
            var potentialName = firstLine.Split('@')[0].Trim();
            var nameError = BattleTemplateLegality.VerifyPokemonName(potentialName, (int)LanguageID.English);

            return new ProcessedPokemonResult<T>
            {
                Error = nameError ?? "Unable to parse Showdown set. Could not identify the Pokémon species.",
                ShowdownSet = set
            };
        }

        byte finalLanguage = LanguageHelper.GetFinalLanguage(
            content, set,
            (byte)Info.Hub.Config.Legality.GenerateLanguage,
            TradeExtensions<T>.DetectShowdownLanguage
        );

        var template = AutoLegalityWrapper.GetTemplate(set);

        if (set.InvalidLines.Count != 0)
        {
            var invalidLines = string.Join("\n", set.InvalidLines);
            return new ProcessedPokemonResult<T>
            {
                Error = $"Unable to parse Showdown Set:\n{invalidLines}",
                ShowdownSet = set
            };
        }

        var sav = LanguageHelper.GetTrainerInfoWithLanguage<T>((LanguageID)finalLanguage);
        var pkm = sav.GetLegal(template, out var result);

        if (pkm == null)
        {
            return new ProcessedPokemonResult<T>
            {
                Error = "Set took too long to legalize.",
                ShowdownSet = set
            };
        }

        var la = new LegalityAnalysis(pkm);
        var spec = GameInfo.Strings.Species[template.Species];

        // Handle egg logic
        if (isEgg && pkm is T eggPk)
        {
            ApplyEggLogic(eggPk, content);
            pkm = eggPk;
            la = new LegalityAnalysis(pkm);
        }
        else
        {
            ApplyStandardItemLogic(pkm);
        }

        // Generate LGPE code if needed
        List<Pictocodes>? lgcode = null;
        if (pkm is PB7)
        {
            lgcode = GenerateRandomPictocodes(3);
            if (pkm.Species == (int)Species.Mew && pkm.IsShiny)
            {
                return await Task.FromResult(new ProcessedPokemonResult<T>
                {
                    Error = $"⚠️ Lo siento {context.User.Mention}, Mew **no** puede ser Shiny en LGPE. PoGo Mew no se transfiere y Pokeball Plus Mew tiene shiny lock.",
                    ShowdownSet = set
                });
            }
        }

        if (pkm is not T pk || !la.Valid)
        {
            var reason = GetFailureReason(result, spec, context);
            var hint = result == "Failed" ? GetLegalizationHint(template, sav, pkm, spec) : null;
            return await Task.FromResult(new ProcessedPokemonResult<T>
            {
                Error = reason,
                LegalizationHint = hint,
                ShowdownSet = set
            });
        }

        // Final preparation
        PrepareForTrade(pk, set, finalLanguage);

        // Check for spam names
        if (Info.Hub.Config.Trade.TradeConfiguration.EnableSpamCheck)
        {
            if (TradeExtensions<T>.HasAdName(pk, out string ad))
            {
                return await Task.FromResult(new ProcessedPokemonResult<T>
                {
                    Error = $"❌ Nombre de anuncio detectado en el nombre del Pokémon o en el nombre del entrenador, lo cual no está permitido.",
                    ShowdownSet = set
                });
            }
        }

        var isNonNative = la.EncounterOriginal.Context != pk.Context || pk.GO;

        return await Task.FromResult(new ProcessedPokemonResult<T>
        {
            Pokemon = pk,
            ShowdownSet = set,
            LgCode = lgcode,
            IsNonNative = isNonNative
        });
    }

    public static void ApplyEggLogic(T pk, string content)
    {
        bool versionSpecified = content.Contains(".Version=", StringComparison.OrdinalIgnoreCase);

        if (!versionSpecified)
        {
            if (pk is PB8 pb8)
                pb8.Version = GameVersion.BD;
            else if (pk is PK8 pk8)
                pk8.Version = GameVersion.SW;
        }

        pk.IsNicknamed = false;
        TradeExtensions<T>.EggTrade(pk, AutoLegalityWrapper.GetTemplate(new ShowdownSet(content)));
    }

    public static void ApplyStandardItemLogic(PKM pkm)
    {
        pkm.HeldItem = pkm switch
        {
            PA8 => (int)HeldItem.None,
            _ when pkm.HeldItem == 0 && !pkm.IsEgg => (int)SysCord<T>.Runner.Config.Trade.TradeConfiguration.DefaultHeldItem,
            _ => pkm.HeldItem
        };
    }

    public static void PrepareForTrade(T pk, ShowdownSet set, byte finalLanguage)
    {
        if (pk.WasEgg)
            pk.EggMetDate = pk.MetDate;

        pk.Language = finalLanguage;

        if (!set.Nickname.Equals(pk.Nickname) && string.IsNullOrEmpty(set.Nickname))
            pk.ClearNickname();

        pk.ResetPartyStats();
    }

    public static string GetFailureReason(string result, string speciesName, SocketCommandContext context)
    {
        return result switch
        {
            "Timeout" => $"El **{speciesName}** tardó demasiado en generarse y se canceló.",
            "VersionMismatch" => "❌ **Solicitud denegada:** La versión de **PKHeX** y **Auto-Legality Mod** no coinciden.",
            _ => $"{context.User.Mention}, no se pudo crear un **{speciesName}** con los datos proporcionados."
        };
    }

    public static string GetLegalizationHint(IBattleTemplate template, ITrainerInfo sav, PKM pkm, string speciesName)
    {
        // Use your custom SetAnalysis extension in BattleTemplateLegality.cs
        var hint = template.SetAnalysis(sav, pkm);

        // Special case: shiny request not possible
        if (hint.Contains("ShinyType"))
        {
            hint = $"### __Error__\n- **{speciesName}** no puede ser shiny.\n\n```📝Solución:\n• Elimina (Shiny: Yes) del conjunto o usa un Pokémon que pueda ser shiny legalmente.```";
        }

        return hint;
    }

    public static async Task SendTradeErrorEmbedAsync(SocketCommandContext context, ProcessedPokemonResult<T> result)
    {
        string spec = (result.ShowdownSet != null && result.ShowdownSet.Species > 0)
            ? GameInfo.Strings.Species[result.ShowdownSet.Species]
            : "Desconocido";

        string formattedTime = DateTime.UtcNow.ToString("hh:mm tt");

        var embed = new EmbedBuilder()
            .WithColor(Color.Red)
            .WithImageUrl("https://i.imgur.com/Y64hLzW.gif")
            .WithThumbnailUrl("https://i.imgur.com/DWLEXyu.png")
            .WithAuthor("Error al generar el Pokemon", "https://img.freepik.com/free-icon/warning_318-478601.jpg")
            .WithFooter(f =>
            {
                f.Text = $"{context.User.Username} • {formattedTime}";
                f.IconUrl = context.User.GetAvatarUrl() ?? context.User.GetDefaultAvatarUrl();
            });

        // ── Caso 1: BattleTemplateLegality (LegalizationHint) → mostrar tal cual
        if (!string.IsNullOrWhiteSpace(result.LegalizationHint))
        {
            embed
                .WithDescription($"No se pudo crear **{spec}**.\n\n{result.LegalizationHint}");

            // Si es Shiny lock, agrega el field de solución
            if (ErrorTranslator.IsShinyLockText(result.LegalizationHint))
                embed.AddField("__Posible solución__", "✔ Quita `Shiny: Yes` del set o solicita otro Pokémon.", false);

            var sentBL = await context.Channel
                .SendMessageAsync(text: context.User.Mention, embed: embed.Build())
                .ConfigureAwait(false);

            _ = DeleteMessagesAfterDelayAsync(sentBL, context.Message, 30);
            return;
        }

        // ── Caso 2: error con formato BattleTemplate (###, ``` o ParseError) → traducir amigable + solución
        string raw = result.Error?.ToString() ?? "Error desconocido.";
        if (IsBattleTemplateFormatted(raw))
        {
            (string friendly, string? solution) = ErrorTranslator.TranslateALMError(raw);

            embed
                .WithDescription($"No se pudo crear **{(spec == "Desconocido" ? "el Pokémon solicitado" : spec)}**.\n\n{friendly}");

            // Si detectamos Shiny lock en el texto, forzamos solución homogénea
            if (ErrorTranslator.IsShinyLockText(raw) && string.IsNullOrWhiteSpace(solution))
                solution = "✔ Quita `Shiny: Yes` del set o solicita otro Pokémon.";

            if (!string.IsNullOrWhiteSpace(solution))
                embed.AddField("__Posible solución__", solution, false);

            var sentBT = await context.Channel
                .SendMessageAsync(text: context.User.Mention, embed: embed.Build())
                .ConfigureAwait(false);

            _ = DeleteMessagesAfterDelayAsync(sentBT, context.Message, 30);
            return;
        }

        // ── Caso 3: parser/otros → versión amigable + solución
        (string friendlyDefault, string? solutionDefault) = ErrorTranslator.TranslateALMError(raw);

        if (ErrorTranslator.IsShinyLockText(raw))
            solutionDefault = "✔ Quita `Shiny: Yes` del set o solicita otro Pokémon.";

        embed
            .WithDescription($"No se pudo crear **{(spec == "Desconocido" ? "el Pokémon solicitado" : spec)}**.")
            .AddField("__Razón__", friendlyDefault, false);

        if (!string.IsNullOrWhiteSpace(solutionDefault))
            embed.AddField("__Posible solución__", solutionDefault, false);

        // Líneas inválidas (si las hubo)
        if (result.ShowdownSet is { InvalidLines.Count: > 0 })
        {
            var invalidLines = string.Join(
                "\n",
                result.ShowdownSet.InvalidLines.Select(x => x.ToString().Trim())
            );
            embed.AddField("__Líneas inválidas__", $"```\n{invalidLines}\n```", false);
        }

        var sent = await context.Channel
            .SendMessageAsync(text: context.User.Mention, embed: embed.Build())
            .ConfigureAwait(false);

        _ = DeleteMessagesAfterDelayAsync(sent, context.Message, 30);
    }

    private static bool IsBattleTemplateFormatted(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        return text.StartsWith("###", StringComparison.OrdinalIgnoreCase)
            || text.Contains("```")
            || text.Contains("BattleTemplateParseError", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Unable to parse Showdown Set", StringComparison.OrdinalIgnoreCase);
    }

    public static T? GetRequest(Download<PKM> dl)
    {
        if (!dl.Success)
            return null;
        return dl.Data switch
        {
            null => null,
            T pk => pk,
            _ => EntityConverter.ConvertToType(dl.Data, typeof(T), out _) as T,
        };
    }

    public static List<Pictocodes> GenerateRandomPictocodes(int count)
    {
        Random rnd = new();
        List<Pictocodes> randomPictocodes = [];
        Array pictocodeValues = Enum.GetValues<Pictocodes>();

        for (int i = 0; i < count; i++)
        {
            Pictocodes randomPictocode = (Pictocodes)pictocodeValues.GetValue(rnd.Next(pictocodeValues.Length))!;
            randomPictocodes.Add(randomPictocode);
        }

        return randomPictocodes;
    }

    public static async Task<T?> ProcessTradeAttachmentAsync(SocketCommandContext context)
    {
        var attachment = context.Message.Attachments.FirstOrDefault();
        if (attachment == default)
        {
            await context.Channel.SendMessageAsync($"⚠️ {context.User.Mention}, no se ha proporcionado ningún archivo adjunto. ¡Por favor, inténtalo de nuevo!").ConfigureAwait(false);
            return null;
        }

        var att = await NetUtil.DownloadPKMAsync(attachment).ConfigureAwait(false);
        var pk = GetRequest(att);

        if (pk == null)
        {
            await context.Channel.SendMessageAsync($"⚠️ {context.User.Mention}, ¡el archivo adjunto proporcionado no es compatible con este módulo!").ConfigureAwait(false);
            return null;
        }

        return pk;
    }

    public static (string filter, int page) ParseListArguments(string args)
    {
        string filter = "";
        int page = 1;
        var parts = args.Split([' '], StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length > 0)
        {
            if (int.TryParse(parts.Last(), out int parsedPage))
            {
                page = parsedPage;
                filter = string.Join(" ", parts.Take(parts.Length - 1));
            }
            else
            {
                filter = string.Join(" ", parts);
            }
        }

        return (filter, page);
    }

    public static async Task AddTradeToQueueAsync(SocketCommandContext context, int code, string trainerName, T? pk, RequestSignificance sig,
        SocketUser usr, bool isBatchTrade = false, int batchTradeNumber = 1, int totalBatchTrades = 1,
        bool isHiddenTrade = false, bool isMysteryTrade = false, bool isMysteryEgg = false, List<Pictocodes>? lgcode = null,
        PokeTradeType tradeType = PokeTradeType.Specific, bool ignoreAutoOT = false, bool setEdited = false,
        bool isNonNative = false)
    {
        lgcode ??= GenerateRandomPictocodes(3);

        if (pk is not null && !pk.CanBeTraded())
        {
            var errorMessage = $"❌ {usr.Mention} revisa el conjunto enviado, algun dato esta bloqueando el intercambio.\n\n```📝Soluciones:\n• Revisa detenidamente cada detalle del conjunto y vuelve a intentarlo!```";
            var errorEmbed = new EmbedBuilder
            {
                Description = errorMessage,
                Color = Color.Red,
                ImageUrl = "https://media.tenor.com/vjgjHDFwyOgAAAAM/pysduck-confused.gif",
                ThumbnailUrl = "https://i.imgur.com/DWLEXyu.png"
            };

            errorEmbed.WithAuthor("Error al crear conjunto!", "https://img.freepik.com/free-icon/warning_318-478601.jpg")
                 .WithFooter(footer =>
                 {
                     footer.Text = $"{context.User.Username} • {DateTime.UtcNow.ToString("hh:mm tt")}";
                     footer.IconUrl = context.User.GetAvatarUrl() ?? context.User.GetDefaultAvatarUrl();
                 });

            var reply = await context.Channel.SendMessageAsync(embed: errorEmbed.Build()).ConfigureAwait(false);
            await Task.Delay(6000).ConfigureAwait(false); // Delay for 6 seconds
            await reply.DeleteAsync().ConfigureAwait(false);
            return;
        }

        var la = new LegalityAnalysis(pk!);

        if (!la.Valid)
        {
            string legalityReport = la.Report(verbose: false);
            var customIconUrl = "https://img.freepik.com/free-icon/warning_318-478601.jpg"; // Custom icon URL for the embed title
            var embedBuilder = new EmbedBuilder(); // Crear el objeto EmbedBuilder
            embedBuilder.WithColor(Color.Red); // Opcional: establecer el color del embed

            if (pk?.IsEgg == true)
            {
                string speciesName = SpeciesName.GetSpeciesName(pk.Species, (int)LanguageID.English);
                embedBuilder.WithAuthor("Conjunto de showdown no válido!", customIconUrl);
                embedBuilder.WithDescription($"❌ {usr.Mention} El conjunto de showdown __no es válido__ para un huevo de **{speciesName}**.");
                embedBuilder.AddField("__**Error**__", $"Puede que __**{speciesName}**__ no se pueda obtener en un huevo o algún dato esté impidiendo el trade.", inline: true);
                embedBuilder.AddField("__**Solución**__", $"Revisa tu __información__ y vuelve a intentarlo.", inline: true);
                embedBuilder.AddField("Reporte:", $"\n```{la.Report()}```");
            }
            else
            {
                string speciesName = SpeciesName.GetSpeciesName(pk!.Species, (int)LanguageID.English);
                embedBuilder.WithAuthor("Archivo adjunto no valido!", customIconUrl);
                embedBuilder.WithDescription($"❌ {usr.Mention}, este **{speciesName}** no es nativo de este juego y no se puede intercambiar!\n### He aquí la razón:\n```{legalityReport}```\n```🔊Consejo:\n• Por favor verifica detenidamente la informacion en PKHeX e intentalo de nuevo!\n• Puedes utilizar el plugin de ALM para legalizar tus pokemons y ahorrarte estos problemas.```");
            }
            embedBuilder.WithThumbnailUrl("https://i.imgur.com/DWLEXyu.png");
            embedBuilder.WithImageUrl("https://usagif.com/wp-content/uploads/gify/37-pikachu-usagif.gif");
            // Añadir el footer con icono y texto
            embedBuilder.WithFooter(footer =>
            {
                footer.WithIconUrl(context.User.GetAvatarUrl() ?? context.User.GetDefaultAvatarUrl());
                footer.WithText($"{context.User.Username} | {DateTimeOffset.Now.ToString("hh:mm tt")}");
            });

            var reply = await context.Channel.SendMessageAsync(embed: embedBuilder.Build()).ConfigureAwait(false); // Enviar el embed
            await context.Message.DeleteAsync().ConfigureAwait(false);
            await Task.Delay(10000); // Esperar antes de borrar
            await reply.DeleteAsync().ConfigureAwait(false); // Borrar el mensaje
            return;
        }

        if (Info.Hub.Config.Legality.DisallowNonNatives && isNonNative)
        {
            var customIconUrl = "https://img.freepik.com/free-icon/warning_318-478601.jpg"; // Custom icon URL for the embed title
            var customImageUrl = "https://usagif.com/wp-content/uploads/gify/37-pikachu-usagif.gif"; // Custom image URL for the embed
            var customthumbnail = "https://i.imgur.com/DWLEXyu.png";
            string speciesName = SpeciesName.GetSpeciesName(pk!.Species, (int)LanguageID.English);
            // Allow the owner to prevent trading entities that require a HOME Tracker even if the file has one already.
            var embedBuilder = new EmbedBuilder()
                .WithAuthor("Error al intentar agregarte a la cola.", customIconUrl)
                .WithDescription($"❌ {usr.Mention}, este **{speciesName}** no es nativo de este juego y no se puede intercambiar!")
                .WithColor(Color.Red)
                .WithImageUrl(customImageUrl)
                .WithThumbnailUrl(customthumbnail);

            // Adding footer with user avatar, username, and current time in 12-hour format
            var footerBuilder = new EmbedFooterBuilder()
                .WithIconUrl(context.User.GetAvatarUrl() ?? context.User.GetDefaultAvatarUrl())
                .WithText($"{context.User.Username} | {DateTimeOffset.Now.ToString("hh:mm tt")}"); // "hh:mm tt" formats time in 12-hour format with AM/PM

            embedBuilder.WithFooter(footerBuilder);

            var embed = embedBuilder.Build();

            var reply2 = await context.Channel.SendMessageAsync(embed: embed).ConfigureAwait(false);
            await Task.Delay(10000); // Delay for 20 seconds
            await reply2.DeleteAsync().ConfigureAwait(false);
            return;
        }

        if (Info.Hub.Config.Legality.DisallowTracked && pk is IHomeTrack { HasTracker: true })
        {
            var customIconUrl = "https://img.freepik.com/free-icon/warning_318-478601.jpg"; // Custom icon URL for the embed title
            var customImageUrl = "https://usagif.com/wp-content/uploads/gify/37-pikachu-usagif.gif"; // Custom image URL for the embed
            var customthumbnail = "https://i.imgur.com/DWLEXyu.png";
            string speciesName = SpeciesName.GetSpeciesName(pk.Species, (int)LanguageID.English);
            // Allow the owner to prevent trading entities that already have a HOME Tracker.
            var embedBuilder = new EmbedBuilder()
                .WithAuthor("Error al intentar agregarte a la cola.", customIconUrl)
                .WithDescription($"❌ {usr.Mention}, este archivo de **{speciesName}** ya tiene un **HOME Tracker** y ni puede ser tradeado!")
                .WithColor(Color.Red)
                .WithImageUrl(customImageUrl)
                .WithThumbnailUrl(customthumbnail);

            // Adding footer with user avatar, username, and current time in 12-hour format
            var footerBuilder = new EmbedFooterBuilder()
                .WithIconUrl(context.User.GetAvatarUrl() ?? context.User.GetDefaultAvatarUrl())
                .WithText($"{context.User.Username} | {DateTimeOffset.Now.ToString("hh:mm tt")}"); // "hh:mm tt" formats time in 12-hour format with AM/PM

            var embed1 = embedBuilder.Build();

            var reply1 = await context.Channel.SendMessageAsync(embed: embed1).ConfigureAwait(false);
            await Task.Delay(10000); // Delay for 20 seconds
            await reply1.DeleteAsync().ConfigureAwait(false);
            return;
        }

        // Handle past gen file requests
        if (!la.Valid && la.Results.Any(m => m.Identifier is CheckIdentifier.Memory))
        {
            var clone = (T)pk!.Clone();
            clone.HandlingTrainerName = pk.OriginalTrainerName;
            clone.HandlingTrainerGender = pk.OriginalTrainerGender;
            if (clone is PK8 or PA8 or PB8 or PK9)
                ((dynamic)clone).HandlingTrainerLanguage = (byte)pk.Language;
            clone.CurrentHandler = 1;
            la = new LegalityAnalysis(clone);
            if (la.Valid) pk = clone;
        }

        await QueueHelper<T>.AddToQueueAsync(context, code, trainerName, sig, pk!, PokeRoutineType.LinkTrade,
            tradeType, usr, isBatchTrade, batchTradeNumber, totalBatchTrades, isHiddenTrade, isMysteryEgg, isHiddenTrade,
            lgcode: lgcode, ignoreAutoOT: ignoreAutoOT, setEdited: setEdited, isNonNative: isNonNative).ConfigureAwait(false);
    }

    // --- Lista de especies/variantes Shiny-locked (Showdown names) ---
    private static readonly string[] ShinyLockedShowdownNames = new[]
    {
    "Pikachu-Original", "Pikachu-Hoenn", "Pikachu-Sinnoh", "Pikachu-Unova", "Pikachu-Kalos", "Pikachu-Alola", "Pikachu-World",
    "Victini", "Greninja-Ash", "Vivillon-Pokeball", "Hoopa", "Volcanion", "Cosmog", "Cosmoem",
    "Magearna", "Magearna-Original", "Marshadow", "Melmetal-Gmax", "Kubfu", "Urshifu", "Zarude", "Zarude-Dada",
    "Glastrier", "Spectrier", "Calyrex", "Ursaluna-Bloodmoon", "Koraidon", "Miraidon", "Chi-Yu",
    "Walking Wake", "Iron Leaves", "Okidogi", "Munkidori", "Fezandipiti", "Ogerpon",
    "Gouging Fire", "Raging Bolt", "Iron Boulder", "Iron Crown", "Terapagos", "Pecharunt"
    };

    private static bool IsShinyRequest(string content)
    {
        var u = content.ToUpperInvariant();
        // Soporta variantes típicas
        return u.Contains("SHINY: YES") || u.Contains("SHINY: TRUE") || u.Contains("SHINY YES");
    }

    private static bool IsShinyLockedRequest(string content)
    {
        // Usamos la primera línea (nombre de especie/forma en Showdown)
        var firstLine = (content.Split('\n').FirstOrDefault() ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(firstLine)) return false;

        // Comparación case-insensitive
        return ShinyLockedShowdownNames.Any(name =>
            firstLine.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static bool IsShinyLockText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return text.IndexOf("shiny lock", StringComparison.OrdinalIgnoreCase) >= 0
            || text.IndexOf("shiny-locked", StringComparison.OrdinalIgnoreCase) >= 0
            || text.IndexOf("shiny locked", StringComparison.OrdinalIgnoreCase) >= 0;
    }

}
