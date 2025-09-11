using Discord;
using Discord.Commands;
using Discord.Net;
using Discord.WebSocket;
using PKHeX.Core;
using PKHeX.Core.AutoMod;
using PKHeX.Drawing.PokeSprite;
using SysBot.Pokemon.Discord.Commands.Bots;
using SysBot.Pokemon.Helpers;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Color = System.Drawing.Color;
using DiscordColor = Discord.Color;

namespace SysBot.Pokemon.Discord;

public static class QueueHelper<T> where T : PKM, new()
{
    private const uint MaxTradeCode = 9999_9999;

    private static readonly Dictionary<int, string> MilestoneImages = new()
    {
        { 1, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/001.png" },
        { 50, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/050.png" },
        { 100, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/100.png" },
        { 150, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/150.png" },
        { 200, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/200.png" },
        { 250, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/250.png" },
        { 300, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/300.png" },
        { 350, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/350.png" },
        { 400, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/400.png" },
        { 450, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/450.png" },
        { 500, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/500.png" },
        { 550, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/550.png" },
        { 600, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/600.png" },
        { 650, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/650.png" },
        { 700, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/700.png" }
    };

    private static string GetMilestoneDescription(int tradeCount)
    {
        return tradeCount switch
        {
            1 => "¡Felicidades por tu primer intercambio!\n**Estado:** Entrenador nuevo.",
            50 => "¡Has alcanzado los 50 intercambios!\n**Estado:** Entrenador novato.",
            100 => "¡Has alcanzado los 100 intercambios!\n**Estado:** Profesor Pokémon.",
            150 => "¡Has alcanzado los 150 intercambios!\n**Estado:** Especialista Pokémon.",
            200 => "¡Has alcanzado los 200 intercambios!\n**Estado:** Campeón Pokémon.",
            250 => "¡Has alcanzado los 250 intercambios!\n**Estado:** Héroe Pokémon.",
            300 => "¡Has alcanzado los 300 intercambios!\n**Estado:** Pokémon Élite.",
            350 => "¡Has alcanzado los 350 intercambios!\n**Estado:** Comerciante Pokémon.",
            400 => "¡Has alcanzado los 400 intercambios!\n**Estado:** Pokémon Sabio.",
            450 => "¡Has alcanzado los 450 intercambios!\n**Estado:** Leyenda Pokémon.",
            500 => "¡Has alcanzado los 500 intercambios!\n**Estado:** Maestro de la Región.",
            550 => "¡Has alcanzado los 550 intercambios!\n**Estado:** Maestro del trade.",
            600 => "¡Has alcanzado los 600 intercambios!\n**Estado:** Famoso en el mundo.",
            650 => "¡Has alcanzado los 650 intercambios!\n**Estado:** Maestro Pokémon.",
            700 => "¡Has alcanzado los 700 intercambios!\n**Estado:** Dios Pokémon.",
            _ => $"¡Felicidades por alcanzar {tradeCount} trades! ¡Sigue así!"
        };
    }

    public static async Task AddToQueueAsync(SocketCommandContext context, int code, string trainer, RequestSignificance sig, T trade, PokeRoutineType routine, PokeTradeType type, SocketUser trader, bool isBatchTrade = false, int batchTradeNumber = 1, int totalBatchTrades = 1, bool isHiddenTrade = false, bool isMysteryTrade = false, bool isMysteryEgg = false, List<Pictocodes>? lgcode = null, bool ignoreAutoOT = false, bool setEdited = false, bool isNonNative = false)
    {
        if ((uint)code > MaxTradeCode)
        {
            await context.Channel.SendMessageAsync($"⚠️ {context.User.Mention} El código de tradeo debe ser un numero entre: **00000000-99999999**!").ConfigureAwait(false);
            return;
        }

        try
        {
            // Only send trade code for non-batch trades (batch container will handle its own)
            if (!isBatchTrade)
            {
                if (trade is PB7 && lgcode != null)
                {
                    var (thefile, lgcodeembed) = CreateLGLinkCodeSpriteEmbed(lgcode);
                    await trader.SendFileAsync(thefile, "Tu código de tradeo sera:", embed: lgcodeembed).ConfigureAwait(false);
                }
                else
                {
                    await EmbedHelper.SendTradeCodeEmbedAsync(trader, code).ConfigureAwait(false);
                }
            }

            var result = await AddToTradeQueue(context, trade, code, trainer, sig, routine, type, trader, isBatchTrade, batchTradeNumber, totalBatchTrades, isHiddenTrade, isMysteryTrade, isMysteryEgg, lgcode, ignoreAutoOT, setEdited, isNonNative).ConfigureAwait(false);
        }
        catch (HttpException ex)
        {
            await HandleDiscordExceptionAsync(context, trader, ex).ConfigureAwait(false);
        }
    }

    public static Task AddToQueueAsync(SocketCommandContext context, int code, string trainer, RequestSignificance sig, T trade, PokeRoutineType routine, PokeTradeType type, bool ignoreAutoOT = false)
    {
        return AddToQueueAsync(context, code, trainer, sig, trade, routine, type, context.User, ignoreAutoOT: ignoreAutoOT);
    }

    private static async Task<TradeQueueResult> AddToTradeQueue(SocketCommandContext context, T pk, int code, string trainerName,
        RequestSignificance sig, PokeRoutineType type, PokeTradeType t, SocketUser trader, bool isBatchTrade,
        int batchTradeNumber, int totalBatchTrades, bool isHiddenTrade, bool isMysteryTrade = false, bool isMysteryEgg = false,
        List<Pictocodes>? lgcode = null, bool ignoreAutoOT = false, bool setEdited = false, bool isNonNative = false)
    {
        string tradingUrl = SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.ExtraEmbedOptions.TradingBotUrl;
        string NonNative = SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.ExtraEmbedOptions.NonNativeTexT;
        string AutocorrectText = SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.ExtraEmbedOptions.AutocorrectText;
        var user = trader;
        var userID = user.Id;
        var name = user.Username;
        var trainer = new PokeTradeTrainerInfo(trainerName, userID);
#pragma warning disable CS8604 // Possible null reference argument.
        var notifier = new DiscordTradeNotifier<T>(pk, trainer, code, trader, batchTradeNumber, totalBatchTrades,
             isMysteryTrade, isMysteryEgg, lgcode: lgcode);
#pragma warning restore CS8604 // Possible null reference argument.

        int uniqueTradeID = GenerateUniqueTradeID();

        var detail = new PokeTradeDetail<T>(pk, trainer, notifier, t, code, sig == RequestSignificance.Favored,
            lgcode, batchTradeNumber, totalBatchTrades, isMysteryTrade, isMysteryEgg, uniqueTradeID, ignoreAutoOT, setEdited);

        var trade = new TradeEntry<T>(detail, userID, PokeRoutineType.LinkTrade, name, uniqueTradeID);
        var hub = SysCord<T>.Runner.Hub;
        var Info = hub.Queues.Info;
        var isSudo = sig == RequestSignificance.Owner;
        var added = Info.AddToTradeQueue(trade, userID, false, isSudo);

        // Start queue position updates for Discord notification
        if (added != QueueResultAdd.AlreadyInQueue && notifier is DiscordTradeNotifier<T> discordNotifier)
        {
            await discordNotifier.SendInitialQueueUpdate().ConfigureAwait(false);
        }

        int totalTradeCount = 0;
        TradeCodeStorage.TradeCodeDetails? tradeDetails = null;
        if (SysCord<T>.Runner.Config.Trade.TradeConfiguration.StoreTradeCodes)
        {
            var tradeCodeStorage = new TradeCodeStorage();
            totalTradeCount = tradeCodeStorage.GetTradeCount(trader.Id);
            tradeDetails = tradeCodeStorage.GetTradeDetails(trader.Id);
        }

        if (added == QueueResultAdd.AlreadyInQueue)
        {
            return new TradeQueueResult(false);
        }

        var embedData = DetailsExtractor<T>.ExtractPokemonDetails(
            pk, trader, isMysteryTrade, isMysteryEgg, type == PokeRoutineType.Clone, type == PokeRoutineType.Dump,
            type == PokeRoutineType.FixOT, type == PokeRoutineType.SeedCheck, isBatchTrade, batchTradeNumber, totalBatchTrades, t
        );

        try
        {
            (string embedImageUrl, DiscordColor embedColor) = await PrepareEmbedDetails(pk, isMysteryEgg);

            embedData.EmbedImageUrl = isMysteryTrade ? "https://i.imgur.com/FdESYAv.png" :
                           type == PokeRoutineType.Dump ? "https://i.imgur.com/9wfEHwZ.png" :
                           type == PokeRoutineType.Clone ? "https://i.imgur.com/aSTCjUn.png" :
                           type == PokeRoutineType.SeedCheck ? "https://i.imgur.com/EI1BHr5.png" :
                           type == PokeRoutineType.FixOT ? "https://i.imgur.com/gRZGFIi.png" :
                           embedImageUrl;

            embedData.HeldItemUrl = string.Empty;
            if (!string.IsNullOrWhiteSpace(embedData.HeldItem))
            {
                string heldItemName = embedData.HeldItem.ToLower().Replace(" ", "");
                // Verificar el tipo de intercambio para decidir la URL
                if (t == PokeTradeType.Item)
                {
                    embedData.HeldItemUrl = $"https://serebii.net/itemdex/sprites/sv/{heldItemName}.png";
                }
                else
                {
                    embedData.HeldItemUrl = $"https://serebii.net/itemdex/sprites/{heldItemName}.png";
                }
            }

            embedData.IsLocalFile = File.Exists(embedData.EmbedImageUrl);

            var position = Info.CheckPosition(userID, uniqueTradeID, type);
            var botct = Info.Hub.Bots.Count;
            var baseEta = position.Position > botct ? Info.Hub.Config.Queues.EstimateDelay(position.Position, botct) : 0;
            var etaMessage = $"Tiempo Estimado: {baseEta:F1} minuto(s) para el trade {batchTradeNumber}/{totalBatchTrades}.";
            string footerText = string.Empty;

            if (totalTradeCount > 0)
            {
                footerText += $"Trades: {totalTradeCount}\n";
            }

            footerText += $"Posición actual: {(position.Position == -1 ? 1 : position.Position)}";

            string userDetailsText = DetailsExtractor<T>.GetUserDetails(totalTradeCount, tradeDetails);
            if (!string.IsNullOrEmpty(userDetailsText))
            {
                footerText += $"\n{userDetailsText}";
            }
            footerText += $"\n{etaMessage}";

            var embedBuilder = new EmbedBuilder()
                .WithColor(embedColor)
                .WithFooter(footerText)
                .WithAuthor(new EmbedAuthorBuilder()
                    .WithName(embedData.AuthorName)
                    .WithIconUrl(trader.GetAvatarUrl() ?? trader.GetDefaultAvatarUrl())
                    .WithUrl(tradingUrl));

            // Decidir la imagen principal y el thumbnail basado en el tipo de intercambio
            if (t == PokeTradeType.Item && !string.IsNullOrEmpty(embedData.HeldItemUrl))
            {
                embedBuilder.WithImageUrl(embedData.HeldItemUrl);
            }
            else
            {
                if (embedData.IsLocalFile)
                {
                    embedBuilder.WithImageUrl($"attachment://{Path.GetFileName(embedData.EmbedImageUrl)}");
                }
                else
                {
                    embedBuilder.WithImageUrl(embedData.EmbedImageUrl);
                }
            }

            if (t == PokeTradeType.Item)
            {
                string mentionInfo = $"**Entrenador:** {trader.Mention}";
                string itemInfo = $"{embedData.HeldItem}";
                string fullInfo = $"{mentionInfo}\n{itemInfo}";

                embedBuilder.AddField("\u200B", fullInfo, inline: false);
            }
            else
            {
                DetailsExtractor<T>.AddAdditionalText(embedBuilder);

                if (!isMysteryTrade && !isMysteryEgg && type != PokeRoutineType.Clone && type != PokeRoutineType.Dump && type != PokeRoutineType.FixOT && type != PokeRoutineType.SeedCheck)
                {
                    DetailsExtractor<T>.AddNormalTradeFields(embedBuilder, embedData, trader.Mention, pk);
                }
                else
                {
                    DetailsExtractor<T>.AddSpecialTradeFields(embedBuilder, isMysteryTrade, isMysteryEgg, type == PokeRoutineType.SeedCheck, type == PokeRoutineType.Clone, type == PokeRoutineType.FixOT, trader.Mention);
                }
            }

            // Check if the Pokemon is Non-Native and/or has a Home Tracker
            if (pk is IHomeTrack homeTrack)
            {
                if (homeTrack.HasTracker && isNonNative)
                {
                    embedBuilder.Footer.IconUrl = "https://raw.githubusercontent.com/hexbyt3/sprites/main/exclamation.gif";
                    string trackerInfo = SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.ShowTracker
                        ? $"\n\n **Home Tracker:** ||{homeTrack.Tracker}||"
                        : string.Empty;

                    embedBuilder.AddField("__**Aviso**__: **Este Pokémon no es nativo y tiene rastreador de Home.**",
                        "*AutoOT no fue aplicado.*" + trackerInfo);
                }
                else if (homeTrack.HasTracker)
                {
                    embedBuilder.Footer.IconUrl = "https://raw.githubusercontent.com/hexbyt3/sprites/main/exclamation.gif";
                    string trackerInfo = SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.ShowTracker
                        ? $"\n\n **Home Tracker:** ||{homeTrack.Tracker}||"
                        : string.Empty;

                    embedBuilder.AddField("__**Aviso**__: **Rastreador de HOME detectado.**",
                        "*AutoOT no fue aplicado.*" + trackerInfo);
                }
                else if (isNonNative)
                {
                    // Only Non-Native
                    embedBuilder.Footer.IconUrl = "https://raw.githubusercontent.com/hexbyt3/sprites/main/exclamation.gif";
                    embedBuilder.AddField("__**Aviso**__: **Este Pokémon no es nativo.**", $"{NonNative}");
                }
            }
            else if (isNonNative)
            {
                // Fallback for Non-Native Pokemon that don't implement IHomeTrack
                embedBuilder.Footer.IconUrl = "https://raw.githubusercontent.com/hexbyt3/sprites/main/exclamation.gif";
                embedBuilder.AddField("__**Aviso**__: **Este Pokémon no es nativo.**", $"{NonNative}");
            }
            if (!isMysteryTrade && t != PokeTradeType.Item)
            {
                DetailsExtractor<T>.AddThumbnails(embedBuilder,
                    type == PokeRoutineType.Clone,
                    type == PokeRoutineType.SeedCheck,
                    type == PokeRoutineType.Dump,
                    type == PokeRoutineType.FixOT,
                    embedData.HeldItemUrl,
                    pk,
                    t);
            }

            if (!isHiddenTrade && SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.UseEmbeds)
            {
                var embed = embedBuilder.Build();
                if (embed == null)
                {
                    Console.WriteLine("Error: El embed es nulo.");
                    await context.Channel.SendMessageAsync("⚠️ Se produjo un error al preparar los detalles comerciales..");
                    return new TradeQueueResult(false);
                }

                if (t != PokeTradeType.Item && embedData.IsLocalFile)
                {
                    await context.Channel.SendFileAsync(embedData.EmbedImageUrl, embed: embed);
                    await ScheduleFileDeletion(embedData.EmbedImageUrl, 0);
                }
                else
                {
                    await context.Channel.SendMessageAsync(embed: embed);
                }
            }
            else
            {
                var message = $"{trader.Mention} ➜ Agregado a la cola de intercambio de enlaces. Posicion actual: {position.Position}. Recibiendo: **{embedData.SpeciesName}**.\n{etaMessage}";
                await context.Channel.SendMessageAsync(message);
            }
        }
        catch (HttpException ex)
        {
            await HandleDiscordExceptionAsync(context, trader, ex);
            return new TradeQueueResult(false);
        }

        if (SysCord<T>.Runner.Hub.Config.Trade.TradeConfiguration.StoreTradeCodes)
        {
            var tradeCodeStorage = new TradeCodeStorage();
            int tradeCount = tradeCodeStorage.GetTradeCount(trader.Id);
            _ = SendMilestoneEmbed(tradeCount, context.Channel, trader);
        }

        return new TradeQueueResult(true);
    }

    public static async Task AddBatchContainerToQueueAsync(SocketCommandContext context, int code, string trainer, T firstTrade, List<T> allTrades, RequestSignificance sig, SocketUser trader, int totalBatchTrades)
    {
        var userID = trader.Id;
        var name = trader.Username;
        var trainer_info = new PokeTradeTrainerInfo(trainer, userID);
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
        var notifier = new DiscordTradeNotifier<T>(firstTrade, trainer_info, code, trader, 1, totalBatchTrades, false, false, lgcode: null);
#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.

        int uniqueTradeID = GenerateUniqueTradeID();

        var detail = new PokeTradeDetail<T>(firstTrade, trainer_info, notifier, PokeTradeType.Batch, code,
            sig == RequestSignificance.Favored, null, 1, totalBatchTrades, false, false, uniqueTradeID)
        {
            BatchTrades = allTrades
        };

        var trade = new TradeEntry<T>(detail, userID, PokeRoutineType.Batch, name, uniqueTradeID);
        var hub = SysCord<T>.Runner.Hub;
        var Info = hub.Queues.Info;
        var added = Info.AddToTradeQueue(trade, userID, false, sig == RequestSignificance.Owner);

        // Send trade code once
        await EmbedHelper.SendTradeCodeEmbedAsync(trader, code).ConfigureAwait(false);

        // Start queue position updates for Discord notification
        if (added != QueueResultAdd.AlreadyInQueue && notifier is DiscordTradeNotifier<T> discordNotifier)
        {
            await discordNotifier.SendInitialQueueUpdate().ConfigureAwait(false);
        }

        // Handle the display
        if (added == QueueResultAdd.AlreadyInQueue)
        {
            await context.Channel.SendMessageAsync($"❌ {context.User.Mention} ¡Ya estás en la cola!").ConfigureAwait(false);
            return;
        }

        var position = Info.CheckPosition(userID, uniqueTradeID, PokeRoutineType.Batch);
        var botct = Info.Hub.Bots.Count;
        var baseEta = position.Position > botct ? Info.Hub.Config.Queues.EstimateDelay(position.Position, botct) : 0;

        // Get user trade details for footer
        int totalTradeCount = 0;
        TradeCodeStorage.TradeCodeDetails? tradeDetails = null;
        if (SysCord<T>.Runner.Config.Trade.TradeConfiguration.StoreTradeCodes)
        {
            var tradeCodeStorage = new TradeCodeStorage();
            totalTradeCount = tradeCodeStorage.GetTradeCount(trader.Id);
            tradeDetails = tradeCodeStorage.GetTradeDetails(trader.Id);
        }

        // Send initial batch summary message
        await context.Channel.SendMessageAsync($"✅ {trader.Mention} ➜ Intercambio por lotes con **{totalBatchTrades}** Pokémon agregado a la cola. Posición: **{position.Position}**. Estimado: **{baseEta:F1}** minuto(s).").ConfigureAwait(false);

        // Create and send embeds for each Pokémon in the batch
        if (SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.UseEmbeds)
        {
            for (int i = 0; i < allTrades.Count; i++)
            {
                string tradingUrl = SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.ExtraEmbedOptions.TradingBotUrl;
                var pk = allTrades[i];
                var batchTradeNumber = i + 1;

                // Extract details for this Pokémon
                var embedData = DetailsExtractor<T>.ExtractPokemonDetails(
                    pk, trader, false, false, false, false, false, false, true, batchTradeNumber, totalBatchTrades, PokeTradeType.Batch
                );

                try
                {
                    // Prepare embed details
                    (string embedImageUrl, DiscordColor embedColor) = await PrepareEmbedDetails(pk);

                    embedData.EmbedImageUrl = embedImageUrl;
                    embedData.HeldItemUrl = string.Empty;
                    if (!string.IsNullOrWhiteSpace(embedData.HeldItem))
                    {
                        string heldItemName = embedData.HeldItem.ToLower().Replace(" ", "");
                        embedData.HeldItemUrl = $"https://serebii.net/itemdex/sprites/{heldItemName}.png";
                    }

                    embedData.IsLocalFile = File.Exists(embedData.EmbedImageUrl);

                    // Construir texto del pie con información del lote
                    string footerText = $"Intercambio por lote {batchTradeNumber} de {totalBatchTrades}";
                    if (i == 0) // Solo mostrar posición y ETA en el primer embed
                    {
                        footerText += $" | Posición: {position.Position}";
                        string userDetailsText = DetailsExtractor<T>.GetUserDetails(totalTradeCount, tradeDetails);
                        if (!string.IsNullOrEmpty(userDetailsText))
                        {
                            footerText += $"\n{userDetailsText}";
                        }
                        footerText += $"\nEstimado: {baseEta:F1} minuto(s) para el lote";
                    }

                    // Create embed
                    var embedBuilder = new EmbedBuilder()
                        .WithColor(embedColor)
                        .WithImageUrl(embedData.IsLocalFile ? $"attachment://{Path.GetFileName(embedData.EmbedImageUrl)}" : embedData.EmbedImageUrl)
                        .WithFooter(footerText)
                        .WithAuthor(new EmbedAuthorBuilder()
                            .WithName(embedData.AuthorName)
                            .WithIconUrl(trader.GetAvatarUrl() ?? trader.GetDefaultAvatarUrl())
                            .WithUrl(tradingUrl));

                    DetailsExtractor<T>.AddAdditionalText(embedBuilder);
                    DetailsExtractor<T>.AddNormalTradeFields(embedBuilder, embedData, trader.Mention, pk);

                    // Check for Non-Native and Home Tracker
#pragma warning disable CS0219 // Variable is assigned but its value is never used
                    bool isNonNative = false; // You may need to pass this from the batch trade processing
#pragma warning restore CS0219 // Variable is assigned but its value is never used
                    if (pk is IHomeTrack homeTrack)
                    {
                        if (homeTrack.HasTracker)
                        {
                            embedBuilder.Footer.IconUrl = "https://raw.githubusercontent.com/hexbyt3/sprites/main/exclamation.gif";
                            string trackerInfo = SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.ShowTracker
                                ? $"\n\n **Home Tracker:** ||{homeTrack.Tracker}||"
                                : string.Empty;

                            embedBuilder.AddField("__**Aviso**__: **Rastreador de HOME detectado.**",
                                "*AutoOT no fue aplicado.*" + trackerInfo);
                        }
                    }

                    DetailsExtractor<T>.AddThumbnails(embedBuilder, false, false, false, false, embedData.HeldItemUrl, pk, PokeTradeType.Batch);

                    var embed = embedBuilder.Build();

                    // Send embed
                    if (embedData.IsLocalFile)
                    {
                        await context.Channel.SendFileAsync(embedData.EmbedImageUrl, embed: embed);
                        await ScheduleFileDeletion(embedData.EmbedImageUrl, 0);
                    }
                    else
                    {
                        await context.Channel.SendMessageAsync(embed: embed);
                    }

                    // Small delay between embeds to avoid rate limiting
                    if (i < allTrades.Count - 1)
                    {
                        await Task.Delay(500);
                    }
                }
                catch (HttpException ex)
                {
                    await HandleDiscordExceptionAsync(context, trader, ex);
                }
            }
        }

        // Send milestone embed if applicable
        if (SysCord<T>.Runner.Hub.Config.Trade.TradeConfiguration.StoreTradeCodes)
        {
            var tradeCodeStorage = new TradeCodeStorage();
            int tradeCount = tradeCodeStorage.GetTradeCount(trader.Id);
            _ = SendMilestoneEmbed(tradeCount, context.Channel, trader);
        }
    }

    private static int GenerateUniqueTradeID()
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        int randomValue = Random.Shared.Next(1000);
        return (int)((timestamp % int.MaxValue) * 1000 + randomValue);
    }

    private static string GetImageFolderPath()
    {
        string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        string imagesFolder = Path.Combine(baseDirectory, "Images");

        if (!Directory.Exists(imagesFolder))
        {
            Directory.CreateDirectory(imagesFolder);
        }

        return imagesFolder;
    }

    private static string SaveImageLocally(System.Drawing.Image image)
    {
        string imagesFolderPath = GetImageFolderPath();
        string filePath = Path.Combine(imagesFolderPath, $"image_{Guid.NewGuid()}.png");

#pragma warning disable CA1416 // Validate platform compatibility
        image.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);
#pragma warning restore CA1416 // Validate platform compatibility

        return filePath;
    }

    // Cambia la firma si aún no lo hiciste:
    private static async Task<(string, DiscordColor)> PrepareEmbedDetails(T pk, bool isMysteryEgg = false)
    {
        string embedImageUrl;
        string speciesImageUrl;

        if (pk.IsEgg || isMysteryEgg)
        {
            // Mystery Egg => SOLO huevo por tipo (sin sprite del Pokémon)
            string eggImageUrl = isMysteryEgg
                ? GetMysteryEggTypeImageUrl(pk)
                : GetEggTypeImageUrl(pk);

            // En Mystery Egg NO componemos con el Pokémon
            if (isMysteryEgg)
            {
                embedImageUrl = eggImageUrl; // solo huevo
                speciesImageUrl = eggImageUrl; // para el overlay de la pokéball más abajo
            }
            else
            {
                // Huevo normal: si quieres seguir mostrando el Pokémon dentro del huevo, deja esto;
                // si también lo quieres solo con cascarón, comenta las 3 líneas siguientes y usa embedImageUrl = eggImageUrl;
                speciesImageUrl = TradeExtensions<T>.PokeImg(pk, false, true, null);
                System.Drawing.Image combinedImage = await OverlaySpeciesOnEgg(eggImageUrl, speciesImageUrl);
                embedImageUrl = SaveImageLocally(combinedImage);
                speciesImageUrl = embedImageUrl; // a partir de aquí trabajamos sobre el archivo local
            }
        }
        else
        {
            bool canGmax = pk is PK8 pk8 && pk8.CanGigantamax;
            speciesImageUrl = TradeExtensions<T>.PokeImg(
                pk,
                canGmax,
                false,
                SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.PreferredImageSize
            );
            embedImageUrl = speciesImageUrl;
        }

        // --- Overlay de la Pokéball (usar SIEMPRE la imagen que vamos a mostrar: embedImageUrl) ---
        // Solo añadimos pokeball si NO es Mystery Egg
        if (!isMysteryEgg)
        {
            var strings = GameInfo.GetStrings("en");
            string ballName = strings.balllist[pk.Ball];
            if (ballName.Contains("(LA)"))
                ballName = "la" + ballName.Replace(" ", "").Replace("(LA)", "").ToLower();
            else
                ballName = ballName.Replace(" ", "").ToLower();

            string ballImgUrl = $"https://raw.githubusercontent.com/hexbyt3/sprites/main/AltBallImg/20x20/{ballName}.png";

            if (Uri.TryCreate(embedImageUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeFile)
            {
#pragma warning disable CA1416 // Validate platform compatibility
                using var localImage = await Task.Run(() => System.Drawing.Image.FromFile(uri.LocalPath));
#pragma warning restore CA1416 // Validate platform compatibility
                using var ballImage = await LoadImageFromUrl(ballImgUrl);
                if (ballImage != null)
                {
#pragma warning disable CA1416 // Validate platform compatibility
                    using (var graphics = Graphics.FromImage(localImage))
                    {
                        var pos = new Point(localImage.Width - ballImage.Width, localImage.Height - ballImage.Height);
#pragma warning disable CA1416 // Validate platform compatibility
                        graphics.DrawImage(ballImage, pos);
#pragma warning restore CA1416 // Validate platform compatibility
                    }
#pragma warning restore CA1416 // Validate platform compatibility
                    embedImageUrl = SaveImageLocally(localImage);
                }
            }
            else
            {
                (System.Drawing.Image finalCombinedImage, bool ballImageLoaded) = await OverlayBallOnSpecies(embedImageUrl, ballImgUrl);
                embedImageUrl = SaveImageLocally(finalCombinedImage);
                if (!ballImageLoaded)
                    Console.WriteLine($"No se pudo cargar la imagen de la pokeball: {ballImgUrl}");
            }
        }

        (int R, int G, int B) = await GetDominantColorAsync(embedImageUrl);
        return (embedImageUrl, new DiscordColor(R, G, B));
    }

    private static async Task<(System.Drawing.Image, bool)> OverlayBallOnSpecies(string speciesImageUrl, string ballImageUrl)
    {
        using var speciesImage = await LoadImageFromUrl(speciesImageUrl);
        if (speciesImage == null)
        {
            Console.WriteLine("Species image could not be loaded.");
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type.
            return (null, false);
#pragma warning restore CS8619 // Nullability of reference types in value doesn't match target type.
        }

        var ballImage = await LoadImageFromUrl(ballImageUrl);
        if (ballImage == null)
        {
            Console.WriteLine($"No se pudo cargar la imagen de la pokeball: {ballImageUrl}");
#pragma warning disable CA1416 // Validate platform compatibility
            return ((System.Drawing.Image)speciesImage.Clone(), false);
#pragma warning restore CA1416 // Validate platform compatibility
        }

        using (ballImage)
        {
#pragma warning disable CA1416 // Validate platform compatibility
            using (var graphics = Graphics.FromImage(speciesImage))
            {
                var ballPosition = new Point(speciesImage.Width - ballImage.Width, speciesImage.Height - ballImage.Height);
                graphics.DrawImage(ballImage, ballPosition);
            }
#pragma warning restore CA1416 // Validate platform compatibility

#pragma warning disable CA1416 // Validate platform compatibility
            return ((System.Drawing.Image)speciesImage.Clone(), true);
#pragma warning restore CA1416 // Validate platform compatibility
        }
    }

    private static async Task<System.Drawing.Image> OverlaySpeciesOnEgg(string eggImageUrl, string speciesImageUrl)
    {
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
        System.Drawing.Image eggImage = await LoadImageFromUrl(eggImageUrl);
        System.Drawing.Image speciesImage = await LoadImageFromUrl(speciesImageUrl);
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.

#pragma warning disable CA1416 // Validate platform compatibility
#pragma warning disable CS8602 // Dereference of a possibly null reference.
        double scaleRatio = Math.Min((double)eggImage.Width / speciesImage.Width, (double)eggImage.Height / speciesImage.Height);
#pragma warning restore CS8602 // Dereference of a possibly null reference.
        Size newSize = new((int)(speciesImage.Width * scaleRatio), (int)(speciesImage.Height * scaleRatio));
        System.Drawing.Image resizedSpeciesImage = new Bitmap(speciesImage, newSize);

        using (Graphics g = Graphics.FromImage(eggImage))
        {
            int speciesX = (eggImage.Width - resizedSpeciesImage.Width) / 2;
            int speciesY = (eggImage.Height - resizedSpeciesImage.Height) / 2;
            g.DrawImage(resizedSpeciesImage, speciesX, speciesY, resizedSpeciesImage.Width, resizedSpeciesImage.Height);
        }

        speciesImage.Dispose();
        resizedSpeciesImage.Dispose();

        double scale = Math.Min(128.0 / eggImage.Width, 128.0 / eggImage.Height);
        int newWidth = (int)(eggImage.Width * scale);
        int newHeight = (int)(eggImage.Height * scale);

        Bitmap finalImage = new(128, 128);

        using (Graphics g = Graphics.FromImage(finalImage))
        {
            int x = (128 - newWidth) / 2;
            int y = (128 - newHeight) / 2;
            g.DrawImage(eggImage, x, y, newWidth, newHeight);
        }

        eggImage.Dispose();
#pragma warning restore CA1416 // Validate platform compatibility
        return finalImage;
    }

    private static async Task<System.Drawing.Image?> LoadImageFromUrl(string url)
    {
        using HttpClient client = new();
        HttpResponseMessage response = await client.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"No se pudo cargar la imagen desde {url}. Código de estado: {response.StatusCode}");
            return null;
        }

        Stream stream = await response.Content.ReadAsStreamAsync();
        if (stream == null || stream.Length == 0)
        {
            Console.WriteLine($"No se recibieron datos o flujo vacío de {url}");
            return null;
        }

        try
        {
#pragma warning disable CA1416 // Validate platform compatibility
            return System.Drawing.Image.FromStream(stream);
#pragma warning restore CA1416 // Validate platform compatibility
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"No se pudo crear la imagen a partir de la transmisión. URL: {url}, excepción: {ex}");
            return null;
        }
    }

    private static async Task ScheduleFileDeletion(string filePath, int delayInMilliseconds)
    {
        await Task.Delay(delayInMilliseconds);
        DeleteFile(filePath);
    }

    private static void DeleteFile(string filePath)
    {
        if (File.Exists(filePath))
        {
            try
            {
                File.Delete(filePath);
            }
            catch (IOException ex)
            {
                Console.WriteLine($"Error al eliminar el archivo: {ex.Message}");
            }
        }
    }

    private static async Task SendMilestoneEmbed(int tradeCount, ISocketMessageChannel channel, SocketUser user)
    {
        if (MilestoneImages.TryGetValue(tradeCount, out string? imageUrl))
        {
            var embed = new EmbedBuilder()
                .WithTitle($"{user.Username}'s Milestone Medal")
                .WithDescription(GetMilestoneDescription(tradeCount))
                .WithColor(new DiscordColor(255, 215, 0)) // Gold color
                .WithThumbnailUrl(imageUrl)
                .Build();

            await channel.SendMessageAsync(embed: embed).ConfigureAwait(false);
        }
    }

    public static async Task<(int R, int G, int B)> GetDominantColorAsync(string imagePath)
    {
        try
        {
            Bitmap image = await LoadImageAsync(imagePath);

            var colorCount = new Dictionary<Color, int>();
#pragma warning disable CA1416 // Validate platform compatibility
            await Task.Run(() =>
            {
                for (int y = 0; y < image.Height; y++)
                {
                    for (int x = 0; x < image.Width; x++)
                    {
                        var pixelColor = image.GetPixel(x, y);

                        if (pixelColor.A < 128 || pixelColor.GetBrightness() > 0.9) continue;

                        var brightnessFactor = (int)(pixelColor.GetBrightness() * 100);
                        var saturationFactor = (int)(pixelColor.GetSaturation() * 100);
                        var combinedFactor = brightnessFactor + saturationFactor;

                        var quantizedColor = Color.FromArgb(
                            pixelColor.R / 10 * 10,
                            pixelColor.G / 10 * 10,
                            pixelColor.B / 10 * 10
                        );

                        if (colorCount.ContainsKey(quantizedColor))
                        {
                            colorCount[quantizedColor] += combinedFactor;
                        }
                        else
                        {
                            colorCount[quantizedColor] = combinedFactor;
                        }
                    }
                }
            });

            image.Dispose();
#pragma warning restore CA1416 // Validate platform compatibility

            if (colorCount.Count == 0)
                return (255, 255, 255);

            var dominantColor = colorCount.Aggregate((a, b) => a.Value > b.Value ? a : b).Key;
            return (dominantColor.R, dominantColor.G, dominantColor.B);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al procesar la imagen desde {imagePath}. Error: {ex.Message}");
            return (255, 255, 255);
        }
    }

    private static async Task<Bitmap> LoadImageAsync(string imagePath)
    {
        if (imagePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            using var httpClient = new HttpClient();
            using var response = await httpClient.GetAsync(imagePath);
            await using var stream = await response.Content.ReadAsStreamAsync();
#pragma warning disable CA1416 // Validate platform compatibility
            return new Bitmap(stream);
#pragma warning restore CA1416 // Validate platform compatibility
        }
        else
        {
#pragma warning disable CA1416 // Validate platform compatibility
            return new Bitmap(imagePath);
#pragma warning restore CA1416 // Validate platform compatibility
        }
    }

    private static async Task HandleDiscordExceptionAsync(SocketCommandContext context, SocketUser trader, HttpException ex)
    {
        string message = string.Empty;
        switch (ex.DiscordCode)
        {
            case DiscordErrorCode.InsufficientPermissions or DiscordErrorCode.MissingPermissions:
                {
                    var permissions = context.Guild.CurrentUser.GetPermissions(context.Channel as IGuildChannel);
                    if (!permissions.SendMessages)
                    {
                        message = "¡Debes otorgarme permisos para \"Enviar mensajes\"!";
                        Base.LogUtil.LogError(message, "QueueHelper");
                        return;
                    }
                    if (!permissions.ManageMessages)
                    {
                        var app = await context.Client.GetApplicationInfoAsync().ConfigureAwait(false);
                        var owner = app.Owner.Id;
                        message = $"<@{owner}> ¡Debes otorgarme permisos de \"Administrar mensajes\"!";
                    }
                }
                break;

            case DiscordErrorCode.CannotSendMessageToUser:
                {
                    message = context.User == trader ? $"⚠️ {context.User.Mention} Debes habilitar los mensajes privados para estar en la cola.!" : $"⚠️ {context.User.Mention} El usuario mencionado debe habilitar los mensajes privados para que estén en cola.!";
                }
                break;

            default:
                {
                    message = ex.DiscordCode != null ? $"Discord error {(int)ex.DiscordCode}: {ex.Reason}" : $"Http error {(int)ex.HttpCode}: {ex.Message}";
                }
                break;
        }
        await context.Channel.SendMessageAsync(message).ConfigureAwait(false);
    }

    private static string GetEggTypeImageUrl(T pk)
    {
        var pi = pk.PersonalInfo;
        byte typeIndex = pi.Type1;

        string[] typeNames = [
            "Normal", "Fighting", "Flying", "Poison", "Ground", "Rock", "Bug", "Ghost",
            "Steel", "Fire", "Water", "Grass", "Electric", "Psychic", "Ice", "Dragon",
            "Dark", "Fairy"
        ];

        string typeName = (typeIndex >= 0 && typeIndex < typeNames.Length)
            ? typeNames[typeIndex]
            : "Normal";

        return $"https://raw.githubusercontent.com/Daiivr/SysBot-Images/refs/heads/main/Eggs2/Egg_{typeName}.png";
    }

    private static string GetMysteryEggTypeImageUrl(T pk)
    {
        var pi = pk.PersonalInfo;
        byte typeIndex = pi.Type1;

        string[] typeNames =
        {
        "Normal", "Fighting", "Flying", "Poison", "Ground", "Rock", "Bug", "Ghost",
        "Steel", "Fire", "Water", "Grass", "Electric", "Psychic", "Ice", "Dragon",
        "Dark", "Fairy"
    };

        string typeName = (typeIndex >= 0 && typeIndex < typeNames.Length)
            ? typeNames[typeIndex]
            : "Normal";

        // Mystery Egg images folder (same repo, different folder)
        return $"https://raw.githubusercontent.com/Daiivr/SysBot-Images/refs/heads/main/MysteryEggs/MEgg_{typeName}.png";
    }

    public static (string, Embed) CreateLGLinkCodeSpriteEmbed(List<Pictocodes> lgcode)
    {
        int codecount = 0;
        List<System.Drawing.Image> spritearray = [];
        foreach (Pictocodes cd in lgcode)
        {
            var showdown = new ShowdownSet(cd.ToString());
            var sav = BlankSaveFile.Get(EntityContext.Gen7b, "pip");
            PKM pk = sav.GetLegalFromSet(showdown).Created;
#pragma warning disable CA1416 // Validate platform compatibility
            System.Drawing.Image png = pk.Sprite();
            var destRect = new Rectangle(-40, -65, 137, 130);
            var destImage = new Bitmap(137, 130);

            destImage.SetResolution(png.HorizontalResolution, png.VerticalResolution);

            using (var graphics = Graphics.FromImage(destImage))
            {
                graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                graphics.DrawImage(png, destRect, 0, 0, png.Width, png.Height, GraphicsUnit.Pixel);
            }
            png = destImage;
            spritearray.Add(png);
#pragma warning restore CA1416 // Validate platform compatibility
            codecount++;
        }

#pragma warning disable CA1416 // Validate platform compatibility
        int outputImageWidth = spritearray[0].Width + 20;
        int outputImageHeight = spritearray[0].Height - 65;

        Bitmap outputImage = new(outputImageWidth, outputImageHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        using (Graphics graphics = Graphics.FromImage(outputImage))
        {
            graphics.DrawImage(spritearray[0], new Rectangle(0, 0, spritearray[0].Width, spritearray[0].Height),
                new Rectangle(new Point(), spritearray[0].Size), GraphicsUnit.Pixel);
            graphics.DrawImage(spritearray[1], new Rectangle(50, 0, spritearray[1].Width, spritearray[1].Height),
                new Rectangle(new Point(), spritearray[1].Size), GraphicsUnit.Pixel);
            graphics.DrawImage(spritearray[2], new Rectangle(100, 0, spritearray[2].Width, spritearray[2].Height),
                new Rectangle(new Point(), spritearray[2].Size), GraphicsUnit.Pixel);
        }

        System.Drawing.Image finalembedpic = outputImage;
        var filename = $"{Directory.GetCurrentDirectory()}//finalcode.png";
        finalembedpic.Save(filename);
#pragma warning restore CA1416 // Validate platform compatibility

        filename = Path.GetFileName($"{Directory.GetCurrentDirectory()}//finalcode.png");
        Embed returnembed = new EmbedBuilder().WithTitle($"{lgcode[0]}, {lgcode[1]}, {lgcode[2]}").WithImageUrl($"attachment://{filename}").Build();
        return (filename, returnembed);
    }
}
