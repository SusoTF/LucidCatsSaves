using System.Collections;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LucidCatsSaves
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class SavesPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "lucidcats.savefiles";
        public const string PluginName = "Save Files";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> ShowSaveIndicator;

        private void Awake()
        {
            Log = Logger;

            ShowSaveIndicator = Config.Bind(
                "Interface", "ShowSaveIndicator", true,
                "Show a small \"Saving... / Game saved\" message in the bottom-right corner when the game is saved.");

            CoroutineRunner.Create();
            SaveIndicator.Create();
            var harmony = new Harmony(PluginGuid);
            SaveSession.Install(harmony);
            MenuWatcher.Install(harmony);
            SceneManager.sceneLoaded += OnSceneLoaded;

            Log.LogInfo($"{PluginName} {PluginVersion} loaded. Saved games: {SaveStore.LoadAll().Count}/{SaveStore.MaxSaves}");
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "MainMenu")
            {
                SaveSession.ResetForMainMenu();
                LoadGameMenu.Create(scene);
            }
        }
    }

    internal class CoroutineRunner : MonoBehaviour
    {
        private static CoroutineRunner instance;

        public static void Create()
        {
            if (instance != null)
                return;
            var go = new GameObject("Save Files Runner (mod)");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            instance = go.AddComponent<CoroutineRunner>();
            go.AddComponent<MoneyDisplay>();
        }

        public static void Run(IEnumerator routine)
        {
            if (instance == null)
                Create();
            instance.StartCoroutine(routine);
        }
    }
}
