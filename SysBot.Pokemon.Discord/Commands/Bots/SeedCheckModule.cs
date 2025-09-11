using Discord;
using Discord.Commands;
using PKHeX.Core;
using System;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord;

[Summary("Pone en cola nuevas operaciones de verificación de semillas")]
public class SeedCheckModule<T> : ModuleBase<SocketCommandContext> where T : PKM, new()
{
    private static TradeQueueInfo<T> Info => SysCord<T>.Runner.Hub.Queues.Info;

    [Command("findFrame")]
    [Alias("ff", "getFrameData")]
    [Summary("Imprime el siguiente fotograma brillante a partir de la semilla proporcionada.")]
    public async Task FindFrameAsync([Remainder] string seedString)
    {
        var me = SysCord<T>.Runner;
        var hub = me.Hub;

        seedString = seedString.ToLower();
        if (seedString.StartsWith("0x"))
            seedString = seedString[2..];

        var seed = Util.GetHexValue64(seedString);

        var r = new SeedSearchResult(Z3SearchResult.Success, seed, -1, hub.Config.SeedCheckSWSH.ResultDisplayMode);
        var msg = r.ToString();

        var embed = new EmbedBuilder { Color = Color.LighterGrey };

        embed.AddField(x =>
        {
            x.Name = $"Semilla: {seed:X16}";
            x.Value = msg;
            x.IsInline = false;
        });
        await ReplyAsync($"Aquí están los detalles para `{r.Seed:X16}`:", embed: embed.Build()).ConfigureAwait(false);
    }

    [Command("seedList")]
    [Alias("sl", "scq", "seedCheckQueue", "seedQueue", "seedList")]
    [Summary("Imprime los usuarios en la cola de Seed Check.")]
    [RequireSudo]
    public async Task GetSeedListAsync()
    {
        string msg = Info.GetTradeList(PokeRoutineType.SeedCheck);
        var embed = new EmbedBuilder();
        embed.AddField(x =>
        {
            x.Name = "Tradeos pendientes";
            x.Value = msg;
            x.IsInline = false;
        });
        await ReplyAsync("📝 Estos son los usuarios que están esperando actualmente:", embed: embed.Build()).ConfigureAwait(false);
    }

    [Command("seedCheck")]
    [Alias("checkMySeed", "checkSeed", "seed", "s", "sc", "specialrequest", "sr")]
    [Summary("Comprueba la semilla de un Pokémon.")]
    [RequireQueueRole(nameof(DiscordManager.RolesSeed))]
    public async Task SeedCheckAsync(int code)
    {
        // Check if the user is already in the queue
        var userID = Context.User.Id;
        if (Info.IsUserInQueue(userID))
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

            // Añadir un field al Embed para indicar el error
            queueEmbed.AddField("__**Error**__:", $"❌ {Context.User.Mention} No pude agregarte a la cola", true);
            queueEmbed.AddField("__**Razón**__:", "No puedes agregar más operaciones hasta que la actual se procese.", true);
            queueEmbed.AddField("__**Solución**__:", "Espera un poco hasta que la operación existente se termine e intentalo de nuevo.");

            queueEmbed.Footer = new EmbedFooterBuilder
            {
                Text = $"{Context.User.Username} • {formattedTime}",
                IconUrl = Context.User.GetAvatarUrl() ?? Context.User.GetDefaultAvatarUrl()
            };

            await ReplyAsync(embed: queueEmbed.Build()).ConfigureAwait(false);
            return;
        }
        var sig = Context.User.GetFavor();
        await QueueHelper<T>.AddToQueueAsync(Context, code, Context.User.Username, sig, new T(), PokeRoutineType.SeedCheck, PokeTradeType.Seed).ConfigureAwait(false);
    }

    [Command("seedCheck")]
    [Alias("checkMySeed", "checkSeed", "seed", "s", "sc", "specialrequest", "sr")]
    [Summary("Comprueba la semilla de un Pokémon.")]
    [RequireQueueRole(nameof(DiscordManager.RolesSeed))]
    public async Task SeedCheckAsync([Summary("Trade Code")][Remainder] string code)
    {
        // Check if the user is already in the queue
        var userID = Context.User.Id;
        if (Info.IsUserInQueue(userID))
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

            // Añadir un field al Embed para indicar el error
            queueEmbed.AddField("__**Error**__:", $"❌ {Context.User.Mention} No pude agregarte a la cola", true);
            queueEmbed.AddField("__**Razón**__:", "No puedes agregar más operaciones hasta que la actual se procese.", true);
            queueEmbed.AddField("__**Solución**__:", "Espera un poco hasta que la operación existente se termine e intentalo de nuevo.");

            queueEmbed.Footer = new EmbedFooterBuilder
            {
                Text = $"{Context.User.Username} • {formattedTime}",
                IconUrl = Context.User.GetAvatarUrl() ?? Context.User.GetDefaultAvatarUrl()
            };

            await ReplyAsync(embed: queueEmbed.Build()).ConfigureAwait(false);
            return;
        }
        int tradeCode = Util.ToInt32(code);
        var sig = Context.User.GetFavor();
        await QueueHelper<T>.AddToQueueAsync(Context, tradeCode == 0 ? Info.GetRandomTradeCode(userID) : tradeCode, Context.User.Username, sig, new T(), PokeRoutineType.SeedCheck, PokeTradeType.Seed).ConfigureAwait(false);
    }

    [Command("seedCheck")]
    [Alias("checkMySeed", "checkSeed", "seed", "s", "sc", "specialrequest", "sr")]
    [Summary("Comprueba la semilla de un Pokémon.")]
    [RequireQueueRole(nameof(DiscordManager.RolesSeed))]
    public async Task SeedCheckAsync()
    {
        // Check if the user is already in the queue
        var userID = Context.User.Id;
        if (Info.IsUserInQueue(userID))
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

            // Añadir un field al Embed para indicar el error
            queueEmbed.AddField("__**Error**__:", $"❌ {Context.User.Mention} No pude agregarte a la cola", true);
            queueEmbed.AddField("__**Razón**__:", "No puedes agregar más operaciones hasta que la actual se procese.", true);
            queueEmbed.AddField("__**Solución**__:", "Espera un poco hasta que la operación existente se termine e intentalo de nuevo.");

            queueEmbed.Footer = new EmbedFooterBuilder
            {
                Text = $"{Context.User.Username} • {formattedTime}",
                IconUrl = Context.User.GetAvatarUrl() ?? Context.User.GetDefaultAvatarUrl()
            };

            await ReplyAsync(embed: queueEmbed.Build()).ConfigureAwait(false);
            return;
        }
        var code = Info.GetRandomTradeCode(userID);
        await SeedCheckAsync(code).ConfigureAwait(false);
        if (Context.Message is IUserMessage userMessage)
            await userMessage.DeleteAsync().ConfigureAwait(false);
    }
}
