using System;
using System.Collections.Generic;
using System.Reflection;
using Duckov.UI.Animations;
using MagicTreasurePower.Treasures;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace MagicTreasurePower.UI
{
    /// <summary>
    /// 「火眼金睛」扫描结果面板：全屏可关闭的覆盖层，按物资箱分组列出价值最高的几件物品。
    ///
    /// <para><b>不暂停游戏：</b>只接管鼠标 / 屏蔽角色输入（与法宝宝箱界面同款处理），
    /// 点空白处、关闭按钮或按 Esc 即可关闭，关掉后立刻恢复操作。</para>
    ///
    /// <para><b>外观：</b>深色半透明圆角面板 + 金色标题，底图与按钮按压动画全部复用
    /// <see cref="RoundedSprite"/> 与法宝宝箱界面同一套参数，风格保持一致。</para>
    /// </summary>
    internal static class TreasureScanUI
    {
        private const float PanelWidth = 960f;
        private const float HeaderHeight = 104f;
        private const float BottomPadding = 26f;
        private const float GroupHeaderHeight = 30f;
        private const float ItemRowHeight = 48f;
        private const float RowSpacing = 6f;
        private const float GroupGap = 12f;
        private const float MinPanelHeight = 320f;
        private const float MaxPanelHeight = 860f;

        private static readonly Color Gold = new Color(0.91f, 0.75f, 0.36f, 1f);
        private static readonly Color PanelColor = new Color(0.07f, 0.08f, 0.09f, 0.98f);
        private static readonly Color RowColor = new Color(0.13f, 0.14f, 0.16f, 1f);
        private static readonly Color HeaderColor = new Color(1f, 1f, 1f, 0.03f);
        private static readonly Color HeaderSelectedColor = new Color(0.22f, 0.19f, 0.11f, 1f);
        private static readonly Color RowSelectedColor = new Color(0.24f, 0.21f, 0.12f, 1f);
        private static readonly Color TextMain = new Color(0.93f, 0.91f, 0.86f, 1f);
        private static readonly Color TextSub = new Color(0.62f, 0.63f, 0.67f, 1f);

        /// <summary>箱子标题行的底图，用于标记「当前标记目标」。</summary>
        private static readonly Dictionary<int, Image> BoxHeaderBackgrounds = new Dictionary<int, Image>();

        /// <summary>箱子标题行对应的分组与文字，用于刷新「已获取」标记。</summary>
        private static readonly Dictionary<int, TreasureScanGroup> BoxGroups = new Dictionary<int, TreasureScanGroup>();
        private static readonly Dictionary<int, TextMeshProUGUI> BoxHeaderTexts = new Dictionary<int, TextMeshProUGUI>();

        /// <summary>每个目标下的物品行底图，用于把「已选中」的整组行一起点亮。</summary>
        private static readonly Dictionary<int, List<Image>> BoxRowBackgrounds = new Dictionary<int, List<Image>>();

        private static float _nextLootedCheck;

        /// <summary>点了箱子行后延迟关闭面板的时刻（先让「已选中」反馈可见）。</summary>
        private static float _closeAt;

        /// <summary>
        /// 本局扫描结果的快照：关掉面板后用 F8 可以直接重新打开，不必重扫（物品可能已被拿走）。
        /// </summary>
        private static List<TreasureScanGroup> _lastGroups;
        private static int _lastWant;
        private static int _lastLevel;
        private static LevelManager _lastLevelManager;
        private static ScanHotkeyHost _hotkeyHost;

        private static GameObject _root;
        private static CursorLockMode _previousLock;
        private static bool _previousVisible;
        private static bool _cursorCaptured;

        internal static bool IsOpen => _root != null;

        /// <summary>停用 mod 时强制关掉界面，避免留下无人管理的覆盖层。</summary>
        internal static void ForceClose()
        {
            ClearLastResult();

            if (_root == null)
                return;

            Close();
        }

        /// <summary>本局扫描结果作废（换图 / 回基地 / 停用 mod）。</summary>
        internal static void ClearLastResult()
        {
            _lastGroups = null;
            _lastLevelManager = null;
        }

        private static void EnsureHotkeyHost()
        {
            if (_hotkeyHost != null && !_hotkeyHost)
                _hotkeyHost = null;

            if (_hotkeyHost == null)
            {
                GameObject go = new GameObject("MagicTreasurePower_ScanHotkey");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _hotkeyHost = go.AddComponent<ScanHotkeyHost>();
            }
        }

        /// <summary>F8：按本局扫描结果重新打开面板（不重新扫描）。</summary>
        internal static void ReopenLast()
        {
            if (_lastGroups == null || _lastGroups.Count == 0)
                return;

            LevelManager manager = LevelManager.Instance;
            if (manager == null || !manager.IsRaidMap ||
                !ReferenceEquals(manager, _lastLevelManager))
            {
                ClearLastResult();
                return;
            }

            Show(_lastGroups, _lastWant, _lastLevel);
        }

        internal static void Show(List<TreasureScanGroup> groups, int want, int level)
        {
            try
            {
                if (groups == null || groups.Count == 0)
                    return;

                _closeAt = 0f;
                _lastGroups = groups;
                _lastWant = want;
                _lastLevel = level;
                _lastLevelManager = LevelManager.Instance;
                EnsureHotkeyHost();

                if (_root != null)
                    Close();

                _previousLock = Cursor.lockState;
                _previousVisible = Cursor.visible;
                _cursorCaptured = true;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;

                _root = new GameObject("MagicTreasurePower_ScanUI");
                UnityEngine.Object.DontDestroyOnLoad(_root);

                Canvas canvas = _root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 32000;

                CanvasScaler scaler = _root.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;

                _root.AddComponent<GraphicRaycaster>();
                _root.AddComponent<ScanUIUpdater>();

                // 屏掉角色输入：否则鼠标显示出来了，点击界面还会顺带开枪 / 移动
                InputManager.DisableInput(_root);

                BuildBackdrop();

                float contentHeight = MeasureContent(groups);
                float panelHeight = Mathf.Clamp(
                    HeaderHeight + contentHeight + BottomPadding, MinPanelHeight, MaxPanelHeight);

                Transform panel = BuildPanel(panelHeight);
                TMP_FontAsset font = ResolveFont();

                BuildHeader(panel, font, want, level, groups);
                BuildList(panel, font, groups);
                RefreshSelectionHighlight();
            }
            catch (Exception e)
            {
                Diag.Report("火眼金睛：构建扫描面板失败", e);
                Close();
            }
        }

        private static float MeasureContent(List<TreasureScanGroup> groups)
        {
            float height = 0f;

            for (int i = 0; i < groups.Count; i++)
            {
                if (i > 0)
                    height += GroupGap;

                height += GroupHeaderHeight + RowSpacing;
                height += groups[i].Entries.Count * (ItemRowHeight + RowSpacing);
            }

            return height;
        }

        private static void BuildBackdrop()
        {
            GameObject backdrop = new GameObject("Backdrop", typeof(RectTransform), typeof(Image), typeof(Button));
            backdrop.transform.SetParent(_root.transform, false);
            Stretch((RectTransform)backdrop.transform);

            Image image = backdrop.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.6f);
            image.raycastTarget = true;

            Button button = backdrop.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(Close);
        }

        private static Transform BuildPanel(float panelHeight)
        {
            GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(_root.transform, false);

            var rect = (RectTransform)panel.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(PanelWidth, panelHeight);

            Image image = panel.GetComponent<Image>();
            image.sprite = RoundedSprite.Get();
            image.type = Image.Type.Sliced;
            image.color = PanelColor;
            image.raycastTarget = true;

            return panel.transform;
        }

        private static void BuildHeader(
            Transform panel, TMP_FontAsset font, int want, int level, List<TreasureScanGroup> groups)
        {
            TextMeshProUGUI title = CreateText(panel, "Title", "火眼金睛 · 物资扫描",
                28f, Gold, font, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(28f, -56f), new Vector2(-140f, -16f));
            title.alignment = TextAlignmentOptions.Left;

            int total = 0;
            for (int i = 0; i < groups.Count; i++)
                total += groups[i].Entries.Count;

            ModConfig config = ModConfig.Current;
            string routeHint = config.ShowRouteLine ? "点箱子行带路 · " : string.Empty;
            string closeHint = " · " + routeHint + config.ScanHotkeyText + " 重开 · Esc 关闭";

            string subtitle = total < want
                ? $"本局共扫到 {total} 件可获取物品（法宝 Lv.{level}）" + closeHint
                : $"本局价值最高的 {total} 件物品（法宝 Lv.{level}）" + closeHint;

            TextMeshProUGUI sub = CreateText(panel, "Subtitle", subtitle,
                15f, TextSub, font, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(28f, -80f), new Vector2(-140f, -58f));
            sub.alignment = TextAlignmentOptions.Left;

            GameObject line = new GameObject("Divider", typeof(RectTransform), typeof(Image));
            line.transform.SetParent(panel, false);
            var lineRect = (RectTransform)line.transform;
            lineRect.anchorMin = new Vector2(0f, 1f);
            lineRect.anchorMax = new Vector2(1f, 1f);
            lineRect.pivot = new Vector2(0.5f, 1f);
            lineRect.offsetMin = new Vector2(26f, -92f);
            lineRect.offsetMax = new Vector2(-26f, -90f);
            Image lineImage = line.GetComponent<Image>();
            lineImage.color = new Color(1f, 1f, 1f, 0.08f);
            lineImage.raycastTarget = false;

            GameObject close = new GameObject("Close", typeof(RectTransform), typeof(Image), typeof(Button));
            close.transform.SetParent(panel, false);
            var closeRect = (RectTransform)close.transform;
            closeRect.anchorMin = new Vector2(1f, 1f);
            closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.sizeDelta = new Vector2(76f, 34f);
            closeRect.anchoredPosition = new Vector2(-24f, -20f);

            Image closeImage = close.GetComponent<Image>();
            closeImage.sprite = RoundedSprite.Get();
            closeImage.type = Image.Type.Sliced;
            closeImage.color = new Color(0.18f, 0.18f, 0.2f, 0.95f);

            Button closeButton = close.GetComponent<Button>();
            closeButton.transition = Selectable.Transition.None;
            closeButton.onClick.AddListener(Close);

            TextMeshProUGUI closeText = CreateText(close.transform, "Text", "关闭",
                15f, TextMain, font, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            closeText.alignment = TextAlignmentOptions.Center;

            ConfigurePressAnimation(close, closeRect);
        }

        private static void BuildList(Transform panel, TMP_FontAsset font, List<TreasureScanGroup> groups)
        {
            GameObject list = new GameObject("List", typeof(RectTransform));
            list.transform.SetParent(panel, false);

            var listRect = (RectTransform)list.transform;
            listRect.anchorMin = new Vector2(0f, 0f);
            listRect.anchorMax = new Vector2(1f, 1f);
            listRect.offsetMin = new Vector2(26f, BottomPadding);
            listRect.offsetMax = new Vector2(-26f, -HeaderHeight);

            float y = 0f;

            for (int g = 0; g < groups.Count; g++)
            {
                TreasureScanGroup group = groups[g];

                if (g > 0)
                    y += GroupGap;

                BuildGroupHeader(list.transform, group, y, font);
                y += GroupHeaderHeight + RowSpacing;

                for (int i = 0; i < group.Entries.Count; i++)
                {
                    BuildItemRow(list.transform, group, group.Entries[i], y, font);
                    y += ItemRowHeight + RowSpacing;
                }
            }
        }

        private static void BuildGroupHeader(Transform parent, TreasureScanGroup group, float y, TMP_FontAsset font)
        {
            GameObject headerGo = new GameObject("GroupHeader_" + group.TargetId,
                typeof(RectTransform), typeof(Image), typeof(Button));
            headerGo.transform.SetParent(parent, false);

            var rect = (RectTransform)headerGo.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -y);
            rect.sizeDelta = new Vector2(0f, GroupHeaderHeight);

            Image background = headerGo.GetComponent<Image>();
            background.sprite = RoundedSprite.Get();
            background.type = Image.Type.Sliced;
            background.color = HeaderColor;
            background.raycastTarget = true;

            Button button = headerGo.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = background;
            button.onClick.AddListener(() => SelectBox(group));

            BoxHeaderBackgrounds[group.TargetId] = background;
            BoxGroups[group.TargetId] = group;

            TextMeshProUGUI header = CreateText(headerGo.transform, "Text", string.Empty, 17f, TextMain, font,
                Vector2.zero, Vector2.one, new Vector2(12f, 0f), new Vector2(-12f, 0f));
            header.alignment = TextAlignmentOptions.MidlineLeft;
            header.overflowMode = TextOverflowModes.Ellipsis;

            BoxHeaderTexts[group.TargetId] = header;
            ApplyHeaderText(group, header);
        }

        private static void BuildItemRow(
            Transform parent, TreasureScanGroup group, TreasureScanEntry entry, float y, TMP_FontAsset font)
        {
            GameObject row = new GameObject("Row_" + entry.BoxId + "_" + y,
                typeof(RectTransform), typeof(Image), typeof(Button));
            row.transform.SetParent(parent, false);

            var rect = (RectTransform)row.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -y);
            rect.sizeDelta = new Vector2(0f, ItemRowHeight);

            Image background = row.GetComponent<Image>();
            background.sprite = RoundedSprite.Get();
            background.type = Image.Type.Sliced;
            background.color = RowColor;
            background.raycastTarget = true;

            List<Image> rows;
            if (!BoxRowBackgrounds.TryGetValue(group.TargetId, out rows))
            {
                rows = new List<Image>();
                BoxRowBackgrounds[group.TargetId] = rows;
            }

            rows.Add(background);

            // 点物品行同样选中它所在的箱子，和点箱子标题等价
            Button button = row.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = background;
            button.onClick.AddListener(() => SelectBox(group));

            Sprite icon = entry.CachedIcon;
            if (icon != null)
            {
                GameObject iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                iconGo.transform.SetParent(row.transform, false);

                var iconRect = (RectTransform)iconGo.transform;
                iconRect.anchorMin = new Vector2(0f, 0.5f);
                iconRect.anchorMax = new Vector2(0f, 0.5f);
                iconRect.pivot = new Vector2(0f, 0.5f);
                iconRect.sizeDelta = new Vector2(34f, 34f);
                iconRect.anchoredPosition = new Vector2(12f, 0f);

                Image iconImage = iconGo.GetComponent<Image>();
                iconImage.sprite = icon;
                iconImage.preserveAspect = true;
                iconImage.raycastTarget = false;
            }

            TextMeshProUGUI name = CreateText(row.transform, "Name", entry.DisplayName,
                17f, TextMain, font, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(58f, 0f), new Vector2(-180f, 0f));
            name.alignment = TextAlignmentOptions.Left;
            name.overflowMode = TextOverflowModes.Ellipsis;

            TextMeshProUGUI value = CreateText(row.transform, "Value", FormatValue(entry.Value),
                17f, Gold, font, new Vector2(1f, 0f), new Vector2(1f, 1f),
                new Vector2(-172f, 0f), new Vector2(-16f, 0f));
            value.alignment = TextAlignmentOptions.Right;
            value.overflowMode = TextOverflowModes.Overflow;
        }

        /// <summary>点箱子行：切换带路绿线（同一个箱子再点一次取消）。</summary>
        private static void SelectBox(TreasureScanGroup group)
        {
            try
            {
                if (group == null || group.Target == null)
                    return;

                // 配置里关掉「带路绿线」后，点箱子行不再画线，也不关面板
                if (!ModConfig.Current.ShowRouteLine)
                {
                    WarnRouteDisabledOnce();
                    return;
                }

                TreasureTargetMarker.Toggle(group.Target);

                // 选中了新的透视目标：先高亮这一行并显示「已选中」，
                // 0.45 秒后再关面板，让玩家看清自己点的是哪一个；
                // 再点同一行是取消，面板保持打开
                if (TreasureTargetMarker.ActiveTargetId == group.TargetId)
                {
                    Diag.Log("扫描面板：已选中「" + group.BoxName + "」");
                    RefreshSelectionHighlight();
                    _closeAt = Time.unscaledTime + 0.7f;
                    return;
                }

                // 取消选中：保持面板打开
                _closeAt = 0f;
                RefreshSelectionHighlight();
            }
            catch (Exception e)
            {
                Diag.Report("火眼金睛：选择带路目标失败", e);
            }
        }

        private static bool _routeDisabledWarned;

        private static void WarnRouteDisabledOnce()
        {
            if (_routeDisabledWarned)
                return;

            _routeDisabledWarned = true;
            Diag.Log("透视标记已在 config.ini（ShowRouteLine=false）中关闭，点箱子行不会显示标记");
        }

        /// <summary>
        /// 箱子标题文案：名称 + 距离；本次战局已经打开过的箱子追加「已获取」并把名字压暗。
        /// </summary>
        private static void ApplyHeaderText(TreasureScanGroup group, TextMeshProUGUI text)
        {
            if (group == null || text == null)
                return;

            bool looted = group.IsLooted();
            bool selected = TreasureTargetMarker.ActiveTargetId == group.TargetId;

            string nameColor = selected ? "#F2D06B" : (looted ? "#8A8D91" : "#E8BF5C");

            string result =
                "<color=" + nameColor + ">" + group.BoxName + "</color>" +
                "  <color=#9A9B9F>· 距你 " + Mathf.RoundToInt(group.Distance) + " 米</color>";

            if (selected)
                result += "  <color=#F2D06B>【已选中】</color>";

            if (looted)
                result += "  <color=#7FBF7F>已获取</color>";

            text.text = result;
        }

        private static void RefreshLootedLabels()
        {
            foreach (KeyValuePair<int, TreasureScanGroup> pair in BoxGroups)
            {
                TextMeshProUGUI text;
                if (BoxHeaderTexts.TryGetValue(pair.Key, out text) && text != null)
                    ApplyHeaderText(pair.Value, text);
            }
        }

        /// <summary>把「当前标记目标」的那一行标成金色底。</summary>
        private static void RefreshSelectionHighlight()
        {
            int active = TreasureTargetMarker.ActiveTargetId;

            foreach (KeyValuePair<int, Image> pair in BoxHeaderBackgrounds)
            {
                if (pair.Value == null)
                    continue;

                pair.Value.color = pair.Key == active ? HeaderSelectedColor : HeaderColor;
            }

            foreach (KeyValuePair<int, List<Image>> pair in BoxRowBackgrounds)
            {
                if (pair.Value == null)
                    continue;

                Color color = pair.Key == active ? RowSelectedColor : RowColor;

                for (int i = 0; i < pair.Value.Count; i++)
                {
                    Image image = pair.Value[i];
                    if (image != null)
                        image.color = color;
                }
            }

            RefreshLootedLabels();
        }

        /// <summary>价值显示：1 万以上按「万」，否则原样输出。</summary>
        private static string FormatValue(long value)
        {
            if (value >= 10000L)
                return (value / 10000f).ToString("0.#") + " 万";

            return value.ToString();
        }

        private static void Close()
        {
            try
            {
                if (_root != null)
                {
                    try
                    {
                        InputManager.ActiveInput(_root);
                    }
                    catch
                    {
                        // InputManager 可能已经销毁（回主菜单），忽略
                    }

                    UnityEngine.Object.Destroy(_root);
                    _root = null;
                }

                BoxHeaderBackgrounds.Clear();
                BoxGroups.Clear();
                BoxHeaderTexts.Clear();
                BoxRowBackgrounds.Clear();
                _closeAt = 0f;

                if (_cursorCaptured)
                {
                    Cursor.lockState = _previousLock;
                    Cursor.visible = _previousVisible;
                    _cursorCaptured = false;
                }
            }
            catch (Exception e)
            {
                Diag.Report("火眼金睛：关闭扫描面板失败", e);
            }
        }

        private static void ConfigurePressAnimation(GameObject target, RectTransform rect)
        {
            try
            {
                SizeDeltaToggle toggle = target.AddComponent<SizeDeltaToggle>();

                Vector2 idle = rect.sizeDelta;
                toggle.idleSizeDelta = idle;
                toggle.activeSizeDelta = idle + new Vector2(-8f, -8f);
                toggle.duration = 0.1f;
                toggle.animationCurve = CreatePressCurve();

                target.AddComponent<PressToggleBridge>().Configure(toggle);
            }
            catch (Exception e)
            {
                Diag.Report("火眼金睛：添加按钮按下动画失败", e);
            }
        }

        private static AnimationCurve CreatePressCurve()
        {
            var curve = new AnimationCurve(
                new Keyframe(0f, 0f, 2f, 2f),
                new Keyframe(0.6662211f, 1.527896f, 0f, 0f, 1f / 3f, 1f / 3f),
                new Keyframe(1f, 1f, 0f, 0f));
            curve.preWrapMode = WrapMode.Clamp;
            curve.postWrapMode = WrapMode.Clamp;
            return curve;
        }

        /// <summary>按下 / 抬起时驱动 <see cref="SizeDeltaToggle"/>（原版使用按钮同款交互）。</summary>
        private sealed class PressToggleBridge : MonoBehaviour,
            IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
        {
            private SizeDeltaToggle _toggle;

            internal void Configure(SizeDeltaToggle toggle)
            {
                _toggle = toggle;
            }

            public void OnPointerDown(PointerEventData eventData)
            {
                SetToggle(true);
            }

            public void OnPointerUp(PointerEventData eventData)
            {
                SetToggle(false);
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                SetToggle(false);
            }

            private void SetToggle(bool value)
            {
                try
                {
                    if (_toggle != null)
                        _toggle.SetToggle(value);
                }
                catch (Exception e)
                {
                    Diag.Report("火眼金睛：播放按钮按下动画失败", e);
                }
            }
        }

        private static TextMeshProUGUI CreateText(
            Transform parent, string name, string text, float size, Color color, TMP_FontAsset font,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.font = font;
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.raycastTarget = false;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Overflow;

            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return tmp;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// 覆盖层的每帧维护：光标强制可见 / 不锁定；按 Esc 关闭；
        /// 已经不在战局（回基地 / 主菜单）时自动关闭。
        /// </summary>
        [DefaultExecutionOrder(10000)]
        private sealed class ScanUIUpdater : MonoBehaviour
        {
            private void LateUpdate()
            {
                try
                {
                    LevelManager manager = LevelManager.Instance;
                    if (manager == null || !manager.IsRaidMap)
                    {
                        Close();
                        return;
                    }

                    Cursor.visible = true;
                    Cursor.lockState = CursorLockMode.None;

                    // 走到箱子附近时标记会自动消失，这里同步取消高亮
                    RefreshSelectionHighlight();

                    // 点了箱子行后延迟关闭，先让「已选中」反馈可见
                    if (_closeAt > 0f && Time.unscaledTime >= _closeAt)
                    {
                        Close();
                        return;
                    }

                    // 面板开着期间每 1 秒同步「已获取」标记（比如刚开过箱子再按热键打开）
                    if (Time.unscaledTime >= _nextLootedCheck)
                    {
                        _nextLootedCheck = Time.unscaledTime + 1f;
                        RefreshLootedLabels();
                    }

                    UnityEngine.InputSystem.Keyboard keyboard = UnityEngine.InputSystem.Keyboard.current;
                    if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                        Close();
                }
                catch
                {
                    // 输入系统 / 关卡状态不可用时忽略
                }
            }
        }

        /// <summary>
        /// 面板关闭后的 F8 热键：按本局扫描结果重新打开面板。
        /// 离开战局（回基地 / 主菜单）时把结果作废，避免在基地里弹出上一局的列表。
        /// </summary>
        [DefaultExecutionOrder(10000)]
        private sealed class ScanHotkeyHost : MonoBehaviour
        {
            private void Update()
            {
                try
                {
                    LevelManager manager = LevelManager.Instance;
                    if (manager == null || !manager.IsRaidMap)
                    {
                        if (_lastGroups != null)
                            ClearLastResult();
                        return;
                    }

                    Key key = ModConfig.Current.ScanKey;
                    if (key == Key.None)
                        return;

                    Keyboard keyboard = Keyboard.current;
                    if (keyboard == null || keyboard[key] == null || !keyboard[key].wasPressedThisFrame)
                        return;

                    // 面板开着时，再按一次 = 关闭（快捷键要能双向切换）
                    if (IsOpen)
                    {
                        Close();
                        return;
                    }

                    // 死亡 / 撤离过程中不重开面板，避免挡住结算界面
                    CharacterMainControl main = CharacterMainControl.Main;
                    if (main == null || (main.Health != null && main.Health.IsDead))
                        return;

                    // 游戏设置 / 暂停菜单 / 其它界面打开时不呼出：
                    // 否则在 ModSetting 里改快捷键时按一下就会弹出扫描面板
                    if (IsGameUiOpen())
                        return;

                    ReopenLast();
                }
                catch
                {
                    // 输入系统不可用时忽略
                }
            }
        }

        /// <summary>
        /// 游戏 UI（暂停菜单 / 设置页 / 背包等）是否正在显示。
        /// 仅用于热键：这些界面开着时不呼出扫描面板，但面板已经开着时仍然允许用热键关闭。
        /// </summary>
        private static bool IsGameUiOpen()
        {
            try
            {
                PauseMenu pause = PauseMenu.Instance;
                if (pause != null && pause.Shown)
                    return true;

                if (Duckov.UI.View.ActiveView != null)
                    return true;

                // 设置页（含 ModSetting 的「模组设置」标签）：OptionsPanel 的 FadeGroup 正在显示
                UIPanel[] panels = UnityEngine.Object.FindObjectsOfType<UIPanel>();
                for (int i = 0; i < panels.Length; i++)
                {
                    UIPanel panel = panels[i];
                    if (panel == null)
                        continue;

                    if (panel.GetType().Name.IndexOf("Options", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    FadeGroup group = GetFadeGroup(panel);
                    if (group != null && group.IsShown)
                        return true;
                }
            }
            catch
            {
                // UI 状态取不到时按「没开」处理，宁可允许呼出
            }

            return false;
        }

        private static FadeGroup GetFadeGroup(UIPanel panel)
        {
            try
            {
                FieldInfo info = typeof(UIPanel).GetField(
                    "fadeGroup",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                return info != null ? info.GetValue(panel) as FadeGroup : null;
            }
            catch
            {
                return null;
            }
        }

        private static TMP_FontAsset ResolveFont()
        {
            TMP_FontAsset fallback = null;

            TextMeshProUGUI[] all = Resources.FindObjectsOfTypeAll<TextMeshProUGUI>();
            for (int i = 0; i < all.Length; i++)
            {
                TextMeshProUGUI text = all[i];
                if (text == null || text.font == null)
                    continue;

                if (fallback == null)
                    fallback = text.font;

                if (text.font.HasCharacter('法') && text.font.HasCharacter('宝'))
                    return text.font;
            }

            return fallback != null ? fallback : TMP_Settings.defaultFontAsset;
        }
    }
}
