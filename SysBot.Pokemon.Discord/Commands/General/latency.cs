using Discord;
using Discord.Commands;
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord
{
    public class LatencyModule : ModuleBase<SocketCommandContext>
    {
        // Momento de arranque del proceso (para uptime)
        private static readonly DateTime ProcessStartUtc = Process.GetCurrentProcess().StartTime.ToUniversalTime();

        [Command("latency")]
        [Alias("latencia", "ping")]
        [Summary("Muestra latencia WebSocket, tiempo de respuesta, uptime, uso de CPU/memoria, servidores y usuarios.")]
        [RequireOwner]
        public async Task LatencyAsync()
        {
            var me = Context.Client.CurrentUser;

            // Paso 1: Mensaje inicial para medir roundtrip
            var sw = Stopwatch.StartNew();
            var probe = await ReplyAsync("🏓 Midiendo latencia...").ConfigureAwait(false);
            sw.Stop();

            // Métricas
            int wsLatency = Context.Client.Latency;                       // WebSocket latency
            long apiRoundtrip = sw.ElapsedMilliseconds;                   // Respuesta (enviar/editar)
            using var proc = Process.GetCurrentProcess();
            double memMb = proc.WorkingSet64 / 1024d / 1024d;
            double cpuPct = GetCpuUsagePercent();
            int guilds = Context.Client.Guilds.Count;
            int users = Context.Client.Guilds.Sum(g => g.MemberCount);

            // Uptime estático (texto fijo, no timestamps que se actualicen)
            var prettyUptime = GetPrettyUptime();

            // Estado/Color por latencia
            var (statusEmoji, statusLabel, color) = GetLatencyStatus(wsLatency, apiRoundtrip);

            var embed = new EmbedBuilder()
                .WithAuthor(a =>
                {
                    a.Name = me?.Username ?? "SysBot";
                    a.IconUrl = me?.GetAvatarUrl() ?? me?.GetDefaultAvatarUrl();
                })
                .WithTitle($"{statusEmoji} Estado de Conexión")
                .WithColor(color)
                .WithFooter($"Solicitado por {Context.User.Username}", Context.User.GetAvatarUrl())
                .WithCurrentTimestamp()
                .AddField("__**📶 Latencias**__",
                    $"- **WebSocket:** `{wsLatency} ms`\n" +
                    $"- **Respuesta API:** `{apiRoundtrip} ms`\n" +
                    $"- **Estado:** `{statusLabel}`",
                    inline: false)
                .AddField("__**⏱️ Uptime**__",
                    $"`{prettyUptime}`",
                    inline: false)
                .AddField("__**🧠 Recursos**__",
                    $"- **Memoria:** `{memMb:N2} MiB`\n" +
                    $"- **CPU (aprox):** `{cpuPct:N2}%`",
                    inline: true)
                .AddField("__**🌐 Alcance**__",
                    $"- **Servidores:** `{guilds:N0}`\n" +
                    $"- **Usuarios:** `{users:N0}`",
                    inline: true)
                .AddField("__**⚙️ Entorno**__",
                    $"`{RuntimeInformation.FrameworkDescription}` `{RuntimeInformation.ProcessArchitecture}`\n" +
                    $"`{RuntimeInformation.OSDescription}` `{RuntimeInformation.OSArchitecture}`",
                    inline: false)
                .Build();

            await probe.ModifyAsync(m =>
            {
                m.Content = string.Empty;
                m.Embed = embed;
            }).ConfigureAwait(false);
        }

        // ------- Helpers -------

        private static (string emoji, string label, Color color) GetLatencyStatus(int wsMs, long apiMs)
        {
            // Usa la peor de ambas para clasificar
            var worst = Math.Max(wsMs, (int)apiMs);
            if (worst <= 120) return ("🟩", "Excelente", Color.Green);
            if (worst <= 250) return ("🟧", "Aceptable", Color.Orange);
            return ("🟥", "Alta latencia", Color.DarkRed);
        }

        private static double GetCpuUsagePercent()
        {
            // Estimación basada en tiempo de CPU acumulado vs vida del proceso
            using var p = Process.GetCurrentProcess();
            var lifetimeMs = (DateTime.UtcNow - ProcessStartUtc).TotalMilliseconds;
            if (lifetimeMs <= 0) return 0;

            // Normaliza por núcleos lógicos
            var cpuMs = p.TotalProcessorTime.TotalMilliseconds;
            var logical = Environment.ProcessorCount;
            var pct = (cpuMs / (lifetimeMs * logical)) * 100.0;
            return Math.Max(0, Math.Min(100, pct));
        }

        private static string GetPrettyUptime()
        {
            var span = DateTime.UtcNow - ProcessStartUtc;
            string Days() => span.Days > 0 ? $"{span.Days}d " : "";
            string Hours() => span.Hours > 0 ? $"{span.Hours}h " : "";
            string Minutes() => span.Minutes > 0 ? $"{span.Minutes}m " : "";
            string Seconds() => span.Seconds > 0 ? $"{span.Seconds}s" : "0s";
            return $"{Days()}{Hours()}{Minutes()}{Seconds()}".Trim();
        }
    }
}
