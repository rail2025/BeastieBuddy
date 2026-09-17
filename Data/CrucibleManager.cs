using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BeastieBuddy.Windows;

namespace BeastieBuddy.Data
{
    public class CrucibleManager : IDisposable
    {
        private readonly ServerClient serverClient;

        // Static baseline fallback: raw potency order with zero buffs/debuffs
        private static readonly string[] BaselineRankings = new[]
        {
            "50", "10", "32", "48", "14", "24", "20", "34", "11", "19",
            "21", "41", "29", "5",  "12", "28", "27", "1",  "40", "33",
            "2",  "6",  "49", "46", "13", "22", "8",  "16", "23", "36",
            "43", "39", "17", "15", "26", "44", "37", "18", "31", "42",
            "45", "35", "9",  "30", "38", "47", "3",  "4",  "7",  "25"
        };

        private readonly object syncLock = new();
        private readonly Dictionary<string, List<string>> cache = new();

        public readonly string?[] Loadout = new string?[3];

        private string[] activeRankings = BaselineRankings;
        public IReadOnlyList<string> CurrentRankings
        {
            get
            {
                lock (syncLock)
                {
                    return activeRankings;
                }
            }
        }

        public bool IsOptimized { get; private set; } = false;
        public bool IsQuerying { get; private set; } = false;

        private CancellationTokenSource? queryCts;

        public CrucibleManager(ServerClient serverClient)
        {
            this.serverClient = serverClient ?? throw new ArgumentNullException(nameof(serverClient));
        }

        public void SetSlot(int slotIndex, string? beastId)
        {
            if (slotIndex >= 0 && slotIndex < Loadout.Length)
            {
                Loadout[slotIndex] = beastId;
            }
        }

        public void RefreshRankings(int slotIndex, string weakness, float weaknessMultiplier, int boardBase, List<string> vulnerabilities)
        {
            var oldCts = queryCts;
            queryCts = new CancellationTokenSource();
            oldCts?.Cancel();

            var token = queryCts.Token;

            var preceding = new List<string>();
            for (int i = 0; i < slotIndex && i < Loadout.Length; i++)
            {
                if (!string.IsNullOrEmpty(Loadout[i]))
                    preceding.Add(Loadout[i]!);
            }

            var requestPayload = new
            {
                slotIndex,
                weakness = weakness ?? "None",
                weaknessMultiplier,
                boardBase,
                vulnerabilities = vulnerabilities ?? new List<string>(),
                activeSlotBeastIds = preceding
            };

            string cacheKey = JsonConvert.SerializeObject(requestPayload);

            lock (syncLock)
            {
                if (cache.TryGetValue(cacheKey, out var cachedList) && cachedList != null)
                {
                    activeRankings = cachedList.ToArray();
                    IsOptimized = true;
                    return;
                }
            }

            IsQuerying = true;
            Task.Run(async () =>
            {
                List<string>? results = null;
                try
                {
                    results = await serverClient.GetCrucibleRankingsAsync(cacheKey, token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Plugin.Log.Error(ex, "[Crucible] Error in RefreshRankings task.");
                }

                if (token.IsCancellationRequested) return;

                lock (syncLock)
                {
                    if (results != null && results.Count > 0)
                    {
                        Plugin.Log.Info($"[Crucible] Received {results.Count} ranked beasts from API.");
                        cache[cacheKey] = results;
                        activeRankings = results.ToArray();
                        IsOptimized = true;
                    }
                    else
                    {
                        Plugin.Log.Warning("[Crucible] Ranking call returned null or empty. Retaining baseline.");
                        activeRankings = BaselineRankings;
                        IsOptimized = false;
                    }
                    IsQuerying = false;
                }
            }, token);
        }

        public void Dispose()
        {
            queryCts?.Cancel();
            queryCts?.Dispose();
        }
    }
}
