using Discord;
using Discord.Commands;
using System;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord
{
    public class StreamModule : ModuleBase<SocketCommandContext>
    {
        private static readonly string[] StreamMessages =
        {
            "¡Dale un vistazo al stream!",
            "¡No te lo pierdas!",
            "¡Transmisión en vivo ahora!",
            "¡Únete a la diversión!",
            "¡En vivo ahora mismo!"
        };

        [Command("stream")]
        [Alias("streamlink")]
        [Summary("Devuelve el enlace de transmisión del anfitrión.")]
        public async Task StreamAsync()
        {
            var settings = SysCordSettings.Settings;
            var iconOption = settings.Stream.StreamIcon;

            // Si no hay link configurado, usar Twitch por defecto
            var streamLink = string.IsNullOrWhiteSpace(settings.Stream.StreamLink)
                ? "https://twitch.tv/"
                : settings.Stream.StreamLink;

            var streamIconUrl = DiscordSettings.StreamOptions.StreamIconUrls[iconOption];
            var embedColor = GetEmbedColor(iconOption);
            var platformName = GetStreamPlatformName(iconOption);

            var streamMessage = StreamMessages[new Random().Next(StreamMessages.Length)];

            var embed = new EmbedBuilder()
                .WithTitle($"🎥 {platformName} Stream 🎥")
                .WithDescription($"{streamMessage}\n\n[🔗 Haz clic aquí para ver el stream]({streamLink})")
                .WithUrl(streamLink)
                .WithThumbnailUrl(streamIconUrl)
                .WithColor(embedColor)
                .AddField("🌐 Plataforma", platformName, inline: true)
                .AddField("📺 Enlace", $"[Click aquí]({streamLink})", inline: true)
                .WithImageUrl("https://i.imgur.com/OmLhdAS.gif") // Banner del stream (puedes cambiar la URL)
                .WithFooter(footer =>
                {
                    footer.Text = $"Solicitado por {Context.User.Username}";
                    footer.IconUrl = Context.User.GetAvatarUrl() ?? Context.User.GetDefaultAvatarUrl();
                })
                .WithCurrentTimestamp()
                .Build();

            await ReplyAsync(embed: embed).ConfigureAwait(false);
        }

        private static Color GetEmbedColor(StreamIconOption icon) =>
            icon switch
            {
                StreamIconOption.Twitch => new Color(145, 70, 255),  // Twitch Purple
                StreamIconOption.Youtube => new Color(255, 0, 0),    // YouTube Red
                StreamIconOption.Facebook => new Color(24, 119, 242), // Facebook Blue
                StreamIconOption.Kick => new Color(0, 255, 0),    // Kick Green
                StreamIconOption.TikTok => new Color(0, 0, 0),      // TikTok Black
                _ => Color.Default
            };

        private static string GetStreamPlatformName(StreamIconOption icon) =>
            icon switch
            {
                StreamIconOption.Twitch => "Twitch",
                StreamIconOption.Youtube => "YouTube",
                StreamIconOption.Facebook => "Facebook",
                StreamIconOption.Kick => "Kick",
                StreamIconOption.TikTok => "TikTok",
                _ => "Stream"
            };
    }
}
