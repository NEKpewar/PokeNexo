using System;
using System.Collections.Concurrent;

namespace SysBot.Pokemon.Discord.Helpers
{
    public static class BattleReadyPlusQuotaTracker
    {
        private static readonly ConcurrentDictionary<(ulong UserId, DateOnly Day), int> Counts = new();

        private static (ulong, DateOnly) Key(ulong userId) => (userId, DateOnly.FromDateTime(DateTime.UtcNow));

        public static int GetTodayCount(ulong userId) => Counts.TryGetValue(Key(userId), out var v) ? v : 0;

        public static int Increment(ulong userId) => Counts.AddOrUpdate(Key(userId), 1, (_, oldVal) => oldVal + 1);

        public static void CleanupOld()
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            foreach (var k in Counts.Keys)
                if (k.Day != today)
                    Counts.TryRemove(k, out _);
        }
    }
}
