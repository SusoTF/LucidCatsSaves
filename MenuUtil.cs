using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

namespace LucidCatsSaves
{
    /// <summary>Small helpers to work with the game's main menu.</summary>
    internal static class MenuUtil
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static Transform Require(Transform parent, string path)
        {
            Transform found = parent.Find(path);
            if (found == null)
                throw new Exception($"Could not find '{path}' under '{parent.name}'.");
            return found;
        }

        public static TMP_Text CloneText(Transform template, Transform parent, string newName)
        {
            GameObject go = Object.Instantiate(template.gameObject, parent, false);
            go.name = newName;
            go.SetActive(true);
            return go.GetComponent<TMP_Text>();
        }

        public static RectTransform NewRect(string rectName, Transform parent)
        {
            var go = new GameObject(rectName, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static void Stretch(RectTransform rect, float minX, float minY, float maxX, float maxY)
        {
            rect.anchorMin = new Vector2(minX, minY);
            rect.anchorMax = new Vector2(maxX, maxY);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>Finds the game's menu panel component (HaniUtils.UI.Menu or a subclass).</summary>
        public static Component FindMenuComponent(GameObject go)
        {
            foreach (MonoBehaviour mb in go.GetComponents<MonoBehaviour>())
            {
                if (mb == null)
                    continue;
                for (Type t = mb.GetType(); t != null; t = t.BaseType)
                    if (t.FullName == "HaniUtils.UI.Menu")
                        return mb;
            }
            return null;
        }

        /// <summary>Finds the "onClick" event of the game's button script.</summary>
        public static UnityEvent FindClickEvent(GameObject go)
        {
            foreach (MonoBehaviour mb in go.GetComponents<MonoBehaviour>())
            {
                if (mb == null)
                    continue;
                for (Type t = mb.GetType(); t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                {
                    FieldInfo field = t.GetField("onClick", Any | BindingFlags.DeclaredOnly);
                    if (field != null && field.GetValue(mb) is UnityEvent unityEvent)
                        return unityEvent;
                }
            }
            return null;
        }

        /// <summary>Calls Open / Close / Toggle on a game menu panel.</summary>
        public static void CallMenu(Component menu, string methodName)
        {
            if (menu == null)
                return;

            try
            {
                for (Type t = menu.GetType(); t != null; t = t.BaseType)
                {
                    MethodInfo method = t.GetMethod(methodName, Any | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                    if (method != null)
                    {
                        method.Invoke(menu, null);
                        return;
                    }
                }
                SavesPlugin.Log.LogWarning($"Menu method '{methodName}' not found on {menu.GetType().Name}.");
            }
            catch (Exception e)
            {
                SavesPlugin.Log.LogWarning($"Could not call {methodName} on {menu.GetType().Name}: {e.InnerException?.Message ?? e.Message}");
            }
        }
    }

    /// <summary>
    /// Listens to the game's own menu system: every time ANY panel opens (the game's or another mod's),
    /// it tells our panels so they can close. This doesn't depend on how the menu is laid out.
    /// </summary>
    internal static class MenuWatcher
    {
        public static Type MenuType { get; private set; }
        public static event Action<Component> MenuOpened;

        private static bool installed;

        public static void Install(Harmony harmony)
        {
            if (installed)
                return;
            installed = true;

            MenuType = AccessTools.TypeByName("HaniUtils.UI.Menu");
            if (MenuType == null)
            {
                SavesPlugin.Log.LogWarning("Could not find the game's menu system; panels may overlap.");
                return;
            }

            MethodInfo open = MenuType.GetMethod("Open", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (open == null)
            {
                SavesPlugin.Log.LogWarning("Could not find Menu.Open; panels will rely on the backup check.");
                return;
            }

            harmony.Patch(open, postfix: new HarmonyMethod(typeof(MenuWatcher).GetMethod(nameof(AfterOpen), BindingFlags.Static | BindingFlags.NonPublic)));
        }

        private static void AfterOpen(object __instance)
        {
            try
            {
                if (__instance is Component menu)
                    MenuOpened?.Invoke(menu);
            }
            catch (Exception e)
            {
                SavesPlugin.Log.LogWarning($"Menu watcher error: {e.Message}");
            }
        }

        /// <summary>Every menu panel component currently loaded (in any scene).</summary>
        public static List<Component> AllMenus()
        {
            var result = new List<Component>();
            if (MenuType == null)
                return result;

            foreach (Object found in Object.FindObjectsByType(MenuType, FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (found is Component component && component != null)
                    result.Add(component);
            return result;
        }
    }

    /// <summary>
    /// Keeps menu panels exclusive: opening our panel closes every other visible panel (closing ALL the
    /// controllers each panel has), and our panel closes as soon as any other panel opens.
    /// </summary>
    internal class ExclusivePanel : MonoBehaviour
    {
        private sealed class Panel
        {
            public GameObject Object;
            public readonly List<Component> Menus = new List<Component>();
            public CanvasGroup Group;
            public bool WasVisible;
        }

        public Component Menu;
        public CanvasGroup Group;

        /// <summary>Never close a panel that contains this (the main menu buttons).</summary>
        public Transform ProtectedRoot;

        private readonly List<Panel> panels = new List<Panel>();
        private float nextScan;

        public bool IsOpen => Group != null && Group.alpha > 0.01f;

        private void OnEnable() => MenuWatcher.MenuOpened += OnAnyMenuOpened;

        private void OnDisable() => MenuWatcher.MenuOpened -= OnAnyMenuOpened;

        public void Open()
        {
            Scan();
            foreach (Panel panel in panels)
                if (IsVisible(panel))
                    foreach (Component menu in panel.Menus)
                        MenuUtil.CallMenu(menu, "Close");

            if (!IsOpen)
                MenuUtil.CallMenu(Menu, "Open");
        }

        public void Close()
        {
            MenuUtil.CallMenu(Menu, "Close");
        }

        private void OnAnyMenuOpened(Component opened)
        {
            if (opened == null || opened.gameObject == gameObject)
                return;
            if (IsOpen)
                Close();
        }

        private void Update()
        {
            if (Time.unscaledTime >= nextScan)
                Scan();

            // Backup check, in case a panel opens without going through the game's Open method.
            foreach (Panel panel in panels)
            {
                bool visible = IsVisible(panel);
                if (visible && !panel.WasVisible && IsOpen)
                    Close();
                panel.WasVisible = visible;
            }
        }

        /// <summary>Finds every other menu panel, grouping all the controllers that live on the same panel.</summary>
        private void Scan()
        {
            nextScan = Time.unscaledTime + 2f;

            List<Component> menus = MenuWatcher.AllMenus();
            if (menus.Count == 0 && transform.parent != null)
            {
                // Backup: look only at the panels next to ours.
                foreach (Transform child in transform.parent)
                    foreach (MonoBehaviour mb in child.GetComponents<MonoBehaviour>())
                        if (mb != null && IsMenuType(mb.GetType()))
                            menus.Add(mb);
            }

            panels.RemoveAll(p => p.Object == null);
            foreach (Panel panel in panels)
                panel.Menus.Clear();

            foreach (Component menu in menus)
            {
                GameObject go = menu.gameObject;
                if (go == gameObject || transform.IsChildOf(go.transform))
                    continue;
                if (ProtectedRoot != null && ProtectedRoot.IsChildOf(go.transform))
                    continue;

                Panel panel = panels.Find(p => p.Object == go);
                if (panel == null)
                {
                    panel = new Panel { Object = go, Group = go.GetComponent<CanvasGroup>() };
                    panel.WasVisible = IsVisible(panel);
                    panels.Add(panel);
                }
                panel.Menus.Add(menu);
            }
            panels.RemoveAll(p => p.Menus.Count == 0);
        }

        private static bool IsVisible(Panel panel)
        {
            if (panel.Object == null || !panel.Object.activeInHierarchy)
                return false;
            return panel.Group == null || panel.Group.alpha > 0.01f;
        }

        private static bool IsMenuType(Type type)
        {
            for (Type t = type; t != null; t = t.BaseType)
                if (t.FullName == "HaniUtils.UI.Menu")
                    return true;
            return false;
        }
    }

    /// <summary>Hover and click feedback for the text buttons in our panel (Load, Delete...).</summary>
    internal class TextButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public TMP_Text Label;
        public Color NormalColor = Color.white;
        public Color HoverColor = Color.white;
        public Action Clicked;

        private bool hovered;

        public void SetColors(Color normal, Color hover)
        {
            NormalColor = normal;
            HoverColor = hover;
            Refresh();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            hovered = true;
            Refresh();
            UiSounds.PlayHover();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            hovered = false;
            Refresh();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            UiSounds.PlayClick();
            Clicked?.Invoke();
        }

        private void OnDisable()
        {
            hovered = false;
            Refresh();
        }

        private void Refresh()
        {
            if (Label != null)
                Label.color = hovered ? HoverColor : NormalColor;
        }
    }
}
