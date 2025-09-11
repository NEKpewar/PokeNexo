using Discord;
using Discord.Commands;
using PKHeX.Core;
using SysBot.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord;

public class HubModule<T> : ModuleBase<SocketCommandContext> where T : PKM, new()
{
    [Command("status")]
    [Alias("stats")]
    [Summary("Muestra el estado actual de los bots y sus colas.")]
    public async Task GetStatusAsync()
    {
        var runner = SysCord<T>.Runner;
        var hub = runner.Hub;
        var allBots = runner.Bots.ConvertAll(z => z.Bot);
        var botCount = allBots.Count;

        // ---- Estado global para color/etiqueta ----
        bool noBots = botCount == 0;
        bool queuesEmpty = hub.Queues.AllQueues.All(q => q.Count == 0);
        var (statusEmoji, statusLabel, embedColor) = GetGlobalStatus(noBots, queuesEmpty);

        var builder = new EmbedBuilder()
            .WithTitle($"{statusEmoji} Estado del Bot")
            .WithColor(embedColor)
            .WithCurrentTimestamp();

        // Autor (usa el bot actual si está disponible)
        var me = Context.Client.CurrentUser;
        builder.WithAuthor(a =>
        {
            a.Name = me?.Username ?? "SysBot";
            a.IconUrl = me?.GetAvatarUrl() ?? me?.GetDefaultAvatarUrl();
        });

        // ---- Resumen ----
        var botsSummary = SummarizeBotsWithBadges(allBots);
        string botsSection = string.IsNullOrWhiteSpace(botsSummary)
            ? ""
            : $"\n```{botsSummary}```";

        builder.AddField("__**📌 Resumen**__",
            $"**Bots activos:** {botCount}{botsSection}\n" +
            $"**Salud:** {statusLabel}\n" +
            $"**Pool disponible:** {hub.Ledy.Pool.Count}",
            inline: false);

        // ---- Recuentos (si hay) ----
        var botsWithCounts = allBots.OfType<ICountBot>();
        var lines = botsWithCounts.SelectMany(z => z.Counts.GetNonZeroCounts()).Distinct();
        var countsMsg = string.Join("\n", lines);
        builder.AddField("__**🔢 Recuentos**__",
            string.IsNullOrWhiteSpace(countsMsg) ? "No hay recuentos todavía." : countsMsg,
            inline: false);

        // ---- Colas ----
        var queues = hub.Queues.AllQueues;
        int totalQueued = queues.Sum(q => q.Count);

        if (totalQueued == 0)
        {
            builder.AddField("✅ Colas vacías", "Nadie está esperando en este momento.", inline: false);
        }
        else
        {
            // Resumen compacto primero
            builder.AddField("__**🧾 Resumen de colas**__",
                $"**Total en espera:** {totalQueued}\n" +
                $"**Tipos de cola activas:** {queues.Count(q => q.Count > 0)}",
                inline: false);

            // Luego, el detalle por cola
            foreach (var q in queues.Where(q => q.Count > 0))
            {
                string queueEmoji = q.Count > 5 ? "🔥" : "⏳";
                builder.AddField($"{queueEmoji} Cola: {q.Type}",
                    $"👤 **Siguiente:** {GetNextName(q)}\n" +
                    $"📦 **Pendientes:** {q.Count}",
                    inline: false);
            }
        }

        await ReplyAsync(embed: builder.Build()).ConfigureAwait(false);
    }

    // ---------- Helpers ----------

    private static (string emoji, string label, Color color) GetGlobalStatus(bool noBots, bool queuesEmpty)
    {
        if (noBots) return ("🟥", "Sin bots configurados", Color.DarkRed);
        if (queuesEmpty) return ("🟩", "Estable (sin espera)", Color.Green);
        return ("🟧", "Operativo (con colas)", Color.Orange);
    }

    private static string GetNextName(PokeTradeQueue<T> q)
    {
        var hasNext = q.TryPeek(out var detail, out _);
        if (!hasNext)
            return "—";

        var name = detail.Trainer.TrainerName;
        var nick = detail.TradeData.Nickname;

        return string.IsNullOrEmpty(nick) ? name : $"{name} - {nick}";
    }

    // ✅ Nueva presentación: badge + inline code por cada bot (sin flecha)
    private static string SummarizeBotsWithBadges(IReadOnlyCollection<RoutineExecutor<PokeBotState>> bots)
    {
        if (bots.Count == 0)
            return string.Empty;

        var lines = bots.Select(z =>
        {
            var summary = z.GetSummary(); // ej: "192.168.0.1 - FlexTrade (Idle)"
            var emoji = GetStatusEmojiFromSummary(summary);
            return $"{emoji} {summary}";
        });

        return string.Join("\n", lines);
    }

    private static string GetStatusEmojiFromSummary(string summary)
    {
        var s = summary?.ToLowerInvariant() ?? string.Empty;

        if (s.Contains("idle")) return "✅";
        if (s.Contains("busy") || s.Contains("running") || s.Contains("trading")) return "🔄";
        if (s.Contains("error") || s.Contains("stopped") || s.Contains("unknown")) return "⚠️";
        return "ℹ️";
    }

    private static string SummarizeBots(IReadOnlyCollection<RoutineExecutor<PokeBotState>> bots)
    {
        if (bots.Count == 0)
            return "⚠️ No hay bots configurados.";

        return string.Join("\n", bots.Select(z => $"• {z.GetSummary()}"));
    }
}
