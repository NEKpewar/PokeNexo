using Discord;
using Discord.Commands;
using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord
{
    public class DonateModule : ModuleBase<SocketCommandContext>
    {
        private static readonly string[] ThankYouMessages =
        {
            "¡Gracias por tu apoyo!",
            "¡Tu donación significa mucho!",
            "¡Eres increíble por apoyarnos!",
            "¡Gracias por ser parte de esto!",
            "¡Tu generosidad es apreciada!"
        };

        [Command("donate")]
        [Alias("donation", "donar", "donación")]
        [Summary("Muestra el enlace de donación del anfitrión, con barra de progreso si está habilitada.")]
        public async Task DonateAsync()
        {
            var settings = SysCordSettings.Settings;
            var donationSettings = settings?.Donation;

            // Validaciones básicas
            var link = donationSettings?.DonationLink?.Trim();
            if (string.IsNullOrWhiteSpace(link))
            {
                await ReplyAsync("❌ No hay un enlace de donación configurado.").ConfigureAwait(false);
                return;
            }

            // Mensaje aleatorio de agradecimiento
            var rng = new Random();
            var thankYouMessage = ThankYouMessages[rng.Next(ThankYouMessages.Length)];

            // Construcción del embed
            var embed = new EmbedBuilder();

            // Si hay barra de progreso, la usamos para decidir el color del embed
            double goal = 0, current = 0, progress = 0;
#pragma warning disable CS8602 // Dereference of a possibly null reference.
            var showProgress = donationSettings.ProgressBar?.ShowProgressBar == true;
#pragma warning restore CS8602 // Dereference of a possibly null reference.

            if (showProgress)
            {
#pragma warning disable CS8602 // Dereference of a possibly null reference.
                goal = ParseMoney(donationSettings.ProgressBar.DonationGoal);
#pragma warning restore CS8602 // Dereference of a possibly null reference.
                current = ParseMoney(donationSettings.ProgressBar.DonationCurrent);
                progress = goal > 0 ? Math.Clamp(current / goal, 0, 1) : 0;
                embed.WithColor(ProgressColor(progress));
            }
            else
            {
                embed.WithColor(new Color(255, 59, 48)); // Rojo llamativo por defecto
            }

            embed
                .WithTitle("❤️ ¡Enlace de Donación! ❤️")
                .WithDescription($"{thankYouMessage}\n\n[Haz clic aquí para donar]({link})")
                .WithUrl(link)
                .WithThumbnailUrl("https://i.imgur.com/0xwz3yL.png") // Ícono más limpio; cambia si prefieres otro
                .WithFooter(footer =>
                {
                    footer.Text = $"Solicitado por {Context.User.Username}";
                    footer.IconUrl = Context.User.GetAvatarUrl() ?? Context.User.GetDefaultAvatarUrl();
                })
                .WithCurrentTimestamp();

            // Si está habilitado, añadimos barra + detalles
            if (showProgress)
            {
                var bar = BuildProgressBar(progress, 12);
                var (fmtCurrent, fmtGoal, fmtRemaining) = FormatMoneyTriple(current, goal);

                embed.AddField("Progreso de la meta", $"{bar}\n**{fmtCurrent} / {fmtGoal}** ({progress * 100:0}%)");
                if (goal > 0 && current < goal)
                    embed.AddField("Restante", fmtRemaining, inline: true);
            }

            // Botón de enlace (mejor UX que solo link en el texto)
            var components = new ComponentBuilder()
                .WithButton("Donar 💖", style: ButtonStyle.Link, url: link)
                .Build();

            await ReplyAsync(embed: embed.Build(), components: components).ConfigureAwait(false);
        }

        // --- Helpers ---

        // Acepta valores con símbolo ($, €, etc.), comas y puntos. Intenta ser tolerante con formatos.
        private static double ParseMoney(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0;

            // Quitar símbolos de moneda y espacios
            var cleaned = Regex.Replace(value, @"[^\d.,-]", "").Trim();

            // Intentar primero InvariantCulture (1234.56)
            if (double.TryParse(cleaned, NumberStyles.Number | NumberStyles.AllowLeadingSign,
                                CultureInfo.InvariantCulture, out var resultInv))
            {
                return Math.Max(0, resultInv);
            }

            // Intentar formato es-ES (1.234,56)
            var es = new CultureInfo("es-ES");
            if (double.TryParse(cleaned, NumberStyles.Number | NumberStyles.AllowLeadingSign,
                                es, out var resultEs))
            {
                return Math.Max(0, resultEs);
            }

            return 0;
        }

        private static (string fmtCurrent, string fmtGoal, string fmtRemaining) FormatMoneyTriple(double current, double goal)
        {
            string F(double v) => v.ToString("C2", CultureInfo.GetCultureInfo("en-US")); // "$1,234.56"
            var remaining = Math.Max(0, goal - current);
            return (F(current), F(goal), F(remaining));
        }

        // Barra de progreso monospace con bloques llenos/vacíos
        private static string BuildProgressBar(double progress, int segments = 10)
        {
            progress = Math.Clamp(progress, 0, 1);
            segments = Math.Max(1, segments);

            // Bloques estilizados que se ven bien en Discord
            const char filled = '▰';
            const char empty = '▱';

            var filledCount = (int)Math.Round(progress * segments, MidpointRounding.AwayFromZero);
            filledCount = Math.Clamp(filledCount, 0, segments);

            return new string(filled, filledCount) + new string(empty, segments - filledCount);
        }

        // Color dinámico (rojo -> amarillo -> verde) según progreso
        private static Color ProgressColor(double t)
        {
            t = Math.Clamp(t, 0, 1);
            // 0..0.5: rojo->amarillo, 0.5..1: amarillo->verde
            if (t < 0.5)
            {
                // Rojo (255,59,48) a Amarillo (255,204,0)
                var k = t / 0.5;
                int r = Lerp(255, 255, k);
                int g = Lerp(59, 204, k);
                int b = Lerp(48, 0, k);
                return new Color(r, g, b);
            }
            else
            {
                // Amarillo (255,204,0) a Verde (16,185,129)
                var k = (t - 0.5) / 0.5;
                int r = Lerp(255, 16, k);
                int g = Lerp(204, 185, k);
                int b = Lerp(0, 129, k);
                return new Color(r, g, b);
            }
        }

        private static int Lerp(int a, int b, double t) => a + (int)Math.Round((b - a) * Math.Clamp(t, 0, 1));
    }
}
