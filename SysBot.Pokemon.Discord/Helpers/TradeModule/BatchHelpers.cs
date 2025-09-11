using Discord;
using Discord.Commands;
using PKHeX.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord;

public static class BatchHelpers<T> where T : PKM, new()
{
    public static List<string> ParseBatchTradeContent(string content)
    {
        var delimiters = new[] { "---", "—-" };
        return [.. content.Split(delimiters, StringSplitOptions.RemoveEmptyEntries).Select(trade => trade.Trim())];
    }

    public static async Task<(T? Pokemon, string? Error, ShowdownSet? Set, string? LegalizationHint)> ProcessSingleTradeForBatch(SocketCommandContext context, string tradeContent)
    {
        var ignoreAutoOT = tradeContent.Contains("OT:") || tradeContent.Contains("TID:") || tradeContent.Contains("SID:");
        var result = await Helpers<T>.ProcessShowdownSetAsync(context, tradeContent, ignoreAutoOT);

        if (result.Pokemon != null)
        {
            return (result.Pokemon, null, result.ShowdownSet, null);
        }

        return (null, result.Error, result.ShowdownSet, result.LegalizationHint);
    }

    public static async Task SendBatchErrorEmbedAsync(
    SocketCommandContext context,
    List<BatchTradeError> errors,
    int totalTrades)
    {
        errors ??= new List<BatchTradeError>();
        var failed = errors.Count;
        var succeeded = Math.Max(0, totalTrades - failed);
        var formattedTime = DateTime.UtcNow.ToString("hh:mm tt");

        // Header / summary
        var embed = new EmbedBuilder()
            .WithTitle("⚠️ Errores en el intercambio por lotes")
            .WithDescription(
                $"Se procesaron **{totalTrades}** solicitudes:\n" +
                $"✅ **{succeeded}** correctas • ❌ **{failed}** con errores.\n\n" +
                $"Corrige los sets inválidos y vuelve a intentarlo.")
            .WithColor(Color.Red)
            .WithImageUrl("https://i.imgur.com/Y64hLzW.gif")
            .WithThumbnailUrl("https://i.imgur.com/DWLEXyu.png")
            .WithAuthor("Error", "https://img.freepik.com/free-icon/warning_318-478601.jpg")
            .WithFooter(f =>
            {
                f.Text = $"{context.User.Username} • {formattedTime}";
                f.IconUrl = context.User.GetAvatarUrl() ?? context.User.GetDefaultAvatarUrl();
            });

        if (failed > 0)
        {
            // Discord: 25 fields max; we’ll keep it readable with top 10
            const int maxFields = 10;

            foreach (var e in errors
                         .OrderBy(x => x.TradeNumber)
                         .Take(maxFields))
            {
                var species = string.IsNullOrWhiteSpace(e.SpeciesName) ? "Desconocido" : e.SpeciesName;

                // Build field value with safe trimming to 1024 chars
                string value = $"__**Error**__: {e.ErrorMessage}".Trim();

                if (!string.IsNullOrWhiteSpace(e.LegalizationHint))
                {
                    // The hint may be verbose (BattleTemplateLegality). Add but keep it short.
                    var hint = e.LegalizationHint.Trim();
                    if (hint.Length > 700) hint = hint[..700] + "…";
                    value += $"\n__Análisis__: {hint}";
                }

                if (!string.IsNullOrWhiteSpace(e.ShowdownSet))
                {
                    var lines = e.ShowdownSet.Split('\n');
                    var preview = string.Join(" | ", lines.Take(2).Select(s => s.Trim()));
                    if (preview.Length > 200) preview = preview[..200] + "…";
                    value += $"\n__Set__: {preview}";
                }

                // Discord field value hard limit is 1024
                if (value.Length > 1024)
                    value = value[..1021] + "…";

                var fieldName = $"Trade #{e.TradeNumber} — {species}";
                if (fieldName.Length > 256)
                    fieldName = fieldName[..253] + "…";

                embed.AddField(fieldName, value, inline: false);
            }

            // If there are more errors than shown, add a final “and more…” field
            if (failed > maxFields)
            {
                embed.AddField(
                    "…y más",
                    $"Hay **{failed - maxFields}** errores adicionales no mostrados para mantener el mensaje legible.",
                    inline: false
                );
            }
        }

        var sent = await context.Channel
            .SendMessageAsync(text: context.User.Mention, embed: embed.Build())
            .ConfigureAwait(false);

        _ = Helpers<T>.DeleteMessagesAfterDelayAsync(sent, context.Message, 30);
    }


    public static async Task ProcessBatchContainer(SocketCommandContext context, List<T> batchPokemonList,
        int batchTradeCode, int totalTrades)
    {
        var sig = context.User.GetFavor();
        var firstPokemon = batchPokemonList[0];

        await QueueHelper<T>.AddBatchContainerToQueueAsync(context, batchTradeCode, context.User.Username,
            firstPokemon, batchPokemonList, sig, context.User, totalTrades).ConfigureAwait(false);
    }
}

