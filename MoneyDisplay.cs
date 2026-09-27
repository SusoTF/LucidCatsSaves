using System;
using System.Reflection;
using Game.Nights;
using Game.Player;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LucidCatsSaves
{
    /// <summary>
    /// Shows the game's money display in the hall when you have money but it isn't on screen
    /// (for example, in a loaded game, where no one has "woken up" after a night yet).
    /// It opens the display directly, without the game's wake-up animation. Runs on every PC with the mod.
    /// </summary>
    internal class MoneyDisplay : MonoBehaviour
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static Type creditsHudType;
        private static MethodInfo openHudMethod;
        private static PropertyInfo menuProperty;
        private static bool lookedUp;

        private float nextCheck;
        private bool openedThisHallVisit;

        private void Update()
        {
            if (Time.unscaledTime < nextCheck)
                return;
            nextCheck = Time.unscaledTime + 0.5f;

            try
            {
                Check();
            }
            catch (Exception e)
            {
                SavesPlugin.Log.LogDebug($"Money display check failed: {e.Message}");
            }
        }

        private void Check()
        {
            NightManager nights = Object.FindFirstObjectByType<NightManager>();
            if (nights == null || nights.CurrentNightState.Value != NightState.WaitingToSleep)
            {
                // Left the hall (asleep, dreaming, back in the menu...): allow it again next time.
                openedThisHallVisit = false;
                return;
            }

            if (openedThisHallVisit)
                return;

            PlayerManager local = FindLocalPlayer();
            if (local == null || local.Valuables == null || local.Valuables.Credits.Value <= 0)
                return;

            LookUp();
            if (creditsHudType == null || openHudMethod == null)
                return;

            Object hud = Object.FindFirstObjectByType(creditsHudType, FindObjectsInactive.Include);
            if (hud == null)
                return;

            if (!IsVisible((Component)hud))
                openHudMethod.Invoke(hud, null);

            openedThisHallVisit = true;
        }

        private static PlayerManager FindLocalPlayer()
        {
            foreach (PlayerManager player in Object.FindObjectsByType<PlayerManager>(FindObjectsSortMode.None))
                if (player != null && player.IsOwner)
                    return player;
            return null;
        }

        private static void LookUp()
        {
            if (lookedUp)
                return;
            lookedUp = true;

            creditsHudType = AccessTools.TypeByName("Game.UI.Huds.CreditsMenu");
            for (Type t = creditsHudType; t != null && openHudMethod == null; t = t.BaseType)
            {
                openHudMethod = t.GetMethod("OpenHud", Any | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                menuProperty = t.GetProperty("Menu", Any | BindingFlags.DeclaredOnly);
            }

            if (creditsHudType == null || openHudMethod == null)
                SavesPlugin.Log.LogWarning("Could not find the game's money display; it won't be opened for loaded games.");
        }

        /// <summary>Whether the money display is already on screen.</summary>
        private static bool IsVisible(Component hud)
        {
            Component menu = menuProperty?.GetValue(hud) as Component;
            CanvasGroup group = null;
            if (menu != null)
                group = menu.GetComponent<CanvasGroup>();
            if (group == null)
                group = hud.GetComponent<CanvasGroup>();
            if (group == null)
                group = hud.GetComponentInChildren<CanvasGroup>(true);

            return group != null && group.gameObject.activeInHierarchy && group.alpha > 0.01f;
        }
    }
}
