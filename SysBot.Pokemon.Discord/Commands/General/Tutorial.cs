using Discord;
using Discord.Commands;
using Discord.WebSocket;
using PKHeX.Core;
using System.Linq;
using SysBot.Pokemon.Discord;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace TuBotDiscord.Modules;

public class TradeModule<T> : ModuleBase<SocketCommandContext> where T : PKM, new()
{
    public class TutorialModule : ModuleBase<SocketCommandContext>
    {
        [Command("ayuda")]
        [Summary("Muestra como usar algunos comandos como el clone, fix, egg y demas.")]
        public async Task HelpAsync(string? command = null)
        {
            var botPrefix = SysCord<T>.Runner.Config.Discord.CommandPrefix;

            // Si el usuario pidió ayuda para un comando específico
            if (!string.IsNullOrEmpty(command))
            {
                var embedBuilder = new EmbedBuilder();
                var icon = "https://i.imgur.com/axXN5Sd.gif";

                ConfigureHelpEmbed(command.ToLower(), embedBuilder, icon, botPrefix);

                try
                {
                    // Enviar el mensaje por DM
                    var dmChannel = await Context.User.CreateDMChannelAsync();
                    await dmChannel.SendMessageAsync(embed: embedBuilder.Build());

                    // Eliminar el mensaje del usuario del canal
                    await Context.Message.DeleteAsync();

                    // Enviar confirmación en el canal
                    var confirmation = await ReplyAsync($"✅ {Context.User.Mention}, la información de ayuda sobre el comando `{command}` ha sido enviada a tu MD. Por favor, revisa tus mensajes directos.");

                    // Borrar el mensaje de confirmación después de 5 segundos
                    await Task.Delay(TimeSpan.FromSeconds(5));
                    await confirmation.DeleteAsync();
                }
                catch
                {
                    // Si el usuario tiene los DMs bloqueados, notificar en el canal
                    await ReplyAsync($"❌ {Context.User.Mention}, no puedo enviarte un mensaje privado. Asegúrate de tener los DMs habilitados.**");
                }

                return;
            }

            var builder = new EmbedBuilder()
                .WithTitle("Comandos disponibles")
                .WithDescription($"Selecciona un comando del menú desplegable para obtener más información.\n\n🔴 **Haz clic en el botón 'Cerrar' cuando hayas terminado.**")
                .AddField("» Menú Ayuda", $"Tenemos `13` categorías de las cuales puedes aprender cómo usar sus correspondientes funciones.\n\n**También puedes usar `{botPrefix}ayuda <comando>` para acceder directamente a un tema.**")
                .AddField("Opciones",
                    $"- `{botPrefix}ayuda sr` ∷ Pedidos Especiales.\n" +
                    $"- `{botPrefix}ayuda brl` ∷ Pokemons Entrenados.\n" +
                    $"- `{botPrefix}ayuda le` ∷ Eventos.\n" +
                    $"- `{botPrefix}ayuda bt` ∷ Intercambio por Lotes.\n" +
                    $"- `{botPrefix}ayuda clone` ∷ Clonar un Pokemon.\n" +
                    $"- `{botPrefix}ayuda fix` ∷ Quitar Anuncios de Pokemons.\n" +
                    $"- `{botPrefix}ayuda ditto` ∷ Como pedir Dittos.\n" +
                    $"- `{botPrefix}ayuda me` ∷ Como pedir Huevos Misteriosos.\n" +
                    $"- `{botPrefix}ayuda egg` ∷ Como pedir Huevos de un Pokemon específico.\n" +
                    $"- `{botPrefix}ayuda rt` ∷ Como generar un equipo VGC random.\n" +
                    $"- `{botPrefix}ayuda pp` ∷ Cómo generar un equipo a partir de un link PokePaste.\n" +
                    $"- `{botPrefix}ayuda srp` ∷ Como pedir Regalos Misteriosos.\n" +
                    $"- `{botPrefix}ayuda codigos` ∷ Gestión de Códigos de Intercambio.")
                .WithColor(Discord.Color.Blue);

            var selectMenu = new SelectMenuBuilder()
                .WithPlaceholder("📜 Selecciona un comando...") // Emoji in placeholder
                .WithCustomId("help_menu")
                .AddOption("Pedidos Especiales", "help_sr", "Información sobre pedidos especiales", new Emoji("📌"))
                .AddOption("Pokemons Entrenados", "help_brl", "Lista de pokémons entrenados", new Emoji("⚔️"))
                .AddOption("Eventos", "help_le", "Cómo solicitar eventos", new Emoji("🎉"))
                .AddOption("Intercambio por Lotes", "help_bt", "Cómo realizar intercambios por lotes", new Emoji("📦"))
                .AddOption("Clone", "help_clone", "Cómo clonar un Pokémon", new Emoji("🔁"))
                .AddOption("Fix", "help_fix", "Eliminar nombres no deseados de Pokémon", new Emoji("🛠️"))
                .AddOption("Ditto", "help_ditto", "Solicitar un Ditto con IVs específicos", new Emoji("✨"))
                .AddOption("Huevo Misterioso", "help_me", "Solicitar un huevo misterioso aleatorio", new Emoji("🥚"))
                .AddOption("Huevos", "help_egg", "Cómo solicitar huevos", new Emoji("🐣"))
                .AddOption("Equipo Random", "help_rt", "Generar un equipo aleatorio", new Emoji("🎲"))
                .AddOption("Equipo Completo", "help_pp", "Cómo obtener un equipo completo", new Emoji("🏆"))
                .AddOption("Regalos Misteriosos", "help_srp", "Solicitar regalos misteriosos", new Emoji("🎁"))
                .AddOption("Códigos de Intercambio", "help_codigos", "Gestionar y usar tus códigos de intercambio", new Emoji("🔑"));

            var closeButton = new ButtonBuilder()
                .WithLabel("Cerrar")
                .WithStyle(ButtonStyle.Danger)
                .WithCustomId("close_help");

            var componentBuilder = new ComponentBuilder()
                .WithSelectMenu(selectMenu)
                .WithButton(closeButton);

            var message = await ReplyAsync(embed: builder.Build(), components: componentBuilder.Build());
            await Context.Message.DeleteAsync();

            await HandleInteractions(message);
        }

        private async Task HandleInteractions(IUserMessage message)
        {
            var cancellationTokenSource = new CancellationTokenSource();
            var timeoutTask = Task.Delay(TimeSpan.FromMinutes(2), cancellationTokenSource.Token);

            while (true)
            {
                var interactionTask = WaitForInteractionResponseAsync(message, TimeSpan.FromMinutes(2));
                var completedTask = await Task.WhenAny(interactionTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    // Timeout occurred, remove the select menu and buttons
                    await message.ModifyAsync(msg => msg.Components = new ComponentBuilder().Build());
                    break;
                }

                var interaction = await interactionTask;
                if (interaction != null)
                {
                    // Reset the timeout
                    cancellationTokenSource.Cancel();
                    cancellationTokenSource = new CancellationTokenSource();
                    timeoutTask = Task.Delay(TimeSpan.FromMinutes(2), cancellationTokenSource.Token);

                    if (interaction.Data.CustomId == "close_help")
                    {
                        await interaction.Message.DeleteAsync();
                        return;
                    }

                    await interaction.DeferAsync(); // No ephemeral response

                    var command = interaction.Data.Values.FirstOrDefault()?.Substring(5) ?? string.Empty;
                    var icon = "https://i.imgur.com/axXN5Sd.gif";
                    var embedBuilder = new EmbedBuilder();

                    ConfigureHelpEmbed(command, embedBuilder, icon, SysCord<T>.Runner.Config.Discord.CommandPrefix);

                    // Edit the main embed instead of sending a new ephemeral message
                    await message.ModifyAsync(msg =>
                    {
                        msg.Embed = embedBuilder.Build();
                    });
                }
            }
        }

        private async Task<SocketMessageComponent?> WaitForInteractionResponseAsync(IUserMessage message, TimeSpan timeout)
        {
            var tcs = new TaskCompletionSource<SocketMessageComponent?>();
            var cancellationTokenSource = new CancellationTokenSource(timeout);

            Context.Client.InteractionCreated += OnInteractionCreated;

            try
            {
                return await tcs.Task;
            }
            catch (TaskCanceledException)
            {
                return null;
            }
            finally
            {
                Context.Client.InteractionCreated -= OnInteractionCreated;
                cancellationTokenSource.Dispose();
            }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
            async Task OnInteractionCreated(SocketInteraction interaction)
            {
                if (interaction is SocketMessageComponent componentInteraction &&
                    componentInteraction.Message.Id == message.Id)
                {
                    tcs.TrySetResult(componentInteraction);
                }
            }
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        }

        private void ConfigureHelpEmbed(string command, EmbedBuilder builder, string icon, string botPrefix)
        {
            // Set the thumbnail for all embeds
            builder.WithThumbnailUrl("https://i.imgur.com/lPU9wFp.png");

            switch (command.ToLower())
            {
                case "sr":
                    builder.WithAuthor("Pedidos Especiales", icon)
                           .WithDescription($"# Pedidos Especiales\n\n" +
                                            $"Este comando permite hacer **modificaciones especiales** a un Pokémon usando un objeto o apodo específico. " +
                                            $"Luego solo debes cambiar a un Pokémon de descarte para completar el intercambio.\n\n" +
                                            $"## Comando:\n" +
                                            $"- **`{botPrefix}sr`**\n\n" +
                                            $"⚠️ El Pokémon de descarte debe ser diferente al original. Asegúrate de que la petición sea **legal** (ej. no intentes hacer shiny un Pokémon con shiny lock o cambiar el OT de un evento).\n\n" +
                                            $"## Opciones Disponibles:\n\n" +
                                            $"### 🔹 Limpieza de OT/Apodo\n" +
                                            $"- **Poké Ball** → Borra apodo.\n" +
                                            $"- **Great Ball** → Borra OT (lo reemplaza por tu nombre de entrenador).\n" +
                                            $"- **Ultra Ball** → Borra OT y apodo.\n" +
                                            $"*Nota: Los apodos vuelven al nombre original del idioma del Pokémon.*\n\n" +
                                            $"### 🌍 Cambios de Idioma\n" +
                                            $"- **Protección X** → Japonés\n" +
                                            $"- **Crítico X** → Inglés\n" +
                                            $"- **Ataque X** → Alemán\n" +
                                            $"- **Defensa X** → Francés\n" +
                                            $"- **Velocidad X** → Español\n" +
                                            $"- **Precisión X** → Coreano\n" +
                                            $"- **Ataque Especial X** → Chino T\n" +
                                            $"- **Defensa Especial X** → Chino S\n" +
                                            $"*Nota: Esto también borra apodos.*\n\n" +
                                            $"### 📊 Estadísticas\n" +
                                            $"- **Cura Total** → 6IV\n" +
                                            $"- **Pokémuñeco** → 5IV, 0 Velocidad\n" +
                                            $"- **Revivir** → 4IV, 0 Velocidad, 0 Ataque\n" +
                                            $"- **Agua Fresca** → 5IV, 0 Ataque\n" +
                                            $"- **Refresco** → Nivel 100\n" +
                                            $"- **Limonada** → 6IV + Nivel 100\n" +
                                            $"*Puedes cambiar la naturaleza con el objeto Menta correspondiente.*\n\n" +
                                            $"### ✨ Shiny\n" +
                                            $"- **Antiquemar** → Shiny\n" +
                                            $"- **Despertar** → Shiny + 6IV\n" +
                                            $"- **Antiparalizador** → Convierte un Pokémon shiny en no-shiny\n" +
                                            $"*Nota: Puedes hacer shiny un huevo mostrándoselo al bot 3-5 segundos y cambiándolo por un descarte.*\n\n" +
                                            $"### 🔮 Tera Type\n" +
                                            $"- **Teralito Agua** → Tipo Agua\n" +
                                            $"- **Teralito Fuego** → Tipo Fuego\n" +
                                            $"- **Teralito Eléctrico** → Tipo Eléctrico\n" +
                                            $"- *(y demás Teralitos por tipo...)*\n\n" +
                                            $"### ⚪ Poké Ball\n" +
                                            $"Apoda al Pokémon con el formato: `?(ball_name)`\n" +
                                            $"Ejemplo: `?beastball` o `?beastba`\n" +
                                            $"*No pidas balls ilegales (ej. Pokémon de GO en Friend Ball).* \n\n" +
                                            $"### ♂️/♀️ Género\n" +
                                            $"Apoda al Pokémon como `!male` o `!female` para cambiar su género.\n" +
                                            $"*No funciona en Pokémon sin género o con género bloqueado.*\n\n" +
                                            $"## Resumen\n" +
                                            $"Elige el efecto deseado, dale el objeto o apodo al Pokémon y usa **`.sr`**. " +
                                            $"En el intercambio, muéstrale el Pokémon al bot, luego cámbialo por uno de descarte para recibir la versión modificada.");
                    break;
                case "brl":
                    builder.WithAuthor("Pokémon Entrenados", icon)
                           .WithDescription($"# Pokémon Entrenados\n\n" +
                                            $"Este comando muestra una lista de **Pokémon listos para batalla**, incluyendo legendarios, míticos y otros populares en competición. Todos vienen con:\n" +
                                            $"- ✅ **EVs entrenados**\n" +
                                            $"- ✅ Compatibilidad con **HOME**\n" +
                                            $"- ✅ Listos para usarse en combates\n\n" +
                                            $"## Comando:\n" +
                                            $"- **`{botPrefix}brl`**\n\n" +
                                            $"## Ejemplos:\n" +
                                            $"```{botPrefix}brl calyrex```\n" +
                                            $"Muestra a Calyrex en la lista de Pokémon entrenados.\n\n" +
                                            $"```{botPrefix}brl 2```\n" +
                                            $"Muestra la **página 2** de la lista completa.\n\n" +
                                            $"## Funcionamiento:\n" +
                                            $"- El comando lista varios Pokémon entrenados.\n" +
                                            $"- Cada Pokémon tendrá un **código** que puedes usar para solicitarlo.\n\n" +
                                            $"⚔️ Ideal para jugadores que buscan Pokémon listos para la competición sin tener que entrenarlos manualmente.");
                    break;
                case "clone":
                    builder.WithAuthor("Clonar un Pokémon", icon)
                           .WithDescription($"# Clonar un Pokémon\n\n" +
                                            $"Este comando te permite clonar cualquier Pokémon que muestres al bot.\n\n" +
                                            $"## Comando:\n" +
                                            $"- **`{botPrefix}clone`**\n\n" +
                                            $"## Ejemplo:\n" +
                                            $"```{botPrefix}clone```\n" +
                                            $"El bot generará un código de intercambio para comenzar el proceso.\n\n" +
                                            $"## Funcionamiento:\n" +
                                            $"1. El bot te dará un **código de intercambio**.\n" +
                                            $"2. Cuando sea tu turno, el bot te avisará que está listo.\n" +
                                            $"3. Enséñale primero el Pokémon que quieres clonar.\n" +
                                            $"4. El bot te pedirá **cancelar ese intercambio**.\n" +
                                            $"5. Elige cualquier Pokémon de descarte para finalizar.\n\n" +
                                            $"✅ Recibirás una copia exacta del Pokémon que mostraste, además de conservar el original.");
                    break;
                case "ditto":
                    builder.WithAuthor("Ditto", icon)
                           .WithDescription($"# Solicitud de Ditto\n\n" +
                                            $"Permite pedir un **Ditto para crianza** con IVs, idioma y naturaleza específicos.\n\n" +
                                            $"## Comando:\n" +
                                            $"- **`{botPrefix}ditto <código> <modificadores> <idioma> <naturaleza>`**\n" +
                                            $"- **`{botPrefix}dt <código> <modificadores> <idioma> <naturaleza>`**\n\n" +
                                            $"⚠️ El **código** es opcional, pero los demás parámetros son obligatorios.\n\n" +
                                            $"## Ejemplo:\n" +
                                            $"```{botPrefix}ditto ATKSPE Japanese Modest```\n" +
                                            $"Genera un Ditto **0 Atk / 0 Spe**, en **Japonés**, con naturaleza **Modest**.\n\n" +
                                            $"## Idiomas Soportados:\n" +
                                            $"```Japanese, English, French, Italian, German, Spanish, Korean, ChineseS, ChineseT```\n\n" +
                                            $"## Modificadores Disponibles:\n" +
                                            $"```ATK       → 0 Ataque\n" +
                                            $"SPE       → 0 Velocidad\n" +
                                            $"SPA       → 0 Ataque Especial\n" +
                                            $"ATKSPE    → 0 Ataque y 0 Velocidad\n" +
                                            $"ATKSPESPA → 0 Ataque, 0 Velocidad y 0 At. Especial```\n\n" +
                                            $"## Naturaleza:\n" +
                                            $"- Se puede especificar cualquier naturaleza, por ejemplo: *Modest*, *Adamant*, *Timid*.\n\n" +
                                            $"## Más Ejemplos:\n" +
                                            $"```{botPrefix}ditto ATK German Adamant```\n" +
                                            $"Ditto con 0 Ataque, idioma Alemán y naturaleza Adamant.\n\n" +
                                            $"```{botPrefix}ditto SPE French Hasty```\n" +
                                            $"Ditto con 0 Velocidad, idioma Francés y naturaleza Hasty.\n\n" +
                                            $"```{botPrefix}ditto ATKSPE Japanese Modest```\n" +
                                            $"Ditto con 0 Ataque, 0 Velocidad, idioma Japonés y naturaleza Modest.\n\n" +
                                            $"```{botPrefix}ditto 6IV Korean Timid```\n" +
                                            $"Ditto 6IV, idioma Coreano y naturaleza Timid.");
                    break;
                case "fix":
                    builder.WithAuthor("Quitar Anuncios de Pokémon", icon)
                           .WithDescription($"# Quitar Anuncios de Pokémon\n\n" +
                                            $"Este comando sirve para **eliminar apodos no deseados** (como páginas web o publicidad) de los Pokémon.\n\n" +
                                            $"## Comando:\n" +
                                            $"- **`{botPrefix}fix`**\n\n" +
                                            $"## Ejemplo:\n" +
                                            $"```{botPrefix}fix```\n" +
                                            $"Clona y devuelve el mismo Pokémon que mostraste, pero sin el apodo publicitario.\n\n" +
                                            $"## Funcionamiento:\n" +
                                            $"- El bot recibe el Pokémon con apodo no deseado.\n" +
                                            $"- Lo clona automáticamente.\n" +
                                            $"- Te devuelve el mismo Pokémon, **limpio y sin anuncios**.\n\n" +
                                            $"⚠️ Solo se elimina el apodo; todas las demás características del Pokémon permanecen iguales.");
                    break;
                case "le":
                    builder.WithAuthor("Eventos", icon)
                           .WithDescription($"# Eventos\n\n" +
                                            $"Este comando te permite listar los **eventos disponibles** para solicitarlos mediante el bot.\n\n" +
                                            $"## Comando:\n" +
                                            $"- **`{botPrefix}le [filtro] [página]`**\n\n" +
                                            $"## Ejemplos:\n" +
                                            $"```{botPrefix}le d```\n" +
                                            $"Muestra los eventos que empiezan con la letra **D** (primeros 10 resultados).\n\n" +
                                            $"```{botPrefix}le d 2```\n" +
                                            $"Muestra la **página 2** de eventos que empiezan con la letra **D**.\n\n" +
                                            $"## Funcionamiento:\n" +
                                            $"- El bot enviará la lista de eventos a tu **MD** con el comando correcto para solicitarlos.\n" +
                                            $"- Cada evento tiene un **índice numérico**.\n" +
                                            $"- Para pedir un evento de la lista, utiliza el comando:\n" +
                                            $"  - **`{botPrefix}er <índice>`**\n\n" +
                                            $"## Notas:\n" +
                                            $"- La búsqueda puede ser por **letra inicial** y también por **página**.\n" +
                                            $"- El comando **de solicitud (`er`)** debe escribirse en el **canal del bot**, no en MD.");
                    break;
                case "bt":
                    builder.WithAuthor("Intercambio por Lotes", icon)
                           .WithDescription($"# Intercambio por Lotes\n\n" +
                                            $"Este comando permite intercambiar **varios Pokémon en un solo proceso**.\n\n" +
                                            $"## Comando:\n" +
                                            $"- **`{botPrefix}bt`** seguido de varios sets de Showdown.\n\n" +
                                            $"## Formato:\n" +
                                            $"```{botPrefix}bt\n" +
                                            $"[Plantilla Showdown]\n" +
                                            $"---\n" +
                                            $"[Plantilla Showdown]\n" +
                                            $"---\n" +
                                            $"[Plantilla Showdown]```\n\n" +
                                            $"⚠️ **Importante**:\n" +
                                            $"- Usa el mismo código de intercambio para todos los Pokémon del lote.\n" +
                                            $"- Separa cada set con **---** (tres guiones).\n" +
                                            $"- El bot cerrará y reabrirá el intercambio automáticamente después de cada trade exitoso.\n\n" +
                                            $"## Ejemplo:\n" +
                                            $"```{botPrefix}bt\n" +
                                            $"Solgaleo @ Ability Patch\n" +
                                            $"Level: 100\n" +
                                            $"Shiny: Yes\n" +
                                            $"EVs: 252 HP / 252 Atk / 6 Spe\n" +
                                            $"Tera Type: Dark\n" +
                                            $"- Calm Mind\n" +
                                            $"- Close Combat\n" +
                                            $"- Cosmic Power\n" +
                                            $"- Heavy Slam\n" +
                                            $"---\n" +
                                            $"Spectrier @ Ability Patch\n" +
                                            $"Level: 100\n" +
                                            $"EVs: 252 HP / 252 SpA / 6 Spe\n" +
                                            $"Tera Type: Dark\n" +
                                            $"- Nasty Plot\n" +
                                            $"- Night Shade\n" +
                                            $"- Phantom Force\n" +
                                            $"- Shadow Ball\n" +
                                            $"---\n" +
                                            $"Thundurus-Therian @ Ability Patch\n" +
                                            $"Level: 100\n" +
                                            $"EVs: 6 Atk / 252 SpA / 252 Spe\n" +
                                            $"Tera Type: Dark\n" +
                                            $"- Hammer Arm\n" +
                                            $"- Smart Strike\n" +
                                            $"- Taunt\n" +
                                            $"- Thunder Wave```");
                    break;
                case "me":
                    builder.WithAuthor("Huevo Misterioso", icon)
                           .WithDescription($"# Huevo Misterioso\n\n" +
                                            $"Solicita un **huevo misterioso aleatorio** que siempre será competitivo y especial.\n\n" +
                                            $"## Comando:\n" +
                                            $"- **`{botPrefix}me`**\n\n" +
                                            $"## Ejemplo:\n" +
                                            $"```{botPrefix}me```\n" +
                                            $"Genera un huevo misterioso.\n\n" +
                                            $"## Características del Huevo Misterioso:\n" +
                                            $"- ✨ Siempre será **Shiny**.\n" +
                                            $"- 📊 Tendrá **IVs perfectos (6IV)**.\n" +
                                            $"- 🌀 Vendrá con **Habilidad Oculta**.\n\n" +
                                            $"⚠️ Estos huevos son completamente aleatorios, no se puede elegir el Pokémon que contienen.");
                    break;
                case "egg":
                    builder.WithAuthor("Huevos", icon)
                           .WithDescription($"# Huevos\n\n" +
                                            $"Permite solicitar un **huevo** de un Pokémon específico usando su nombre o set de Showdown.\n\n" +
                                            $"## Comando:\n" +
                                            $"- **`{botPrefix}egg <pokemon>`**\n\n" +
                                            $"## Ejemplo:\n" +
                                            $"```{botPrefix}egg Charmander```\n" +
                                            $"Genera un huevo de Charmander.\n\n" +
                                            $"## Notas:\n" +
                                            $"- Puedes incluir un set de Showdown si deseas personalizar el huevo.\n" +
                                            $"- Algunos parámetros como `Shiny: Yes` se aplican al Pokémon que nacerá del huevo.\n\n" +
                                            $"⚠️ Ten en cuenta que los **huevos heredarán los datos del set** que indiques (naturaleza, shiny, IVs, etc).");
                    break;
                case "rt":
                    builder.WithAuthor("Equipo Random", icon)
                           .WithDescription($"# Equipo Aleatorio VGC\n\n" +
                                            $"Genera un **equipo VGC aleatorio** a partir de la hoja de cálculo **VGCPastes**.\n\n" +
                                            $"## Comandos:\n" +
                                            $"- **`{botPrefix}randomteam`**\n" +
                                            $"- **`{botPrefix}rt`**\n\n" +
                                            $"## Ejemplo:\n" +
                                            $"```{botPrefix}rt```\n" +
                                            $"Genera un equipo aleatorio de 6 Pokémon.\n\n" +
                                            $"## Descripción:\n" +
                                            $"El bot mostrará un embed con información detallada del equipo generado, incluyendo:\n" +
                                            $"- 📖 **Descripción del equipo** → Resumen de la estrategia.\n" +
                                            $"- 👤 **Nombre del entrenador** → Autor del equipo.\n" +
                                            $"- 📅 **Fecha compartida** → Cuándo se publicó.\n" +
                                            $"- 🔑 **Código de alquiler** → Mostrado solo si está disponible para usar en el juego.\n\n" +
                                            $"## Beneficios:\n" +
                                            $"- 🎲 Genera equipos únicos automáticamente.\n" +
                                            $"- ⚔️ Ideal para practicar con variedad de estrategias.\n" +
                                            $"- 🤝 Perfecto para sorprender en batallas rápidas.")
                           .WithImageUrl("https://i.imgur.com/jUBAz0a.png");
                    break;
                case "pp":
                    builder.WithAuthor("Equipo Completo a partir de PokePaste", icon)
                           .WithDescription($"# Equipo Completo desde PokePaste\n\n" +
                                            $"Con este comando puedes generar equipos Pokémon VGC completos directamente desde una URL de **PokePaste**.\n\n" +
                                            $"## Comando:\n" +
                                            $"- **`{botPrefix}pp <URL>`**\n" +
                                            $"- **`{botPrefix}pokepaste <URL>`**\n\n" +
                                            $"## Ejemplo:\n" +
                                            $"```{botPrefix}pp https://pokepast.es/xxxxxx```\n" +
                                            $"Genera el equipo completo de esa URL.\n\n" +
                                            $"## Descripción:\n" +
                                            $"Este comando agiliza el proceso de compartir y usar equipos, permitiendo importar un equipo entero de forma rápida.\n\n" +
                                            $"## Beneficios:\n" +
                                            $"- 📥 Importación rápida de equipos.\n" +
                                            $"- ⚔️ Compatible con equipos de VGC.\n" +
                                            $"- 🤝 Facilita compartir estrategias en la comunidad.\n" +
                                            $"- 🔗 Solo necesitas la URL de PokePaste.\n\n" +
                                            $"## Recursos:\n" +
                                            $"Si no tienes un link de equipo, aquí puedes encontrar varios ya preparados:\n" +
                                            $"[📑 Hoja de PokePaste](https://docs.google.com/spreadsheets/d/1axlwmzPA49rYkqXh7zHvAtSP-TKbM0ijGYBPRflLSWw/edit?gid=736919171#gid=736919171)");
                    break;
                case "srp":
                    builder.WithAuthor("Pedir Regalos Misteriosos", icon)
                           .WithDescription($"# Regalos Misteriosos\n\n" +
                                            $"Los **regalos misteriosos** te permiten solicitar eventos especiales de distintos juegos usando el comando `{botPrefix}srp`.\n\n" +
                                            $"## Cómo Funciona:\n" +
                                            $"- Usa `{botPrefix}srp <juego> <página>` para listar eventos.\n" +
                                            $"- Cada página muestra **25 eventos** con su índice.\n" +
                                            $"- Para pedir uno, escribe el código con el índice del evento.\n\n" +
                                            $"## Ejemplos:\n" +
                                            $"```{botPrefix}srp swsh```\n" +
                                            $"Muestra los eventos de Sword/Shield.\n\n" +
                                            $"```{botPrefix}srp gen9```\n" +
                                            $"Muestra los eventos de Escarlata/Violeta.\n\n" +
                                            $"```{botPrefix}srp gen9 page2```\n" +
                                            $"Muestra la **página 2** de eventos de Escarlata/Violeta.\n\n" +
                                            $"```{botPrefix}srp gen9 10```\n" +
                                            $"Solicita el evento número 10 de Escarlata/Violeta.\n\n" +
                                            $"## Juegos Disponibles:\n" +
                                            $"- `{botPrefix}srp gen9` → Escarlata/Violeta\n" +
                                            $"- `{botPrefix}srp bdsp` → Diamante Brillante/Perla Reluciente\n" +
                                            $"- `{botPrefix}srp swsh` → Espada/Escudo\n" +
                                            $"- `{botPrefix}srp pla` → Leyendas: Arceus\n" +
                                            $"- `{botPrefix}srp gen7` → Sol y Luna / Ultrasol y Ultraluna\n" +
                                            $"- `{botPrefix}srp gen6` → Pokémon X e Y\n" +
                                            $"- `{botPrefix}srp gen5` → Negro/Blanco / Negro2/Blanco2\n" +
                                            $"- `{botPrefix}srp gen4` → Diamante/Perla/Platino\n" +
                                            $"- `{botPrefix}srp gen3` → Rubí/Safiro/Esmeralda\n\n" +
                                            $"## Solicitudes Entre Juegos:\n" +
                                            $"Puedes pedir eventos de otro juego y el bot los **legalizará** para el juego que estés usando.\n\n" +
                                            $"Ejemplo: Pides `{botPrefix}srp swsh` para ver eventos de Sword/Shield, pero usas el código en un bot de Escarlata/Violeta y el evento será adaptado para ese juego.\n\n" +
                                            $"## Características:\n" +
                                            $"- 📖 Fácil de usar con comandos simples.\n" +
                                            $"- 🌐 Compatibilidad entre juegos.\n" +
                                            $"- 📥 Generación automática y legal de wondercards.\n" +
                                            $"- 🤖 No requiere configuración adicional para los dueños del bot.");
                    break;
                case "codigos":
                    builder.WithAuthor("Códigos de Intercambio", icon)
                           .WithDescription($"# Códigos de Intercambio\n\n" +
                                            $"Los **códigos de intercambio** te permiten guardar un código fijo (8 dígitos) para que el bot lo use siempre en tus trades.\n\n" +
                                            $"## Comandos Disponibles:\n" +
                                            $"- **`{botPrefix}atc <código>`** → Guarda tu código de intercambio.\n" +
                                            $"- **`{botPrefix}utc <código>`** → Actualiza tu código de intercambio.\n" +
                                            $"- **`{botPrefix}dtc`** → Elimina tu código de intercambio guardado.\n\n" +
                                            $"## Ejemplos:\n" +
                                            $"```{botPrefix}atc 12345678```\n" +
                                            $"Guarda **1234 5678** como tu código de intercambio permanente.\n\n" +
                                            $"```{botPrefix}utc 87654321```\n" +
                                            $"Actualiza tu código a **8765 4321**.\n\n" +
                                            $"```{botPrefix}dtc```\n" +
                                            $"Elimina tu código actual.\n\n" +
                                            $"⚠️ **Nota**: Los códigos siempre deben tener **8 dígitos**. El bot los mostrará en formato `XXXX YYYY` para más claridad.");
                    break;
                default:
                    builder.WithAuthor("Comando no encontrado", icon)
                          .WithDescription($"No se encontró información sobre el comando: `{command}`.");
                    break;
            }
        }
    }
}
