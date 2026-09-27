using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using UnityEngine;

namespace LucidCatsSaves
{
    internal class PlayerSave
    {
        public string steamId = "";
        public string name = "";
        public int credits;
        public List<int> items = new List<int>();
    }

    [Serializable]
    internal class SaveData
    {
        public int format = 1;
        public string id = "";

        public string name = "";

        public int night;

        public string gameVersion = "";
        public string modVersion = "";
        public string createdAt = "";
        public string lastSavedAt = "";

        public List<string> lastPlayers = new List<string>();
        public int lastTotalCredits;

        public List<string> playerSteamIds = new List<string>();
        public List<string> playerNames = new List<string>();
        public List<int> playerCredits = new List<int>();
        public List<string> playerItems = new List<string>();

        [NonSerialized]
        public List<PlayerSave> players = new List<PlayerSave>();

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

    internal static class SaveStore
    {
        public const int MaxSaves = 5;

        private static string Folder => Path.Combine(Paths.ConfigPath, "LucidCatsSaves");

        private static string PathFor(string id) => Path.Combine(Folder, id + ".json");

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

        public static string NextDefaultName(List<SaveData> saves)
        {
            for (int n = 1; ; n++)
            {
                string candidate = "Save " + n;
                if (!saves.Exists(s => string.Equals(s.name, candidate, StringComparison.OrdinalIgnoreCase)))
                    return candidate;
            }
        }

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
