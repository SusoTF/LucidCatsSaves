using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace LucidCatsSaves
{
    internal class LoadGameMenu : MonoBehaviour
    {
        private static readonly Color LoadColor = new Color(1f, 0.85f, 0.25f);
        private static readonly Color DeleteColor = new Color(1f, 0.35f, 0.3f);
        private static readonly Color ConfirmColor = new Color(1f, 0.15f, 0.15f);
        private static readonly Color HoverColor = Color.white;
        private const float DeleteConfirmSeconds = 4f;

        private MainMenuController controller;
        private ExclusivePanel panel;
        private Transform grid;

        private GameObject headerTemplate;
        private GameObject rowTemplate;

        private TMP_Text counterText;
        private readonly List<GameObject> listItems = new List<GameObject>();

        private bool warningMode;
        private string deletePendingId;
        private float deletePendingUntil;
        private TextButton deletePendingButton;

        private int versionRetries;
        private float nextVersionRetry;

        private TMP_InputField renameField;
        private bool rebuildRequested;

        public static void Create(Scene scene)
        {
            var go = new GameObject("Save Files (mod)");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<LoadGameMenu>();
        }

        private void Start()
        {
            try
            {
                Build();
                SavesPlugin.Log.LogInfo("Load Game added to the main menu.");
            }
            catch (Exception e)
            {
                SavesPlugin.Log.LogError($"Could not build the Load Game menu: {e}");
            }
        }

        private void Update()
        {
            if (rebuildRequested)
            {
                rebuildRequested = false;
                RebuildList();
            }

            if (deletePendingId != null && Time.unscaledTime > deletePendingUntil)
                CancelDeleteConfirmation();

            if (string.IsNullOrEmpty(SaveSession.GameVersion) && versionRetries < 5 && Time.unscaledTime >= nextVersionRetry)
            {
                versionRetries++;
                nextVersionRetry = Time.unscaledTime + 1f;
                SaveSession.GameVersion = DetectGameVersion();
            }
        }


        private void Build()
        {
            Transform root = FindMenuRoot();
            if (root == null)
                throw new Exception("Could not find 'Canvas/4x3' in the main menu.");

            Transform hostButton = MenuUtil.Require(root, "Margins/grid/UIButton (host)");
            Transform statsButton = MenuUtil.Require(root, "Margins/grid/UIButton (stats)");
            Transform statsPanel = MenuUtil.Require(root, "Stats menu");

            UiSounds.CaptureFrom(statsButton.gameObject);
            SaveSession.GameVersion = DetectGameVersion();

            BuildPanel(statsPanel);
            panel.ProtectedRoot = statsButton.parent;
            BuildLoadGameButton(statsButton, hostButton);
            TakeOverHostButton(hostButton);
        }

        private Transform FindMenuRoot()
        {
            foreach (GameObject go in gameObject.scene.GetRootGameObjects())
            {
                if (go.name != "Canvas")
                    continue;
                Transform found = go.transform.Find("4x3");
                if (found != null)
                    return found;
            }
            return null;
        }

        private void BuildLoadGameButton(Transform template, Transform hostButton)
        {
            GameObject button = Instantiate(template.gameObject, template.parent, false);
            button.name = "UIButton (load game)";
            button.transform.SetSiblingIndex(hostButton.GetSiblingIndex() + 1);

            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
                label.text = "Load Game";

            UnityEvent click = MenuUtil.FindClickEvent(button);
            if (click == null)
                throw new Exception("Could not find the click event of the Load Game button.");

            for (int i = 0; i < click.GetPersistentEventCount(); i++)
                click.SetPersistentListenerState(i, UnityEventCallState.Off);
            click.AddListener(OnLoadGameClicked);
        }

        private void TakeOverHostButton(Transform hostButton)
        {
            UnityEvent click = MenuUtil.FindClickEvent(hostButton.gameObject);
            if (click == null || click.GetPersistentEventCount() == 0)
            {
                SavesPlugin.Log.LogWarning("Could not take over the Host button; the full-slots warning won't show.");
                return;
            }

            controller = click.GetPersistentTarget(0) as MainMenuController;
            if (controller == null || click.GetPersistentMethodName(0) != "HostUI")
            {
                SavesPlugin.Log.LogWarning("The Host button isn't set up as expected; leaving it untouched.");
                controller = null;
                return;
            }

            click.SetPersistentListenerState(0, UnityEventCallState.Off);
            click.AddListener(OnHostClicked);
        }

        private void BuildPanel(Transform statsPanel)
        {
            var holder = new GameObject("Load Game Holder");
            holder.SetActive(false);

            GameObject panelObject = Instantiate(statsPanel.gameObject, holder.transform, false);
            panelObject.name = "Load Game menu";
            foreach (Game.UI.LifetimeStatsDisplay stats in panelObject.GetComponentsInChildren<Game.UI.LifetimeStatsDisplay>(true))
                DestroyImmediate(stats);

            Transform title = panelObject.transform.Find("Text (TMP)");
            if (title != null && title.GetComponent<TMP_Text>() != null)
                title.GetComponent<TMP_Text>().text = "Load Game";

            grid = MenuUtil.Require(panelObject.transform, "grid");
            Transform header = MenuUtil.Require(grid, "Text name");
            Transform row = MenuUtil.Require(grid, "stat display");

            headerTemplate = Instantiate(header.gameObject, panelObject.transform, false);
            headerTemplate.name = "Header Template";
            headerTemplate.SetActive(false);
            rowTemplate = Instantiate(row.gameObject, panelObject.transform, false);
            rowTemplate.name = "Row Template";
            rowTemplate.SetActive(false);

            var oldChildren = new List<GameObject>();
            foreach (Transform child in grid)
                oldChildren.Add(child.gameObject);
            foreach (GameObject old in oldChildren)
                DestroyImmediate(old);

            counterText = MenuUtil.CloneText(headerTemplate.transform, grid, "Counter");

            panelObject.transform.SetParent(statsPanel.parent, false);
            panelObject.transform.SetSiblingIndex(statsPanel.GetSiblingIndex() + 1);
            Destroy(holder);

            panel = panelObject.AddComponent<ExclusivePanel>();
            panel.Menu = MenuUtil.FindMenuComponent(panelObject);
            panel.Group = panelObject.GetComponent<CanvasGroup>();
            if (panel.Menu == null)
                throw new Exception("The cloned panel has no Menu component.");
        }


        private void OnLoadGameClicked()
        {
            if (panel.IsOpen && !warningMode)
            {
                panel.Close();
                return;
            }

            warningMode = false;
            RebuildList();
            panel.Open();
        }

        private void OnHostClicked()
        {
            SaveSession.PendingLoad = null;
            SaveSession.SkipSaving = false;

            if (SaveStore.LoadAll().Count >= SaveStore.MaxSaves)
            {
                warningMode = true;
                RebuildList();
                panel.Open();
                return;
            }

            StartHosting();
        }

        private void StartHosting()
        {
            panel.Close();

            MainMenuController target = controller != null ? controller : Object.FindFirstObjectByType<MainMenuController>();
            if (target == null)
            {
                SavesPlugin.Log.LogError("Could not find the main menu controller to start hosting.");
                return;
            }
            target.HostUI();
        }

        private void LoadSave(SaveData save)
        {
            SaveSession.BeginLoad(save);
            SavesPlugin.Log.LogInfo("Hosting a loaded game.");
            StartHosting();
        }

        private void OnDeleteClicked(SaveData save, TextButton button)
        {
            if (deletePendingId != save.id)
            {
                CancelDeleteConfirmation();
                deletePendingId = save.id;
                deletePendingUntil = Time.unscaledTime + DeleteConfirmSeconds;
                deletePendingButton = button;
                button.Label.text = "Sure?";
                button.SetColors(ConfirmColor, HoverColor);
                return;
            }

            deletePendingId = null;
            deletePendingButton = null;
            SaveStore.Delete(save.id);
            SavesPlugin.Log.LogInfo("Save deleted.");
            RebuildList();
        }

        private void CancelDeleteConfirmation()
        {
            if (deletePendingButton != null)
            {
                deletePendingButton.Label.text = "Delete";
                deletePendingButton.SetColors(DeleteColor, HoverColor);
            }
            deletePendingId = null;
            deletePendingButton = null;
        }


        private void RebuildList()
        {
            deletePendingId = null;
            deletePendingButton = null;
            renameField = null;

            foreach (GameObject item in listItems)
                if (item != null)
                    DestroyImmediate(item);
            listItems.Clear();

            List<SaveData> saves = SaveStore.LoadAll();
            counterText.text = $"SAVES {saves.Count}/{SaveStore.MaxSaves}";

            if (warningMode)
                AddWarning(saves.Count);

            if (saves.Count == 0)
            {
                TMP_Text empty = MenuUtil.CloneText(headerTemplate.transform, grid, "Empty");
                empty.text = "<size=70%>No saved games yet. A save is created after you survive your first night.</size>";
                empty.textWrappingMode = TextWrappingModes.Normal;
                SetHeight(empty.gameObject, 90f);
                listItems.Add(empty.gameObject);
                return;
            }

            foreach (SaveData save in saves)
                AddSaveRow(save);
        }

        private void AddWarning(int saveCount)
        {
            bool full = saveCount >= SaveStore.MaxSaves;
            string message = full
                ? $"You have {SaveStore.MaxSaves} saves. This game won't be saved unless you delete one."
                : "You now have a free slot, so this game will be saved.";

            GameObject row = CreateRow("Warning", message, 110f, out RectTransform actions, out _);
            TextButton host = AddAction(actions, full ? "Host anyway" : "Host", LoadColor, 0.5f, 1f);
            host.Clicked = () =>
            {
                SaveSession.SkipSaving = SaveStore.LoadAll().Count >= SaveStore.MaxSaves;
                SaveSession.PendingLoad = null;
                warningMode = false;
                StartHosting();
            };
            TextButton cancel = AddAction(actions, "Cancel", DeleteColor, 0f, 0.5f);
            cancel.Clicked = () =>
            {
                warningMode = false;
                panel.Close();
            };
            listItems.Add(row);
        }

        private void AddSaveRow(SaveData save)
        {
            string current = SaveSession.VersionOrFallback();
            string version = string.IsNullOrEmpty(save.gameVersion) ? "?" : save.gameVersion;
            string versionText = version == current || string.IsNullOrEmpty(current)
                ? $"v{version}"
                : $"<color=#FF8C1A>v{version} (you have v{current})</color>";

            string players = save.lastPlayers != null && save.lastPlayers.Count > 0
                ? string.Join(", ", save.lastPlayers)
                : "-";

            string info =
                $"<color=#FF3B30>NIGHT {save.night + 1}</color>  ·  " +
                $"<color=#FFD940>{save.lastTotalCredits.ToString("N0", CultureInfo.InvariantCulture)} cr</color>\n" +
                $"<size=70%><color=#FFFFFF>Last players on this save: {players}</color>\n" +
                $"Created {FormatDate(save.Created)}  ·  Saved {FormatDate(save.LastSaved)}  ·  {versionText}</size>";

            GameObject row = CreateRow("Save " + save.id, info, 124f, out RectTransform actions, out TMP_Text details);

            if (details != null)
                details.rectTransform.offsetMax = new Vector2(0f, -38f);
            AddNameLabel(row, details, save);

            TextButton load = AddAction(actions, "Load", LoadColor, 0.5f, 1f);
            load.Clicked = () => LoadSave(save);

            TextButton delete = AddAction(actions, "Delete", DeleteColor, 0f, 0.5f);
            delete.Clicked = () => OnDeleteClicked(save, delete);

            listItems.Add(row);
        }

        private GameObject CreateRow(string rowName, string text, float height, out RectTransform actions, out TMP_Text details)
        {
            GameObject row = Instantiate(rowTemplate, grid, false);
            row.name = rowName;
            row.SetActive(true);
            SetHeight(row, height);

            Transform nameText = row.transform.Find("Text name");
            Transform valueText = row.transform.Find("Text value");

            TMP_Text info = nameText != null ? nameText.GetComponent<TMP_Text>() : null;
            if (info != null)
            {
                RectTransform rect = info.rectTransform;
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(0.74f, 1f);
                rect.offsetMin = new Vector2(12f, 6f);
                rect.offsetMax = new Vector2(0f, -6f);
                info.text = text;
                info.alignment = TextAlignmentOptions.Left;
                info.textWrappingMode = TextWrappingModes.Normal;
                info.enableAutoSizing = false;
            }
            details = info;

            actions = MenuUtil.NewRect("Actions", row.transform);
            actions.anchorMin = new Vector2(0.76f, 0f);
            actions.anchorMax = new Vector2(1f, 1f);
            actions.offsetMin = new Vector2(0f, 6f);
            actions.offsetMax = new Vector2(-12f, -6f);

            if (valueText != null)
            {
                valueText.SetParent(actions, false);
                valueText.gameObject.SetActive(false);
                valueText.name = "Action Template";
            }
            return row;
        }

        private static TextButton AddAction(RectTransform actions, string label, Color color, float minY, float maxY)
        {
            Transform template = actions.Find("Action Template");
            TMP_Text text = template != null
                ? MenuUtil.CloneText(template, actions, label)
                : null;
            if (text == null)
                throw new Exception("The row has no text to build buttons from.");

            RectTransform rect = text.rectTransform;
            rect.anchorMin = new Vector2(0f, minY);
            rect.anchorMax = new Vector2(1f, maxY);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            text.text = label;
            text.alignment = TextAlignmentOptions.Right;
            text.raycastTarget = true;

            TextButton button = text.gameObject.AddComponent<TextButton>();
            button.Label = text;
            button.SetColors(color, HoverColor);
            return button;
        }

        private void AddNameLabel(GameObject row, TMP_Text template, SaveData save)
        {
            if (template == null)
                return;

            TMP_Text label = MenuUtil.CloneText(template.transform, row.transform, "Save Name");
            RectTransform rect = label.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0.74f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.offsetMin = new Vector2(12f, -38f);
            rect.offsetMax = new Vector2(0f, -6f);

            label.text = string.IsNullOrWhiteSpace(save.name) ? "Unnamed save" : save.name;
            label.alignment = TextAlignmentOptions.Left;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.enableAutoSizing = false;
            label.raycastTarget = true;

            TextButton button = label.gameObject.AddComponent<TextButton>();
            button.Label = label;
            button.SetColors(Color.white, LoadColor);
            button.Clicked = () => StartRename(save, label);
        }

        private void StartRename(SaveData save, TMP_Text label)
        {
            if (renameField != null)
                return;

            RectTransform labelRect = label.rectTransform;

            var boxObject = new GameObject("Rename Box", typeof(RectTransform));
            boxObject.transform.SetParent(labelRect.parent, false);
            var box = (RectTransform)boxObject.transform;
            box.anchorMin = labelRect.anchorMin;
            box.anchorMax = labelRect.anchorMax;
            box.pivot = labelRect.pivot;
            box.offsetMin = labelRect.offsetMin + new Vector2(-4f, 0f);
            box.offsetMax = labelRect.offsetMax;
            Image background = boxObject.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.75f);

            var areaObject = new GameObject("Text Area", typeof(RectTransform));
            areaObject.transform.SetParent(box, false);
            var area = (RectTransform)areaObject.transform;
            area.anchorMin = Vector2.zero;
            area.anchorMax = Vector2.one;
            area.offsetMin = new Vector2(4f, 0f);
            area.offsetMax = new Vector2(-4f, 0f);
            areaObject.AddComponent<RectMask2D>();

            TMP_Text text = MenuUtil.CloneText(label.transform, area, "Text");
            Object.DestroyImmediate(text.GetComponent<TextButton>());
            MenuUtil.Stretch(text.rectTransform, 0f, 0f, 1f, 1f);
            text.text = string.Empty;
            text.color = Color.white;

            label.gameObject.SetActive(false);

            boxObject.SetActive(false);
            TMP_InputField field = boxObject.AddComponent<TMP_InputField>();
            field.textViewport = area;
            field.textComponent = text;
            field.characterLimit = SaveStore.MaxNameLength;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.richText = false;
            field.text = save.name ?? string.Empty;
            field.onEndEdit.AddListener(value => FinishRename(save, field, value));
            boxObject.SetActive(true);

            renameField = field;
            field.Select();
            field.ActivateInputField();
        }

        private void FinishRename(SaveData save, TMP_InputField field, string value)
        {
            if (renameField != field)
                return;
            renameField = null;

            string cleaned = SaveStore.CleanName(value);
            if (!field.wasCanceled && cleaned.Length > 0 && cleaned != save.name)
            {
                SaveStore.Rename(save.id, cleaned);
                SavesPlugin.Log.LogInfo($"Save renamed to \"{cleaned}\".");
            }

            rebuildRequested = true;
        }

        private static void SetHeight(GameObject go, float height)
        {
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);

            LayoutElement layout = go.GetComponent<LayoutElement>();
            if (layout == null)
                layout = go.AddComponent<LayoutElement>();
            layout.minHeight = height;
            layout.preferredHeight = height;
        }

        private static string FormatDate(DateTime date)
        {
            return date == DateTime.MinValue ? "?" : date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        private string DetectGameVersion()
        {
            const string prefix = "Version ";
            try
            {
                foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                {
                    foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
                    {
                        string value = text.text;
                        if (!string.IsNullOrEmpty(value) && value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                            return value.Substring(prefix.Length).Trim();
                    }

                    foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        if (mb == null || mb is TMP_Text)
                            continue;
                        FieldInfo field = mb.GetType().GetField("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (field != null && field.GetValue(mb) is string value &&
                            value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                            return value.Substring(prefix.Length).Trim();
                    }
                }
            }
            catch (Exception e)
            {
                SavesPlugin.Log.LogDebug($"Could not read the game version: {e.Message}");
            }
            return string.Empty;
        }
    }
}
