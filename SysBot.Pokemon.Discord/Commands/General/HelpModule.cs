using Discord;
using Discord.Commands;
using Discord.Net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord;

public class HelpModule(CommandService commandService) : ModuleBase<SocketCommandContext>
{
#pragma warning disable CS9124
    private readonly CommandService _commandService = commandService;
#pragma warning restore CS9124

    private static readonly Color HelpColor = new(114, 137, 218);

    // ---------------- General Help ----------------
    [Command("help")]
    [Summary("Muestra los comandos disponibles agrupados por módulo.")]
    public async Task HelpAsync()
    {
        var botPrefix = SysCordSettings.HubConfig.Discord.CommandPrefix;
        var app = await Context.Client.GetApplicationInfoAsync().ConfigureAwait(false);
        var ownerId = app.Owner.Id;
        var userId = Context.User.Id;

        // 1) Reúne comandos visibles (pasa precondiciones)
        var modules = new List<(string ModuleName, List<CommandInfo> Commands)>();
        foreach (var module in _commandService.Modules)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var visible = new List<CommandInfo>();

            foreach (var cmd in module.Commands)
            {
                // Evita duplicados por alias
                if (!seen.Add(cmd.Name))
                    continue;

                // Respeta precondiciones
                var pre = await cmd.CheckPreconditionsAsync(Context).ConfigureAwait(false);
                if (pre.IsSuccess)
                    visible.Add(cmd);
            }

            if (visible.Count > 0)
            {
                var cleanName = module.Name;
                var idx = cleanName.IndexOf('`');
                if (idx != -1)
                    cleanName = cleanName[..idx];

                modules.Add((cleanName, visible.OrderBy(c => c.Name).ToList()));
            }
        }

        if (modules.Count == 0)
        {
            await ReplyAsync("😶 No hay comandos disponibles para ti en este momento.").ConfigureAwait(false);
            return;
        }

        // 2) Construye embeds con máximo 2 campos por fila (sin exceder 25 campos)
        var embeds = BuildHelpEmbeds(modules, botPrefix, Context.Client.CurrentUser);

        try
        {
            // DM primero
            var dm = await Context.User.CreateDMChannelAsync().ConfigureAwait(false);
            foreach (var e in embeds)
                await dm.SendMessageAsync(embed: e).ConfigureAwait(false);

            if (Context.Channel is IGuildChannel)
            {
                await Context.Message.DeleteAsync().ConfigureAwait(false);
                var notice = await ReplyAsync($"✅ {Context.User.Mention}, te envié la lista de comandos por MD.").ConfigureAwait(false);
                await Task.Delay(10_000).ConfigureAwait(false);
                await notice.DeleteAsync().ConfigureAwait(false);
            }
        }
        catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
        {
            // DMs cerrados: publica en el canal con paginación
            foreach (var e in embeds)
                await ReplyAsync(embed: e).ConfigureAwait(false);
        }
    }

    // ---------------- Command-specific Help ----------------
    [Command("help")]
    [Summary("Muestra información sobre un comando específico.")]
    public async Task HelpAsync([Summary("Comando del que quieres ayuda")] string command)
    {
        var result = _commandService.Search(Context, command);
        if (!result.IsSuccess)
        {
            await ReplyAsync($"⚠️ No pude encontrar un comando llamado **{command}**.").ConfigureAwait(false);
            return;
        }

        var botPrefix = SysCordSettings.HubConfig.Discord.CommandPrefix;

        var eb = new EmbedBuilder()
            .WithColor(HelpColor)
            .WithAuthor(a => a.WithName("Ayuda del comando"))
            .WithTitle($"{botPrefix}{command}")
            .WithThumbnailUrl(Context.Client.CurrentUser.GetAvatarUrl() ?? Context.Client.CurrentUser.GetDefaultAvatarUrl())
            .WithFooter($"Usa {botPrefix}help para ver todos los comandos")
            .WithCurrentTimestamp();

        foreach (var match in result.Commands)
        {
            var cmd = match.Command;

            string ParamLine(ParameterInfo p)
            {
                var optional = p.IsOptional ? " (opcional)" : "";
                var summary = string.IsNullOrWhiteSpace(p.Summary) ? "" : $" — {p.Summary}";
                return $"• `{p.Name}`{optional}{summary}";
            }

            var parameters = cmd.Parameters.Count > 0
                ? string.Join("\n", cmd.Parameters.Select(ParamLine))
                : "_Este comando no requiere parámetros._";

            var examples = BuildExamples(cmd, botPrefix);

            eb.AddField(new EmbedFieldBuilder
            {
                Name = $"🔹 {cmd.Name}",
                Value = $"{(string.IsNullOrWhiteSpace(cmd.Summary) ? "_Sin descripción._" : cmd.Summary)}\n\n**Parámetros:**\n{parameters}{examples}",
                IsInline = false
            });
        }

        try
        {
            var dm = await Context.User.CreateDMChannelAsync().ConfigureAwait(false);
            await dm.SendMessageAsync(embed: eb.Build()).ConfigureAwait(false);

            if (Context.Channel is IGuildChannel)
            {
                await Context.Message.DeleteAsync().ConfigureAwait(false);
                var notice = await ReplyAsync($"✉️ {Context.User.Mention}, te envié los detalles por MD.").ConfigureAwait(false);
                await Task.Delay(10_000).ConfigureAwait(false);
                await notice.DeleteAsync().ConfigureAwait(false);
            }
        }
        catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
        {
            await ReplyAsync(embed: eb.Build()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await ReplyAsync($"❌ Ocurrió un error mostrando la ayuda: `{ex.Message}`").ConfigureAwait(false);
        }
    }

    // ---------------- Helpers ----------------
    private static IReadOnlyList<Embed> BuildHelpEmbeds(
        List<(string ModuleName, List<CommandInfo> Commands)> modules,
        string botPrefix,
        IUser botUser)
    {
        var embeds = new List<Embed>();
        var builder = MakeBaseHelpEmbed(botPrefix, botUser);

        // Control de columnas y límite de 25 campos
        int inlineCol = 0; // 0 o 1 (máximo 2 por fila)

        foreach (var (module, cmds) in modules.OrderBy(m => m.ModuleName))
        {
            var lines = cmds
                .Select(c => $"• `{botPrefix}{c.Aliases.FirstOrDefault() ?? c.Name}`")
                .ToList();

            var value = string.Join("\n", lines);

            // Antes de agregar un campo, asegúrate de que hay espacio
            EnsureCapacityOrNew(ref builder, embeds, botPrefix, botUser, neededSlots: 1);

            builder.AddField(new EmbedFieldBuilder
            {
                Name = $"📦 {module}",
                Value = value,
                IsInline = true
            });

            inlineCol++;

            // Después de 2 campos inline, fuerza salto de línea con un separador NO inline
            if (inlineCol == 2)
            {
                // Si no hay espacio para el separador, cerramos el embed actual y seguimos (ya hay salto)
                if (!EnsureCapacityOrNew(ref builder, embeds, botPrefix, botUser, neededSlots: 1))
                {
                    inlineCol = 0;
                    continue;
                }

                builder.AddField("\u200B", "\u200B", false);
                inlineCol = 0;
            }
        }

        if (builder.Fields.Count > 0)
            embeds.Add(builder.Build());

        return embeds;
    }

    /// <summary>
    /// Asegura espacio para 'neededSlots' campos. Si no hay, agrega el embed actual a la lista
    /// y crea uno nuevo. Devuelve true si aún estamos en el mismo builder (hay espacio), false
    /// si se creó uno nuevo.
    /// </summary>
    private static bool EnsureCapacityOrNew(ref EmbedBuilder builder, List<Embed> embeds, string botPrefix, IUser botUser, int neededSlots)
    {
        const int MaxFields = 25;
        if (builder.Fields.Count + neededSlots <= MaxFields)
            return true;

        // Cierra el embed actual y crea uno nuevo base
        embeds.Add(builder.Build());
        builder = MakeBaseHelpEmbed(botPrefix, botUser);
        return false;
    }

    private static EmbedBuilder MakeBaseHelpEmbed(string botPrefix, IUser botUser)
    {
        return new EmbedBuilder()
            .WithColor(HelpColor)
            .WithAuthor(a => a.WithName("Centro de Ayuda"))
            .WithDescription("📝 Estos son los comandos que puedes usar, organizados por módulo.")
            .WithThumbnailUrl(botUser.GetAvatarUrl() ?? botUser.GetDefaultAvatarUrl())
            .WithFooter($"Consejo: Usa {botPrefix}help <comando> para ver detalles y parámetros")
            .WithCurrentTimestamp();
    }

    private static string BuildExamples(CommandInfo cmd, string prefix)
    {
        var alias = cmd.Aliases.FirstOrDefault() ?? cmd.Name;
        var exampleArgs = string.Join(" ",
            cmd.Parameters.Select(p => p.IsOptional ? $"[{p.Name}]" : $"<{p.Name}>"));

        var example = $"{prefix}{alias} {exampleArgs}".TrimEnd();
        return $"\n\n**Ejemplo:**\n`{example}`";
    }
}
