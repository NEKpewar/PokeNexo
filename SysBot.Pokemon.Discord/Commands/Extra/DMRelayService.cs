using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord.Extra
{
    /// <summary>
    /// Relevo de DMs:
    /// - Reenvía mensajes directos (DM) recibidos por el bot hacia un destino (usuario o canal) en formato de embed.
    /// - Implementa "Entrega Segura": sin menciones accidentales y con saneo básico del contenido.
    /// - Agrega "Contexto Útil": edad de cuenta, servidores en común, fecha de ingreso (si aplica), dispositivo activo, etc.
    /// </summary>
    public class DMRelayService
    {
        private readonly DiscordSocketClient _client;
        private readonly ulong _forwardTargetId;

        // Prefijo de comandos para ignorar DMs que en realidad son comandos hacia el bot.
        private static string Prefix => SysCordSettings.Settings.CommandPrefix;

        public DMRelayService(DiscordSocketClient client, ulong forwardTargetId)
        {
            _client = client;
            _forwardTargetId = forwardTargetId;

            if (_forwardTargetId != 0)
                _client.MessageReceived += HandleMessageAsync;
        }

        /// <summary>
        /// Maneja cada mensaje recibido y, si es un DM válido (no bot / no comando), lo reenvía como embed.
        /// </summary>
        private async Task HandleMessageAsync(SocketMessage msg)
        {
            if (msg is not SocketUserMessage umsg) return;
            if (umsg.Author.IsBot) return;                        // Ignorar bots
            if (umsg.Channel is not SocketDMChannel) return;      // Solo DMs

            // Ignorar comandos (si la persona intenta usar comandos del bot en DM)
            int argPos = 0;
            if (umsg.HasStringPrefix(Prefix, ref argPos) || umsg.HasMentionPrefix(_client.CurrentUser, ref argPos))
                return;

            // Construir embed con toda la información útil
            var embed = BuildDmEmbed(umsg);

            // Envío con "Entrega Segura": sin menciones en el reenvío
            var safeMentions = new AllowedMentions(AllowedMentionTypes.None);

            // Preferir reenviar a un usuario si el ID objetivo es un usuario
            var targetUser = _client.GetUser(_forwardTargetId);
            if (targetUser != null)
            {
                await targetUser.SendMessageAsync(embed: embed, allowedMentions: safeMentions).ConfigureAwait(false);
                return;
            }

            // Si no es usuario, intentar como canal (ej. canal de staff/logs)
            if (_client.GetChannel(_forwardTargetId) is IMessageChannel channel)
            {
                await channel.SendMessageAsync(embed: embed, allowedMentions: safeMentions).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Construye un Embed elegante con datos del autor, contenido, adjuntos y contexto útil.
        /// </summary>
        private Embed BuildDmEmbed(SocketUserMessage umsg)
        {
            var autor = umsg.Author;
            var socketAutor = autor as SocketUser; // para ActiveClients si está disponible
            var avatarUrl = autor.GetAvatarUrl(ImageFormat.Auto, 512) ?? autor.GetDefaultAvatarUrl();

            // -------- Entrega Segura: sanitizar contenido y limitar longitudes --------
            string contenido = string.IsNullOrWhiteSpace(umsg.Content) ? "*Sin contenido de mensaje.*" : umsg.Content;
            contenido = SanitizarContenido(contenido);
            if (contenido.Length > 4096)
                contenido = contenido.Substring(0, 4093) + "...";

            // -------- Adjuntos: lista clicable + previsualización de la primera imagen --------
            string listaAdjuntos = null;
            if (umsg.Attachments.Count > 0)
            {
                var sb = new StringBuilder();
                foreach (var att in umsg.Attachments)
                    sb.AppendLine($"• [{att.Filename}]({att.Url})");
                listaAdjuntos = sb.ToString().TrimEnd();
                if (listaAdjuntos.Length > 1024)
                    listaAdjuntos = listaAdjuntos.Substring(0, 1021) + "...";
            }

            string imagenUrl = null;
            var primeraImagen = umsg.Attachments.FirstOrDefault(a =>
                (a.ContentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ?? false) ||
                a.Url.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                a.Url.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                a.Url.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                a.Url.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
                a.Url.EndsWith(".webp", StringComparison.OrdinalIgnoreCase));
            if (primeraImagen != null)
                imagenUrl = primeraImagen.Url;

            // -------- Contexto Útil --------
            // Edad de cuenta en días (simple y sin dependencias externas)
            var edadCuentaDias = Math.Max(0, (int)(DateTimeOffset.UtcNow - autor.CreatedAt).TotalDays);

            // Servidores en común
            int servidoresComunes = _client.Guilds.Count(g => g.GetUser(autor.Id) != null);

            // Fecha de ingreso al primer servidor en común (si aplica)
            string ingresoServidor = null;
            var guildComun = _client.Guilds.FirstOrDefault(g => g.GetUser(autor.Id) is not null);
            if (guildComun != null)
            {
                var miembro = guildComun.GetUser(autor.Id);
                if (miembro != null && miembro.JoinedAt.HasValue)
                    ingresoServidor = $"{miembro.JoinedAt.Value.UtcDateTime:yyyy-MM-dd}";
            }

            // Dispositivo/plataforma activa (si existe ActiveClients en la versión de Discord.Net)
            string activoEn = "Desconocido";
            var activo = socketAutor?.ActiveClients?.FirstOrDefault();
            if (activo.HasValue)
                activoEn = activo.Value.ToString();

            // -------- Construcción del Embed --------
            var etiquetaAutor = $"{autor.Username}" +
                                (string.IsNullOrEmpty(autor.Discriminator) || autor.Discriminator == "0000"
                                    ? ""
                                    : $"#{autor.Discriminator}") +
                                $"  •  ID: {autor.Id}";

            var eb = new EmbedBuilder()
                .WithColor(new Color(0x5865F2)) // morado Discord
                .WithAuthor(a =>
                {
                    a.Name = etiquetaAutor;
                    a.IconUrl = avatarUrl;
                })
                .WithDescription(contenido)
                .WithThumbnailUrl(avatarUrl)
                .WithTimestamp(umsg.Timestamp) // Discord lo mostrará en la zona horaria del visor
                .WithFooter(f =>
                {
                    f.Text = "Relé de DMs • Mensaje directo";
                });

            // Campos principales
            eb.AddField("Usuario", $"{MentionUtils.MentionUser(autor.Id)}", inline: true);
            eb.AddField("ID del mensaje", umsg.Id.ToString(), inline: true);
            eb.AddField("Recibido (UTC)", $"{umsg.Timestamp.UtcDateTime:yyyy-MM-dd HH:mm:ss}", inline: true);

            // Contexto útil
            eb.AddField("Edad de la cuenta", $"{edadCuentaDias} días", inline: true);
            eb.AddField("Servidores en común", servidoresComunes.ToString(), inline: true);
            if (!string.IsNullOrEmpty(ingresoServidor))
                eb.AddField("Se unió al servidor", ingresoServidor, inline: true);
            eb.AddField("Activo en", activoEn, inline: true);

            // Adjuntos (si hay)
            if (!string.IsNullOrEmpty(listaAdjuntos))
                eb.AddField("Adjuntos", listaAdjuntos, inline: false);

            // Imagen en grande (si hay)
            if (!string.IsNullOrEmpty(imagenUrl))
                eb.WithImageUrl(imagenUrl);

            return eb.Build();
        }

        /// <summary>
        /// Sanea el contenido para evitar activación de menciones especiales o patrones molestos en logs.
        /// </summary>
        private static string SanitizarContenido(string texto)
        {
            if (string.IsNullOrEmpty(texto))
                return texto;

            // Evita @everyone y @here
            texto = texto.Replace("@everyone", "[@everyone]")
                         .Replace("@here", "[@here]");

            // Opcional: neutralizar "<@123>", "<@!123>" y "<@&456>" (menciones de usuarios/roles) para staff logs
            // Reemplazamos el inicio "<@" por "[<@" para que no resuelva la mención.
            texto = texto.Replace("<@", "[<@")
                         .Replace("<@!", "[<@!")
                         .Replace("<@&", "[<@&");

            return texto;
        }
    }
}
