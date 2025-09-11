using PKHeX.Core;
using SysBot.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TwitchLib.Client;

namespace SysBot.Pokemon.Twitch
{
    public class TwitchTradeNotifier<T> : IPokeTradeNotifier<T> where T : PKM, new()
    {
        private int BatchTradeNumber { get; set; } = 1;
        private int TotalBatchTrades { get; set; } = 1;

        public TwitchTradeNotifier(T data, PokeTradeTrainerInfo info, int code, string username, TwitchClient client, string channel, TwitchSettings settings, int batchTradeNumber = 1, int totalBatchTrades = 1)
        {
            Data = data;
            Info = info;
            Code = code;
            Username = username;
            Client = client;
            Channel = channel;
            Settings = settings;
            BatchTradeNumber = batchTradeNumber;
            TotalBatchTrades = totalBatchTrades;

            LogUtil.LogText($"Detalles comerciales creados para {Username} - {Code}");
        }

        public Action<PokeRoutineExecutor<T>>? OnFinish { private get; set; }

        private string Channel { get; }

        private TwitchClient Client { get; }

        private int Code { get; }

        private T Data { get; set; }

        private PokeTradeTrainerInfo Info { get; }

        private TwitchSettings Settings { get; }

        private string Username { get; }

        public Task SendInitialQueueUpdate()
        {
            return Task.CompletedTask;
        }

        // Dummy methods because not available on Twitch.
        public void SendEtumrepEmbed(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, IReadOnlyList<PA8> pkms)
        { }

        public void UpdateBatchProgress(int currentBatchNumber, T currentPokemon, int uniqueTradeID)
        {
            BatchTradeNumber = currentBatchNumber;
            Data = currentPokemon;
            // uniqueTradeID is not used in Twitch notifier
        }

        public void SendIncompleteEtumrepEmbed(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, string msg, IReadOnlyList<PA8> pkms)
        { }

        public void SendNotification(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, string message)
        {
            LogUtil.LogText(message);
            SendMessage($"@{info.Trainer.TrainerName}: {message}", Settings.NotifyDestination);
        }

        public void SendNotification(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, PokeTradeSummary message)
        {
            var msg = message.Summary;
            if (message.Details.Count > 0)
                msg += ", " + string.Join(", ", message.Details.Select(z => $"{z.Heading}: {z.Detail}"));
            LogUtil.LogText(msg);
            SendMessage(msg, Settings.NotifyDestination);
        }

        public void SendNotification(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, T result, string message)
        {
            var msg = $"Detalles para {result.FileName}: " + message;
            LogUtil.LogText(msg);
            SendMessage(msg, Settings.NotifyDestination);
        }

        public void TradeCanceled(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, PokeTradeResult msg)
        {
            OnFinish?.Invoke(routine);
            var line = $"⚠️ @{info.Trainer.TrainerName}: trade cancelado, {msg}";
            LogUtil.LogText(line);
            SendMessage(line, Settings.TradeCanceledDestination);
        }

        public void TradeFinished(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, T result)
        {
            OnFinish?.Invoke(routine);
            var tradedToUser = Data.Species;

            string message;
            if (TotalBatchTrades > 1)
            {
                if (BatchTradeNumber == TotalBatchTrades)
                {
                    message = $"✅ @{info.Trainer.TrainerName}: ¡Todos los {TotalBatchTrades} intercambios completados! ¡Gracias por intercambiar!";
                }
                else
                {
                    message = $"✅ @{info.Trainer.TrainerName}: Intercambio {BatchTradeNumber}/{TotalBatchTrades} completado.";
                }
            }
            else
            {
                message = $"@{info.Trainer.TrainerName}: " + (tradedToUser != 0 ? $"✅ Intercambio finalizado. ¡Disfruta tu {(Species)tradedToUser}!" : "✅ Intercambio finalizado.");
            }

            LogUtil.LogText(message);
            SendMessage(message, Settings.TradeFinishDestination);
        }

        public void TradeInitialize(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info)
        {
            var receive = Data.Species == 0 ? string.Empty : Data.IsEgg || (Data.Species == 132 && Data.IsNicknamed) ? $" ({Data.Species} ({Data.Nickname}))" : $" ({Data.Nickname})";
            var msg = $"@{info.Trainer.TrainerName} (ID: {info.ID}): Inicializando trade con usted. Recibiendo: {receive}. Por favor, prepárate. ¡Usa el código que me susurraste para buscar!";
            var dest = Settings.TradeStartDestination;
            if (dest == TwitchMessageDestination.Whisper)
                msg += $" Tu código de tradeo es: {info.Code:0000 0000}";
            LogUtil.LogText(msg);
            SendMessage(msg, dest);
        }

        public void TradeSearching(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info)
        {
            var name = Info.TrainerName;
            var trainer = string.IsNullOrEmpty(name) ? string.Empty : $", @{name}";
            var message = $"Te estoy esperando {trainer}! Mi IGN es {routine.InGameName}.";
            var dest = Settings.TradeSearchDestination;
            if (dest == TwitchMessageDestination.Channel)
                message += " ¡Usa el código que me has susurrado para buscar!";
            else if (dest == TwitchMessageDestination.Whisper)
                message += $" Tu código de tradeo es: {info.Code:0000 0000}";
            LogUtil.LogText(message);
            SendMessage($"@{info.Trainer.TrainerName} {message}", dest);
        }

        private void SendMessage(string message, TwitchMessageDestination dest)
        {
            switch (dest)
            {
                case TwitchMessageDestination.Channel:
                    Client.SendMessage(Channel, message);
                    break;

                case TwitchMessageDestination.Whisper:
                    Client.SendWhisper(Username, message);
                    break;
            }
        }
    }
}
