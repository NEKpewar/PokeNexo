using Discord;
using Discord.Commands;
using PKHeX.Core;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord;

public class LegalityCheckModule : ModuleBase<SocketCommandContext>
{
    [Command("lc"), Alias("check", "validate", "verify")]
    [Summary("Verifica la legalidad del archivo adjunto.")]
    public async Task LegalityCheck()
    {
        foreach (var att in (System.Collections.Generic.IReadOnlyCollection<Attachment>)Context.Message.Attachments)
            await LegalityCheck(att, false).ConfigureAwait(false);
    }

    [Command("lcv"), Alias("verbose")]
    [Summary("Verifica la legalidad del archivo adjunto con una salida detallada.")]
    public async Task LegalityCheckVerbose()
    {
        foreach (var att in (System.Collections.Generic.IReadOnlyCollection<Attachment>)Context.Message.Attachments)
            await LegalityCheck(att, true).ConfigureAwait(false);
    }

    private async Task LegalityCheck(IAttachment att, bool verbose)
    {
        var download = await NetUtil.DownloadPKMAsync(att).ConfigureAwait(false);
        if (!download.Success)
        {
            await ReplyAsync(download.ErrorMessage).ConfigureAwait(false);
            return;
        }

        var pkm = download.Data!;
        var la = new LegalityAnalysis(pkm);
        var builder = new EmbedBuilder
        {
            Color = la.Valid ? Color.Green : Color.Red,
            Description = $"Informe de legalidad para: {download.SanitizedFileName}:",
        };

        builder.AddField(x =>
        {
            x.Name = la.Valid ? $"✅ Válido" : $"❌ Inválido";
            x.Value = la.Report(verbose);
            x.IsInline = false;
        });

        await ReplyAsync("Aquí está el informe de legalidad!", false, builder.Build()).ConfigureAwait(false);
    }
}
