using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Game;
using Game.Nights;
using Game.Player;
using Game.Shop;
using HaniUtils.Steam.Netcode;
using HarmonyLib;
using Steamworks;
using Steamworks.Data;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LucidCatsSaves
{
    internal static class SaveSession
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        public static SaveData PendingLoad;

        public static bool SkipSaving;

        public static SaveData Current;

        public static string GameVersion = "";

        private static MethodInfo forEachPlayerMethod;
        private static MethodInfo applyMethod;
        private static MethodInfo confirmPurchaseRpc;
        private static FieldInfo currentLobbyField;

        private static Dictionary<PlayerManager, PlayerSave> hallHandled = new Dictionary<PlayerManager, PlayerSave>();

        private static bool hallChangesToSave;

        private static bool restoring;
        private static float suppressSavesUntil;
        private static bool saveScheduled;

        public static void Install(Harmony harmony)
        {
            GameManager.OnGameStarted += OnGameStarted;
            harmony.PatchAll(typeof(SavePatches));
        }

        public static string VersionOrFallback()
        {
            return string.IsNullOrEmpty(GameVersion) ? Application.version : GameVersion;
        }

        public static void ResetForMainMenu()
        {
            PendingLoad = null;
            Current = null;
            SkipSaving = false;
            saveScheduled = false;
            hallHandled = new Dictionary<PlayerManager, PlayerSave>();
            hallChangesToSave = false;
        }

        public static void BeginLoad(SaveData save)
        {
            PendingLoad = save;
            SkipSaving = false;
            CoroutineRunner.Run(ApplyInHallRoutine(save));
        }

        private static IEnumerator ApplyInHallRoutine(SaveData save)
        {
            float giveUpAt = Time.unscaledTime + 120f;
            hallHandled = new Dictionary<PlayerManager, PlayerSave>();
            hallChangesToSave = false;
            var handled = hallHandled;
            var firstSeen = new Dictionary<PlayerManager, float>();
            var usedEntries = new HashSet<PlayerSave>();
            bool announced = false;

            while (PendingLoad == save)
            {
                NightManager nights = Object.FindFirstObjectByType<NightManager>();
                GameManager game = Object.FindFirstObjectByType<GameManager>();

                if (nights != null && game != null && nights.IsServer)
                {
                    if (game.CurrentGameState.Value != GameState.WaitingToStart)
                        yield break;

                    if (!announced)
                    {
                        announced = true;
                        SavesPlugin.Log.LogInfo($"Loaded game ready in the hall: {save.night} night(s) survived, {save.players.Count} player record(s).");
                    }

                    try
                    {
                        ApplyInHall(nights, save, handled, firstSeen, usedEntries);
                    }
                    catch (Exception e)
                    {
                        SavesPlugin.Log.LogError($"Could not apply the save in the hall: {e}");
                    }
                }
                else if (Time.unscaledTime > giveUpAt)
                {
                    yield break;
                }

                yield return new WaitForSecondsRealtime(0.5f);
            }
        }

        private static void ApplyInHall(NightManager nights, SaveData save,
            Dictionary<PlayerManager, PlayerSave> handled, Dictionary<PlayerManager, float> firstSeen,
            HashSet<PlayerSave> usedEntries)
        {
            if (nights.CurrentNightNumber.Value != save.night)
                nights.CurrentNightNumber.Value = save.night;

            var gone = new List<PlayerManager>();
            foreach (KeyValuePair<PlayerManager, PlayerSave> pair in handled)
                if (pair.Key == null)
                    gone.Add(pair.Key);
            foreach (PlayerManager player in gone)
            {
                if (handled[player] != null)
                    usedEntries.Remove(handled[player]);
                handled.Remove(player);
                firstSeen.Remove(player);
            }

            foreach (PlayerManager player in GetPlayers(nights))
            {
                if (player == null || handled.ContainsKey(player))
                    continue;

                if (!firstSeen.ContainsKey(player))
                    firstSeen[player] = Time.unscaledTime;

                if (!IsNameReady(player) && Time.unscaledTime - firstSeen[player] < 10f)
                    continue;

                PlayerSave entry = FindPlayer(save, GetSteamId(player), player.DisplayName, usedEntries);
                if (entry != null)
                {
                    usedEntries.Add(entry);
                    RestorePlayer(player, entry);
                    SavesPlugin.Log.LogInfo($"Restored {player.DisplayName} in the hall: {entry.credits} cr, {entry.items.Count} upgrade(s).");
                }
                else
                {
                    SavesPlugin.Log.LogInfo($"{player.DisplayName} wasn't in this save, so they start fresh.");
                }

                handled[player] = entry;
            }
        }

        public static void CaptureHallState(bool log = true)
        {
            SaveData save = PendingLoad;
            if (save == null)
                return;

            NightManager nights = Object.FindFirstObjectByType<NightManager>();
            if (nights == null || !nights.IsServer)
                return;

            try
            {
                foreach (PlayerManager player in GetPlayers(nights))
                {
                    if (player == null || !hallHandled.TryGetValue(player, out PlayerSave entry))
                        continue;

                    int credits = player.Valuables.Credits.Value;
                    List<int> items = GetPurchasedItems(player);

                    if (entry == null)
                    {
                        if (credits == 0 && items.Count == 0)
                            continue;
                        entry = new PlayerSave();
                        save.players.Add(entry);
                        hallHandled[player] = entry;
                        hallChangesToSave = true;
                    }
                    else if (entry.credits != credits || !SameItems(entry.items, items))
                    {
                        hallChangesToSave = true;
                    }

                    string steamId = GetSteamId(player);
                    if (!string.IsNullOrEmpty(steamId))
                        entry.steamId = steamId;
                    entry.name = player.DisplayName;
                    entry.credits = credits;
                    entry.items = items;
                }

                if (hallChangesToSave && log)
                    SavesPlugin.Log.LogInfo("Keeping what was done in the hall (purchases, money) for the run.");
            }
            catch (Exception e)
            {
                SavesPlugin.Log.LogError($"Could not capture the hall state: {e}");
            }
        }

        private static void SaveLoadedGameInHall()
        {
            SaveData save = PendingLoad;
            if (save == null || SkipSaving || !SaveStore.Exists(save.id))
                return;

            NightManager nights = Object.FindFirstObjectByType<NightManager>();
            if (nights == null || !nights.IsServer)
                return;

            try
            {
                CaptureHallState(log: false);

                var present = new List<string>();
                int total = 0;
                foreach (KeyValuePair<PlayerManager, PlayerSave> pair in hallHandled)
                {
                    if (pair.Key == null || pair.Value == null)
                        continue;
                    present.Add(pair.Value.name);
                    total += pair.Value.credits;
                }
                if (present.Count > 0)
                {
                    save.lastPlayers = present;
                    save.lastTotalCredits = total;
                }

                save.gameVersion = VersionOrFallback();
                save.modVersion = SavesPlugin.PluginVersion;
                save.lastSavedAt = SaveData.Now();

                SaveStore.Write(save);
                hallChangesToSave = false;
                SaveIndicator.Show();
                SavesPlugin.Log.LogInfo($"Game saved (purchase in the hall, before starting): {present.Count} player(s), {total} cr.");
            }
            catch (Exception e)
            {
                SavesPlugin.Log.LogError($"Could not save the game in the hall: {e}");
            }
        }

        private static bool SameItems(List<int> a, List<int> b)
        {
            if (a == null || b == null)
                return (a == null || a.Count == 0) && (b == null || b.Count == 0);
            if (a.Count != b.Count)
                return false;
            var sa = new List<int>(a);
            var sb = new List<int>(b);
            sa.Sort();
            sb.Sort();
            for (int i = 0; i < sa.Count; i++)
                if (sa[i] != sb[i])
                    return false;
            return true;
        }

        private static void OnGameStarted()
        {
            try
            {
                NightManager nights = Object.FindFirstObjectByType<NightManager>();
                if (nights == null || !nights.IsServer)
                    return;

                if (PendingLoad != null)
                {
                    Current = PendingLoad;
                    PendingLoad = null;

                    RestoreAll(nights, Current, restoreItems: false, log: false);
                    CoroutineRunner.Run(RestoreAtStart(Current));
                }
                else if (SkipSaving)
                {
                    Current = null;
                    SavesPlugin.Log.LogInfo("This run won't be saved (you chose to host without a free save slot).");
                }
                else if (SaveStore.LoadAll().Count >= SaveStore.MaxSaves)
                {
                    Current = null;
                    SavesPlugin.Log.LogWarning($"You already have {SaveStore.MaxSaves} saves, so this run won't be saved.");
                }
                else
                {
                    Current = new SaveData
                    {
                        id = Guid.NewGuid().ToString("N"),
                        createdAt = SaveData.Now(),
                    };
                    SavesPlugin.Log.LogInfo("New run started. It will be saved after every night you survive.");
                }
            }
            catch (Exception e)
            {
                SavesPlugin.Log.LogError($"Error when the game started: {e}");
            }
        }

        private static IEnumerator RestoreAtStart(SaveData save)
        {
            yield return null;
            yield return null;

            NightManager nights = Object.FindFirstObjectByType<NightManager>();
            if (nights == null || !nights.IsServer)
                yield break;

            try
            {
                int restored = RestoreAll(nights, save, restoreItems: true, log: true);
                SavesPlugin.Log.LogInfo($"Save loaded. Next night: {save.night + 1}. Players restored: {restored}.");

                if (hallChangesToSave)
                {
                    hallChangesToSave = false;
                    SaveNow(nights, "hall purchases kept");
                }
                hallHandled = new Dictionary<PlayerManager, PlayerSave>();
            }
            catch (Exception e)
            {
                SavesPlugin.Log.LogError($"Could not restore the save: {e}");
            }
        }

        private static int RestoreAll(NightManager nights, SaveData save, bool restoreItems, bool log)
        {
            nights.CurrentNightNumber.Value = save.night;

            var used = new HashSet<PlayerSave>();
            int restored = 0;
            foreach (PlayerManager player in GetPlayers(nights))
            {
                PlayerSave entry = FindPlayer(save, GetSteamId(player), player.DisplayName, used);
                if (entry != null)
                {
                    used.Add(entry);
                    if (restoreItems)
                    {
                        int items = RestorePlayer(player, entry);
                        if (log)
                            SavesPlugin.Log.LogInfo($"Restored {player.DisplayName}: {entry.credits} cr, {items} upgrade(s).");
                    }
                    else
                    {
                        player.Valuables.Credits.Value = entry.credits;
                    }
                    restored++;
                }
                else if (log)
                {
                    SavesPlugin.Log.LogInfo($"{player.DisplayName} wasn't in this save, so they start fresh.");
                }
            }
            return restored;
        }

        private static int RestorePlayer(PlayerManager player, PlayerSave entry)
        {
            restoring = true;
            suppressSavesUntil = Time.unscaledTime + 3f;
            try
            {
                player.Valuables.Credits.Value = entry.credits;
                return RestoreItems(player, entry.items);
            }
            finally
            {
                restoring = false;
            }
        }

        private static int RestoreItems(PlayerManager player, List<int> itemIds)
        {
            PlayerUpgrades upgrades = player.Upgrades;
            if (upgrades == null || itemIds == null)
                return 0;

            if (applyMethod == null)
                applyMethod = typeof(PlayerUpgrades).GetMethod("Apply", Private, null, new[] { typeof(ShopItemData) }, null);
            if (confirmPurchaseRpc == null)
                confirmPurchaseRpc = typeof(PlayerUpgrades).GetMethod("ConfirmPurchaseOwnerRpc", Private, null, new[] { typeof(int) }, null);
            if (applyMethod == null || confirmPurchaseRpc == null)
            {
                SavesPlugin.Log.LogError("Could not find the game's upgrade methods; upgrades were not restored.");
                return 0;
            }

            int count = 0;
            foreach (int id in itemIds)
            {
                ShopItemData item = FindItem(upgrades, id);
                if (item == null || item.unlockedByDefault || upgrades.IsOwned(item))
                    continue;

                applyMethod.Invoke(upgrades, new object[] { item });
                confirmPurchaseRpc.Invoke(upgrades, new object[] { id });
                count++;
            }
            return count;
        }

        public static void SaveNow(NightManager nights, string reason)
        {
            if (Current == null || SkipSaving || nights == null || !nights.IsServer)
                return;

            try
            {
                var used = new HashSet<PlayerSave>();
                var present = new List<string>();
                int total = 0;

                foreach (PlayerManager player in GetPlayers(nights))
                {
                    string steamId = GetSteamId(player);
                    string name = player.DisplayName;

                    PlayerSave entry = FindPlayer(Current, steamId, name, used);
                    if (entry == null)
                    {
                        entry = new PlayerSave();
                        Current.players.Add(entry);
                    }
                    used.Add(entry);

                    if (!string.IsNullOrEmpty(steamId))
                        entry.steamId = steamId;
                    entry.name = name;
                    entry.credits = player.Valuables.Credits.Value;
                    entry.items = GetPurchasedItems(player);

                    present.Add(name);
                    total += entry.credits;
                }

                Current.night = nights.CurrentNightNumber.Value;
                Current.gameVersion = VersionOrFallback();
                Current.modVersion = SavesPlugin.PluginVersion;
                Current.lastSavedAt = SaveData.Now();
                if (string.IsNullOrEmpty(Current.createdAt))
                    Current.createdAt = Current.lastSavedAt;
                Current.lastPlayers = present;
                Current.lastTotalCredits = total;

                SaveStore.Write(Current);
                SaveIndicator.Show();
                SavesPlugin.Log.LogInfo($"Game saved ({reason}): {Current.night} night(s) survived, {present.Count} player(s), {total} cr.");
                foreach (PlayerSave p in Current.players)
                    SavesPlugin.Log.LogInfo($"  - {p.name}: {p.credits} cr, {p.items.Count} upgrade(s)");
            }
            catch (Exception e)
            {
                SavesPlugin.Log.LogError($"Could not save the game: {e}");
            }
        }
        public static void OnUpgradeApplied(PlayerUpgrades upgrades)
        {
            if (restoring || Time.unscaledTime < suppressSavesUntil || saveScheduled)
                return;
            if (upgrades == null || !upgrades.IsServer || SkipSaving)
                return;

            NightManager nights = Object.FindFirstObjectByType<NightManager>();
            if (nights == null || nights.CurrentNightState.Value != NightState.WaitingToSleep)
                return;

            bool duringRun = Current != null && SaveStore.Exists(Current.id);
            bool inLoadedHall = Current == null && PendingLoad != null && SaveStore.Exists(PendingLoad.id);
            if (!duringRun && !inLoadedHall)
                return;

            saveScheduled = true;
            CoroutineRunner.Run(SaveShortly());
        }

        private static IEnumerator SaveShortly()
        {
            yield return new WaitForSecondsRealtime(0.5f);
            saveScheduled = false;

            if (Current != null)
                SaveNow(Object.FindFirstObjectByType<NightManager>(), "after a purchase");
            else if (PendingLoad != null)
                SaveLoadedGameInHall();
        }

        public static void OnRunLost(NightManager nights)
        {
            if (nights == null || !nights.IsServer || Current == null)
                return;

            if (SaveStore.Exists(Current.id))
            {
                SaveStore.Delete(Current.id);
                SavesPlugin.Log.LogInfo("The run was lost, so its save was deleted.");
            }
            Current = null;
        }

        private static List<PlayerManager> GetPlayers(NightManager nights)
        {
            var players = new List<PlayerManager>();

            if (forEachPlayerMethod == null)
                forEachPlayerMethod = typeof(NightManager).GetMethod("ForEachPlayer", Private);

            if (forEachPlayerMethod != null)
                forEachPlayerMethod.Invoke(nights, new object[] { new Action<PlayerManager>(players.Add) });
            else
                players.AddRange(Object.FindObjectsByType<PlayerManager>(FindObjectsSortMode.None));

            return players;
        }

        private static bool IsNameReady(PlayerManager player)
        {
            return player.IsOwner || player.DisplayName != $"Player {player.OwnerClientId}";
        }

        private static List<int> GetPurchasedItems(PlayerManager player)
        {
            var ids = new List<int>();
            PlayerUpgrades upgrades = player.Upgrades;
            if (upgrades == null)
                return ids;

            foreach (ShopItemData item in upgrades.CatalogItems)
                if (item != null && !item.unlockedByDefault && upgrades.IsOwned(item))
                    ids.Add(item.id);
            return ids;
        }

        private static ShopItemData FindItem(PlayerUpgrades upgrades, int id)
        {
            foreach (ShopItemData item in upgrades.CatalogItems)
                if (item != null && item.id == id)
                    return item;
            return null;
        }

        private static PlayerSave FindPlayer(SaveData save, string steamId, string name, HashSet<PlayerSave> used)
        {
            if (!string.IsNullOrEmpty(steamId))
                foreach (PlayerSave p in save.players)
                    if (!used.Contains(p) && p.steamId == steamId)
                        return p;

            foreach (PlayerSave p in save.players)
                if (!used.Contains(p) && string.Equals(p.name, name, StringComparison.OrdinalIgnoreCase))
                    return p;

            return null;
        }
        private static string GetSteamId(PlayerManager player)
        {
            try
            {
                if (!SteamClient.IsValid)
                    return string.Empty;

                if (player.IsOwner)
                    return SteamClient.SteamId.Value.ToString();

                Lobby? lobby = GetLobby();
                if (lobby.HasValue)
                    foreach (Friend member in lobby.Value.Members)
                        if (string.Equals(member.Name, player.DisplayName, StringComparison.Ordinal))
                            return member.Id.Value.ToString();
            }
            catch (Exception e)
            {
                SavesPlugin.Log.LogDebug($"Could not read the Steam account of {player.DisplayName}: {e.Message}");
            }
            return string.Empty;
        }

        private static Lobby? GetLobby()
        {
            SteamLobbyManager manager = Object.FindFirstObjectByType<SteamLobbyManager>();
            if (manager == null)
                return null;

            if (currentLobbyField == null)
                currentLobbyField = typeof(SteamLobbyManager).GetField("currentLobby", Private);

            return currentLobbyField?.GetValue(manager) as Lobby?;
        }
    }

    [HarmonyPatch]
    internal static class SavePatches
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(NightManager), "ResetForNextRun")]
        private static void AfterNightWon(NightManager __instance)
        {
            SaveSession.SaveNow(__instance, "night survived");
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(NightManager), "EndRunAndEvaluate")]
        private static void AfterNightEvaluated(NightManager __instance, bool __result)
        {
            if (!__result)
                SaveSession.OnRunLost(__instance);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(GameManager), "InternalStartGame")]
        private static void BeforeRunStarts(GameManager __instance)
        {
            if (__instance != null && __instance.IsServer)
                SaveSession.CaptureHallState();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlayerUpgrades), "Apply")]
        private static void AfterUpgradeApplied(PlayerUpgrades __instance)
        {
            SaveSession.OnUpgradeApplied(__instance);
        }
    }
}
