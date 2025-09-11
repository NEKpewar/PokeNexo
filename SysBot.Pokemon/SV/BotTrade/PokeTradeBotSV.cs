using PKHeX.Core;
using PKHeX.Core.Searching;
using SysBot.Base;
using SysBot.Base.Util;
using SysBot.Pokemon.Helpers;
using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using static SysBot.Base.SwitchButton;
using static SysBot.Pokemon.PokeDataOffsetsSV;
using static SysBot.Pokemon.TradeHub.SpecialRequests;

namespace SysBot.Pokemon;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public class PokeTradeBotSV(PokeTradeHub<PK9> Hub, PokeBotState Config) : PokeRoutineExecutor9SV(Config), ICountBot, ITradeBot
{
    public readonly TradeAbuseSettings AbuseSettings = Hub.Config.TradeAbuse;

    /// <summary>
    /// Folder to dump received trade data to.
    /// </summary>
    /// <remarks>If null, will skip dumping.</remarks>
    private readonly FolderSettings DumpSetting = Hub.Config.Folder;

    private readonly TradeSettings TradeSettings = Hub.Config.Trade;

    // Cached offsets that stay the same per session.
    private ulong BoxStartOffset;

    private ulong ConnectedOffset;

    private uint DisplaySID;

    private uint DisplayTID;

    // Track the last Pokémon we were offered since it persists between trades.
    private byte[] lastOffered = new byte[8];


    // Store the current save's OT and TID/SID for comparison.
    private string OT = string.Empty;

    private ulong OverworldOffset;

    private ulong PortalOffset;

    // Stores whether we returned all the way to the overworld, which repositions the cursor.
    private bool StartFromOverworld = true;

    private ulong TradePartnerNIDOffset;

    private ulong TradePartnerOfferedOffset;

    public event EventHandler<Exception>? ConnectionError;

    public event EventHandler? ConnectionSuccess;

    public ICountSettings Counts => TradeSettings;

    /// <summary>
    /// Tracks failed synchronized starts to attempt to re-sync.
    /// </summary>
    public int FailedBarrier { get; private set; }

    /// <summary>
    /// Synchronized start for multiple bots.
    /// </summary>
    public bool ShouldWaitAtBarrier { get; private set; }

    public override Task HardStop()
    {
        UpdateBarrier(false);
        return CleanExit(CancellationToken.None);
    }

    public override async Task MainLoop(CancellationToken token)
    {
        try
        {
            Hub.Queues.Info.CleanStuckTrades();
            await InitializeHardware(Hub.Config.Trade, token).ConfigureAwait(false);

            Log("Identificando los datos del entrenador de la consola host.");
            var sav = await IdentifyTrainer(token).ConfigureAwait(false);
            OT = sav.OT;
            DisplaySID = sav.DisplaySID;
            DisplayTID = sav.DisplayTID;
            RecentTrainerCache.SetRecentTrainer(sav);
            await InitializeSessionOffsets(token).ConfigureAwait(false);
            OnConnectionSuccess();

            // Force the bot to go through all the motions again on its first pass.
            StartFromOverworld = true;

            Log($"Iniciando el bucle principal {nameof(PokeTradeBotSV)}.");
            await InnerLoop(sav, token).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            OnConnectionError(e);
            throw;
        }

        Log($"Finalizando el bucle {nameof(PokeTradeBotSV)}.");
        await HardStop().ConfigureAwait(false);
    }

    public override async Task RebootAndStop(CancellationToken t)
    {
        Hub.Queues.Info.CleanStuckTrades();
        await Task.Delay(2_000, t).ConfigureAwait(false);
        await ReOpenGame(Hub.Config, t).ConfigureAwait(false);
        await HardStop().ConfigureAwait(false);
        await Task.Delay(2_000, t).ConfigureAwait(false);
        if (!t.IsCancellationRequested)
        {
            Log("Reiniciando el bucle principal.");
            await MainLoop(t).ConfigureAwait(false);
        }
    }

    protected virtual async Task<(PK9 toSend, PokeTradeResult check)> GetEntityToSend(SAV9SV sav, PokeTradeDetail<PK9> poke, PK9 offered, byte[] oldEC, PK9 toSend, PartnerDataHolder partnerID, SpecialTradeType? stt, CancellationToken token)
    {
        return poke.Type switch
        {
            PokeTradeType.Random => await HandleRandomLedy(sav, poke, offered, toSend, partnerID, token).ConfigureAwait(false),
            PokeTradeType.Clone => await HandleClone(sav, poke, offered, oldEC, token).ConfigureAwait(false),
            PokeTradeType.FixOT => await HandleFixOT(sav, poke, offered, partnerID, token).ConfigureAwait(false),
            PokeTradeType.Seed when stt is not SpecialTradeType.WonderCard => await HandleClone(sav, poke, offered, oldEC, token).ConfigureAwait(false),
            PokeTradeType.Seed when stt is SpecialTradeType.WonderCard => await JustInject(sav, offered, token).ConfigureAwait(false),
            _ => (toSend, PokeTradeResult.Success),
        };
    }

    protected virtual (PokeTradeDetail<PK9>? detail, uint priority) GetTradeData(PokeRoutineType type)
    {
        string botName = Connection.Name;

        // First check the specific type's queue
        if (Hub.Queues.TryDequeue(type, out var detail, out var priority, botName))
        {
            return (detail, priority);
        }

        // If we're doing FlexTrade, also check the Batch queue
        if (type == PokeRoutineType.FlexTrade)
        {
            if (Hub.Queues.TryDequeue(PokeRoutineType.Batch, out detail, out priority, botName))
            {
                return (detail, priority);
            }
        }

        if (Hub.Queues.TryDequeueLedy(out detail))
        {
            return (detail, PokeTradePriorities.TierFree);
        }
        return (null, PokeTradePriorities.TierFree);
    }

    // Upon connecting, their Nintendo ID will instantly update.
    protected virtual async Task<bool> WaitForTradePartner(CancellationToken token)
    {
        Log("Esperando al entrenador...");
        int ctr = (Hub.Config.Trade.TradeConfiguration.TradeWaitTime * 1_000) - 2_000;
        await Task.Delay(2_000, token).ConfigureAwait(false);
        while (ctr > 0)
        {
            await Task.Delay(1_000, token).ConfigureAwait(false);
            ctr -= 1_000;
            var newNID = await GetTradePartnerNID(TradePartnerNIDOffset, token).ConfigureAwait(false);
            if (newNID != 0)
            {
                TradePartnerOfferedOffset = await SwitchConnection.PointerAll(Offsets.LinkTradePartnerPokemonPointer, token).ConfigureAwait(false);
                return true;
            }

            // Fully load into the box.
            await Task.Delay(1_000, token).ConfigureAwait(false);
        }
        return false;
    }

    private async Task<PK9> ApplyAutoOT(PK9 toSend, TradeMyStatus tradePartner, SAV9SV sav, CancellationToken token)
    {
        if (toSend.Version == GameVersion.GO)
        {
            var goClone = toSend.Clone();
            goClone.OriginalTrainerName = tradePartner.OT;

            ClearOTTrash(goClone, tradePartner);

            if (!toSend.ChecksumValid)
                goClone.RefreshChecksum();

            Log("Se aplicó solo el nombre del OT al Pokémon proveniente de Pokémon GO.");
            await SetBoxPokemonAbsolute(BoxStartOffset, goClone, token, sav).ConfigureAwait(false);
            return goClone;
        }

        if (toSend is IHomeTrack pk && pk.HasTracker)
        {
            Log("Rastreador de Home detectado. No se puede aplicar Auto OT.");
            return toSend;
        }

        if (toSend.Generation != toSend.Format)
        {
            Log("No se pueden aplicar los detalles del socio: El manejador actual no puede tener un OT de género diferente.");
            return toSend;
        }

        bool isMysteryGift = toSend.FatefulEncounter;

        // Check if Mystery Gift has legitimate preset OT/TID/SID (not PKHeX defaults)
        bool hasDefaultTrainerInfo = toSend.OriginalTrainerName.Equals("Gengar", StringComparison.OrdinalIgnoreCase) &&
                                    toSend.TID16 == 12345 &&
                                    toSend.SID16 == 54321;

        if (isMysteryGift && !hasDefaultTrainerInfo)
        {
            Log("Se detectó un Regalo Misterioso con OT/TID/SID predefinidos. Se omite completamente el AutoOT.");
            return toSend;
        }

        var cln = toSend.Clone();

        if (isMysteryGift)
        {
            Log("Se detectó un Regalo Misterioso. Solo se aplicará la información del OT, preservando el idioma.");
            cln.OriginalTrainerGender = (byte)tradePartner.Gender;
            cln.TrainerTID7 = (uint)Math.Abs(tradePartner.DisplayTID);
            cln.TrainerSID7 = (uint)Math.Abs(tradePartner.DisplaySID);
            cln.OriginalTrainerName = tradePartner.OT;
        }
        else
        {
            cln.OriginalTrainerGender = (byte)tradePartner.Gender;
            cln.TrainerTID7 = (uint)Math.Abs(tradePartner.DisplayTID);
            cln.TrainerSID7 = (uint)Math.Abs(tradePartner.DisplaySID);
            cln.Language = tradePartner.Language;
            cln.OriginalTrainerName = tradePartner.OT;
        }

        ClearOTTrash(cln, tradePartner);

        ushort species = toSend.Species;
        GameVersion version;
        switch (species)
        {
            case (ushort)Species.Koraidon:
            case (ushort)Species.GougingFire:
            case (ushort)Species.RagingBolt:
                version = GameVersion.SL;
                Log("Pokémon exclusivo versión Escarlata, cambiando la versión a Escarlata.");
                break;

            case (ushort)Species.Miraidon:
            case (ushort)Species.IronCrown:
            case (ushort)Species.IronBoulder:
                version = GameVersion.VL;
                Log("Pokémon exclusivo de la versión Violeta, cambiando la versión a Violeta.");
                break;

            default:
                version = (GameVersion)tradePartner.Game;
                break;
        }
        cln.Version = version;

        if (!toSend.IsNicknamed)
            cln.ClearNickname();

        if (toSend.IsShiny)
            cln.PID = (uint)((cln.TID16 ^ cln.SID16 ^ (cln.PID & 0xFFFF) ^ toSend.ShinyXor) << 16) | (cln.PID & 0xFFFF);

        if (!toSend.ChecksumValid)
            cln.RefreshChecksum();

        var tradeSV = new LegalityAnalysis(cln);
        if (tradeSV.Valid)
        {
            Log("Pokémon es válido, utilizare la información del entrenador comercial (Auto OT).");
            await SetBoxPokemonAbsolute(BoxStartOffset, cln, token, sav).ConfigureAwait(false);
            return cln;
        }
        else
        {
            Log("No se puede aplicar AutoOT a los Pokémon de intercambio.");
            return toSend;
        }
    }

    private static void ClearOTTrash(PK9 pokemon, TradeMyStatus tradePartner)
    {
        Span<byte> trash = pokemon.OriginalTrainerTrash;
        trash.Clear();
        string name = tradePartner.OT;
        int maxLength = trash.Length / 2;
        int actualLength = Math.Min(name.Length, maxLength);
        for (int i = 0; i < actualLength; i++)
        {
            char value = name[i];
            trash[i * 2] = (byte)value;
            trash[(i * 2) + 1] = (byte)(value >> 8);
        }
        if (actualLength < maxLength)
        {
            trash[actualLength * 2] = 0x00;
            trash[(actualLength * 2) + 1] = 0x00;
        }
    }

    private async Task<PokeTradeResult> ConfirmAndStartTrading(PokeTradeDetail<PK9> detail, CancellationToken token)
    {
        // We'll keep watching B1S1 for a change to indicate a trade started -> should try quitting at that point.
        var oldEC = await SwitchConnection.ReadBytesAbsoluteAsync(BoxStartOffset, 8, token).ConfigureAwait(false);

        await Click(A, 3_000, token).ConfigureAwait(false);
        for (int i = 0; i < Hub.Config.Trade.TradeConfiguration.MaxTradeConfirmTime; i++)
        {
            // We can fall out of the box if the user offers, then quits.
            if (!await IsInBox(PortalOffset, token).ConfigureAwait(false))
                return PokeTradeResult.TrainerLeft;

            await Click(A, 1_000, token).ConfigureAwait(false);

            // EC is detectable at the start of the animation.
            var newEC = await SwitchConnection.ReadBytesAbsoluteAsync(BoxStartOffset, 8, token).ConfigureAwait(false);
            if (!newEC.SequenceEqual(oldEC))
            {
                await Task.Delay(25_000, token).ConfigureAwait(false);
                return PokeTradeResult.Success;
            }
        }

        // If we don't detect a B1S1 change, the trade didn't go through in that time.
        return PokeTradeResult.TrainerTooSlow;
    }

    // Should be used from the overworld. Opens X menu, attempts to connect online, and enters the Portal.
    // The cursor should be positioned over Link Trade.
    private async Task<bool> ConnectAndEnterPortal(CancellationToken token)
    {
        if (!await IsOnOverworld(OverworldOffset, token).ConfigureAwait(false))
            await RecoverToOverworld(token).ConfigureAwait(false);

        Log("Abriendo el Poké Portal.");

        // Open the X Menu.
        await Click(X, 1_000, token).ConfigureAwait(false);

        // Handle the news popping up.
        if (await SwitchConnection.IsProgramRunning(LibAppletWeID, token).ConfigureAwait(false))
        {
            Log("Noticias detectadas, se cerrarán una vez cargadas!");
            await Task.Delay(5_000, token).ConfigureAwait(false);
            await Click(B, 2_000, token).ConfigureAwait(false);
        }

        // Scroll to the bottom of the Main Menu, so we don't need to care if Picnic is unlocked.
        await Click(DRIGHT, 0_300, token).ConfigureAwait(false);
        await PressAndHold(DDOWN, 1_000, 1_000, token).ConfigureAwait(false);
        await Click(DUP, 0_200, token).ConfigureAwait(false);
        await Click(DUP, 0_200, token).ConfigureAwait(false);
        await Click(DUP, 0_200, token).ConfigureAwait(false);
        await Click(A, 1_000, token).ConfigureAwait(false);

        return await SetUpPortalCursor(token).ConfigureAwait(false);
    }

    private async Task<bool> ConnectToOnline(PokeTradeHubConfig config, CancellationToken token)
    {
        int attemptCount = 0;
        const int maxAttempt = 5;
        const int waitTime = 10; // time in minutes to wait after max attempts

        while (true) // Loop until a successful connection is made or the task is canceled
        {
            if (token.IsCancellationRequested)
            {
                Log("Intento de conexión cancelado.");
                break;
            }
            try
            {
                if (await IsConnectedOnline(ConnectedOffset, token).ConfigureAwait(false))
                {
                    Log("Conexión establecida con éxito.");
                    break; // Exit the loop if connected successfully
                }

                if (attemptCount >= maxAttempt)
                {
                    Log($"No se pudo conectar después de {maxAttempt} intentos. Se asume un softban. Iniciando espera de {waitTime} minutos antes de reintentar.");
                    // Waiting process
                    await Click(B, 0_500, token).ConfigureAwait(false);
                    await Click(B, 0_500, token).ConfigureAwait(false);
                    Log($"Esperando {waitTime} minutos antes de intentar reconectar.");
                    await Task.Delay(TimeSpan.FromMinutes(waitTime), token).ConfigureAwait(false);
                    Log("Intentando volver a abrir el juego.");
                    await ReOpenGame(Hub.Config, token).ConfigureAwait(false);
                    attemptCount = 0; // Reset attempt count
                }

                attemptCount++;
                Log($"Intento {attemptCount} de {maxAttempt}: Intentando conectarse en línea...");

                // Connection attempt logic
                await Click(X, 3_000, token).ConfigureAwait(false);
                await Click(L, 5_000 + config.Timings.MiscellaneousSettings.ExtraTimeConnectOnline, token).ConfigureAwait(false);

                // Wait a bit before rechecking the connection status
                await Task.Delay(5000, token).ConfigureAwait(false); // Wait 5 seconds before rechecking

                if (attemptCount < maxAttempt)
                {
                    Log("Revisando nuevamente el estado de la conexión en línea...");
                    // Wait and recheck logic
                    await Click(B, 0_500, token).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Log($"Ocurrió una excepción durante el intento de conexión: {ex.Message}");

                if (attemptCount >= maxAttempt)
                {
                    Log($"No se pudo conectar después de {maxAttempt} intentos debido a una excepción. Esperando {waitTime} minutos antes de reintentar.");
                    await Task.Delay(TimeSpan.FromMinutes(waitTime), token).ConfigureAwait(false);
                    Log("Intentando volver a abrir el juego.");
                    await ReOpenGame(Hub.Config, token).ConfigureAwait(false);
                    attemptCount = 0;
                }
            }
        }

        // Final steps after connection is established
        await Task.Delay(3_000 + config.Timings.MiscellaneousSettings.ExtraTimeConnectOnline, token).ConfigureAwait(false);

        return true;
    }

    private async Task DoNothing(CancellationToken token)
    {
        Log("Ninguna tarea asignada. Esperando nueva asignación de tarea.");
        while (!token.IsCancellationRequested && Config.NextRoutineType == PokeRoutineType.Idle)
            await Task.Delay(1_000, token).ConfigureAwait(false);
    }

    private async Task DoTrades(SAV9SV sav, CancellationToken token)
    {
        var type = Config.CurrentRoutineType;
        int waitCounter = 0;
        await SetCurrentBox(0, token).ConfigureAwait(false);
        while (!token.IsCancellationRequested && Config.NextRoutineType == type)
        {
            var (detail, priority) = GetTradeData(type);
            if (detail is null)
            {
                await WaitForQueueStep(waitCounter++, token).ConfigureAwait(false);
                continue;
            }
            waitCounter = 0;

            detail.IsProcessing = true;
            string tradetype = $" ({detail.Type})";
            Log($"Empezando el próximo intercambio de bots de {type}{tradetype} Obteniendo datos...");
            Hub.Config.Stream.StartTrade(this, detail, Hub);
            Hub.Queues.StartTrade(this, detail);

            await PerformTrade(sav, detail, type, priority, token).ConfigureAwait(false);
        }
    }

    private async Task ExitTradeToPortal(bool unexpected, CancellationToken token)
    {
        await Task.Delay(1_000, token).ConfigureAwait(false);
        if (await IsInPokePortal(PortalOffset, token).ConfigureAwait(false))
            return;

        if (unexpected)
            Log("Comportamiento inesperado, recuperando el Portal.");

        // Ensure we're not in the box first.
        // Takes a long time for the Portal to load up, so once we exit the box, wait 5 seconds.
        Log("Dejando la caja...");
        var attempts = 0;
        while (await IsInBox(PortalOffset, token).ConfigureAwait(false))
        {
            await Click(B, 1_000, token).ConfigureAwait(false);
            if (!await IsInBox(PortalOffset, token).ConfigureAwait(false))
            {
                await Task.Delay(1_000, token).ConfigureAwait(false);
                break;
            }

            await Click(A, 1_000, token).ConfigureAwait(false);
            if (!await IsInBox(PortalOffset, token).ConfigureAwait(false))
            {
                await Task.Delay(1_000, token).ConfigureAwait(false);
                break;
            }

            await Click(B, 1_000, token).ConfigureAwait(false);
            if (!await IsInBox(PortalOffset, token).ConfigureAwait(false))
            {
                await Task.Delay(1_000, token).ConfigureAwait(false);
                break;
            }

            // Didn't make it out of the box for some reason.
            if (++attempts > 20)
            {
                Log("No se pudo salir del cuadro, reiniciando el juego.");
                if (!await RecoverToOverworld(token).ConfigureAwait(false))
                    await RestartGameSV(token).ConfigureAwait(false);
                await ConnectAndEnterPortal(token).ConfigureAwait(false);
                return;
            }
        }

        // Wait for the portal to load.
        Log("Esperando que se cargue el portal...");
        attempts = 0;
        while (!await IsInPokePortal(PortalOffset, token).ConfigureAwait(false))
        {
            await Task.Delay(1_000, token).ConfigureAwait(false);
            if (await IsInPokePortal(PortalOffset, token).ConfigureAwait(false))
                break;

            // Didn't make it into the portal for some reason.
            if (++attempts > 40)
            {
                Log("No se pudo cargar el portal, reiniciando el juego.");
                if (!await RecoverToOverworld(token).ConfigureAwait(false))
                    await RestartGameSV(token).ConfigureAwait(false);
                await ConnectAndEnterPortal(token).ConfigureAwait(false);
                return;
            }
        }
    }

    private async Task<TradeMyStatus> GetTradePartnerFullInfo(CancellationToken token)
    {
        // We're able to see both users' MyStatus, but one of them will be ourselves.
        var trader_info = await GetTradePartnerMyStatus(Offsets.Trader1MyStatusPointer, token).ConfigureAwait(false);
        if (trader_info.OT == OT && trader_info.DisplaySID == DisplaySID && trader_info.DisplayTID == DisplayTID) // This one matches ourselves.
            trader_info = await GetTradePartnerMyStatus(Offsets.Trader2MyStatusPointer, token).ConfigureAwait(false);
        return trader_info;
    }

    private void HandleAbortedTrade(PokeTradeDetail<PK9> detail, PokeRoutineType type, uint priority, PokeTradeResult result)
    {
        // Skip processing if we've already handled the notification (e.g., NoTrainerFound)
        if (result == PokeTradeResult.NoTrainerFound)
            return;

        detail.IsProcessing = false;
        if (result.ShouldAttemptRetry() && detail.Type != PokeTradeType.Random && !detail.IsRetry)
        {
            detail.IsRetry = true;
            Hub.Queues.Enqueue(type, detail, Math.Min(priority, PokeTradePriorities.Tier2));
            detail.SendNotification(this, $"⚠️ Oops! Algo ocurrió. Intentemoslo una vez mas.");
        }
        else
        {
            detail.SendNotification(this, $"⚠️ Oops! Algo ocurrió. Cancelando el trade: **{result.GetDescription()}**.");
            detail.TradeCanceled(this, result);
        }
    }

    private async Task<(PK9 toSend, PokeTradeResult check)> HandleClone(SAV9SV sav, PokeTradeDetail<PK9> poke, PK9 offered, byte[] oldEC, CancellationToken token)
    {
        if (Hub.Config.Discord.ReturnPKMs)
            poke.SendNotification(this, offered, $"¡Esto es lo que me mostraste - {GameInfo.GetStrings("en").Species[offered.Species]}");

        var la = new LegalityAnalysis(offered);
        if (!la.Valid)
        {
            Log($"Solicitud de clonación (de {poke.Trainer.TrainerName}) ha detectado un Pokémon no válido: {GameInfo.GetStrings("en").Species[offered.Species]}.");
            if (DumpSetting.Dump)
                DumpPokemon(DumpSetting.DumpFolder, "hacked", offered);

            var report = la.Report();
            Log(report);
            poke.SendNotification(this, $"❌ Este Pokémon no es __**legal**__ según los controles de legalidad de __PKHeX__. Tengo prohibido clonar esto. Cancelando trade...");
            poke.SendNotification(this, report);

            return (offered, PokeTradeResult.IllegalTrade);
        }

        var clone = offered.Clone();
        if (Hub.Config.Legality.ResetHOMETracker)
            clone.Tracker = 0;

        poke.SendNotification(this, $"⚠️ He __clonado__ tu **{GameInfo.GetStrings("en").Species[clone.Species]}!**\nAhora __preciosa__ **B** para cancelar y luego seleccione un Pokémon que no quieras para reliazar el tradeo.");
        Log($"Cloné un {GameInfo.GetStrings("en").Species[clone.Species]}. Esperando que el usuario cambie su Pokémon...");

        // Separate this out from WaitForPokemonChanged since we compare to old EC from original read.
        var partnerFound = await ReadUntilChanged(TradePartnerOfferedOffset, oldEC, 15_000, 0_200, false, true, token).ConfigureAwait(false);
        if (!partnerFound)
        {
            poke.SendNotification(this, "**Porfavor cambia el pokemon ahora o cancelare el tradeo!!!**");

            // They get one more chance.
            partnerFound = await ReadUntilChanged(TradePartnerOfferedOffset, oldEC, 15_000, 0_200, false, true, token).ConfigureAwait(false);
        }
        // Check if the user has cancelled the trade
        if (!await IsInBox(PortalOffset, token).ConfigureAwait(false))
        {
            Log("El usuario canceló la operación. Saliendo...");
            await ExitTradeToPortal(false, token).ConfigureAwait(false);
            return (offered, PokeTradeResult.TrainerTooSlow);
        }
        var pk2 = await ReadUntilPresent(TradePartnerOfferedOffset, 25_000, 1_000, BoxFormatSlotSize, token).ConfigureAwait(false);
        if (!partnerFound || pk2 is null || SearchUtil.HashByDetails(pk2) == SearchUtil.HashByDetails(offered))
        {
            Log("El entrenador comercial no cambió sus Pokémon.");
            await ExitTradeToPortal(false, token).ConfigureAwait(false);
            return (offered, PokeTradeResult.TrainerTooSlow);
        }

        await Click(A, 0_800, token).ConfigureAwait(false);
        await SetBoxPokemonAbsolute(BoxStartOffset, clone, token, sav).ConfigureAwait(false);

        return (clone, PokeTradeResult.Success);
    }

    private async Task<(PK9 toSend, PokeTradeResult check)> HandleFixOT(SAV9SV sav, PokeTradeDetail<PK9> poke, PK9 offered, PartnerDataHolder partnerID, CancellationToken token)
    {
        if (Hub.Config.Discord.ReturnPKMs)
            poke.SendNotification(this, offered, $"¡Esto es lo que me mostraste - {GameInfo.GetStrings("en").Species[offered.Species]}");

        var adOT = TradeExtensions<PK9>.HasAdName(offered, out _);
        var laInit = new LegalityAnalysis(offered);
        if (!adOT && laInit.Valid)
        {
            poke.SendNotification(this, $"⚠️ No se detectó ningún anuncio en Apodo ni OT, y el Pokémon es legal. Saliendo del comercio.");
            return (offered, PokeTradeResult.TrainerRequestBad);
        }

        var clone = (PK9)offered.Clone();
        if (Hub.Config.Legality.ResetHOMETracker)
            clone.Tracker = 0;

        string shiny = string.Empty;
        if (!TradeExtensions<PK9>.ShinyLockCheck(offered.Species, TradeExtensions<PK9>.FormOutput(offered.Species, offered.Form, out _), $"{(Ball)offered.Ball}"))
            shiny = $"\nShiny: {(offered.ShinyXor == 0 ? "Square" : offered.IsShiny ? "Star" : "No")}";
        else shiny = "\nShiny: No";

        var name = partnerID.TrainerName;
        var ball = $"\n{(Ball)offered.Ball}";
        var extraInfo = $"OT: {name}{ball}{shiny}";
        var set = ShowdownParsing.GetShowdownText(offered).Split('\n').ToList();
        set.Remove(set.Find(x => x.Contains("Shiny")) ?? "");
        set.InsertRange(1, extraInfo.Split('\n'));

        if (!laInit.Valid)
        {
            Log($"La solicitud de FixOT ha detectado un Pokémon ilegal de {name}: {(Species)offered.Species}");
            var report = laInit.Report();
            Log(laInit.Report());
            poke.SendNotification(this, $"❌ **Los Pokémon mostrados no son legales. Intentando regenerar...**\n\n```{report}```");
            if (DumpSetting.Dump)
                DumpPokemon(DumpSetting.DumpFolder, "hacked", offered);
        }

        if (clone.FatefulEncounter)
        {
            clone.SetDefaultNickname(laInit);
            var info = new SimpleTrainerInfo { Gender = clone.OriginalTrainerGender, Language = clone.Language, OT = name, TID16 = clone.TID16, SID16 = clone.SID16, Generation = 9 };
            var mg = EncounterEvent.GetAllEvents().Where(x => x.Species == clone.Species && x.Form == clone.Form && x.IsShiny == clone.IsShiny && x.OriginalTrainerName == clone.OriginalTrainerName).ToList();
            if (mg.Count > 0)
                clone = TradeExtensions<PK9>.CherishHandler(mg.First(), info);
            else clone = (PK9)sav.GetLegal(AutoLegalityWrapper.GetTemplate(new ShowdownSet(string.Join("\n", set))), out _);
        }
        else
        {
            clone = (PK9)sav.GetLegal(AutoLegalityWrapper.GetTemplate(new ShowdownSet(string.Join("\n", set))), out _);
        }

        clone = (PK9)TradeExtensions<PK9>.TrashBytes(clone, new LegalityAnalysis(clone));
        clone.ResetPartyStats();

        var la = new LegalityAnalysis(clone);
        if (!la.Valid)
        {
            poke.SendNotification(this, $"❌ Este Pokémon no es __**legal**__ según los controles de legalidad de __PKHeX__. No pude arreglar esto. Cancelando trade...");
            return (clone, PokeTradeResult.IllegalTrade);
        }

        TradeExtensions<PK9>.HasAdName(offered, out string detectedAd);
        poke.SendNotification(this, $"{(!laInit.Valid ? "**Legalizado" : "**Apodo/OT corregido para")} {(Species)clone.Species}** (anuncio detectado: {detectedAd}) ¡Ahora confirma el intercambio!");
        Log($"{(!laInit.Valid ? "Legalizado" : "Arreglado Nickname/OT para")} {(Species)clone.Species}!");

        // Wait for a bit in case trading partner tries to switch out.
        await Task.Delay(2_000, token).ConfigureAwait(false);

        var pk2 = await ReadUntilPresent(TradePartnerOfferedOffset, 15_000, 0_200, BoxFormatSlotSize, token).ConfigureAwait(false);
        bool changed = pk2 is null || pk2.Species != offered.Species || offered.OriginalTrainerName != pk2.OriginalTrainerName;
        if (changed)
        {
            // They get one more chance.
            poke.SendNotification(this, "**Ofrece el Pokémon mostrado originalmente o me voy!**");

            var timer = 10_000;
            while (changed)
            {
                pk2 = await ReadUntilPresent(TradePartnerOfferedOffset, 2_000, 0_500, BoxFormatSlotSize, token).ConfigureAwait(false);
                changed = pk2 == null || clone.Species != pk2.Species || offered.OriginalTrainerName != pk2.OriginalTrainerName;
                await Task.Delay(1_000, token).ConfigureAwait(false);
                timer -= 1_000;

                if (timer <= 0)
                    break;
            }
        }

        if (changed)
        {
            poke.SendNotification(this, $"⚠️ Pokémon intercambiado y no cambiado de nuevo. Saliendo del intercambio.");
            Log("El socio comercial no quiso despedir su ad-mon.");
            return (offered, PokeTradeResult.TrainerTooSlow);
        }

        await Click(A, 0_800, token).ConfigureAwait(false);
        await SetBoxPokemonAbsolute(BoxStartOffset, clone, token, sav).ConfigureAwait(false);

        return (clone, PokeTradeResult.Success);
    }

    private async Task<(PK9 toSend, PokeTradeResult check)> HandleRandomLedy(SAV9SV sav, PokeTradeDetail<PK9> poke, PK9 offered, PK9 toSend, PartnerDataHolder partner, CancellationToken token)
    {
        // Allow the trade partner to do a Ledy swap.
        var config = Hub.Config.Distribution;
        var trade = Hub.Ledy.GetLedyTrade(offered, partner.TrainerOnlineID, config.LedySpecies);
        if (trade != null)
        {
            if (trade.Type == LedyResponseType.AbuseDetected)
            {
                var msg = $"⚠️ Se ha detectado a {partner.TrainerName} por abusar de las operaciones de Ledy.";
                if (AbuseSettings.EchoNintendoOnlineIDLedy)
                    msg += $"\nID: {partner.TrainerOnlineID}";
                if (!string.IsNullOrWhiteSpace(AbuseSettings.LedyAbuseEchoMention))
                    msg = $"{AbuseSettings.LedyAbuseEchoMention} {msg}";
                EchoUtil.Echo(msg);

                return (toSend, PokeTradeResult.SuspiciousActivity);
            }

            toSend = trade.Receive;
            poke.TradeData = toSend;

            poke.SendNotification(this, $"⏳ Inyectando el Pokémon solicitado.");
            await SetBoxPokemonAbsolute(BoxStartOffset, toSend, token, sav).ConfigureAwait(false);
        }
        else if (config.LedyQuitIfNoMatch)
        {
            var nickname = offered.IsNicknamed ? $" (Apodo: \"{offered.Nickname}\")" : string.Empty;
            poke.SendNotification(this, $"⚠️ No se encontró coincidencia para el {GameInfo.GetStrings("en").Species[offered.Species]} ofrecido{nickname}.");
            return (toSend, PokeTradeResult.TrainerRequestBad);
        }

        return (toSend, PokeTradeResult.Success);
    }

    // These don't change per session and we access them frequently, so set these each time we start.
    private async Task InitializeSessionOffsets(CancellationToken token)
    {
        Log("Compensaciones de la sesión de almacenamiento en caché ...");
        BoxStartOffset = await SwitchConnection.PointerAll(Offsets.BoxStartPokemonPointer, token).ConfigureAwait(false);
        OverworldOffset = await SwitchConnection.PointerAll(Offsets.OverworldPointer, token).ConfigureAwait(false);
        PortalOffset = await SwitchConnection.PointerAll(Offsets.PortalBoxStatusPointer, token).ConfigureAwait(false);
        ConnectedOffset = await SwitchConnection.PointerAll(Offsets.IsConnectedPointer, token).ConfigureAwait(false);
        TradePartnerNIDOffset = await SwitchConnection.PointerAll(Offsets.LinkTradePartnerNIDPointer, token).ConfigureAwait(false);
    }

    private async Task InnerLoop(SAV9SV sav, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            Config.IterateNextRoutine();
            var task = Config.CurrentRoutineType switch
            {
                PokeRoutineType.Idle => DoNothing(token),
                _ => DoTrades(sav, token),
            };
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (SocketException e)
            {
                if (e.StackTrace != null)
                    Connection.LogError(e.StackTrace);
                var attempts = Hub.Config.Timings.MiscellaneousSettings.ReconnectAttempts;
                var delay = Hub.Config.Timings.MiscellaneousSettings.ExtraReconnectDelay;
                var protocol = Config.Connection.Protocol;
                if (!await TryReconnect(attempts, delay, protocol, token).ConfigureAwait(false))
                    return;
            }
        }
    }

    private async Task<(PK9 toSend, PokeTradeResult check)> JustInject(SAV9SV sav, PK9 offered, CancellationToken token)
    {
        await Click(A, 0_800, token).ConfigureAwait(false);
        await SetBoxPokemonAbsolute(BoxStartOffset, offered, token, sav).ConfigureAwait(false);

        for (int i = 0; i < 5; i++)
            await Click(A, 0_500, token).ConfigureAwait(false);

        return (offered, PokeTradeResult.Success);
    }

    private void OnConnectionError(Exception ex)
    {
        ConnectionError?.Invoke(this, ex);
    }

    private void OnConnectionSuccess()
    {
        ConnectionSuccess?.Invoke(this, EventArgs.Empty);
    }

    private async Task<PokeTradeResult> PerformBatchTrade(SAV9SV sav, PokeTradeDetail<PK9> poke, CancellationToken token)
    {
        int completedTrades = 0;
        var startingDetail = poke;
        var originalTrainerID = startingDetail.Trainer.ID;

        var tradesToProcess = poke.BatchTrades ?? [poke.TradeData];
        var totalBatchTrades = tradesToProcess.Count;

        // Cache trade partner info after first successful connection
        TradeMyStatus? cachedTradePartnerInfo = null;

        void SendCollectedPokemonAndCleanup()
        {
            var allReceived = BatchTracker.GetReceivedPokemon(originalTrainerID);
            if (allReceived.Count > 0)
            {
                poke.SendNotification(this, $"Enviándote los {allReceived.Count} Pokémon que me intercambiaste antes de la interrupción.");

                Log($"Devolviendo {allReceived.Count} Pokémon al entrenador {originalTrainerID}.");

                // Send each Pokemon directly instead of calling TradeFinished
                for (int j = 0; j < allReceived.Count; j++)
                {
                    var pokemon = allReceived[j];
                    var speciesName = SpeciesName.GetSpeciesName(pokemon.Species, 2);
                    Log($"  - Devolviendo: {speciesName} (Checksum: {pokemon.Checksum:X8})");

                    // Send the Pokemon directly to the notifier
                    poke.SendNotification(this, pokemon, $"Pokémon que me intercambiaste: {speciesName}");
                    Thread.Sleep(500);
                }
            }
            else
            {
                Log($"No se encontraron Pokémon para devolver al entrenador {originalTrainerID}.");
            }

            BatchTracker.ClearReceivedPokemon(originalTrainerID);
            BatchTracker.ReleaseBatch(originalTrainerID, startingDetail.UniqueTradeID);
            poke.IsProcessing = false;
            Hub.Queues.Info.Remove(new TradeEntry<PK9>(poke, originalTrainerID, PokeRoutineType.Batch, poke.Trainer.TrainerName, poke.UniqueTradeID));
        }

        for (int i = 0; i < totalBatchTrades; i++)
        {
            var currentTradeIndex = i;
            var toSend = tradesToProcess[currentTradeIndex];

            poke.TradeData = toSend;
            poke.Notifier.UpdateBatchProgress(currentTradeIndex + 1, toSend, poke.UniqueTradeID);

            if (currentTradeIndex == 0)
            {
                // First trade - prepare Pokemon before searching for partner
                if (toSend.Species != 0)
                    await SetBoxPokemonAbsolute(BoxStartOffset, toSend, token, sav).ConfigureAwait(false);
            }
            else
            {
                // Subsequent trades - we're already in the trade screen
                // FIRST: Prepare the Pokemon BEFORE allowing user to offer
                poke.SendNotification(this, $"¡Intercambio {completedTrades} completado! **NO OFREZCAS AÚN** - Preparando tu próximo Pokémon ({completedTrades + 1}/{totalBatchTrades})...");

                // Wait for trade animation to fully complete
                await Task.Delay(5_000, token).ConfigureAwait(false);

                // Prepare the next Pokemon with AutoOT if needed
                if (toSend.Species != 0)
                {
                    if (Hub.Config.Legality.UseTradePartnerInfo && !poke.IgnoreAutoOT && cachedTradePartnerInfo != null)
                    {
                        toSend = await ApplyAutoOT(toSend, cachedTradePartnerInfo, sav, token);
                        tradesToProcess[currentTradeIndex] = toSend; // Update the list
                    }
                    await SetBoxPokemonAbsolute(BoxStartOffset, toSend, token, sav).ConfigureAwait(false);
                }

                // Give time for the Pokemon to be properly set
                await Task.Delay(1_000, token).ConfigureAwait(false);

                // NOW tell the user they can offer
                poke.SendNotification(this, $"**¡Listo!** Ahora puedes ofrecer tu Pokémon para el intercambio {currentTradeIndex + 1}/{totalBatchTrades}.");

                // Store the last offered state before allowing new offers
                lastOffered = await SwitchConnection.ReadBytesAbsoluteAsync(TradePartnerOfferedOffset, 8, token).ConfigureAwait(false);

                // Additional delay to ensure we're ready to detect offers
                await Task.Delay(5_000, token).ConfigureAwait(false);
            }

            // For first trade only - search for partner
            if (currentTradeIndex == 0)
            {
                await Click(A, 0_500, token).ConfigureAwait(false);
                await Click(A, 0_500, token).ConfigureAwait(false);

                await ClearTradePartnerNID(TradePartnerNIDOffset, token).ConfigureAwait(false);

                WaitAtBarrierIfApplicable(token);
                await Click(A, 1_000, token).ConfigureAwait(false);

                poke.TradeSearching(this);
                var partnerFound = await WaitForTradePartner(token).ConfigureAwait(false);

                if (token.IsCancellationRequested)
                {
                    StartFromOverworld = true;
                            await ExitTradeToPortal(false, token).ConfigureAwait(false);
                    poke.SendNotification(this, "⚠️ Cancelando los intercambios por lote. La rutina ha sido interrumpida.");
                    SendCollectedPokemonAndCleanup();
                    return PokeTradeResult.RoutineCancel;
                }

                if (!partnerFound)
                {
                    poke.IsProcessing = false;
                    poke.SendNotification(this, "⚠️ No se encontró un compañero de intercambio. Cancelando los intercambios por lote.");
                    poke.TradeCanceled(this, PokeTradeResult.NoTrainerFound);
                    SendCollectedPokemonAndCleanup();

                    if (!await RecoverToPortal(token).ConfigureAwait(false))
                    {
                        Log("No se pudo recuperar el portal.");
                        await RecoverToOverworld(token).ConfigureAwait(false);
                    }
                    return PokeTradeResult.NoTrainerFound;
                }

                Hub.Config.Stream.EndEnterCode(this);

                var cnt = 0;
                while (!await IsInBox(PortalOffset, token).ConfigureAwait(false))
                {
                    await Task.Delay(0_500, token).ConfigureAwait(false);
                    if (++cnt > 20)
                    {
                        await Click(A, 1_000, token).ConfigureAwait(false);
                        SendCollectedPokemonAndCleanup();

                        if (!await RecoverToPortal(token).ConfigureAwait(false))
                        {
                            Log("No se recuperó al portal.");
                            await RecoverToOverworld(token).ConfigureAwait(false);
                        }
                        poke.SendNotification(this, "⚠️ No se pudo entrar a la caja de intercambio. Cancelando los intercambios por lote.");
                        return PokeTradeResult.RecoverOpenBox;
                    }
                }
                await Task.Delay(3_000 + Hub.Config.Timings.MiscellaneousSettings.ExtraTimeOpenBox, token).ConfigureAwait(false);

                // Get trade partner info and verify
                var tradePartnerFullInfo = await GetTradePartnerFullInfo(token).ConfigureAwait(false);
                cachedTradePartnerInfo = tradePartnerFullInfo; // Cache for subsequent trades
                var tradePartner = new TradePartnerSV(tradePartnerFullInfo);
                var trainerNID = await GetTradePartnerNID(TradePartnerNIDOffset, token).ConfigureAwait(false);
                RecordUtil<PokeTradeBotSV>.Record($"Iniciando\t{trainerNID:X16}\t{tradePartner.TrainerName}\t{poke.Trainer.TrainerName}\t{poke.Trainer.ID}\t{poke.ID}\t{toSend.EncryptionConstant:X8}");

                var tradeCodeStorage = new TradeCodeStorage();
                var existingTradeDetails = tradeCodeStorage.GetTradeDetails(poke.Trainer.ID);

                bool shouldUpdateOT = existingTradeDetails?.OT != tradePartner.TrainerName;
                bool shouldUpdateTID = existingTradeDetails?.TID != int.Parse(tradePartner.TID7);
                bool shouldUpdateSID = existingTradeDetails?.SID != int.Parse(tradePartner.SID7);

                if (shouldUpdateOT || shouldUpdateTID || shouldUpdateSID)
                {
                    string? ot = shouldUpdateOT ? tradePartner.TrainerName : existingTradeDetails?.OT;
                    int? tid = shouldUpdateTID ? int.Parse(tradePartner.TID7) : existingTradeDetails?.TID;
                    int? sid = shouldUpdateSID ? int.Parse(tradePartner.SID7) : existingTradeDetails?.SID;

                    if (ot != null && tid.HasValue && sid.HasValue)
                    {
                        tradeCodeStorage.UpdateTradeDetails(poke.Trainer.ID, ot, tid.Value, sid.Value);
                    }
                    else
                    {
                        Log("El OT, TID o SID es nulo. Omitir UpdateTradeDetails.");
                    }
                }

                var partnerCheck = CheckPartnerReputation(this, poke, trainerNID, tradePartner.TrainerName, AbuseSettings, token);
                if (partnerCheck != PokeTradeResult.Success)
                {
                    poke.SendNotification(this, "⚠️ Falló la verificación del compañero de intercambio. Cancelando los intercambios por lote.");
                    SendCollectedPokemonAndCleanup();
                    await Click(A, 1_000, token).ConfigureAwait(false);
                    await ExitTradeToPortal(false, token).ConfigureAwait(false);
                    return partnerCheck;
                }

                var tradeOffered = await ReadUntilChanged(TradePartnerOfferedOffset, lastOffered, 10_000, 0_500, false, true, token).ConfigureAwait(false);
                if (!tradeOffered)
                {
                    poke.SendNotification(this, "⚠️ Falló la verificación del compañero de intercambio.Cancelando los intercambios por lote.");
                    SendCollectedPokemonAndCleanup();
                    await ExitTradeToPortal(false, token).ConfigureAwait(false);
                    return PokeTradeResult.TrainerTooSlow;
                }

                Log($"Se encontró un compañero de intercambio por enlace: {tradePartner.TrainerName}-{tradePartner.TID7} (ID: {trainerNID})");
                poke.SendNotification(this, $"Entrenador encontrado: **{tradePartner.TrainerName}**.\n\n▼\n Aqui esta tu Informacion\n **TID**: __{tradePartner.TID7}__\n **SID**: __{tradePartner.SID7}__\n▲\n\n Esperando por un __Pokémon__...");

                // Apply AutoOT for first trade if needed
                if (Hub.Config.Legality.UseTradePartnerInfo && !poke.IgnoreAutoOT)
                {
                    toSend = await ApplyAutoOT(toSend, tradePartnerFullInfo, sav, token).ConfigureAwait(false);
                    poke.TradeData = toSend;
                    if (toSend.Species != 0)
                        await SetBoxPokemonAbsolute(BoxStartOffset, toSend, token, sav).ConfigureAwait(false);
                }
            }

            // Wait for user to offer a Pokemon
            if (currentTradeIndex == 0)
            {
                poke.SendNotification(this, $"Por favor, ofrece tu Pokémon para el intercambio 1/{totalBatchTrades}.");
            }

            var offered = await ReadUntilPresent(TradePartnerOfferedOffset, 25_000, 1_000, BoxFormatSlotSize, token).ConfigureAwait(false);
            var oldEC = await SwitchConnection.ReadBytesAbsoluteAsync(TradePartnerOfferedOffset, 8, token).ConfigureAwait(false);
            if (offered == null || offered.Species == 0 || !offered.ChecksumValid)
            {
                Log("El intercambio finalizó porque no se ofreció un Pokémon válido.");
                poke.SendNotification(this, $"⚠️ Se ofreció un Pokémon no válido para el intercambio {currentTradeIndex + 1}/{totalBatchTrades}. Cancelando los intercambios restantes.");
                SendCollectedPokemonAndCleanup();
                await ExitTradeToPortal(false, token).ConfigureAwait(false);
                return PokeTradeResult.TrainerTooSlow;
            }

            // Get trade partner info for subsequent trades
            var trainer = new PartnerDataHolder(0, "", "");
            if (cachedTradePartnerInfo != null)
            {
                var tradePartner = new TradePartnerSV(cachedTradePartnerInfo);
                trainer = new PartnerDataHolder(0, tradePartner.TrainerName, tradePartner.TID7);
            }

            PokeTradeResult update;
            (toSend, update) = await GetEntityToSend(sav, poke, offered, oldEC, toSend, trainer, null, token).ConfigureAwait(false);
            if (update != PokeTradeResult.Success)
            {
                poke.SendNotification(this, $"⚠️ Falló la verificación de actualización para el intercambio {currentTradeIndex + 1}/{totalBatchTrades}. Cancelando los intercambios restantes.");
                SendCollectedPokemonAndCleanup();
                await ExitTradeToPortal(false, token).ConfigureAwait(false);
                return update;
            }

            Log($"Confirming trade {currentTradeIndex + 1}/{totalBatchTrades}.");
            var tradeResult = await ConfirmAndStartTrading(poke, token).ConfigureAwait(false);
            if (tradeResult != PokeTradeResult.Success)
            {
                poke.SendNotification(this, $"⚠️ Falló la confirmación del intercambio {currentTradeIndex + 1}/{totalBatchTrades}. Cancelando los intercambios restantes.");
                SendCollectedPokemonAndCleanup();
                await ExitTradeToPortal(false, token).ConfigureAwait(false);
                return tradeResult;
            }

            if (token.IsCancellationRequested)
            {
                StartFromOverworld = true;
                    poke.SendNotification(this, "⚠️ Cancelando los intercambios restantes. La rutina ha sido interrumpida.");
                SendCollectedPokemonAndCleanup();
                await ExitTradeToPortal(false, token).ConfigureAwait(false);
                return PokeTradeResult.RoutineCancel;
            }

            var received = await ReadPokemon(BoxStartOffset, BoxFormatSlotSize, token).ConfigureAwait(false);
            if (SearchUtil.HashByDetails(received) == SearchUtil.HashByDetails(toSend) && received.Checksum == toSend.Checksum)
            {
                poke.SendNotification(this, $"⚠️ El compañero no completó el intercambio {currentTradeIndex + 1}/{totalBatchTrades}. Cancelando los intercambios restantes.");
                SendCollectedPokemonAndCleanup();
                await ExitTradeToPortal(false, token).ConfigureAwait(false);
                return PokeTradeResult.TrainerTooSlow;
            }

            UpdateCountsAndExport(poke, received, toSend);

            // Get the trainer NID for logging
            var logTrainerNID = currentTradeIndex == 0 ? await GetTradePartnerNID(TradePartnerNIDOffset, token).ConfigureAwait(false) : 0;
            LogSuccessfulTrades(poke, logTrainerNID, trainer.TrainerName);

            BatchTracker.AddReceivedPokemon(originalTrainerID, received);
            completedTrades = currentTradeIndex + 1;
            Log($"Pokémon recibido {received.Species} (Checksum: {received.Checksum:X8}) agregado al rastreador por lotes para el entrenador {originalTrainerID} (Intercambio {completedTrades}/{totalBatchTrades})");

            if (completedTrades == totalBatchTrades)
            {
                // Get all collected Pokemon before cleaning anything up
                var allReceived = BatchTracker.GetReceivedPokemon(originalTrainerID);
                Log($"Trades por lotes completados. Se encontraron {allReceived.Count} Pokémon almacenados para el entrenador {originalTrainerID}");

                // First send notification that trades are complete
                poke.SendNotification(this, $"✅ ¡Todos los intercambios por lotes completados! ¡Gracias por tradear!");

                // Send back all received Pokemon if ReturnPKMs is enabled
                if (Hub.Config.Discord.ReturnPKMs && allReceived.Count > 0)
                {
                    poke.SendNotification(this, $"Aquí están los {allReceived.Count} Pokémon que me intercambiaste:");

                    // Send each Pokemon directly instead of calling TradeFinished
                    for (int j = 0; j < allReceived.Count; j++)
                    {
                        var pokemon = allReceived[j];
                        var speciesName = SpeciesName.GetSpeciesName(pokemon.Species, 2);
                        Log($"  - Devolviendo: {speciesName} (Checksum: {pokemon.Checksum:X8})");

                        // Send the Pokemon directly to the notifier
                        poke.SendNotification(this, pokemon, $"Pokémon que me cambiaste: {speciesName}");
                        await Task.Delay(500, token).ConfigureAwait(false);
                    }
                }

                // Now call TradeFinished ONCE for the entire batch with the last received Pokemon
                // This signals that the entire batch trade transaction is complete
                if (allReceived.Count > 0)
                {
                    poke.TradeFinished(this, allReceived[^1]);
                }
                else
                {
                    poke.TradeFinished(this, received);
                }

                // Mark the batch as fully completed and clean up
                Hub.Queues.CompleteTrade(this, startingDetail);
                BatchTracker.ClearReceivedPokemon(originalTrainerID);

                // Exit the trade state to prevent further searching
                await ExitTradeToPortal(false, token).ConfigureAwait(false);
                poke.IsProcessing = false;
                break;
            }

            // Store the last offered Pokemon before moving to next trade
            lastOffered = await SwitchConnection.ReadBytesAbsoluteAsync(TradePartnerOfferedOffset, 8, token).ConfigureAwait(false);
        }

        // Ensure we exit properly even if the loop breaks unexpectedly
        await ExitTradeToPortal(false, token).ConfigureAwait(false);
        poke.IsProcessing = false;
        return PokeTradeResult.Success;
    }

    private async Task PerformTrade(SAV9SV sav, PokeTradeDetail<PK9> detail, PokeRoutineType type, uint priority, CancellationToken token)
    {
        PokeTradeResult result;
        try
        {
            // All trades go through PerformLinkCodeTrade which will handle both regular and batch trades
            result = await PerformLinkCodeTrade(sav, detail, token).ConfigureAwait(false);

            if (result != PokeTradeResult.Success)
            {
                if (detail.Type == PokeTradeType.Batch)
                    await HandleAbortedBatchTrade(detail, type, priority, result, token).ConfigureAwait(false);
                else
                    HandleAbortedTrade(detail, type, priority, result);
            }
        }
        catch (SocketException socket)
        {
            Log(socket.Message);
            result = PokeTradeResult.ExceptionConnection;
            if (detail.Type == PokeTradeType.Batch)
                await HandleAbortedBatchTrade(detail, type, priority, result, token).ConfigureAwait(false);
            else
                HandleAbortedTrade(detail, type, priority, result);
            throw;
        }
        catch (Exception e)
        {
            Log(e.Message);
            result = PokeTradeResult.ExceptionInternal;
            if (detail.Type == PokeTradeType.Batch)
                await HandleAbortedBatchTrade(detail, type, priority, result, token).ConfigureAwait(false);
            else
                HandleAbortedTrade(detail, type, priority, result);
        }
    }

    private async Task<PokeTradeResult> PerformLinkCodeTrade(SAV9SV sav, PokeTradeDetail<PK9> poke, CancellationToken token)
    {
        // Update Barrier Settings
        UpdateBarrier(poke.IsSynchronized);
        poke.TradeInitialize(this);
        Hub.Config.Stream.EndEnterCode(this);

        // Handle connection and portal entry
        if (!await EnsureConnectedAndInPortal(token).ConfigureAwait(false))
        {
            return PokeTradeResult.RecoverStart;
        }

        // Enter Link Trade and code
        if (!await EnterLinkTradeAndCode(poke.Code, token).ConfigureAwait(false))
        {
            return PokeTradeResult.RecoverStart;
        }

        StartFromOverworld = false;

        // Route to appropriate trade handling based on trade type
        if (poke.Type == PokeTradeType.Batch)
            return await PerformBatchTrade(sav, poke, token).ConfigureAwait(false);

        return await PerformNonBatchTrade(sav, poke, token).ConfigureAwait(false);
    }

    private async Task<bool> EnsureConnectedAndInPortal(CancellationToken token)
    {
        // StartFromOverworld can be true on first pass or if something went wrong last trade.
        if (StartFromOverworld && !await IsOnOverworld(OverworldOffset, token).ConfigureAwait(false))
            await RecoverToOverworld(token).ConfigureAwait(false);

        if (!StartFromOverworld && !await IsConnectedOnline(ConnectedOffset, token).ConfigureAwait(false))
        {
            await RecoverToOverworld(token).ConfigureAwait(false);
            if (!await ConnectAndEnterPortal(token).ConfigureAwait(false))
            {
                await RecoverToOverworld(token).ConfigureAwait(false);
                return false;
            }
        }
        else if (StartFromOverworld && !await ConnectAndEnterPortal(token).ConfigureAwait(false))
        {
            await RecoverToOverworld(token).ConfigureAwait(false);
            return false;
        }
        return true;
    }

    private async Task<bool> EnterLinkTradeAndCode(int code, CancellationToken token)
    {
        // Assumes we're freshly in the Portal and the cursor is over Link Trade.
        Log("Seleccionando Link Trade.");
        await Click(A, 1_500, token).ConfigureAwait(false);

        // Always clear Link Codes and enter a new one based on the current trade type
        await Click(X, 1_000, token).ConfigureAwait(false);
        await Click(PLUS, 1_000, token).ConfigureAwait(false);
        await Task.Delay(Hub.Config.Timings.MiscellaneousSettings.ExtraTimeOpenCodeEntry, token).ConfigureAwait(false);

        Log($"Ingresando el código de Link Trade: {code:0000 0000}...");
        await EnterLinkCode(code, Hub.Config, token).ConfigureAwait(false);
        await Click(PLUS, 3_000, token).ConfigureAwait(false);

        return true;
    }

    private async Task<PokeTradeResult> PerformNonBatchTrade(SAV9SV sav, PokeTradeDetail<PK9> poke, CancellationToken token)
    {
        var toSend = poke.TradeData;
        if (toSend.Species != 0)
            await SetBoxPokemonAbsolute(BoxStartOffset, toSend, token, sav).ConfigureAwait(false);

        // Search for a trade partner for a Link Trade.
        await Click(A, 0_500, token).ConfigureAwait(false);
        await Click(A, 0_500, token).ConfigureAwait(false);

        // Clear it so we can detect it loading.
        await ClearTradePartnerNID(TradePartnerNIDOffset, token).ConfigureAwait(false);

        // Wait for Barrier to trigger all bots simultaneously.
        WaitAtBarrierIfApplicable(token);
        await Click(A, 1_000, token).ConfigureAwait(false);

        // Wait for a Trainer...
        poke.TradeSearching(this);
        var partnerFound = await WaitForTradePartner(token).ConfigureAwait(false);

        if (token.IsCancellationRequested)
        {
            StartFromOverworld = true;
            await ExitTradeToPortal(false, token).ConfigureAwait(false);
            return PokeTradeResult.RoutineCancel;
        }
        if (!partnerFound)
        {
            // Fast-path for no trainer found - handle immediately
            poke.IsProcessing = false;
            poke.SendNotification(this, "⚠️ No se encontró un compañero de intercambio. Cancelando el intercambio.");
            poke.TradeCanceled(this, PokeTradeResult.NoTrainerFound);

            if (!await RecoverToPortal(token).ConfigureAwait(false))
            {
                Log("No se pudo recuperar el portal.");
                await RecoverToOverworld(token).ConfigureAwait(false);
            }
            return PokeTradeResult.NoTrainerFound;
        }

        Hub.Config.Stream.EndEnterCode(this);

        // Wait until we get into the box.
        var cnt = 0;
        while (!await IsInBox(PortalOffset, token).ConfigureAwait(false))
        {
            await Task.Delay(0_500, token).ConfigureAwait(false);
            if (++cnt > 20) // Didn't make it in after 10 seconds.
            {
                await Click(A, 1_000, token).ConfigureAwait(false); // Ensures we dismiss a popup.
                if (!await RecoverToPortal(token).ConfigureAwait(false))
                {
                    Log("No se pudo recuperar el portal.");
                    await RecoverToOverworld(token).ConfigureAwait(false);
                }
                return PokeTradeResult.RecoverOpenBox;
            }
        }
        await Task.Delay(3_000 + Hub.Config.Timings.MiscellaneousSettings.ExtraTimeOpenBox, token).ConfigureAwait(false);

        var tradePartnerFullInfo = await GetTradePartnerFullInfo(token).ConfigureAwait(false);
        var tradePartner = new TradePartnerSV(tradePartnerFullInfo);
        var trainerNID = await GetTradePartnerNID(TradePartnerNIDOffset, token).ConfigureAwait(false);
        RecordUtil<PokeTradeBotSV>.Record($"Iniciando\t{trainerNID:X16}\t{tradePartner.TrainerName}\t{poke.Trainer.TrainerName}\t{poke.Trainer.ID}\t{poke.ID}\t{toSend.EncryptionConstant:X8}");
        Log($"Encontré un entrenador para intercambiar: {tradePartner.TrainerName}-{tradePartner.TID7} (ID: {trainerNID})");
        poke.SendNotification(this, $"Entrenador encontrado: **{tradePartner.TrainerName}**.\n\n▼\n Aqui esta tu Informacion\n **TID**: __{tradePartner.TID7}__\n **SID**: __{tradePartner.SID7}__\n▲\n\n Esperando por un __Pokémon__...");

        var tradeCodeStorage = new TradeCodeStorage();
        var existingTradeDetails = tradeCodeStorage.GetTradeDetails(poke.Trainer.ID);

        bool shouldUpdateOT = existingTradeDetails?.OT != tradePartner.TrainerName;
        bool shouldUpdateTID = existingTradeDetails?.TID != int.Parse(tradePartner.TID7);
        bool shouldUpdateSID = existingTradeDetails?.SID != int.Parse(tradePartner.SID7);

        if (shouldUpdateOT || shouldUpdateTID || shouldUpdateSID)
        {
            string? ot = shouldUpdateOT ? tradePartner.TrainerName : existingTradeDetails?.OT;
            int? tid = shouldUpdateTID ? int.Parse(tradePartner.TID7) : existingTradeDetails?.TID;
            int? sid = shouldUpdateSID ? int.Parse(tradePartner.SID7) : existingTradeDetails?.SID;

            if (ot != null && tid.HasValue && sid.HasValue)
            {
                tradeCodeStorage.UpdateTradeDetails(poke.Trainer.ID, ot, tid.Value, sid.Value);
            }
            else
            {
                Log("OT, TID o SID es nulo. Omitir UpdateTradeDetails.");
            }
        }

        var partnerCheck = CheckPartnerReputation(this, poke, trainerNID, tradePartner.TrainerName, AbuseSettings, token);
        if (partnerCheck != PokeTradeResult.Success)
        {
            await Click(A, 1_000, token).ConfigureAwait(false); // Ensures we dismiss a popup.
            await ExitTradeToPortal(false, token).ConfigureAwait(false);
            return partnerCheck;
        }

        // Hard check to verify that the offset changed from the last thing offered from the previous trade.
        // This is because box opening times can vary per person, the offset persists between trades, and can also change offset between trades.
        var tradeOffered = await ReadUntilChanged(TradePartnerOfferedOffset, lastOffered, 10_000, 0_500, false, true, token).ConfigureAwait(false);
        if (!tradeOffered)
        {
            await ExitTradeToPortal(false, token).ConfigureAwait(false);
            return PokeTradeResult.TrainerTooSlow;
        }

        if (poke.Type == PokeTradeType.Dump)
        {
            var result = await ProcessDumpTradeAsync(poke, token).ConfigureAwait(false);
            await ExitTradeToPortal(false, token).ConfigureAwait(false);
            return result;
        }
        if (Hub.Config.Legality.UseTradePartnerInfo && !poke.IgnoreAutoOT)
        {
            toSend = await ApplyAutoOT(toSend, tradePartnerFullInfo, sav, token);
        }
        // Wait for user input...
        var offered = await ReadUntilPresent(TradePartnerOfferedOffset, 25_000, 1_000, BoxFormatSlotSize, token).ConfigureAwait(false);
        var oldEC = await SwitchConnection.ReadBytesAbsoluteAsync(TradePartnerOfferedOffset, 8, token).ConfigureAwait(false);
        if (offered == null || offered.Species == 0 || !offered.ChecksumValid)
        {
            Log("El intercambio finalizó porque no se ofreció un Pokémon válido.");
            await ExitTradeToPortal(false, token).ConfigureAwait(false);
            return PokeTradeResult.TrainerTooSlow;
        }

        SpecialTradeType itemReq = SpecialTradeType.None;
        if (poke.Type == PokeTradeType.Seed)
            itemReq = CheckItemRequest(ref offered, this, poke, tradePartner.TrainerName, sav);
        if (itemReq == SpecialTradeType.FailReturn)
            return PokeTradeResult.IllegalTrade;

        if (poke.Type == PokeTradeType.Seed && itemReq == SpecialTradeType.None)
        {
            // Immediately exit, we aren't trading anything.
            poke.SendNotification(this, "⚠️ ¡No hay item ni solicitud válida! Cancelando esta operación.");
            await ExitTradeToPortal(true, token).ConfigureAwait(false);
            return PokeTradeResult.TrainerRequestBad;
        }

        var trainer = new PartnerDataHolder(0, tradePartner.TrainerName, tradePartner.TID7);
        PokeTradeResult update;
        (toSend, update) = await GetEntityToSend(sav, poke, offered, oldEC, toSend, trainer, poke.Type == PokeTradeType.Seed ? itemReq : null, token).ConfigureAwait(false);
        if (update != PokeTradeResult.Success)
        {
            if (itemReq != SpecialTradeType.None)
            {
                poke.SendNotification(this, $"⚠️ Su solicitud no es legal. Prueba con un Pokémon diferente o solicitud.");
            }

            return update;
        }

        if (itemReq == SpecialTradeType.WonderCard)
            poke.SendNotification(this, $"✅ Éxito en la distribución!");
        else if (itemReq != SpecialTradeType.None && itemReq != SpecialTradeType.Shinify)
            poke.SendNotification(this, $"✅ Solicitud especial exitosa!");
        else if (itemReq == SpecialTradeType.Shinify)
            poke.SendNotification(this, $"✅ Shinify con éxito!  Gracias por ser parte de la comunidad!");

        Log("Confirmando el trade.");
        var tradeResult = await ConfirmAndStartTrading(poke, token).ConfigureAwait(false);
        if (tradeResult != PokeTradeResult.Success)
        {
            await ExitTradeToPortal(false, token).ConfigureAwait(false);
            return tradeResult;
        }

        if (token.IsCancellationRequested)
        {
            StartFromOverworld = true;
            await ExitTradeToPortal(false, token).ConfigureAwait(false);
            return PokeTradeResult.RoutineCancel;
        }

        // Trade was Successful!
        var received = await ReadPokemon(BoxStartOffset, BoxFormatSlotSize, token).ConfigureAwait(false);

        // Pokémon in b1s1 is same as the one they were supposed to receive (was never sent).
        if (SearchUtil.HashByDetails(received) == SearchUtil.HashByDetails(toSend) && received.Checksum == toSend.Checksum)
        {
            Log("El usuario no completó el intercambio.");
            await ExitTradeToPortal(false, token).ConfigureAwait(false);
            return PokeTradeResult.TrainerTooSlow;
        }

        // As long as we got rid of our inject in b1s1, assume the trade went through.
        Log("El usuario completó el intercambio.");

        poke.TradeFinished(this, received);

        // Only log if we completed the trade.
        UpdateCountsAndExport(poke, received, toSend);

        // Log for Trade Abuse tracking.
        LogSuccessfulTrades(poke, trainerNID, tradePartner.TrainerName);

        // Sometimes they offered another mon, so store that immediately upon leaving Union Room.
        lastOffered = await SwitchConnection.ReadBytesAbsoluteAsync(TradePartnerOfferedOffset, 8, token).ConfigureAwait(false);

        await ExitTradeToPortal(false, token).ConfigureAwait(false);
        return PokeTradeResult.Success;
    }

    private async Task HandleAbortedBatchTrade(PokeTradeDetail<PK9> detail, PokeRoutineType type, uint priority, PokeTradeResult result, CancellationToken token)
    {
        detail.IsProcessing = false;

        // Always remove from UsersInQueue on abort
        Hub.Queues.Info.Remove(new TradeEntry<PK9>(detail, detail.Trainer.ID, type, detail.Trainer.TrainerName, detail.UniqueTradeID));

        if (detail.TotalBatchTrades > 1)
        {
            // Release the batch claim on failure
            BatchTracker.ReleaseBatch(detail.Trainer.ID, detail.UniqueTradeID);

            if (result.ShouldAttemptRetry() && detail.Type != PokeTradeType.Random && !detail.IsRetry)
            {
                detail.IsRetry = true;
                Hub.Queues.Enqueue(type, detail, Math.Min(priority, PokeTradePriorities.Tier2));
                detail.SendNotification(this, "⚠️ ¡Ups! Algo ocurrió durante tu intercambio por lote. Te volveré a poner en la cola para otro intento.");
            }
            else
            {
                detail.SendNotification(this, $"El intercambio por lote falló: {result}");
                detail.TradeCanceled(this, result);
                await ExitTradeToPortal(false, token).ConfigureAwait(false);
            }
        }
        else
        {
            HandleAbortedTrade(detail, type, priority, result);
        }
    }

    private async Task<PokeTradeResult> ProcessDumpTradeAsync(PokeTradeDetail<PK9> detail, CancellationToken token)
    {
        int ctr = 0;
        var time = TimeSpan.FromSeconds(Hub.Config.Trade.TradeConfiguration.MaxDumpTradeTime);
        var start = DateTime.Now;

        var pkprev = new PK9();
        var bctr = 0;
        while (ctr < Hub.Config.Trade.TradeConfiguration.MaxDumpsPerTrade && DateTime.Now - start < time)
        {
            if (!await IsInBox(PortalOffset, token).ConfigureAwait(false))
                break;
            if (bctr++ % 3 == 0)
                await Click(B, 0_100, token).ConfigureAwait(false);

            // Wait for user input... Needs to be different from the previously offered Pokémon.
            var pk = await ReadUntilPresent(TradePartnerOfferedOffset, 3_000, 0_050, BoxFormatSlotSize, token).ConfigureAwait(false);
            if (pk == null || pk.Species == 0 || !pk.ChecksumValid || SearchUtil.HashByDetails(pk) == SearchUtil.HashByDetails(pkprev))
                continue;

            // Save the new Pokémon for comparison next round.
            pkprev = pk;

            // Send results from separate thread; the bot doesn't need to wait for things to be calculated.
            if (DumpSetting.Dump)
            {
                var subfolder = detail.Type.ToString().ToLower();
                DumpPokemon(DumpSetting.DumpFolder, subfolder, pk); // received
            }

            var la = new LegalityAnalysis(pk);
            var verbose = $"```{la.Report(true)}```";
            Log($"El Pokémon mostrado es: {(la.Valid ? "Válido" : "Inválido")}.");

            ctr++;
            var msg = Hub.Config.Trade.TradeConfiguration.DumpTradeLegalityCheck ? verbose : $"File {ctr}";

            // Extra information about trainer data for people requesting with their own trainer data.
            var ot = pk.OriginalTrainerName;
            var ot_gender = pk.OriginalTrainerGender == 0 ? "Male" : "Female";
            var tid = pk.GetDisplayTID().ToString(pk.GetTrainerIDFormat().GetTrainerIDFormatStringTID());
            var sid = pk.GetDisplaySID().ToString(pk.GetTrainerIDFormat().GetTrainerIDFormatStringSID());
            msg += $"\n**Datos del entrenador**\n```OT: {ot}\nOTGender: {ot_gender}\nTID: {tid}\nSID: {sid}```";

            // Extra information for shiny eggs, because of people dumping to skip hatching.
            var eggstring = pk.IsEgg ? "Egg " : string.Empty;
            msg += pk.IsShiny ? $"\n**Este Pokémon {eggstring}es shiny!**" : string.Empty;
            detail.SendNotification(this, pk, msg);
        }

        Log($"Finalizó el ciclo de dump después de procesar {ctr} Pokémon.");
        if (ctr == 0)
            return PokeTradeResult.TrainerTooSlow;

        TradeSettings.CountStatsSettings.AddCompletedDumps();
        detail.Notifier.SendNotification(this, detail, $"Dumped {ctr} Pokémon.");
        detail.Notifier.TradeFinished(this, detail, detail.TradeData); // blank PK9
        return PokeTradeResult.Success;
    }

    // If we can't manually recover to overworld, reset the game.
    // Try to avoid pressing A which can put us back in the portal with the long load time.
    private async Task<bool> RecoverToOverworld(CancellationToken token)
    {
        if (await IsOnOverworld(OverworldOffset, token).ConfigureAwait(false))
            return true;

        Log("Intentando recuperarse al supramundo.");
        var attempts = 0;
        while (!await IsOnOverworld(OverworldOffset, token).ConfigureAwait(false))
        {
            attempts++;
            if (attempts >= 30)
                break;

            await Click(B, 1_000, token).ConfigureAwait(false);
            if (await IsOnOverworld(OverworldOffset, token).ConfigureAwait(false))
                break;

            await Click(B, 1_000, token).ConfigureAwait(false);
            if (await IsOnOverworld(OverworldOffset, token).ConfigureAwait(false))
                break;

            if (await IsInBox(PortalOffset, token).ConfigureAwait(false))
                await Click(A, 1_000, token).ConfigureAwait(false);
        }

        // We didn't make it for some reason.
        if (!await IsOnOverworld(OverworldOffset, token).ConfigureAwait(false))
        {
            Log("No se pudo volver a Overworld, reiniciando el juego.");
            await RestartGameSV(token).ConfigureAwait(false);
        }
        await Task.Delay(1_000, token).ConfigureAwait(false);

        // Force the bot to go through all the motions again on its first pass.
        StartFromOverworld = true;
        return true;
    }

    // If we didn't find a trainer, we're still in the portal but there can be
    // different numbers of pop-ups we have to dismiss to get back to when we can trade.
    // Rather than resetting to overworld, try to reset out of portal and immediately go back in.
    private async Task<bool> RecoverToPortal(CancellationToken token)
    {
        Log("Reorientando al portal de Poké.");
        var attempts = 0;
        while (await IsInPokePortal(PortalOffset, token).ConfigureAwait(false))
        {
            await Click(B, 2_500, token).ConfigureAwait(false);
            if (++attempts >= 30)
            {
                Log("No se pudo recuperar el Poké Portal.");
                return false;
            }
        }

        // Should be in the X menu hovered over Poké Portal.
        await Click(A, 1_000, token).ConfigureAwait(false);

        return await SetUpPortalCursor(token).ConfigureAwait(false);
    }

    private async Task RestartGameSV(CancellationToken token)
    {
        await ReOpenGame(Hub.Config, token).ConfigureAwait(false);
        await InitializeSessionOffsets(token).ConfigureAwait(false);
    }

    // Waits for the Portal to load (slow) and then moves the cursor down to Link Trade.
    private async Task<bool> SetUpPortalCursor(CancellationToken token)
    {
        // Wait for the portal to load.
        var attempts = 0;
        while (!await IsInPokePortal(PortalOffset, token).ConfigureAwait(false))
        {
            await Task.Delay(0_500, token).ConfigureAwait(false);
            if (++attempts > 20)
            {
                Log("No se pudo cargar el Poké Portal.");
                return false;
            }
        }
        await Task.Delay(2_000 + Hub.Config.Timings.MiscellaneousSettings.ExtraTimeLoadPortal, token).ConfigureAwait(false);

        // Connect online if not already.
        if (!await ConnectToOnline(Hub.Config, token).ConfigureAwait(false))
        {
            Log("No se pudo conectar en línea.");
            return false; // Failed, either due to connection or softban.
        }

        // Handle the news popping up.
        if (await SwitchConnection.IsProgramRunning(LibAppletWeID, token).ConfigureAwait(false))
        {
            Log("¡Noticia detectada, se cerrará una vez cargada!");
            await Task.Delay(5_000, token).ConfigureAwait(false);
            await Click(B, 2_000 + Hub.Config.Timings.MiscellaneousSettings.ExtraTimeLoadPortal, token).ConfigureAwait(false);
        }

        Log("Ajustando el cursor en el Portal.");

        // Move down to Link Trade.
        await Click(DDOWN, 0_300, token).ConfigureAwait(false);
        await Click(DDOWN, 0_300, token).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Checks if the barrier needs to get updated to consider this bot.
    /// If it should be considered, it adds it to the barrier if it is not already added.
    /// If it should not be considered, it removes it from the barrier if not already removed.
    /// </summary>
    private void UpdateBarrier(bool shouldWait)
    {
        if (ShouldWaitAtBarrier == shouldWait)
            return; // no change required

        ShouldWaitAtBarrier = shouldWait;
        if (shouldWait)
        {
            Hub.BotSync.Barrier.AddParticipant();
            Log($"Se unió a la barrera. Conteo: {Hub.BotSync.Barrier.ParticipantCount}");
        }
        else
        {
            Hub.BotSync.Barrier.RemoveParticipant();
            Log($"Dejó la barrera. Conteo: {Hub.BotSync.Barrier.ParticipantCount}");
        }
    }

    private void UpdateCountsAndExport(PokeTradeDetail<PK9> poke, PK9 received, PK9 toSend)
    {
        var counts = TradeSettings;
        if (poke.Type == PokeTradeType.Random)
            counts.CountStatsSettings.AddCompletedDistribution();
        else if (poke.Type == PokeTradeType.Clone)
            counts.CountStatsSettings.AddCompletedClones();
        else if (poke.Type == PokeTradeType.FixOT)
            counts.CountStatsSettings.AddCompletedFixOTs();
        else
            counts.CountStatsSettings.AddCompletedTrade();

        if (DumpSetting.Dump && !string.IsNullOrEmpty(DumpSetting.DumpFolder))
        {
            var subfolder = poke.Type.ToString().ToLower();
            var service = poke.Notifier.GetType().ToString().ToLower();
            var tradedFolder = service.Contains("twitch") ? Path.Combine("traded", "twitch") : service.Contains("discord") ? Path.Combine("traded", "discord") : "traded";
            DumpPokemon(DumpSetting.DumpFolder, subfolder, received); // received by bot
            if (poke.Type is PokeTradeType.Specific or PokeTradeType.Clone)
                DumpPokemon(DumpSetting.DumpFolder, tradedFolder, toSend); // sent to partner
        }
    }

    private void WaitAtBarrierIfApplicable(CancellationToken token)
    {
        if (!ShouldWaitAtBarrier)
            return;
        var opt = Hub.Config.Distribution.SynchronizeBots;
        if (opt == BotSyncOption.NoSync)
            return;

        var timeoutAfter = Hub.Config.Distribution.SynchronizeTimeout;
        if (FailedBarrier == 1) // failed last iteration
            timeoutAfter *= 2; // try to re-sync in the event things are too slow.

        var result = Hub.BotSync.Barrier.SignalAndWait(TimeSpan.FromSeconds(timeoutAfter), token);

        if (result)
        {
            FailedBarrier = 0;
            return;
        }

        FailedBarrier++;
        Log($"La sincronización de barrera se agotó después de {timeoutAfter} segundos. Continuando.");
    }

    private Task WaitForQueueStep(int waitCounter, CancellationToken token)
    {
        if (waitCounter == 0)
        {
            // Updates the assets.
            Hub.Config.Stream.IdleAssets(this);
            Log("Nada que comprobar, esperando nuevos usuarios...");
        }

        return Task.Delay(1_000, token);
    }
}
