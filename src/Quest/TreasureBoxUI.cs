using System;
using System.Collections.Generic;
using Duckov.UI;
using Duckov.UI.Animations;
using HarmonyLib;
using ItemStatsSystem;
using MagicTreasurePower.Treasures;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MagicTreasurePower.Quests
{
    /// <summary>
    /// 法宝宝箱的七选一界面。
    ///
    /// <para><b>布局：</b>仿主界面 MOD 列表——左侧竖排列表（图标 + 名称 + 效果摘要），
    /// 右侧详情面板（大图标 + 效果 + 背景故事 + 「选择」按钮）。</para>
    ///
    /// <para><b>背包保持打开：</b>展示界面时不关闭背包 / 容器界面，
    /// 否则鼠标会被重新锁定，无法点选。见 <see cref="TreasureBoxMenuUsePatch"/>。</para>
    ///
    /// <para>选择一件法宝后发放到背包（背包满则进基地仓库），并消耗一个宝箱；
    /// 直接关闭界面不消耗宝箱。</para>
    /// </summary>
    internal static class TreasureBoxUI
    {
        private static GameObject _root;
        private static Item _box;
        private static CursorLockMode _previousLock;
        private static bool _previousVisible;
        private static bool _cursorCaptured;

        private static TextMeshProUGUI _detailName;
        private static TextMeshProUGUI _detailEffect;
        private static TextMeshProUGUI _detailLore;
        private static Image _detailIcon;
        private static TextMeshProUGUI _confirmText;

        private static readonly List<Image> RowBackgrounds = new List<Image>();
        private static readonly List<GameObject> RowAccents = new List<GameObject>();

        private static int _selected = -1;

        /// <summary>本次界面实际列出的法宝（只放注册成功的，避免出现点了没反应的死选项）。</summary>
        private static readonly List<TreasureDefinition> Entries = new List<TreasureDefinition>();

        private static readonly Color Gold = new Color(0.91f, 0.75f, 0.36f, 1f);
        private static readonly Color GoldDim = new Color(0.66f, 0.54f, 0.26f, 1f);
        private static readonly Color PanelColor = new Color(0.07f, 0.08f, 0.09f, 0.98f);
        private static readonly Color RowColor = new Color(0.13f, 0.14f, 0.16f, 1f);
        private static readonly Color RowSelectedColor = new Color(0.22f, 0.19f, 0.11f, 1f);
        private static readonly Color DetailColor = new Color(0.10f, 0.11f, 0.13f, 1f);
        private static readonly Color TextMain = new Color(0.93f, 0.91f, 0.86f, 1f);
        private static readonly Color TextSub = new Color(0.62f, 0.63f, 0.67f, 1f);

        private const float PanelWidth = 980f;

        /// <summary>
        /// 面板高度。列表区可用高度 = 660 - 104 - 24 = 532，
        /// 7 行（7 × 70 + 6 × 4 = 514）刚好一屏；再多的法宝由列表里的 ScrollRect 滚动查看。
        /// </summary>
        private const float PanelHeight = 660f;

        private const float RowHeight = 70f;
        private const float RowSpacing = 4f;

        internal static bool IsOpen => _root != null;

        /// <summary>停用 mod 时强制关掉界面，避免留下一个无人管理的覆盖层。</summary>
        internal static void ForceClose()
        {
            if (_root == null)
                return;

            Close();
        }

        internal static void Open(Item box)
        {
            if (_root != null)
                return;

            _box = box;

            try
            {
                Build();
            }
            catch (Exception e)
            {
                Diag.Report("打开法宝宝箱界面失败", e);
                Close();
            }
        }

        private static void Build()
        {
            _previousLock = Cursor.lockState;
            _previousVisible = Cursor.visible;
            _cursorCaptured = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            RowBackgrounds.Clear();
            RowAccents.Clear();
            Entries.Clear();
            _selected = -1;

            // 只列出注册成功的法宝：ID 被其它 mod 抢占 / 注册失败的不会出现，
            // 否则点「选择」会拿到空白兜底物品或对方的物品
            for (int i = 0; i < TreasureCatalog.All.Length; i++)
            {
                TreasureDefinition definition = TreasureCatalog.All[i];
                if (TreasureRegistrar.IsRegistered(definition.TypeId))
                    Entries.Add(definition);
            }

            if (Entries.Count == 0)
                Diag.Warn("法宝宝箱：没有已注册的法宝可选，界面将为空");

            _root = new GameObject("MagicTreasurePower_BoxUI");
            UnityEngine.Object.DontDestroyOnLoad(_root);

            Canvas canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;

            CanvasScaler scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _root.AddComponent<GraphicRaycaster>();
            _root.AddComponent<BoxUIUpdater>();

            // 关掉角色输入：否则鼠标虽然显示出来，点击界面还会顺带开枪 / 移动。
            // 输入被阻断后 InputManager 每帧的 UpdateCursor 也会保持光标可见。
            InputManager.DisableInput(_root);

            BuildBackdrop();
            Transform panel = BuildPanel();
            TMP_FontAsset font = ResolveFont();

            BuildHeader(panel, font);
            BuildList(panel, font);
            BuildDetail(panel, font);

            // 默认选中第一件，右侧详情不会是空的
            SelectIndex(0);
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

        private static Transform BuildPanel()
        {
            GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(_root.transform, false);

            var rect = (RectTransform)panel.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(PanelWidth, PanelHeight);

            Image image = panel.GetComponent<Image>();
            image.sprite = UI.RoundedSprite.Get();
            image.type = Image.Type.Sliced;
            image.color = PanelColor;

            return panel.transform;
        }

        private static void BuildHeader(Transform panel, TMP_FontAsset font)
        {
            // 标题
            TextMeshProUGUI title = CreateText(panel, "Title", "法宝宝箱", 28f, Gold, font,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(28f, -56f), new Vector2(-140f, -16f));
            title.alignment = TextAlignmentOptions.Left;

            TextMeshProUGUI subtitle = CreateText(panel, "Subtitle", "选择一件法宝带走", 15f, TextSub, font,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(28f, -78f), new Vector2(-140f, -58f));
            subtitle.alignment = TextAlignmentOptions.Left;

            // 分隔线
            GameObject line = new GameObject("Divider", typeof(RectTransform), typeof(Image));
            line.transform.SetParent(panel, false);
            var lineRect = (RectTransform)line.transform;
            lineRect.anchorMin = new Vector2(0f, 1f);
            lineRect.anchorMax = new Vector2(1f, 1f);
            lineRect.pivot = new Vector2(0.5f, 1f);
            lineRect.offsetMin = new Vector2(26f, -92f);
            lineRect.offsetMax = new Vector2(-26f, -90f);
            line.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);
            line.GetComponent<Image>().raycastTarget = false;

            // 关闭按钮（圆角）
            GameObject close = new GameObject("Close", typeof(RectTransform), typeof(Image), typeof(Button));
            close.transform.SetParent(panel, false);
            var closeRect = (RectTransform)close.transform;
            closeRect.anchorMin = new Vector2(1f, 1f);
            closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.sizeDelta = new Vector2(76f, 34f);
            closeRect.anchoredPosition = new Vector2(-24f, -20f);

            Image closeImage = close.GetComponent<Image>();
            closeImage.sprite = UI.RoundedSprite.Get();
            closeImage.type = Image.Type.Sliced;
            closeImage.color = new Color(0.18f, 0.18f, 0.2f, 0.95f);

            Button closeButton = close.GetComponent<Button>();
            closeButton.transition = Selectable.Transition.None;
            closeButton.onClick.AddListener(Close);

            TextMeshProUGUI closeText = CreateText(close.transform, "Text", "关闭", 15f, TextMain, font,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            closeText.alignment = TextAlignmentOptions.Center;

            ConfigurePressAnimation(close, closeRect);
        }

        /// <summary>
        /// 左侧法宝列表。外面套一层 ScrollRect：现在 7 件刚好一屏，
        /// 以后加法宝（≤12 件）超出可视高度时可以直接滚轮 / 拖拽滚动，不用再改布局。
        /// </summary>
        private static void BuildList(Transform panel, TMP_FontAsset font)
        {
            GameObject scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect));
            scrollGo.transform.SetParent(panel, false);

            var scrollRect = (RectTransform)scrollGo.transform;
            scrollRect.anchorMin = new Vector2(0f, 0f);
            scrollRect.anchorMax = new Vector2(0.56f, 1f);
            scrollRect.offsetMin = new Vector2(26f, 24f);
            scrollRect.offsetMax = new Vector2(-6f, -104f);

            GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(scrollGo.transform, false);
            var viewportRect = (RectTransform)viewport.transform;
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;

            GameObject content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            var contentRect = (RectTransform)content.transform;
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.offsetMin = Vector2.zero;
            contentRect.offsetMax = Vector2.zero;

            int count = Entries.Count;
            float contentHeight = count > 0
                ? count * RowHeight + (count - 1) * RowSpacing
                : 0f;
            contentRect.sizeDelta = new Vector2(0f, contentHeight);

            ScrollRect scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.content = contentRect;
            scroll.viewport = viewportRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            for (int i = 0; i < count; i++)
                BuildRow(content.transform, Entries[i], i, font);
        }

        private static void BuildRow(Transform parent, TreasureDefinition definition, int index, TMP_FontAsset font)
        {
            GameObject row = new GameObject(
                "Row_" + definition.TypeId, typeof(RectTransform), typeof(Image), typeof(Button));
            row.transform.SetParent(parent, false);

            var rect = (RectTransform)row.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(0f, 0f);
            rect.offsetMax = new Vector2(0f, 0f);
            rect.sizeDelta = new Vector2(0f, RowHeight);
            rect.anchoredPosition = new Vector2(0f, -index * (RowHeight + RowSpacing));

            Image background = row.GetComponent<Image>();
            background.sprite = UI.RoundedSprite.Get();
            background.type = Image.Type.Sliced;
            background.color = RowColor;

            Button button = row.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = background;
            int captured = index;
            button.onClick.AddListener(() => SelectIndex(captured));

            RowBackgrounds.Add(background);

            // 左侧金色竖条（选中时显示）
            GameObject accent = new GameObject("Accent", typeof(RectTransform), typeof(Image));
            accent.transform.SetParent(row.transform, false);
            var accentRect = (RectTransform)accent.transform;
            accentRect.anchorMin = new Vector2(0f, 0f);
            accentRect.anchorMax = new Vector2(0f, 1f);
            accentRect.pivot = new Vector2(0f, 0.5f);
            accentRect.sizeDelta = new Vector2(4f, -16f);
            accentRect.anchoredPosition = new Vector2(2f, 0f);
            accent.GetComponent<Image>().sprite = UI.RoundedSprite.Get();
            accent.GetComponent<Image>().type = Image.Type.Sliced;
            accent.GetComponent<Image>().color = Gold;
            accent.GetComponent<Image>().raycastTarget = false;
            accent.SetActive(false);
            RowAccents.Add(accent);

            // 图标
            Sprite icon = TreasureRegistrar.GetIcon(definition.TypeId);
            if (icon != null)
            {
                GameObject iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                iconGo.transform.SetParent(row.transform, false);
                var iconRect = (RectTransform)iconGo.transform;
                iconRect.anchorMin = new Vector2(0f, 0.5f);
                iconRect.anchorMax = new Vector2(0f, 0.5f);
                iconRect.pivot = new Vector2(0f, 0.5f);
                iconRect.sizeDelta = new Vector2(56f, 56f);
                iconRect.anchoredPosition = new Vector2(16f, 0f);

                Image iconImage = iconGo.GetComponent<Image>();
                iconImage.sprite = icon;
                iconImage.preserveAspect = true;
                iconImage.raycastTarget = false;
            }

            // 名称
            TextMeshProUGUI name = CreateText(row.transform, "Name", definition.DisplayName,
                20f, Gold, font, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(86f, -44f), new Vector2(-16f, -10f));
            name.alignment = TextAlignmentOptions.Left;
            name.overflowMode = TextOverflowModes.Ellipsis;

            // 效果摘要（单行省略）
            TextMeshProUGUI summary = CreateText(row.transform, "Summary", EffectTextOf(definition),
                13f, TextSub, font, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(86f, 10f), new Vector2(-16f, 34f));
            summary.alignment = TextAlignmentOptions.Left;
            summary.overflowMode = TextOverflowModes.Ellipsis;

            // 右侧箭头
            TextMeshProUGUI arrow = CreateText(row.transform, "Arrow", "›", 22f, GoldDim, font,
                new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-28f, 0f), new Vector2(-8f, 0f));
            arrow.alignment = TextAlignmentOptions.Center;
        }

        private static void BuildDetail(Transform panel, TMP_FontAsset font)
        {
            GameObject detail = new GameObject("Detail", typeof(RectTransform), typeof(Image));
            detail.transform.SetParent(panel, false);

            var detailRect = (RectTransform)detail.transform;
            detailRect.anchorMin = new Vector2(0.56f, 0f);
            detailRect.anchorMax = new Vector2(1f, 1f);
            detailRect.offsetMin = new Vector2(6f, 24f);
            detailRect.offsetMax = new Vector2(-26f, -104f);

            Image detailImage = detail.GetComponent<Image>();
            detailImage.sprite = UI.RoundedSprite.Get();
            detailImage.type = Image.Type.Sliced;
            detailImage.color = DetailColor;

            // 大图标
            GameObject iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(detail.transform, false);
            var iconRect = (RectTransform)iconGo.transform;
            iconRect.anchorMin = new Vector2(0.5f, 1f);
            iconRect.anchorMax = new Vector2(0.5f, 1f);
            iconRect.pivot = new Vector2(0.5f, 1f);
            iconRect.sizeDelta = new Vector2(132f, 132f);
            iconRect.anchoredPosition = new Vector2(0f, -20f);

            _detailIcon = iconGo.GetComponent<Image>();
            _detailIcon.preserveAspect = true;
            _detailIcon.raycastTarget = false;
            _detailIcon.enabled = false;

            // 名称
            _detailName = CreateText(detail.transform, "Name", string.Empty, 24f, Gold, font,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20f, -190f), new Vector2(-20f, -158f));
            _detailName.alignment = TextAlignmentOptions.Center;

            // 效果
            _detailEffect = CreateText(detail.transform, "Effect", string.Empty, 15f, TextMain, font,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20f, -300f), new Vector2(-20f, -196f));
            _detailEffect.alignment = TextAlignmentOptions.TopLeft;
            _detailEffect.enableWordWrapping = true;
            _detailEffect.overflowMode = TextOverflowModes.Overflow;

            // 背景故事
            _detailLore = CreateText(detail.transform, "Lore", string.Empty, 13f, TextSub, font,
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(20f, 88f), new Vector2(-20f, -304f));
            _detailLore.alignment = TextAlignmentOptions.TopLeft;
            _detailLore.enableWordWrapping = true;
            _detailLore.overflowMode = TextOverflowModes.Overflow;

            // 选择按钮
            GameObject confirm = new GameObject("Confirm", typeof(RectTransform), typeof(Image), typeof(Button));
            confirm.transform.SetParent(detail.transform, false);
            var confirmRect = (RectTransform)confirm.transform;
            confirmRect.anchorMin = new Vector2(0f, 0f);
            confirmRect.anchorMax = new Vector2(1f, 0f);
            confirmRect.pivot = new Vector2(0.5f, 0f);
            confirmRect.offsetMin = new Vector2(20f, 20f);
            confirmRect.offsetMax = new Vector2(-20f, 72f);

            Image confirmImage = confirm.GetComponent<Image>();
            confirmImage.sprite = UI.RoundedSprite.Get();
            confirmImage.type = Image.Type.Sliced;
            confirmImage.color = new Color(0.55f, 0.43f, 0.16f, 1f);

            Button confirmButton = confirm.GetComponent<Button>();
            confirmButton.transition = Selectable.Transition.ColorTint;
            confirmButton.onClick.AddListener(ConfirmSelection);

            _confirmText = CreateText(confirm.transform, "Text", "选择", 18f, TextMain, font,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            _confirmText.alignment = TextAlignmentOptions.Center;

            ConfigurePressAnimation(confirm, confirmRect);
        }

        /// <summary>
        /// 复刻原版「使用」按钮（<c>ItemOperationMenu/.../Btn_Use/BG</c>）的按下动画：
        /// <c>SizeDeltaToggle</c>，idle → idle + (-8, -8)，0.1 秒，带 1.53 倍过冲回弹的原版曲线。
        /// 参数直接取自预制体序列化数据。
        /// </summary>
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
                Diag.Report("法宝宝箱：添加按钮按下动画失败", e);
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

            /// <summary>按住后把指针移出按钮时也要复位，避免卡在按下状态。</summary>
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
                    Diag.Report("法宝宝箱：播放按钮按下动画失败", e);
                }
            }
        }

        /// <summary>
        /// 宝箱发出来的法宝是 Lv.1，界面必须显示 Lv.1 的数值——
        /// 用 TreasureCatalog.EffectText 会显示 Lv.7 的满级数值，和到手后不一样。
        /// </summary>
        private static string EffectTextOf(TreasureDefinition definition)
        {
            string text = TreasureBalance.FormatEffectText(
                definition.TypeId, TreasureProgression.DefaultLevel);

            return string.IsNullOrEmpty(text) ? definition.EffectText : text;
        }

        private static void SelectIndex(int index)
        {
            if (index < 0 || index >= Entries.Count)
                return;

            _selected = index;

            for (int i = 0; i < RowBackgrounds.Count; i++)
            {
                bool selected = i == index;
                RowBackgrounds[i].color = selected ? RowSelectedColor : RowColor;
                if (i < RowAccents.Count)
                    RowAccents[i].SetActive(selected);
            }

            TreasureDefinition definition = Entries[index];

            if (_detailName != null)
                _detailName.text = definition.DisplayName;

            if (_detailEffect != null)
                _detailEffect.text = EffectTextOf(definition);

            if (_detailLore != null)
                _detailLore.text = definition.Description;

            if (_detailIcon != null)
            {
                // 没有图标时把 Image 关掉：Image 的 sprite 为空会渲染成一个白方块
                Sprite icon = TreasureRegistrar.GetIcon(definition.TypeId);
                _detailIcon.sprite = icon;
                _detailIcon.enabled = icon != null;
            }

            if (_confirmText != null)
                _confirmText.text = "选择 · " + definition.DisplayName;
        }

        private static void ConfirmSelection()
        {
            if (_selected < 0 || _selected >= Entries.Count)
                return;

            Select(Entries[_selected].TypeId);
        }

        private static void Select(int typeId)
        {
            try
            {
                Item granted = ItemAssetsCollection.InstantiateSync(typeId);

                // InstantiateSync 在物品查不到时会返回兜底物品（不为 null），
                // 必须先确认 TypeID 对得上，否则会发出一个无效物品。
                if (granted == null || granted.TypeID != typeId)
                {
                    Diag.Warn("法宝宝箱：生成法宝失败 " + typeId);
                    return;
                }

                // 只有确实发放成功才消耗宝箱，避免扣了箱子却没拿到东西
                if (!GiveToPlayer(granted))
                {
                    Diag.Warn("法宝宝箱：发放失败，宝箱未消耗 " + typeId);
                    return;
                }

                ConsumeBox();
                Close();
            }
            catch (Exception e)
            {
                Diag.Report("法宝宝箱选择法宝失败", e);
            }
        }

        /// <summary>把法宝发给玩家；返回是否成功（失败时不能消耗宝箱）。</summary>
        private static bool GiveToPlayer(Item item)
        {
            try
            {
                CharacterMainControl main = CharacterMainControl.Main;
                Inventory inventory = main != null && main.CharacterItem != null
                    ? main.CharacterItem.Inventory
                    : null;

                if (inventory != null && inventory.AddItem(item))
                    return true;

                // 背包放不下时进基地仓库，避免物品丢失
                PlayerStorage.Push(item, toBufferDirectly: true);
                return true;
            }
            catch (Exception e)
            {
                Diag.Report("法宝宝箱：发放法宝失败", e);
                return false;
            }
        }

        private static void ConsumeBox()
        {
            try
            {
                if (_box == null)
                    return;

                // 宝箱不可堆叠：直接销毁；可堆叠时扣 1，扣到 0 再销毁
                if (_box.Stackable)
                {
                    _box.StackCount = Mathf.Max(0, _box.StackCount - 1);
                    if (_box.StackCount > 0)
                        return;
                }

                _box.DestroyTree();
            }
            catch (Exception e)
            {
                Diag.Report("法宝宝箱：消耗失败", e);
            }
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

                _box = null;
                _detailName = null;
                _detailEffect = null;
                _detailLore = null;
                _detailIcon = null;
                _confirmText = null;
                RowBackgrounds.Clear();
                RowAccents.Clear();
                _selected = -1;

                // 只有确实接管过鼠标时才恢复，避免构建失败时把鼠标状态改坏
                if (_cursorCaptured)
                {
                    Cursor.lockState = _previousLock;
                    Cursor.visible = _previousVisible;
                    _cursorCaptured = false;
                }
            }
            catch (Exception e)
            {
                Diag.Report("关闭法宝宝箱界面失败", e);
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
        /// 覆盖层的每帧维护：
        /// <list type="bullet">
        ///   <item>光标强制可见 / 不锁定——原版 InputManager 每帧会按「有没有 View」重设光标；</item>
        ///   <item>按 Esc 关闭（游戏用新 Input System，不能用 UnityEngine.Input）；</item>
        ///   <item>关卡已销毁（回主菜单 / 换图）时自动关闭，避免遮罩永久残留。</item>
        /// </list>
        /// </summary>
        [DefaultExecutionOrder(10000)]
        private sealed class BoxUIUpdater : MonoBehaviour
        {
            private void LateUpdate()
            {
                try
                {
                    if (LevelManager.Instance == null)
                    {
                        Close();
                        return;
                    }

                    Cursor.visible = true;
                    Cursor.lockState = CursorLockMode.None;

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

    /// <summary>
    /// 右键菜单点「使用」时接管法宝宝箱：只关右键菜单，<b>不关背包</b>。
    ///
    /// 原版 <c>ItemOperationMenu.Use()</c> 是「UseItem → InventoryView.Hide() → Close()」，
    /// 背包一关鼠标就被锁回去，宝箱界面根本点不了。
    /// </summary>
    [HarmonyPatch(typeof(ItemOperationMenu), "Use")]
    internal static class TreasureBoxMenuUsePatch
    {
        private static System.Reflection.PropertyInfo _targetItemProperty;

        [HarmonyPrefix]
        private static bool Prefix(ItemOperationMenu __instance)
        {
            try
            {
                Item target = GetTargetItem(__instance);
                if (!TreasureBoxItem.IsOurBox(target))
                    return true;

                TreasureBoxUI.Open(target);

                // 只收起右键菜单；背包 / 容器界面保持原样
                AccessTools.Method(typeof(ItemOperationMenu), "Close")?.Invoke(__instance, null);
                return false;
            }
            catch (Exception e)
            {
                Diag.Report("法宝宝箱：接管右键使用失败", e);
                return true;
            }
        }

        private static Item GetTargetItem(ItemOperationMenu menu)
        {
            if (_targetItemProperty == null)
            {
                _targetItemProperty = AccessTools.Property(
                    typeof(ItemOperationMenu), "TargetItem");
            }

            return _targetItemProperty?.GetValue(menu, null) as Item;
        }
    }
}
