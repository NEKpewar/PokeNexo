using Discord;
using Discord.Commands;
using Discord.WebSocket;
using PKHeX.Core;
using SysBot.Base;
using SysBot.Pokemon.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using System.IO.Compression;
using System.Net.Http;


namespace SysBot.Pokemon.Discord;

[Summary("Pone en cola nuevos intercambios de códigos de enlace")]
public class TradeModule<T> : ModuleBase<SocketCommandContext> where T : PKM, new()
{
    private static TradeQueueInfo<T> Info => SysCord<T>.Runner.Hub.Queues.Info;

    #region Trade Commands

    [Command("trade")]
    [Alias("t")]
    [Summary("Hace que el bot te intercambie un Pokémon convertido del conjunto showdown proporcionado.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public Task TradeAsync([Summary("Showdown Set")][Remainder] string content)
    {
        var userID = Context.User.Id;
        var code = Info.GetRandomTradeCode(userID);
        return ProcessTradeAsync(code, content);
    }

    [Command("trade")]
    [Alias("t")]
    [Summary("Hace que el robot te intercambie un Pokémon convertido del conjunto de showdown proporcionado.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public Task TradeAsync([Summary("Trade Code")] int code, [Summary("Showdown Set")][Remainder] string content)
        => ProcessTradeAsync(code, content);

    [Command("trade")]
    [Alias("t")]
    [Summary("Hace que el bot te intercambie el archivo Pokémon proporcionado.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public Task TradeAsyncAttach([Summary("Trade Code")] int code, [Summary("Ignore AutoOT")] bool ignoreAutoOT = false)
    {
        var sig = Context.User.GetFavor();
        return ProcessTradeAttachmentAsync(code, sig, Context.User, ignoreAutoOT: ignoreAutoOT);
    }

    [Command("trade")]
    [Alias("t")]
    [Summary("Hace que el bot le intercambie el archivo adjunto.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public async Task TradeAsyncAttach([Summary("Ignore AutoOT")] bool ignoreAutoOT = false)
    {
        var userID = Context.User.Id;

        if (!await Helpers<T>.EnsureUserNotInQueueAsync(userID).ConfigureAwait(false))
        {
            await Helpers<T>.SendAlreadyInQueueEmbedAsync(Context).ConfigureAwait(false);
            return;
        }

        var code = Info.GetRandomTradeCode(userID);
        var sig = Context.User.GetFavor();

        await Task.Run(async () =>
        {
            await ProcessTradeAttachmentAsync(code, sig, Context.User, ignoreAutoOT: ignoreAutoOT).ConfigureAwait(false);
        }).ConfigureAwait(false);

        if (Context.Message is IUserMessage userMessage)
            _ = Helpers<T>.DeleteMessagesAfterDelayAsync(userMessage, null, 2);
    }

    [Command("hidetrade")]
    [Alias("ht")]
    [Summary("Hace que el bot te intercambie un Pokémon convertido del conjunto showdown proporcionado sin mostrar los detalles del intercambio.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public Task HideTradeAsync([Summary("Showdown Set")][Remainder] string content)
    {
        var userID = Context.User.Id;
        var code = Info.GetRandomTradeCode(userID);
        return ProcessTradeAsync(code, content, isHiddenTrade: true);
    }

    [Command("hidetrade")]
    [Alias("ht")]
    [Summary("Hace que el bot te intercambie un Pokémon convertido del conjunto de showdown proporcionado sin mostrar los detalles del intercambio.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public Task HideTradeAsync([Summary("Trade Code")] int code, [Summary("Showdown Set")][Remainder] string content)
        => ProcessTradeAsync(code, content, isHiddenTrade: true);

    [Command("hidetrade")]
    [Alias("ht")]
    [Summary("Hace que el bot te intercambie el archivo Pokémon proporcionado sin mostrar los detalles del intercambio.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public Task HideTradeAsyncAttach([Summary("Trade Code")] int code, [Summary("Ignore AutoOT")] bool ignoreAutoOT = false)
    {
        var sig = Context.User.GetFavor();
        return ProcessTradeAttachmentAsync(code, sig, Context.User, isHiddenTrade: true, ignoreAutoOT: ignoreAutoOT);
    }

    [Command("hidetrade")]
    [Alias("ht")]
    [Summary("Hace que el bot le intercambie el archivo adjunto sin mostrar los detalles de la inserción comercial.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public async Task HideTradeAsyncAttach([Summary("Ignore AutoOT")] bool ignoreAutoOT = false)
    {
        var userID = Context.User.Id;

        if (!await Helpers<T>.EnsureUserNotInQueueAsync(userID).ConfigureAwait(false))
        {
            await Helpers<T>.SendAlreadyInQueueEmbedAsync(Context).ConfigureAwait(false);
            return;
        }

        var code = Info.GetRandomTradeCode(userID);
        var sig = Context.User.GetFavor();

        await ProcessTradeAttachmentAsync(code, sig, Context.User, isHiddenTrade: true, ignoreAutoOT: ignoreAutoOT).ConfigureAwait(false);

        if (Context.Message is IUserMessage userMessage)
            _ = Helpers<T>.DeleteMessagesAfterDelayAsync(userMessage, null, 2);
    }

    [Command("tradeUser")]
    [Alias("tu", "tradeOther")]
    [Summary("Hace que el bot intercambie al usuario mencionado el archivo adjunto.")]
    [RequireSudo]
    public async Task TradeAsyncAttachUser([Summary("Trade Code")] int code, [Remainder] string _)
    {
        if (Context.Message.MentionedUsers.Count > 1)
        {
            await ReplyAsync("⚠️ Demasiadas menciones. Solo puedes agregar a la lista un usario a la vez.").ConfigureAwait(false);
            return;
        }

        if (Context.Message.MentionedUsers.Count == 0)
        {
            await ReplyAsync("⚠️ Un usuario debe ser mencionado para hacer esto.").ConfigureAwait(false);
            return;
        }

        var usr = Context.Message.MentionedUsers.ElementAt(0);
        var sig = usr.GetFavor();
        await ProcessTradeAttachmentAsync(code, sig, usr).ConfigureAwait(false);
    }

    [Command("tradeUser")]
    [Alias("tu", "tradeOther")]
    [Summary("Hace que el bot intercambie al usuario mencionado el archivo adjunto.")]
    [RequireSudo]
    public Task TradeAsyncAttachUser([Remainder] string _)
    {
        var userID = Context.User.Id;
        var code = Info.GetRandomTradeCode(userID);
        return TradeAsyncAttachUser(code, _);
    }

    #endregion

    #region Special Trade Commands

    [Command("egg")]
    [Alias("Egg")]
    [Summary("Intercambia un huevo generado a partir del nombre de Pokémon proporcionado.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public async Task TradeEgg([Remainder] string egg)
    {
        var userID = Context.User.Id;
        var code = Info.GetRandomTradeCode(userID);
        await TradeEggAsync(code, egg).ConfigureAwait(false);
    }

    [Command("egg")]
    [Alias("Egg")]
    [Summary("Intercambia un huevo generado a partir del nombre de Pokémon proporcionado.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public async Task TradeEggAsync([Summary("Trade Code")] int code, [Summary("Showdown Set")][Remainder] string content)
    {
        var userID = Context.User.Id;
        if (!await Helpers<T>.EnsureUserNotInQueueAsync(userID).ConfigureAwait(false))
        {
            await Helpers<T>.SendAlreadyInQueueEmbedAsync(Context).ConfigureAwait(false);
            return;
        }

        content = ReusableActions.StripCodeBlock(content);
        var set = new ShowdownSet(content);
        var template = AutoLegalityWrapper.GetTemplate(set);

        _ = Task.Run(async () =>
        {
            try
            {
                var sav = AutoLegalityWrapper.GetTrainerInfo<T>();
                var pkm = sav.GetLegal(template, out var result);

                if (pkm == null)
                {
                    await ReplyAsync("Set took too long to legalize.");
                    return;
                }

                pkm = EntityConverter.ConvertToType(pkm, typeof(T), out _) ?? pkm;
                if (pkm is not T pk)
                {
                    await Helpers<T>.ReplyAndDeleteAsync(Context, "Oops! I wasn't able to create an egg for that.", 2);
                    return;
                }

                Helpers<T>.ApplyEggLogic(pk, content);

                var sig = Context.User.GetFavor();
                await Helpers<T>.AddTradeToQueueAsync(Context, code, Context.User.Username, pk, sig, Context.User).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogUtil.LogSafe(ex, nameof(TradeModule<T>));
                await Helpers<T>.ReplyAndDeleteAsync(Context, "An error occurred while processing the request.", 2);
            }
        });

        if (Context.Message is IUserMessage userMessage)
            _ = Helpers<T>.DeleteMessagesAfterDelayAsync(userMessage, null, 2);
    }

    [Command("fixOT")]
    [Alias("fix", "f")]
    [Summary("Corrige el OT y el apodo de un Pokémon que muestras a través de Link Trade si se detecta un anuncio.")]
    [RequireQueueRole(nameof(DiscordManager.RolesFixOT))]
    public async Task FixAdOT()
    {
        var userID = Context.User.Id;
        if (!await Helpers<T>.EnsureUserNotInQueueAsync(userID).ConfigureAwait(false))
        {
            await Helpers<T>.SendAlreadyInQueueEmbedAsync(Context).ConfigureAwait(false);
            return;
        }

        var code = Info.GetRandomTradeCode(userID);
        await ProcessFixOTAsync(code);
    }

    [Command("fixOT")]
    [Alias("fix", "f")]
    [Summary("Corrige el OT y el apodo de un Pokémon que muestras a través de Link Trade si se detecta un anuncio.")]
    [RequireQueueRole(nameof(DiscordManager.RolesFixOT))]
    public async Task FixAdOT([Summary("Trade Code")] int code)
    {
        var userID = Context.User.Id;
        if (!await Helpers<T>.EnsureUserNotInQueueAsync(userID).ConfigureAwait(false))
        {
            await Helpers<T>.SendAlreadyInQueueEmbedAsync(Context).ConfigureAwait(false);
            return;
        }

        await ProcessFixOTAsync(code);
    }

    private async Task ProcessFixOTAsync(int code)
    {
        var trainerName = Context.User.Username;
        var sig = Context.User.GetFavor();
        var lgcode = Info.GetRandomLGTradeCode();

        await QueueHelper<T>.AddToQueueAsync(Context, code, trainerName, sig, new T(),
            PokeRoutineType.FixOT, PokeTradeType.FixOT, Context.User, false, 1, 1, false, false, lgcode: lgcode).ConfigureAwait(false);

        if (Context.Message is IUserMessage userMessage)
            _ = Helpers<T>.DeleteMessagesAfterDelayAsync(userMessage, null, 2);
    }

    [Command("dittoTrade")]
    [Alias("dt", "ditto")]
    [Summary("Hace que el bot te intercambie un Ditto con un idioma y una extensión de estadísticas solicitados.")]
    public async Task DittoTrade([Summary("A combination of \"ATK/SPA/SPE\" or \"6IV\"")] string keyword,
        [Summary("Language")] string language, [Summary("Nature")] string nature)
    {
        var userID = Context.User.Id;
        if (!await Helpers<T>.EnsureUserNotInQueueAsync(userID).ConfigureAwait(false))
        {
            await Helpers<T>.SendAlreadyInQueueEmbedAsync(Context).ConfigureAwait(false);
            return;
        }

        var code = Info.GetRandomTradeCode(userID);
        await ProcessDittoTradeAsync(code, keyword, language, nature);
    }

    [Command("dittoTrade")]
    [Alias("dt", "ditto")]
    [Summary("Hace que el bot te intercambie un Ditto con un idioma y una extensión de estadísticas solicitados.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public async Task DittoTrade([Summary("Trade Code")] int code,
        [Summary("A combination of \"ATK/SPA/SPE\" or \"6IV\"")] string keyword,
        [Summary("Language")] string language, [Summary("Nature")] string nature)
    {
        var userID = Context.User.Id;
        if (!await Helpers<T>.EnsureUserNotInQueueAsync(userID).ConfigureAwait(false))
        {
            await Helpers<T>.SendAlreadyInQueueEmbedAsync(Context).ConfigureAwait(false);
            return;
        }

        await ProcessDittoTradeAsync(code, keyword, language, nature);
    }

    private async Task ProcessDittoTradeAsync(int code, string keyword, string language, string nature)
    {
        keyword = keyword.ToLower().Trim();

        if (!Enum.TryParse(language, true, out LanguageID lang))
        {
            await Helpers<T>.ReplyAndDeleteAsync(Context, $"Couldn't recognize language: {language}.", 2);
            return;
        }

        nature = nature.Trim()[..1].ToUpper() + nature.Trim()[1..].ToLower();
        var set = new ShowdownSet($"{keyword}(Ditto)\nLanguage: {lang}\nNature: {nature}");
        var template = AutoLegalityWrapper.GetTemplate(set);
        var sav = AutoLegalityWrapper.GetTrainerInfo<T>();
        var pkm = sav.GetLegal(template, out var result);

        if (pkm == null)
        {
            await ReplyAsync("Set took too long to legalize.");
            return;
        }

        TradeExtensions<T>.DittoTrade((T)pkm);
        var la = new LegalityAnalysis(pkm);

        if (pkm is not T pk || !la.Valid)
        {
            var reason = result == "Timeout" ? "That set took too long to generate." : "I wasn't able to create something from that.";
            var imsg = $"Oops! {reason} Here's my best attempt for that Ditto!";
            await Context.Channel.SendPKMAsync(pkm, imsg).ConfigureAwait(false);
            return;
        }

        pk.ResetPartyStats();

        // Ad Name Check
        if (Info.Hub.Config.Trade.TradeConfiguration.EnableSpamCheck)
        {
            if (TradeExtensions<T>.HasAdName(pk, out string ad))
            {
                await Helpers<T>.ReplyAndDeleteAsync(Context, "Detected Adname in the Pokémon's name or trainer name, which is not allowed.", 5);
                return;
            }
        }

        var sig = Context.User.GetFavor();
        await QueueHelper<T>.AddToQueueAsync(Context, code, Context.User.Username, sig, pk,
            PokeRoutineType.LinkTrade, PokeTradeType.Specific).ConfigureAwait(false);

        if (Context.Message is IUserMessage userMessage)
            _ = Helpers<T>.DeleteMessagesAfterDelayAsync(userMessage, null, 2);
    }

    [Command("itemTrade")]
    [Alias("it", "item")]
    [Summary("Hace que el bot te intercambie un Pokémon que tenga el objeto solicitado, o un ditto si se proporciona la palabra clave de distribución de estadísticas.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public async Task ItemTrade([Remainder] string item)
    {
        var userID = Context.User.Id;
        if (!await Helpers<T>.EnsureUserNotInQueueAsync(userID).ConfigureAwait(false))
        {
            await Helpers<T>.SendAlreadyInQueueEmbedAsync(Context).ConfigureAwait(false);
            return;
        }

        var code = Info.GetRandomTradeCode(userID);
        await ProcessItemTradeAsync(code, item);
    }

    [Command("itemTrade")]
    [Alias("it", "item")]
    [Summary("Hace que el robot te intercambie un Pokémon que tenga el objeto solicitado.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public async Task ItemTrade([Summary("Trade Code")] int code, [Remainder] string item)
    {
        var userID = Context.User.Id;
        if (!await Helpers<T>.EnsureUserNotInQueueAsync(userID).ConfigureAwait(false))
        {
            await Helpers<T>.SendAlreadyInQueueEmbedAsync(Context).ConfigureAwait(false);
            return;
        }

        await ProcessItemTradeAsync(code, item);
    }

    private async Task ProcessItemTradeAsync(int code, string item)
    {
        Species species = Info.Hub.Config.Trade.TradeConfiguration.ItemTradeSpecies == Species.None
            ? Species.Diglett
            : Info.Hub.Config.Trade.TradeConfiguration.ItemTradeSpecies;

        var set = new ShowdownSet($"{SpeciesName.GetSpeciesNameGeneration((ushort)species, 2, 8)} @ {item.Trim()}");
        var template = AutoLegalityWrapper.GetTemplate(set);
        var sav = AutoLegalityWrapper.GetTrainerInfo<T>();
        var pkm = sav.GetLegal(template, out var result);

        if (pkm == null)
        {
            await ReplyAsync("Set took too long to legalize.");
            return;
        }

        pkm = EntityConverter.ConvertToType(pkm, typeof(T), out _) ?? pkm;

        if (pkm.HeldItem == 0)
        {
            await Helpers<T>.ReplyAndDeleteAsync(Context, $"{Context.User.Username}, the item you entered wasn't recognized.", 2);
            return;
        }

        var la = new LegalityAnalysis(pkm);
        if (pkm is not T pk || !la.Valid)
        {
            var reason = result == "Timeout" ? "That set took too long to generate." : "I wasn't able to create something from that.";
            var imsg = $"Oops! {reason} Here's my best attempt for that {species}!";
            await Context.Channel.SendPKMAsync(pkm, imsg).ConfigureAwait(false);
            return;
        }

        pk.ResetPartyStats();
        var sig = Context.User.GetFavor();
        await QueueHelper<T>.AddToQueueAsync(Context, code, Context.User.Username, sig, pk,
            PokeRoutineType.LinkTrade, PokeTradeType.Item).ConfigureAwait(false);

        if (Context.Message is IUserMessage userMessage)
            _ = Helpers<T>.DeleteMessagesAfterDelayAsync(userMessage, null, 2);
    }

    #endregion

    #region List Commands

    [Command("tradeList")]
    [Alias("tl")]
    [Summary("Muestra los usuarios en las colas comerciales.")]
    [RequireSudo]
    public async Task GetTradeListAsync()
    {
        string msg = Info.GetTradeList(PokeRoutineType.LinkTrade);
        var embed = new EmbedBuilder();
        embed.AddField(x =>
        {
            x.Name = "Pending Trades";
            x.Value = msg;
            x.IsInline = false;
        });
        await ReplyAsync("These are the users who are currently waiting:", embed: embed.Build()).ConfigureAwait(false);
    }

    [Command("fixOTList")]
    [Alias("fl", "fq")]
    [Summary("Muestra los usuarios en la cola Fix OT.")]
    [RequireSudo]
    public async Task GetFixListAsync()
    {
        string msg = Info.GetTradeList(PokeRoutineType.FixOT);
        var embed = new EmbedBuilder();
        embed.AddField(x =>
        {
            x.Name = "Pending Trades";
            x.Value = msg;
            x.IsInline = false;
        });
        await ReplyAsync("These are the users who are currently waiting:", embed: embed.Build()).ConfigureAwait(false);
    }

    [Command("listevents")]
    [Alias("le")]
    [Summary("Enumera los archivos de eventos disponibles, filtrados por una letra o subcadena específica, y envía la lista a través de DM.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public Task ListEventsAsync([Remainder] string args = "")
        => ListHelpers<T>.HandleListCommandAsync(
            Context,
            SysCord<T>.Runner.Config.Trade.RequestFolderSettings.EventsFolder,
            "events",
            "er",
            args
        );

    [Command("battlereadylist")]
    [Alias("brl")]
    [Summary("Enumera los archivos disponibles listos para la batalla, filtrados por una letra o subcadena específica, y envía la lista a través de DM.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public Task BattleReadyListAsync([Remainder] string args = "")
        => ListHelpers<T>.HandleListCommandAsync(
            Context,
            SysCord<T>.Runner.Config.Trade.RequestFolderSettings.BattleReadyPKMFolder,
            "battle-ready files",
            "brr",
            args
        );

    #endregion

    #region Request Commands

    [Command("eventrequest")]
    [Alias("er")]
    [Summary("Descarga archivos adjuntos de eventos de la carpeta de eventos especificada y los agrega a la cola de transacciones.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public Task EventRequestAsync(int index)
        => ListHelpers<T>.HandleRequestCommandAsync(
            Context,
            SysCord<T>.Runner.Config.Trade.RequestFolderSettings.EventsFolder,
            index,
            "event",
            "le"
        );

    [Command("battlereadyrequest")]
    [Alias("brr", "br")]
    [Summary("Descarga archivos adjuntos listos para la batalla desde la carpeta especificada y los agrega a la cola de intercambios.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTradePlus))]
    public Task BattleReadyRequestAsync(int index)
        => ListHelpers<T>.HandleRequestCommandAsync(
            Context,
            SysCord<T>.Runner.Config.Trade.RequestFolderSettings.BattleReadyPKMFolder,
            index,
            "battle-ready file",
            "brl"
        );

    #endregion

    #region Batch Trades

    [Command("batchTrade")]
    [Alias("bt")]
    [Summary("Hace que el bot intercambie varios Pokémon de la lista proporcionada, hasta un máximo de 3 intercambios.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTradePlus))]
    public async Task BatchTradeAsync([Summary("List of Showdown Sets separated by '---'")][Remainder] string content)
    {
        var userID = Context.User.Id;
        if (!await Helpers<T>.EnsureUserNotInQueueAsync(userID).ConfigureAwait(false))
        {
            await Helpers<T>.SendAlreadyInQueueEmbedAsync(Context).ConfigureAwait(false);
            return;
        }
        content = ReusableActions.StripCodeBlock(content);
        var trades = BatchHelpers<T>.ParseBatchTradeContent(content);
        const int maxTradesAllowed = 4;
        if (maxTradesAllowed < 1 || trades.Count > maxTradesAllowed)
        {
            await Helpers<T>.ReplyAndDeleteAsync(Context,
                $"You can only process up to {maxTradesAllowed} trades at a time. Please reduce the number of trades in your batch.", 5);
            return;
        }

        var processingMessage = await Context.Channel.SendMessageAsync($"{Context.User.Mention} Processing your batch trade with {trades.Count} Pokémon...");

        _ = Task.Run(async () =>
        {
            try
            {
                var batchPokemonList = new List<T>();
                var errors = new List<BatchTradeError>();
                for (int i = 0; i < trades.Count; i++)
                {
                    (T? pk, string? error, ShowdownSet? set, string? legalizationHint) = await BatchHelpers<T>.ProcessSingleTradeForBatch(Context, trades[i]);

                    if (pk != null)
                    {
                        batchPokemonList.Add(pk);
                    }
                    else
                    {
                        var speciesName = set != null && set.Species > 0
                            ? GameInfo.Strings.Species[set.Species]
                            : "Unknown";
                        errors.Add(new BatchTradeError
                        {
                            TradeNumber = i + 1,
                            SpeciesName = speciesName,
                            ErrorMessage = error ?? "Unknown error",
                            LegalizationHint = legalizationHint,
                            ShowdownSet = set != null ? string.Join("\n", set.GetSetLines()) : trades[i]
                        });
                    }
                }

                await processingMessage.DeleteAsync();

                if (errors.Count > 0)
                {
                    await BatchHelpers<T>.SendBatchErrorEmbedAsync(Context, errors, trades.Count);
                    return;
                }
                if (batchPokemonList.Count > 0)
                {
                    var batchTradeCode = Info.GetRandomTradeCode(userID);
                    await BatchHelpers<T>.ProcessBatchContainer(Context, batchPokemonList, batchTradeCode, trades.Count);
                }
            }
            catch (Exception ex)
            {
                try
                {
                    await processingMessage.DeleteAsync();
                }
                catch { }

                await Context.Channel.SendMessageAsync($"{Context.User.Mention} An error occurred while processing your batch trade. Please try again.");
                Base.LogUtil.LogError($"Batch trade processing error: {ex.Message}", nameof(BatchTradeAsync));
            }
        });

        if (Context.Message is IUserMessage userMessage)
            _ = Helpers<T>.DeleteMessagesAfterDelayAsync(userMessage, null, 2);
    }

    #endregion

    #region Batch Trades from ZIP

    [Command("batchtradezip")]
    [Alias("btz")]
    [Summary("Hace que el bot intercambie varios Pokémon desde el archivo .zip proporcionado, hasta un máximo de 6 intercambios.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTradePlus))]
    public async Task BatchTradeZipAsync()
    {
        var userID = Context.User.Id;
        var code = Info.GetRandomTradeCode(userID);
        await BatchTradeZipAsync(code).ConfigureAwait(false);
    }

    [Command("batchtradezip")]
    [Alias("btz")]
    [Summary("Hace que el bot intercambie varios Pokémon desde el archivo .zip proporcionado, hasta un máximo de 6 intercambios.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTradePlus))]
    public async Task BatchTradeZipAsync([Summary("Trade Code")] int code)
    {
        // 1) Reglas del servidor: batch habilitado
        if (!SysCord<T>.Runner.Config.Trade.TradeConfiguration.AllowBatchTrades)
        {
            await Helpers<T>.ReplyAndDeleteAsync(Context,
                $"❌ {Context.User.Mention} Los intercambios por lotes están actualmente deshabilitados.", 2);
            return;
        }

        // 2) Usuario no debe estar en cola
        var userID = Context.User.Id;
        if (!await Helpers<T>.EnsureUserNotInQueueAsync(userID).ConfigureAwait(false))
        {
            await Helpers<T>.SendAlreadyInQueueEmbedAsync(Context).ConfigureAwait(false);
            return;
        }

        // 3) Validar adjunto
        var attachment = Context.Message.Attachments.FirstOrDefault();
        if (attachment == default)
        {
            await Helpers<T>.ReplyAndDeleteAsync(Context,
                $"⚠️ {Context.User.Mention}, no se ha adjuntado ningún archivo. ¡Por favor, intenta de nuevo!", 2);
            return;
        }

        if (!attachment.Filename.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            await Helpers<T>.ReplyAndDeleteAsync(Context,
                $"⚠️ {Context.User.Mention}, el formato de archivo no es válido. Por favor, proporciona un archivo en formato .zip.", 2);
            return;
        }

        const int maxTradesAllowed = 6;

        // Mensaje de procesamiento
        var processingMessage = await Context.Channel.SendMessageAsync(
            $"{Context.User.Mention} Procesando tu archivo .zip…").ConfigureAwait(false);

        _ = Task.Run(async () =>
        {
            try
            {
                // 4) Descargar y abrir zip
                var http = new HttpClient();
                var zipBytes = await http.GetByteArrayAsync(attachment.Url).ConfigureAwait(false);

                await using var zipStream = new MemoryStream(zipBytes);
                using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

                // 5) Leer entradas .pk* reales (no directorios), hasta 6
                var entries = archive.Entries
                    .Where(e => !string.IsNullOrEmpty(e.Name)) // ignora "directorios"
                    .Where(e =>
                    {
                        var n = e.Name.ToLowerInvariant();
                        return n.EndsWith(".pk1") || n.EndsWith(".pk2") || n.EndsWith(".pk3") ||
                               n.EndsWith(".pk4") || n.EndsWith(".pk5") || n.EndsWith(".pk6") ||
                               n.EndsWith(".pk7") || n.EndsWith(".pb7") || n.EndsWith(".pa8") ||
                               n.EndsWith(".pb8") || n.EndsWith(".pk8") || n.EndsWith(".pk9");
                    })
                    .Take(maxTradesAllowed)
                    .ToList();

                // Límite
                if (entries.Count == 0)
                {
                    await processingMessage.DeleteAsync().ConfigureAwait(false);
                    await Helpers<T>.ReplyAndDeleteAsync(Context,
                        $"⚠️ {Context.User.Mention}, no se encontraron archivos de Pokémon válidos dentro del .zip.", 5);
                    return;
                }

                // 6) Parsear/validar cada PKM y acumular errores al estilo batch
                var batchPokemonList = new List<T>();
                var errors = new List<BatchTradeError>();
                int index = 0;

                foreach (var entry in entries)
                {
                    index++;

                    try
                    {
                        await using var entryStream = entry.Open();
                        var pkBytes = await ReadAllBytesAsync(entryStream).ConfigureAwait(false);
                        var parsed = EntityFormat.GetFromBytes(pkBytes);

                        if (parsed is not T pk)
                        {
                            errors.Add(new BatchTradeError
                            {
                                TradeNumber = index,
                                SpeciesName = "Desconocido",
                                ErrorMessage = "El archivo no corresponde a un formato compatible para este bot.",
                                LegalizationHint = null,
                                ShowdownSet = entry.Name
                            });
                            continue;
                        }

                        // Aplicar lógica estándar de ítems
                        Helpers<T>.ApplyStandardItemLogic(pk);

                        // Validación de legalidad
                        var la = new LegalityAnalysis(pk);
                        if (!la.Valid)
                        {
                            errors.Add(new BatchTradeError
                            {
                                TradeNumber = index,
                                SpeciesName = SpeciesName.GetSpeciesName(pk.Species, (int)LanguageID.English),
                                ErrorMessage = "El archivo no es legal para intercambio.",
                                LegalizationHint = la.Report(verbose: false),
                                ShowdownSet = entry.Name
                            });
                            continue;
                        }

                        // Mew Shiny en LGPE (PB7) no permitido
                        if (pk is PB7 && pk.Species == (int)Species.Mew && pk.IsShiny)
                        {
                            errors.Add(new BatchTradeError
                            {
                                TradeNumber = index,
                                SpeciesName = "Mew",
                                ErrorMessage = "Mew no puede ser Shiny en LGPE (shiny lock).",
                                LegalizationHint = "✔ Quita `Shiny: Yes` del archivo o solicita otro Pokémon.",
                                ShowdownSet = entry.Name
                            });
                            continue;
                        }

                        // AdName / Spam
                        if (SysCord<T>.Runner.Config.Trade.TradeConfiguration.EnableSpamCheck &&
                            TradeExtensions<T>.HasAdName(pk, out _))
                        {
                            errors.Add(new BatchTradeError
                            {
                                TradeNumber = index,
                                SpeciesName = SpeciesName.GetSpeciesName(pk.Species, (int)LanguageID.English),
                                ErrorMessage = "Nombre de anuncio detectado en el Pokémon o en el OT.",
                                LegalizationHint = "✔ Cambia el mote/OT a algo permitido.",
                                ShowdownSet = entry.Name
                            });
                            continue;
                        }

                        // Preparación final
                        pk.ResetPartyStats();

                        batchPokemonList.Add(pk);
                    }
                    catch (Exception exEntry)
                    {
                        errors.Add(new BatchTradeError
                        {
                            TradeNumber = index,
                            SpeciesName = "Desconocido",
                            ErrorMessage = $"Error leyendo '{entry.Name}': {exEntry.Message}",
                            LegalizationHint = null,
                            ShowdownSet = entry.Name
                        });
                    }
                }

                // limpiar "procesando…"
                try { await processingMessage.DeleteAsync().ConfigureAwait(false); } catch { }

                // ¿hubo errores?
                if (errors.Count > 0)
                {
                    await BatchHelpers<T>.SendBatchErrorEmbedAsync(Context, errors, entries.Count).ConfigureAwait(false);
                }

                // 7) Si hay al menos 1 válido → contenedor de lote y a la cola
                if (batchPokemonList.Count > 0)
                {
                    var batchTradeCode = Info.GetRandomTradeCode(userID);
                    await BatchHelpers<T>.ProcessBatchContainer(Context, batchPokemonList, batchTradeCode, batchPokemonList.Count)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                try { await processingMessage.DeleteAsync().ConfigureAwait(false); } catch { }
                await Context.Channel.SendMessageAsync(
                    $"{Context.User.Mention} Ocurrió un error al procesar tu .zip. Inténtalo de nuevo.")
                    .ConfigureAwait(false);
                Base.LogUtil.LogError($"BatchTradeZip error: {ex.Message}", nameof(BatchTradeZipAsync));
            }
        });

        if (Context.Message is IUserMessage userMessage)
            _ = Helpers<T>.DeleteMessagesAfterDelayAsync(userMessage, null, 2);
    }

    // Helper local para leer bytes de una entrada del zip
    private static async Task<byte[]> ReadAllBytesAsync(Stream stream)
    {
        await using var ms = new MemoryStream();
        await stream.CopyToAsync(ms).ConfigureAwait(false);
        return ms.ToArray();
    }

    #endregion

    #region Private Helper Methods

    private async Task ProcessTradeAsync(int code, string content, bool isHiddenTrade = false)
    {
        var userID = Context.User.Id;

        // Verificar si el usuario ya está en la cola
        if (!await Helpers<T>.EnsureUserNotInQueueAsync(userID).ConfigureAwait(false))
        {
            await Helpers<T>.SendAlreadyInQueueEmbedAsync(Context).ConfigureAwait(false);
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                // Detecta si el set trae OT/TID/SID para ignorar AutoOT
                var ignoreAutoOT = content.Contains("OT:") || content.Contains("TID:") || content.Contains("SID:");

                // Firma actual: (SocketCommandContext context, string content, bool ignoreAutoOT)
                var result = await Helpers<T>.ProcessShowdownSetAsync(Context, content, ignoreAutoOT).ConfigureAwait(false);

                if (result.Pokemon == null)
                {
                    await Helpers<T>.SendTradeErrorEmbedAsync(Context, result).ConfigureAwait(false);
                    return;
                }

                var sig = Context.User.GetFavor();

                await Helpers<T>.AddTradeToQueueAsync(
                    Context, code, Context.User.Username, result.Pokemon, sig, Context.User,
                    isHiddenTrade: isHiddenTrade,
                    lgcode: result.LgCode,
                    ignoreAutoOT: ignoreAutoOT,
                    isNonNative: result.IsNonNative
                ).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogUtil.LogSafe(ex, nameof(TradeModule<T>));
                var msg = "¡Ups! Ocurrió un problema inesperado con este Showdown Set.";
                await Helpers<T>.ReplyAndDeleteAsync(Context, msg, 2).ConfigureAwait(false);
            }
        });

        if (Context.Message is IUserMessage userMessage)
            _ = Helpers<T>.DeleteMessagesAfterDelayAsync(userMessage, null, isHiddenTrade ? 0 : 2);
    }

    private async Task ProcessTradeAttachmentAsync(int code, RequestSignificance sig, SocketUser user, bool isHiddenTrade = false, bool ignoreAutoOT = false)
    {
        var pk = await Helpers<T>.ProcessTradeAttachmentAsync(Context);
        if (pk == null)
            return;

        await Helpers<T>.AddTradeToQueueAsync(Context, code, user.Username, pk, sig, user,
            isHiddenTrade: isHiddenTrade, ignoreAutoOT: ignoreAutoOT);
    }

    #endregion
}
