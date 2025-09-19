using Discord.WebSocket;
using System;
using System.Linq;

namespace SysBot.Pokemon.Discord.Helpers
{
    public static class AccessListExtensions
    {
        public static bool IsUserAllowed(this SysBot.Pokemon.RemoteControlAccessList list, SocketUser user)
        {
            if (list == null) return false;
            if (list.AllowIfEmpty) return true;

            if (user is SocketGuildUser gu)
            {
                // By Role ID
                foreach (var entry in list.List)
                    if (entry.ID != 0 && gu.Roles.Any(r => r.Id == entry.ID))
                        return true;

                // By Role Name
                foreach (var entry in list.List)
                    if (!string.IsNullOrWhiteSpace(entry.Name) &&
                        gu.Roles.Any(r => string.Equals(r.Name, entry.Name, StringComparison.OrdinalIgnoreCase)))
                        return true;
            }
            return false;
        }
    }
}
