using Discord;
using Discord.Commands;
using SysBot.Pokemon.Helpers;
using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord;

// src original inspirado en patek (ISC)
public class InfoModule : ModuleBase<SocketCommandContext>
{
    private const string Detail =
        "Soy un bot de Discord impulsado por PKHeX.Core y otros proyectos de código abierto. " +
        "Gracias por usarme 💚";

    private const string PokeBotRepo = "https://github.com/hexbyt3/PokeBot";
    private const string ForkRepo = "https://github.com/Daiivr/PokeNexo";
    private const ulong DisallowedUserId = 195756980873199618;

    [Command("info")]
    [Alias("about", "owner")]
    [Summary("Muestra información general del bot (proyecto, versiones y entorno).")]
    public async Task InfoAsync()
    {
        if (Context.User.Id == DisallowedUserId)
        {
            await ReplyAsync("❌ No permitimos que personas turbias usen este comando.").ConfigureAwait(false);
            return;
        }

        var app = await Context.Client.GetApplicationInfoAsync().ConfigureAwait(false);

        // Color fijo agradable para “about”
        var builder = new EmbedBuilder()
            .WithAuthor(a =>
            {
                var me = Context.Client.CurrentUser;
                a.Name = $"{me?.Username ?? "SysBot"}";
                a.IconUrl = me?.GetAvatarUrl() ?? me?.GetDefaultAvatarUrl();
            })
            .WithTitle("ℹ️ Información del Bot")
            .WithDescription(Detail)
            .WithColor(new Color(67, 181, 129))
            .WithThumbnailUrl("https://i.imgur.com/jYp2WsN.png")
            .WithFooter($"Solicitado por {Context.User.Username}", Context.User.GetAvatarUrl())
            .WithCurrentTimestamp();

        // Proyecto / dueño / librería
        builder.AddField("__**📦 Proyecto**__",
            $"- **Código fuente (PokeBot):** {Format.Url("GitHub", PokeBotRepo)}\n" +
            $"- **Código fuente (este fork):** {Format.Url("GitHub", ForkRepo)}\n" +
            $"- **Propietario:** {app.Owner} (`{app.Owner.Id}`)\n" +
            $"- **Librería:** Discord.Net (`{DiscordConfig.Version}`)",
            inline: false);

        // Versiones (ajusta PokeNexo.Version si aplica en tu solución)
        builder.AddField("__**🏷️ Versiones**__",
            $"- **Bot:** `{PokeNexo.Version}`\n" +
            $"- **PKHeX.Core:** `{GetVersionInfo("PKHeX.Core")}`\n" +
            $"- **AutoLegality:** `{GetVersionInfo("PKHeX.Core.AutoMod")}`\n" +
            $"- **SysBot.Base (build):** `{GetVersionInfo("SysBot.Base", inclVersion: false)}`",
            inline: false);

        // Entorno (runtime/OS/arquitectura)
        builder.AddField("__**⚙️ Entorno**__",
            $"`{RuntimeInformation.FrameworkDescription}` `{RuntimeInformation.ProcessArchitecture}`\n" +
            $"`{RuntimeInformation.OSDescription}` `{RuntimeInformation.OSArchitecture}`",
            inline: false);

        // Agradecimiento (de nuevo incluido)
        builder.AddField("__**🙏 Créditos**__",
            $"**Gracias**, __**{Format.Url("Project Pokémon", "https://projectpokemon.org")}**__ " +
            "**por los sprites e imágenes públicos usados en este bot.**",
            inline: false);

        await ReplyAsync(embed: builder.Build()).ConfigureAwait(false);
    }

    // ----------------- Helpers -----------------

    private static string GetVersionInfo(string assemblyName, bool inclVersion = true)
    {
        const string _default = "Unknown";
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        var assembly = Array.Find(assemblies, x => x.GetName().Name == assemblyName);

        var attribute = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        if (attribute is null)
            return _default;

        var info = attribute.InformationalVersion; // e.g., "1.0.0+240910231500"
        var split = info.Split('+');
        if (split.Length >= 2)
        {
            var version = split[0];
            var revision = split[1];
            if (DateTime.TryParseExact(revision, "yyMMddHHmmss", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var buildTime))
            {
                return (inclVersion ? $"{version} " : "") + $@"{buildTime:yy-MM-dd\.HH\:mm}";
            }

            return inclVersion ? version : _default;
        }

        return inclVersion ? info : _default;
    }
}
