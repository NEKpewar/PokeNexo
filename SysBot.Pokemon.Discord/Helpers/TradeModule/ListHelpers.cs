using Discord;
using Discord.Commands;
using Discord.Net;
using PKHeX.Core;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord;

public static class ListHelpers<T> where T : PKM, new()
{
    private static TradeQueueInfo<T> Info => SysCord<T>.Runner.Hub.Queues.Info;

    public static async Task HandleListCommandAsync(SocketCommandContext context, string folderPath, string itemType,
        string commandPrefix, string args)
    {
        const int itemsPerPage = 20;
        var botPrefix = SysCord<T>.Runner.Config.Discord.CommandPrefix;

        if (string.IsNullOrEmpty(folderPath))
        {
            await Helpers<T>.ReplyAndDeleteAsync(context, $"❌ Lo siento {context.User.Mention}, Este bot no tiene esta función configurada.", 2);
            return;
        }

        var (filter, page) = Helpers<T>.ParseListArguments(args);

        var allFiles = Directory.GetFiles(folderPath)
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(file => file)
            .ToList();

        var filteredFiles = allFiles
            .Where(file => string.IsNullOrWhiteSpace(filter) ||
                   (file != null && file.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (filteredFiles.Count == 0)
        {
            var replyMessage = await context.Channel.SendMessageAsync($"⚠️ {context.User.Mention} No se encontraron eventos que coincidan con el filtro '{filter}'.");
            _ = Helpers<T>.DeleteMessagesAfterDelayAsync(replyMessage, context.Message, 10);
            return;
        }

        var pageCount = (int)Math.Ceiling(filteredFiles.Count / (double)itemsPerPage);
        page = Math.Clamp(page, 1, pageCount);

        var pageItems = filteredFiles.Skip((page - 1) * itemsPerPage).Take(itemsPerPage);

        var embed = new EmbedBuilder()
            .WithTitle($"Disponibles {char.ToUpper(itemType[0]) + itemType[1..]} - Filtro: '{filter}'")
            .WithDescription($"Página {page} de {pageCount}")
            .WithColor(Color.Blue);

        foreach (var item in pageItems)
        {
            var index = allFiles.IndexOf(item) + 1;
            embed.AddField($"{index}. {item}", $"Usa `{botPrefix}{commandPrefix} {index}` para solicitar este {itemType.TrimEnd('s')}");
        }

        await SendDMOrReplyAsync(context, embed.Build());
    }

    public static async Task SendDMOrReplyAsync(SocketCommandContext context, Embed embed)
    {
        IUserMessage replyMessage;

        if (context.User is IUser user)
        {
            try
            {
                var dmChannel = await user.CreateDMChannelAsync();
                await dmChannel.SendMessageAsync(embed: embed);
                replyMessage = await context.Channel.SendMessageAsync($"✅ {context.User.Mention}, Te envié un DM con la lista de eventos.");
            }
            catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
            {
                replyMessage = await context.Channel.SendMessageAsync($"⚠️ {context.User.Mention}, No puedo enviarte un DM. Por favor verifique su **Configuración de privacidad del servidor**.");
            }
        }
        else
        {
            replyMessage = await context.Channel.SendMessageAsync("❌ **Error**: No se puede enviar un DM. Por favor verifique su **Configuración de privacidad del servidor**.");
        }

        _ = Helpers<T>.DeleteMessagesAfterDelayAsync(replyMessage, context.Message, 10);
    }

    public static async Task HandleRequestCommandAsync(SocketCommandContext context, string folderPath, int index,
        string itemType, string listCommand)
    {
        var userID = context.User.Id;
        if (!await Helpers<T>.EnsureUserNotInQueueAsync(userID))
        {
            var eb = new EmbedBuilder()
                .WithTitle("⛔ No se pudo agregarte a la cola")
                .WithColor(Color.Red)
                .WithThumbnailUrl("https://i.imgur.com/DWLEXyu.png")
                .WithImageUrl("https://c.tenor.com/rDzirQgBPwcAAAAd/tenor.gif")
                .AddField("__**Error**__:", $"{context.User.Mention} ya tienes un intercambio pendiente en la cola.", false)
                .AddField("__**Razón**__:", "Debes esperar a que tu operación actual termine antes de iniciar otra.", false)
                .WithFooter(footer =>
                {
                    footer.Text = $"{context.User.Username} • {DateTime.UtcNow:hh:mm tt}";
                    footer.IconUrl = context.User.GetAvatarUrl() ?? context.User.GetDefaultAvatarUrl();
                });

            var msg = await context.Channel.SendMessageAsync(embed: eb.Build());
            _ = Helpers<T>.DeleteMessagesAfterDelayAsync(msg, context.Message, 30);
            return;
        }

        try
        {
            if (string.IsNullOrEmpty(folderPath))
            {
                await Helpers<T>.ReplyAndDeleteAsync(context, $"❌ Lo siento {context.User.Mention}, Este bot no tiene esta función configurada.", 2);
                return;
            }

            var files = Directory.GetFiles(folderPath)
                .Select(Path.GetFileName)
                .OrderBy(x => x)
                .ToList();

            if (index < 1 || index > files.Count)
            {
                await Helpers<T>.ReplyAndDeleteAsync(context,
                    $"⚠️ Índice de {itemType} no válido. Por favor usa un número válido del comando `.{listCommand}`.", 2);
                return;
            }

            var selectedFile = files[index - 1];
            var fileData = await File.ReadAllBytesAsync(Path.Combine(folderPath, selectedFile ?? string.Empty));
            var download = new Download<PKM>
            {
                Data = EntityFormat.GetFromBytes(fileData),
                Success = true
            };

            var pk = Helpers<T>.GetRequest(download);
            if (pk == null)
            {
                await Helpers<T>.ReplyAndDeleteAsync(context,
                    $"⚠️ No se pudo convertir el archivo {itemType} al tipo PKM requerido.", 2);
                return;
            }

            var code = Info.GetRandomTradeCode(userID);
            var lgcode = Info.GetRandomLGTradeCode();
            var sig = context.User.GetFavor();

            await context.Channel.SendMessageAsync($"{char.ToUpper(itemType[0]) + itemType[1..]} solicitud agregada a la cola.").ConfigureAwait(false);
            await Helpers<T>.AddTradeToQueueAsync(context, code, context.User.Username, pk, sig,
                context.User, lgcode: lgcode).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await Helpers<T>.ReplyAndDeleteAsync(context, $"Ocurrió un error: {ex.Message}", 2);
        }
        finally
        {
            if (context.Message is IUserMessage userMessage)
                _ = Helpers<T>.DeleteMessagesAfterDelayAsync(userMessage, null, 2);
        }
    }
}
