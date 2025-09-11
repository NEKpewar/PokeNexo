using Discord;
using PKHeX.Core;
using SysBot.Pokemon.Helpers;
using System;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord;

public static class EmbedHelper
{
    public static async Task SendNotificationEmbedAsync(IUser user, string message)
    {
        var embed = new EmbedBuilder()
            .WithTitle("Aviso")
            .WithDescription(message)
            .WithTimestamp(DateTimeOffset.Now)
            .WithThumbnailUrl("https://raw.githubusercontent.com/hexbyt3/sprites/main/exclamation.gif")
            .WithColor(Color.Red)
            .Build();

        await user.SendMessageAsync(embed: embed).ConfigureAwait(false);
    }

    public static async Task SendTradeCanceledEmbedAsync(IUser user, string reason)
    {
        var embed = new EmbedBuilder()
            .WithTitle("Su trade fue cancelado...")
            .WithDescription($"Su trade ha sido cancelado.\nInténtelo de nuevo. Si el problema persiste, reinicie su consola y compruebe su conexión a Internet:\n\n**Razón**: {reason}")
            .WithTimestamp(DateTimeOffset.Now)
            .WithThumbnailUrl("https://raw.githubusercontent.com/hexbyt3/sprites/main/dmerror.gif")
            .WithColor(Color.Red)
            .Build();

        await user.SendMessageAsync(embed: embed).ConfigureAwait(false);
    }

    public static async Task SendTradeCodeEmbedAsync(IUser user, int code)
    {
        var embed = new EmbedBuilder()
            .WithTitle("¡Agregado a la Cola!")
            .WithDescription($"✅ Te he añadido a la __lista__! Te enviaré un __mensaje__ aquí cuando comience tu operación...\n\n¡Aquí está tu código comercial!\n# {code:0000 0000}")
            .WithTimestamp(DateTimeOffset.Now)
            .WithThumbnailUrl("https://raw.githubusercontent.com/hexbyt3/sprites/main/tradecode.gif")
            .WithColor(Color.Blue)
            .Build();

        await user.SendMessageAsync(embed: embed).ConfigureAwait(false);
    }

    public static async Task SendTradeFinishedEmbedAsync<T>(IUser user, string message, T pk, bool isMysteryTrade, bool isMysteryEgg, PokeTradeType type)
        where T : PKM, new()
    {
        string thumbnailUrl;

        if (isMysteryEgg)
        {
            // Huevo misterioso: se elige según el tipo
            thumbnailUrl = GetMysteryEggTypeImageUrl(pk);
        }
        else if (isMysteryTrade)
        {
            // Trade misterioso: imagen genérica
            thumbnailUrl = "https://i.imgur.com/FdESYAv.png";
        }
        else if (type == PokeTradeType.Item)
        {
            // Trade de ítem: mostrar sprite del objeto
            var itemName = GameInfo.Strings.Item[pk.HeldItem];
            // Scarlet/Violet tiene carpeta distinta en Serebii
            thumbnailUrl = $"https://serebii.net/itemdex/sprites/sv/{itemName.ToLower().Replace(" ", "").Replace("é", "e")}.png";
        }
        else
        {
            // Por defecto: sprite del Pokémon
            thumbnailUrl = TradeExtensions<T>.PokeImg(pk, false, true, null);
        }

        var embed = new EmbedBuilder()
            .WithTitle("Trade Completado!")
            .WithDescription(message)
            .WithTimestamp(DateTimeOffset.Now)
            .WithThumbnailUrl(thumbnailUrl)
            .WithColor(Color.Teal)
            .Build();

        await user.SendMessageAsync(embed: embed).ConfigureAwait(false);
    }

    public static async Task SendTradeInitializingEmbedAsync(IUser user, string speciesName, int code, bool isMysteryTrade, bool isMysteryEgg, string? message = null)
    {
        if (isMysteryEgg)
        {
            speciesName = "**Huevo Misterioso**";
        }
        else if (isMysteryTrade)
        {
            speciesName = "**Pokemon Misterioso**";
        }

        var embed = new EmbedBuilder()
            .WithTitle("Cargando el Pokeportal...")
            .WithDescription($"**Intercambio**: {speciesName}\n**Trade Code**: {code:0000 0000}")
            .WithTimestamp(DateTimeOffset.Now)
            .WithThumbnailUrl("https://raw.githubusercontent.com/hexbyt3/sprites/main/initializing.gif")
            .WithColor(Color.Orange);

        if (!string.IsNullOrEmpty(message))
        {
            embed.WithDescription($"{embed.Description}\n\n{message}");
        }

        var builtEmbed = embed.Build();
        await user.SendMessageAsync(embed: builtEmbed).ConfigureAwait(false);
    }

    public static async Task SendTradeSearchingEmbedAsync(IUser user, string trainerName, string inGameName, string? message = null)
    {
        var embed = new EmbedBuilder()
            .WithTitle($"Buscando entrenador...")
            .WithDescription($"**Esperando por**: {trainerName}\n**Mi IGN**: {inGameName}")
            .WithTimestamp(DateTimeOffset.Now)
            .WithThumbnailUrl("https://raw.githubusercontent.com/hexbyt3/sprites/main/searching.gif")
            .WithColor(Color.Green);

        if (!string.IsNullOrEmpty(message))
        {
            embed.WithDescription($"{embed.Description}\n\n{message}");
        }

        var builtEmbed = embed.Build();
        await user.SendMessageAsync(embed: builtEmbed).ConfigureAwait(false);
    }

    private static string GetMysteryEggTypeImageUrl<T>(T pk) where T : PKM, new()
    {
        var pi = pk.PersonalInfo;
        byte typeIndex = pi.Type1;

        string[] typeNames = {
        "Normal","Fighting","Flying","Poison","Ground","Rock","Bug","Ghost",
        "Steel","Fire","Water","Grass","Electric","Psychic","Ice","Dragon",
        "Dark","Fairy"
    };

        string typeName = (typeIndex >= 0 && typeIndex < typeNames.Length) ? typeNames[typeIndex] : "Normal";

        return $"https://raw.githubusercontent.com/Daiivr/SysBot-Images/refs/heads/main/MysteryEggs/MEgg_{typeName}.png";
    }

    private static string GetItemImageUrl(string heldItemName, bool isScarletViolet = true)
    {
        // Normalizamos el nombre del ítem
        string itemName = heldItemName.ToLower().Replace(" ", "");

        // En Pokémon Scarlet/Violet usan una carpeta distinta en Serebii
        if (isScarletViolet)
            return $"https://serebii.net/itemdex/sprites/sv/{itemName}.png";
        else
            return $"https://serebii.net/itemdex/sprites/{itemName}.png";
    }


}
