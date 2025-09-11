using PKHeX.Core;
using SysBot.Base;
using SysBot.Pokemon.Helpers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.Pokemon;

public abstract class PokeRoutineExecutor<T>(IConsoleBotManaged<IConsoleConnection, IConsoleConnectionAsync> Config)
    : PokeRoutineExecutorBase(Config)
    where T : PKM, new()
{
    private const ulong dmntID = 0x010000000000000d;
    public BatchTradeTracker<T> BatchTracker => BatchTradeTracker<T>.Instance;
    // Check if either Tesla or dmnt are active if the sanity check for Trainer Data fails, as these are common culprits.
    private const ulong ovlloaderID = 0x420000000007e51a;

    public static void DumpPokemon(string folder, string subfolder, T pk)
    {
        if (!Directory.Exists(folder))
            return;
        var dir = Path.Combine(folder, subfolder);
        Directory.CreateDirectory(dir);
        var fn = Path.Combine(dir, PathUtil.CleanFileName(pk.FileName));
        File.WriteAllBytes(fn, pk.DecryptedPartyData);
        LogUtil.LogInfo($"Archivo guardado: {fn}", "Dump");
    }

    public static void LogSuccessfulTrades(PokeTradeDetail<T> poke, ulong TrainerNID, string TrainerName)
    {
        // All users who traded, tracked by whether it was a targeted trade or distribution.
        if (poke.Type == PokeTradeType.Random)
            PreviousUsersDistribution.TryRegister(TrainerNID, TrainerName);
        else
            PreviousUsers.TryRegister(TrainerNID, TrainerName, poke.Trainer.ID);
    }

    public async Task CheckForRAMShiftingApps(CancellationToken token)
    {
        Log("Los datos del entrenador no son válidos.");

        bool found = false;
        var msg = "";
        if (await SwitchConnection.IsProgramRunning(ovlloaderID, token).ConfigureAwait(false))
        {
            msg += "Menú de Tesla encontrado";
            found = true;
        }

        if (await SwitchConnection.IsProgramRunning(dmntID, token).ConfigureAwait(false))
        {
            if (found)
                msg += " y ";
            msg += "dmnt (¿códigos de trucos?)";
            found = true;
        }
        if (found)
        {
            msg += ".";
            Log(msg);
            Log("Elimine las aplicaciones interferentes y reinicie la switch.");
        }
    }

    public abstract Task<T> ReadBoxPokemon(int box, int slot, CancellationToken token);

    public abstract Task<T> ReadPokemon(ulong offset, CancellationToken token);

    public abstract Task<T> ReadPokemon(ulong offset, int size, CancellationToken token);

    public abstract Task<T> ReadPokemonPointer(IEnumerable<long> jumps, int size, CancellationToken token);

    public async Task<T?> ReadUntilPresent(ulong offset, int waitms, int waitInterval, int size, CancellationToken token)
    {
        int msWaited = 0;
        while (msWaited < waitms)
        {
            var pk = await ReadPokemon(offset, size, token).ConfigureAwait(false);
            if (pk.Species != 0 && pk.ChecksumValid)
                return pk;
            await Task.Delay(waitInterval, token).ConfigureAwait(false);
            msWaited += waitInterval;
        }
        return null;
    }

    public async Task<T?> ReadUntilPresentPointer(IReadOnlyList<long> jumps, int waitms, int waitInterval, int size, CancellationToken token)
    {
        int msWaited = 0;
        while (msWaited < waitms)
        {
            var pk = await ReadPokemonPointer(jumps, size, token).ConfigureAwait(false);
            if (pk.Species != 0 && pk.ChecksumValid)
                return pk;
            await Task.Delay(waitInterval, token).ConfigureAwait(false);
            msWaited += waitInterval;
        }
        return null;
    }

    public async Task<bool> TryReconnect(int attempts, int extraDelay, SwitchProtocol protocol, CancellationToken token)
    {
        // USB can have several reasons for connection loss, some of which is not recoverable (power loss, sleep).
        // Only deal with Wi-Fi for now.
        if (protocol is SwitchProtocol.WiFi)
        {
            // If ReconnectAttempts is set to -1, this should allow it to reconnect (essentially) indefinitely.
            for (int i = 0; i < (uint)attempts; i++)
            {
                LogUtil.LogInfo($"Tratando de volver a conectar ... ({i + 1})", Connection.Label);
                Connection.Reset();
                if (Connection.Connected)
                    break;

                await Task.Delay(30_000 + extraDelay, token).ConfigureAwait(false);
            }
        }
        return Connection.Connected;
    }

    public async Task VerifyBotbaseVersion(CancellationToken token)
    {
        var data = await SwitchConnection.GetBotbaseVersion(token).ConfigureAwait(false);
        var version = decimal.TryParse(data, CultureInfo.InvariantCulture, out var v) ? v : 0;
        if (version < BotbaseVersion)
        {
            var protocol = Config.Connection.Protocol;
            var msg = protocol is SwitchProtocol.WiFi ? "sys-botbase" : "usb-botbase";
            msg += $" ⚠️ Versión no compatible. Versión esperada **{BotbaseVersion}** o superior, y la versión actual es **{version}**. Descargue la última versión desde: ";
            if (protocol is SwitchProtocol.WiFi)
                msg += "https://github.com/olliz0r/sys-botbase/releases/latest";
            else
                msg += "https://github.com/Koi-3088/usb-botbase/releases/latest";
            throw new Exception(msg);
        }
    }

    // Tesla Menu
    // dmnt used for cheats
    protected PokeTradeResult CheckPartnerReputation(PokeRoutineExecutor<T> bot, PokeTradeDetail<T> poke, ulong TrainerNID, string TrainerName,
        TradeAbuseSettings AbuseSettings, CancellationToken token)
    {
        bool quit = false;
        var user = poke.Trainer;
        var isDistribution = poke.Type == PokeTradeType.Random;
        var list = isDistribution ? PreviousUsersDistribution : PreviousUsers;

        // Matches to a list of banned NIDs, in case the user ever manages to enter a trade.
        var entry = AbuseSettings.BannedIDs.List.Find(z => z.ID == TrainerNID);
        if (entry != null)
        {
            return PokeTradeResult.SuspiciousActivity;
        }

        // Check within the trade type (distribution or non-Distribution).
        var previous = list.TryGetPreviousNID(TrainerNID);
        if (previous != null)
        {
            var delta = DateTime.Now - previous.Time; // Time that has passed since last trade.
            Log($"Último intercambio con {user.TrainerName} hace {delta.TotalMinutes:F1} minuto(s) (OT: {TrainerName}).");

            // Allows setting a cooldown for repeat trades. If the same user is encountered within the cooldown period for the same trade type, the user is warned and the trade will be ignored.
            var cd = AbuseSettings.TradeCooldown; // Time they must wait before trading again.
            if (cd != 0 && TimeSpan.FromMinutes(cd) > delta)
            {
                Log($"Se encontró a {user.TrainerName} ignorando el cooldown de intercambio de {cd} {(cd == 1 ? "minuto" : "minutos")}. Último encuentro hace {delta.TotalMinutes:F1} {(delta.TotalMinutes == 1 ? "minuto" : "minutos")}.");
                return PokeTradeResult.SuspiciousActivity;
            }

            // For distribution trades only, flag users using multiple Discord/Twitch accounts to send to the same in-game player within the TradeAbuseExpiration time limit.
            // This is usually to evade a ban or a trade cooldown.
            if (isDistribution && previous.NetworkID == TrainerNID && previous.RemoteID != user.ID)
            {
                if (delta < TimeSpan.FromMinutes(AbuseSettings.TradeAbuseExpiration))
                {
                    quit = true;
                    Log($"Se detectó que {user.TrainerName} está usando múltiples cuentas.\nIntercambió previamente con {previous.Name} ({previous.RemoteID}) hace {delta.TotalMinutes:F1} {(delta.TotalMinutes == 1 ? "minuto" : "minutos")} en el OT: {TrainerName}.");
                }
            }
        }

        if (quit)
            return PokeTradeResult.SuspiciousActivity;

        return PokeTradeResult.Success;
    }

    protected async Task<(bool, ulong)> ValidatePointerAll(IEnumerable<long> jumps, CancellationToken token)
    {
        var solved = await SwitchConnection.PointerAll(jumps, token).ConfigureAwait(false);
        return (solved != 0, solved);
    }
}
