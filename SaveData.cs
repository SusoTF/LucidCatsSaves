using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using UnityEngine;

namespace LucidCatsSaves
{
    /// <summary>What we remember about one player in a save (kept in memory; see SaveData.Pack).</summary>
    internal class PlayerSave
    {
        public string steamId = "";
        public string name = "";
        public int credits;
        public List<int> items = new List<int>();
    }

    /// <summary>One saved game. Stored as a small text (JSON) file.</summary>
    [Serializable]
    internal class SaveData
    {
        public int format = 1;
        public string id = "";

        /// <summary>Name shown in Load Game ("Save 1" by default; can be renamed).</summary>
        public string name = "";

        /// <summary>Nights survived. The next night to play is night + 1.</summary>
        public int night;

        public string gameVersion = "";
        public string modVersion = "";
        public string createdAt = "";
        public string lastSavedAt = "";

        public List<string> lastPlayers = new List<string>();
        public int lastTotalCredits;

        // Unity can't reliably write custom data types that live inside a mod, so each player's
        // data is written as simple parallel lists (one entry per player, same order in every list).
        public List<string> playerSteamIds = new List<string>();
        public List<string> playerNames = new List<string>();
        public List<int> playerCredits = new List<int>();
        public List<string> playerItems = new List<string>();

        /// <summary>The players, rebuilt from the lists above when the file is read.</summary>
        [NonSerialized]
        public List<PlayerSave> players = new List<PlayerSave>();

        /// <summary>Copies the players into the simple lists that get written to the file.</summary>
        public void Pack()
        {
            playerSteamIds = new List<string>();
            playerNames = new List<string>();
            playerCredits = new List<int>();
            playerItems = new List<string>();

            foreach (PlayerSave p in players ?? new List<PlayerSave>())
            {
                playerSteamIds.Add(p.steamId ?? string.Empty);
                playerNames.Add(p.name ?? string.Empty);
                playerCredits.Add(p.credits);
                playerItems.Add(p.items != null ? string.Join(",", p.items) : string.Empty);
            }
        }

        /// <summary>Rebuilds the players from the simple lists read from the file.</summary>
        public void Unpack()
        {
            players = new List<PlayerSave>();
            int count = playerNames?.Count ?? 0;

            for (int i = 0; i < count; i++)
            {
                var p = new PlayerSave
                {
                    steamId = playerSteamIds != null && i < playerSteamIds.Count ? playerSteamIds[i] : string.Empty,
                    name = playerNames[i] ?? string.Empty,
                    credits = playerCredits != null && i < playerCredits.Count ? playerCredits[i] : 0,
                };

                string items = playerItems != null && i < playerItems.Count ? playerItems[i] : string.Empty;
                if (!string.IsNullOrEmpty(items))
                    foreach (string part in items.Split(','))
                        if (int.TryParse(part.Trim(), out int id))
                            p.items.Add(id);

                players.Add(p);
            }

            if (lastPlayers == null)
                lastPlayers = new List<string>();
        }

        public DateTime Created => ParseDate(createdAt);
        public DateTime LastSaved => ParseDate(lastSavedAt);

        public static string Now() => DateTime.Now.ToString("o", CultureInfo.InvariantCulture);

        private static DateTime ParseDate(string value)
        {
            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime date)
                ? date
                : DateTime.MinValue;
        }
    }

    /// <summary>Reads and writes save files in BepInEx\config\LucidCatsSaves.</summary>
    internal static class SaveStore
    {
        public const int MaxSaves = 5;

        private static string Folder => Path.Combine(Paths.ConfigPath, "LucidCatsSaves");

        private static string PathFor(string id) => Path.Combine(Folder, id + ".json");

        /// <summary>All valid saves, most recently saved first.</summary>
        public static List<SaveData> LoadAll()
        {
            var saves = new List<SaveData>();
            if (!Directory.Exists(Folder))
                return saves;

            foreach (string file in Directory.GetFiles(Folder, "*.json"))
            {
                try
                {
                    SaveData save = JsonUtility.FromJson<SaveData>(File.ReadAllText(file));
                    if (save == null)
                        continue;
                    save.id = Path.GetFileNameWithoutExtension(file);
                    save.Unpack();
                    saves.Add(save);
                }
                catch (Exception e)
                {
                    SavesPlugin.Log.LogWarning($"Skipping unreadable save '{Path.GetFileName(file)}': {e.Message}");
                }
            }

            AssignMissingNames(saves);
            saves.Sort((a, b) => b.LastSaved.CompareTo(a.LastSaved));
            return saves;
        }

        public const int MaxNameLength = 24;

        /// <summary>First free "Save N" name among the given saves.</summary>
        public static string NextDefaultName(List<SaveData> saves)
        {
            for (int n = 1; ; n++)
            {
                string candidate = "Save " + n;
                if (!saves.Exists(s => string.Equals(s.name, candidate, StringComparison.OrdinalIgnoreCase)))
                    return candidate;
            }
        }

        /// <summary>Removes characters that would break the text and keeps the name short.</summary>
        public static string CleanName(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return string.Empty;
            // Text formatting tags (like <b>) would change how the name looks, so they're removed.
            string cleaned = System.Text.RegularExpressions.Regex.Replace(raw, "<[^>]*>", string.Empty);
            cleaned = cleaned.Replace("<", string.Empty).Replace(">", string.Empty).Trim();
            if (cleaned.Length > MaxNameLength)
                cleaned = cleaned.Substring(0, MaxNameLength).Trim();
            return cleaned;
        }

        public static void Rename(string id, string newName)
        {
            SaveData save = LoadAll().Find(s => s.id == id);
            if (save == null)
                return;
            save.name = newName;
            WriteFile(save);
        }

        /// <summary>Saves made before names existed get "Save N" (oldest first), once.</summary>
        private static void AssignMissingNames(List<SaveData> saves)
        {
            var unnamed = saves.FindAll(s => string.IsNullOrWhiteSpace(s.name));
            if (unnamed.Count == 0)
                return;

            unnamed.Sort((a, b) => a.Created.CompareTo(b.Created));
            foreach (SaveData save in unnamed)
            {
                save.name = NextDefaultName(saves);
                try
                {
                    WriteFile(save);
                }
                catch (Exception e)
                {
                    SavesPlugin.Log.LogWarning($"Could not name an older save: {e.Message}");
                }
            }
        }

        public static bool Exists(string id) => !string.IsNullOrEmpty(id) && File.Exists(PathFor(id));

        public static void Write(SaveData save)
        {
            if (string.IsNullOrWhiteSpace(save.name))
            {
                List<SaveData> others = LoadAll();
                others.RemoveAll(s => s.id == save.id);
                save.name = NextDefaultName(others);
            }
            WriteFile(save);
        }

        private static void WriteFile(SaveData save)
        {
            Directory.CreateDirectory(Folder);
            string path = PathFor(save.id);
            string temp = path + ".tmp";

            save.Pack();

            // Write to a temporary file first, so a crash mid-write never corrupts an existing save.
            File.WriteAllText(temp, JsonUtility.ToJson(save, true));
            if (File.Exists(path))
                File.Delete(path);
            File.Move(temp, path);
        }

        public static void Delete(string id)
        {
            if (Exists(id))
                File.Delete(PathFor(id));
        }
    }
}
