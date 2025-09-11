using Discord;
using Discord.WebSocket;
using PKHeX.Core;
using PKHeX.Core.AutoMod;
using PKHeX.Drawing.PokeSprite;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Color = Discord.Color;

namespace SysBot.Pokemon.Discord;

public class DiscordTradeNotifier<T> : IPokeTradeNotifier<T>
    where T : PKM, new()
{
    private T Data { get; set; }
    private PokeTradeTrainerInfo Info { get; }
    private int Code { get; }
    private List<Pictocodes>? LGCode { get; }
    private SocketUser Trader { get; }

    private int BatchTradeNumber { get; set; }
    private int TotalBatchTrades { get; }
    private bool IsMysteryEgg { get; }
    private bool IsMysteryTrade { get; }

    private readonly ulong _traderID;
    private int _uniqueTradeID;
    private Timer? _periodicUpdateTimer;
    private const int PeriodicUpdateInterval = 60000; // 60 seconds in milliseconds
    private bool _isTradeActive = true;
    private bool _initialUpdateSent = false;
    private bool _almostUpNotificationSent = false;
    private int _lastReportedPosition = -1;

    public readonly PokeTradeHub<T> Hub = SysCord<T>.Runner.Hub;

    public DiscordTradeNotifier(T data, PokeTradeTrainerInfo info, int code, SocketUser trader, int batchTradeNumber, int totalBatchTrades, bool isMysteryTrade, bool isMysteryEgg, List<Pictocodes>? lgcode)
    {
        Data = data;
        Info = info;
        Code = code;
        Trader = trader;
        BatchTradeNumber = batchTradeNumber;
        TotalBatchTrades = totalBatchTrades;
        IsMysteryEgg = isMysteryEgg;
        IsMysteryTrade = isMysteryTrade;
        LGCode = lgcode;
        _traderID = trader.Id;
        _uniqueTradeID = DiscordTradeNotifier<T>.GetUniqueTradeID();
    }

    public Action<PokeRoutineExecutor<T>>? OnFinish { private get; set; }

    public void UpdateBatchProgress(int currentBatchNumber, T currentPokemon, int uniqueTradeID)
    {
        BatchTradeNumber = currentBatchNumber;
        Data = currentPokemon;
        _uniqueTradeID = uniqueTradeID;
    }

    private static int GetUniqueTradeID()
    {
        // Generate a unique trade ID using timestamp or another method
        return (int)(DateTime.UtcNow.Ticks % int.MaxValue);
    }

    private void StartPeriodicUpdates()
    {
        // Dispose existing timer if it exists
        _periodicUpdateTimer?.Dispose();

        _isTradeActive = true;

        // Create a new timer that sends queue position updates every minute
        _periodicUpdateTimer = new Timer(async _ =>
        {
            if (!_isTradeActive)
                return;

            // Check the current position using the unique trade ID
            var position = Hub.Queues.Info.CheckPosition(_traderID, _uniqueTradeID, PokeRoutineType.LinkTrade);
            if (!position.InQueue)
                return;

            var currentPosition = position.Position < 1 ? 1 : position.Position;

            // Store the latest position for future reference
            _lastReportedPosition = currentPosition;

            var botct = Hub.Bots.Count;
            var currentETA = currentPosition > botct ? Hub.Config.Queues.EstimateDelay(currentPosition, botct) : 0;

            // Only send update if the trade is still in queue (not being processed)
            if (position.InQueue && position.Detail != null)
            {
                // Check if the trade is ready to be processed (next in line)
                bool isNextInLine = currentPosition <= botct;

                if (isNextInLine && currentPosition <= 2 && _initialUpdateSent && !_almostUpNotificationSent)
                {
                    // Send a more prominent notification when user is getting close to their turn
                    // Only send this notification once
                    _almostUpNotificationSent = true;

                    var batchInfo = TotalBatchTrades > 1 ? $"\n\n**Importante:** Esta es una operación en lote con {TotalBatchTrades} Pokémon. ¡Por favor permanece en el intercambio hasta que todos estén completados!" : "";

                    var almostUpEmbed = new EmbedBuilder
                    {
                        Color = Color.Gold,
                        Title = "🎯 ¡Tu turno está por llegar!",
                        Description = $"Prepárate, tu intercambio comenzará en breve.\n\n📌 Posición actual en la cola: **{currentPosition}**.{batchInfo}",
                        Footer = new EmbedFooterBuilder
                        {
                            Text = $"⏳ Tiempo estimado de espera: {(currentETA > 0 ? $"{currentETA} minutos" : "Menos de un minuto")}"
                        },
                        Timestamp = DateTimeOffset.Now
                    }.Build();

                    await Trader.SendMessageAsync(embed: almostUpEmbed).ConfigureAwait(false);
                }
                else if (!position.Detail.Trade.IsProcessing && _initialUpdateSent && !_almostUpNotificationSent && _lastReportedPosition % 3 == 0)
                {
                    // Regular queue update - only send every 3 position changes to avoid spam
                    // Don't send regular updates if we've already sent the "almost up" notification
                    var queueUpdateEmbed = new EmbedBuilder
                    {
                        Color = Color.Blue,
                        Title = "📢 Actualización de tu posición",
                        Description = $"Aún estás en la cola.\n\n📌 Posición actual: **{currentPosition}**",
                        Footer = new EmbedFooterBuilder
                        {
                            Text = $"⏳ Tiempo estimado de espera: {(currentETA > 0 ? $"{currentETA} minutos" : "Menos de un minuto")}"
                        },
                        Timestamp = DateTimeOffset.Now
                    }.Build();

                    await Trader.SendMessageAsync(embed: queueUpdateEmbed).ConfigureAwait(false);
                }
            }
        },
        null,
        PeriodicUpdateInterval, // Start after 60 seconds
        PeriodicUpdateInterval); // Repeat every 60 seconds
    }

    private void StopPeriodicUpdates()
    {
        _isTradeActive = false;
        _periodicUpdateTimer?.Dispose();
        _periodicUpdateTimer = null;
    }

    public async Task SendInitialQueueUpdate()
    {
        var position = Hub.Queues.Info.CheckPosition(_traderID, _uniqueTradeID, PokeRoutineType.LinkTrade);
        var currentPosition = position.Position < 1 ? 1 : position.Position;
        var botct = Hub.Bots.Count;
        var currentETA = currentPosition > botct ? Hub.Config.Queues.EstimateDelay(currentPosition, botct) : 0;

        _lastReportedPosition = currentPosition;

        var batchDescription = TotalBatchTrades > 1
            ? $"Tu solicitud de intercambio por lotes ({TotalBatchTrades} Pokémon) ha sido añadida a la cola.\n\n⚠️ **Instrucciones importantes:**\n• Permanece en el intercambio durante los {TotalBatchTrades} intercambios\n• Ten listos los {TotalBatchTrades} Pokémon para entregar\n• No salgas hasta ver el mensaje de finalización\n\nPosición en la cola: **{currentPosition}**"
            : $"Tu solicitud de intercambio ha sido añadida a la cola. Posición en la cola: **{currentPosition}**";

        var initialEmbed = new EmbedBuilder
        {
            Color = Color.Green,
            Title = TotalBatchTrades > 1
                ? "🎁 Solicitud de intercambio por lotes en cola"
                : "✅ Solicitud de intercambio en cola",
            Description = batchDescription,
            Footer = new EmbedFooterBuilder
            {
                Text = $"⏳ Tiempo estimado de espera: {(currentETA > 0 ? $"{currentETA} minutos" : "Menos de un minuto")}"
            },
            Timestamp = DateTimeOffset.Now
        }.Build();


        await Trader.SendMessageAsync(embed: initialEmbed).ConfigureAwait(false);

        _initialUpdateSent = true;

        // Start sending periodic updates about queue position
        StartPeriodicUpdates();
    }

    public void TradeInitialize(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info)
    {
        // Update unique trade ID from the detail
        _uniqueTradeID = info.UniqueTradeID;

        // Stop periodic updates as we're now moving to the active trading phase
        StopPeriodicUpdates();

        // Mark trade as active to prevent any further queue messages
        _almostUpNotificationSent = true;

        int language = 2;
        var speciesName = SpeciesName.GetSpeciesName(Data.Species, language);
        var receive = Data.Species == 0 ? string.Empty : $" ({Data.Nickname})";

        if (info.Type == PokeTradeType.Item)
        {
            speciesName = GameInfo.GetStrings("en").itemlist[Data.HeldItem];
            receive = $" ({speciesName})";
        }
        else
        {
            speciesName = SpeciesName.GetSpeciesName(Data.Species, language);
            receive = IsMysteryTrade ? " (Pokemon Misterioso)" :
                      (Data.Species == 0 ? string.Empty : $" ({Data.Nickname})");
        }

        if (Data is PK9)
        {
            string message;
            if (TotalBatchTrades > 1)
            {
                if (BatchTradeNumber == 1)
                {
                    message = $"¡Iniciando tu intercambio por lotes! Serán {TotalBatchTrades} Pokémon.\n\n" +
                             $"**Intercambio 1/{TotalBatchTrades}**: {speciesName}{receive}\n\n" +
                             $"⚠️ **IMPORTANTE:** Permanece en el intercambio hasta que se completen los {TotalBatchTrades} intercambios.";
                }
                else
                {
                    message = $"Preparando intercambio {BatchTradeNumber}/{TotalBatchTrades}: {speciesName}{receive}";
                }
            }
            else
            {
                message = $"Inicializando intercambio{receive}. Por favor, prepárate.";
            }

            EmbedHelper.SendTradeInitializingEmbedAsync(Trader, speciesName, Code, IsMysteryTrade, IsMysteryEgg, message).ConfigureAwait(false);
        }
        else if (Data is PB7 && LGCode != null)
        {
            var (thefile, lgcodeembed) = CreateLGLinkCodeSpriteEmbed(LGCode);
            Trader.SendFileAsync(thefile, $"Inicializando intercambio{receive}. Por favor, prepárate. Tu código es:", embed: lgcodeembed).ConfigureAwait(false);
        }
        else
        {
            EmbedHelper.SendTradeInitializingEmbedAsync(Trader, speciesName, Code, IsMysteryTrade, IsMysteryEgg).ConfigureAwait(false);
        }
    }

    public void TradeSearching(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info)
    {
        // Ensure periodic updates are stopped (extra safety check)
        StopPeriodicUpdates();

        var name = Info.TrainerName;
        var trainer = string.IsNullOrEmpty(name) ? string.Empty : $" {name}";

        if (Data is PB7 && LGCode != null && LGCode.Count != 0)
        {
            var batchInfo = TotalBatchTrades > 1 ? $" (Trade {BatchTradeNumber}/{TotalBatchTrades})" : "";
            var message = $"Estoy esperando por ti,**{trainer}{batchInfo}**! __Tienes **40 segundos**__. Mi IGN es **{routine.InGameName}**.";
            Trader.SendMessageAsync(message).ConfigureAwait(false);
        }
        else
        {
            string? additionalMessage = null;
            if (TotalBatchTrades > 1)
            {
                if (BatchTradeNumber == 1)
                {
                    additionalMessage = $"Iniciando intercambio por lotes ({TotalBatchTrades} Pokémon en total). **¡Selecciona tu primer Pokémon!**";
                }
                else
                {
                    var speciesName = SpeciesName.GetSpeciesName(Data.Species, 2);
                    additionalMessage = $"Intercambio {BatchTradeNumber}/{TotalBatchTrades}: Ahora intercambiando {speciesName}. **¡Selecciona tu siguiente Pokémon!**";
                }
            }

            EmbedHelper.SendTradeSearchingEmbedAsync(Trader, trainer, routine.InGameName, additionalMessage).ConfigureAwait(false);
        }
    }

    public void TradeCanceled(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, PokeTradeResult msg)
    {
        OnFinish?.Invoke(routine);
        StopPeriodicUpdates();

        var cancelMessage = TotalBatchTrades > 1
            ? $"❌ Intercambio por lotes cancelado: {msg}. Todos los intercambios restantes han sido cancelados."
            : msg.ToString();

        EmbedHelper.SendTradeCanceledEmbedAsync(Trader, cancelMessage).ConfigureAwait(false);
    }

    public void TradeFinished(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, T result)
    {
        // Only stop updates and invoke OnFinish for single trades or the last trade in a batch
        if (info.TotalBatchTrades <= 1 || info.BatchTradeNumber == info.TotalBatchTrades)
        {
            OnFinish?.Invoke(routine);
            StopPeriodicUpdates();
        }

        var tradedToUser = Data.Species;

        // Create different messages based on whether this is a single trade or part of a batch
        string message;
        if (info.TotalBatchTrades > 1)
        {
            if (info.BatchTradeNumber == info.TotalBatchTrades)
            {
                // Final trade in the batch
                message = $"✅ **¡Todos los {info.TotalBatchTrades} trades se completaron con éxito!** ¡Gracias por intercambiar!";
            }
            else
            {
                // Mid-batch trade
                var speciesName = SpeciesName.GetSpeciesName(Data.Species, 2);
                message = $"✅ Intercambio {info.BatchTradeNumber}/{info.TotalBatchTrades} completado ({speciesName}).\n" +
                          $"Preparando intercambio {info.BatchTradeNumber + 1}/{info.TotalBatchTrades}...";
            }

            // Send the embed only on the first trade of the batch (same behavior as your previous block)
            if (info.BatchTradeNumber == 1)
            {
                _ = EmbedHelper
                    .SendTradeFinishedEmbedAsync(Trader, message, Data, info.IsMysteryTrade, info.IsMysteryEgg, info.Type)
                    .ConfigureAwait(false);
            }
        }
        else
        {
            if (info.Type == PokeTradeType.Item)
            {
                string itemName = GameInfo.GetStrings("en").itemlist[Data.HeldItem];
                message = $"✅ Trade finalizado. ¡Disfruta de tu **{itemName}**!";
            }
            else
            {
                message = tradedToUser != 0
                    ? (info.IsMysteryTrade ? $"✅ Trade finalizado. ¡Has recibido un **Pokemon Misterioso**!" :
                       info.IsMysteryEgg ? $"✅ Trade finalizado. ¡Disfruta de tu **Huevo Misterioso**!" :
                       $"✅ Trade finalizado. Disfruta de tu **{(Species)tradedToUser}**!")
                    : $"✅ Trade finalizado!";
            }

            // Single trade: always send the embed
            _ = EmbedHelper
                .SendTradeFinishedEmbedAsync(Trader, message, Data, info.IsMysteryTrade, info.IsMysteryEgg, info.Type)
                .ConfigureAwait(false);
        }

        // Devolver siempre el PKM recibido si la opción está activada (soporta batch)
        if (result is not null && result.Species != 0 && Hub.Config.Discord.ReturnPKMs)
        {
            // Nombre legible del Pokémon que el usuario nos envió
            string tradedSpeciesName = Enum.GetName(typeof(Species), result.Species) ?? "Desconocido";

            // Mensaje contextual según si es lote o unitario
            string fileMessage = info.TotalBatchTrades > 1
                ? $"▼ Aquí está el **{tradedSpeciesName}** que me enviaste (Intercambio {info.BatchTradeNumber}/{info.TotalBatchTrades}) ▼"
                : $"▼ Aquí está el **{tradedSpeciesName}** que me enviaste ▼";

            _ = Trader.SendPKMAsync(result, fileMessage).ConfigureAwait(false);
        }
    }

    public void SendNotification(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, string message)
    {
        // Add batch context to notifications if applicable
        if (TotalBatchTrades > 1 && !message.Contains("Trade") && !message.Contains("batch"))
        {
            message = $"Trade {BatchTradeNumber}/{TotalBatchTrades}: {message}";
        }

        EmbedHelper.SendNotificationEmbedAsync(Trader, message).ConfigureAwait(false);
    }

    public void SendNotification(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, PokeTradeSummary message)
    {
        if (message.ExtraInfo is SeedSearchResult r)
        {
            SendNotificationZ3(r);
            return;
        }

        var msg = message.Summary;
        if (message.Details.Count > 0)
            msg += ", " + string.Join(", ", message.Details.Select(z => $"{z.Heading}: {z.Detail}"));
        Trader.SendMessageAsync(msg).ConfigureAwait(false);
    }

    public void SendNotification(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, T result, string message)
    {
        // Always send the Pokemon if requested, regardless of trade type
        if (result.Species != 0 && (Hub.Config.Discord.ReturnPKMs || info.Type == PokeTradeType.Dump))
        {
            Trader.SendPKMAsync(result, message).ConfigureAwait(false);
        }
    }

    private void SendNotificationZ3(SeedSearchResult r)
    {
        var lines = r.ToString();

        var embed = new EmbedBuilder { Color = Color.LighterGrey };
        embed.AddField(x =>
        {
            x.Name = $"Seed: {r.Seed:X16}";
            x.Value = lines;
            x.IsInline = false;
        });
        var msg = $"Aquí están los detalles para `{r.Seed:X16}`:";
        Trader.SendMessageAsync(msg, embed: embed.Build()).ConfigureAwait(false);
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
            codecount++;
        }
        int outputImageWidth = spritearray[0].Width + 20;

        int outputImageHeight = spritearray[0].Height - 65;

        Bitmap outputImage = new Bitmap(outputImageWidth, outputImageHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

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
        var filename = $"{System.IO.Directory.GetCurrentDirectory()}//finalcode.png";
        finalembedpic.Save(filename);
        filename = System.IO.Path.GetFileName($"{System.IO.Directory.GetCurrentDirectory()}//finalcode.png");
        Embed returnembed = new EmbedBuilder().WithTitle($"{lgcode[0]}, {lgcode[1]}, {lgcode[2]}").WithImageUrl($"attachment://{filename}").Build();
        return (filename, returnembed);
    }
}
