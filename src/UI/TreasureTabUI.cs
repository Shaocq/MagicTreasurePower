using System;
using System.Collections.Generic;
using Duckov.UI;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using SodaCraft.Localizations;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MagicTreasurePower.UI
{
    /// <summary>
    /// 装备页（左上角 LootView 左栏）的「装备 / 法宝」页签。
    ///
    /// <para><b>外观来源：</b>全部复用原版资源——</para>
    /// <list type="bullet">
    ///   <item>页签文字克隆原版横幅里的「装备」TMP 文本，字体、字号、对齐完全一致；</item>
    ///   <item>法宝页签的槽位面板克隆原版 `ItemSlotsDisplay`（GridLayoutGroup 尺寸、间距、
    ///         内边距、LayoutElement 全部保留），只把里面的模板格子换成从原版
    ///         <see cref="SlotDisplay"/> 对象池取出的真实格子。</item>
    /// </list>
    ///
    /// <para><b>切换：</b>点「装备」显示原版槽位面板并隐藏法宝面板；点「法宝」反之。
    /// 当前页签文字不透明，另一页签文字与原版标题一样是 50% 透明度。</para>
    /// </summary>
    internal sealed class TreasureTabUI : MonoBehaviour
    {
        private const string EquipmentPanelName = "ItemSlotsDisplay";
        private const string TreasurePanelName = "MagicTreasureSlots";
        private const string TreasureTabName = "MagicTreasureTab";
        private const string TitleBarPath = "Main/EquipmentAndInventory/Content/TitleBar";

        /// <summary>原版标题文字的透明度（原版就是 0.5）。</summary>
        private const float InactiveAlpha = 0.5f;
        private const float ActiveAlpha = 1f;

        private LootView _view;
        private bool _built;
        private bool _buildFailed;
        private bool _treasureActive;

        private GameObject _equipmentPanel;
        private GameObject _treasurePanel;
        private Transform _grid;
        private TextMeshProUGUI _equipmentLabel;
        private TextMeshProUGUI _treasureLabel;

        private Item _boundCharacterItem;
        private readonly List<SlotDisplay> _cells = new List<SlotDisplay>();

        /// <summary>在 LootView 打开后调用：首次构建页签，之后只刷新绑定。</summary>
        internal static void EnsureAttached(LootView view)
        {
            if (view == null)
                return;

            try
            {
                TreasureTabUI ui = view.GetComponent<TreasureTabUI>();
                if (ui == null)
                    ui = view.gameObject.AddComponent<TreasureTabUI>();

                ui.EnsureBuilt(view);
                ui.Refresh();
            }
            catch (Exception e)
            {
                Diag.Report("挂接「法宝」页签失败", e);
            }
        }

        /// <summary>构建页签与法宝槽位面板（只构建一次）。</summary>
        private void EnsureBuilt(LootView view)
        {
            _view = view;

            if (_built || _buildFailed)
                return;

            // 明确按原版层级定位：Main/EquipmentAndInventory/Content/ItemSlotsDisplay。
            // 不能随便找 ItemSlotCollectionDisplay —— 详情面板里也有一个，会认错。
            Transform content = view.transform.Find("Main/EquipmentAndInventory/Content");
            Transform equipmentPanelTransform = content != null
                ? content.Find(EquipmentPanelName)
                : null;

            if (equipmentPanelTransform == null)
            {
                // 兜底：直接读 LootView 的私有字段 characterSlotCollectionDisplay
                System.Reflection.FieldInfo field = typeof(LootView).GetField(
                    "characterSlotCollectionDisplay",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                ItemSlotCollectionDisplay display =
                    field?.GetValue(view) as ItemSlotCollectionDisplay;
                if (display != null)
                {
                    equipmentPanelTransform = display.transform;
                    content = display.transform.parent;
                }
            }

            if (equipmentPanelTransform != null)
                _equipmentPanel = equipmentPanelTransform.gameObject;

            if (content == null || _equipmentPanel == null)
            {
                _buildFailed = true;
                Diag.Warn("未找到装备页的槽位面板，法宝页签未创建");
                return;
            }

            Transform titleBar = content.Find("TitleBar");
            if (titleBar == null)
            {
                _buildFailed = true;
                Diag.Warn("未找到装备页横幅（Content/TitleBar），法宝页签未创建");
                return;
            }

            TextMeshProUGUI vanillaLabel = FindTitleLabel(titleBar);
            if (vanillaLabel == null)
            {
                _buildFailed = true;
                Diag.Warn("装备页横幅里没有找到标题文字，法宝页签未创建");
                return;
            }

            // 横幅是横向布局，加一点间距让两个页签分开
            HorizontalLayoutGroup titleLayout = titleBar.GetComponent<HorizontalLayoutGroup>();
            if (titleLayout != null && titleLayout.spacing < 24f)
                titleLayout.spacing = 24f;

            _equipmentLabel = vanillaLabel;

            // 克隆原版标题文字作为「法宝」页签
            GameObject treasureLabelObject =
                UnityEngine.Object.Instantiate(vanillaLabel.gameObject, titleBar);
            treasureLabelObject.name = TreasureTabName;
            treasureLabelObject.transform.SetAsLastSibling();
            _treasureLabel = treasureLabelObject.GetComponent<TextMeshProUGUI>();

            // 克隆体上带着原版本地化组件（key = UI_Loot_Equipment），
            // 不禁用的话会把文字刷回「装备」
            TextLocalizor copiedLocalizor = treasureLabelObject.GetComponent<TextLocalizor>();
            if (copiedLocalizor != null)
            {
                copiedLocalizor.enabled = false;
                Destroy(copiedLocalizor);
            }

            _treasureLabel.text = Slots.TreasureSlots.DisplayName;

            TreasureTabButton equipmentButton =
                vanillaLabel.gameObject.AddComponent<TreasureTabButton>();
            equipmentButton.Initialize(this, false);

            TreasureTabButton treasureButton =
                treasureLabelObject.AddComponent<TreasureTabButton>();
            treasureButton.Initialize(this, true);

            // 复制原版槽位面板：布局、尺寸、网格参数全部照搬
            GameObject treasurePanel = UnityEngine.Object.Instantiate(_equipmentPanel, content);
            treasurePanel.name = TreasurePanelName;

            // 关键：Content 上的 VerticalLayoutGroup 按子物体顺序排版。
            // Instantiate 默认追加到末尾，会排到 Scroll View（背包）后面，
            // 法宝页签看起来就是「空的一块，下面直接是背包」。
            // 这里把它插到原版槽位面板所在的布局位置。
            treasurePanel.transform.SetSiblingIndex(_equipmentPanel.transform.GetSiblingIndex());

            // 原版槽位刷新组件只服务于原版槽位列表，这里只借用布局，移除掉它
            ItemSlotCollectionDisplay copiedCollection =
                treasurePanel.GetComponent<ItemSlotCollectionDisplay>();
            if (copiedCollection != null)
                Destroy(copiedCollection);

            Transform grid = treasurePanel.transform.Find("GridLayout");
            if (grid == null)
                grid = treasurePanel.transform;
            _grid = grid;

            // 法宝共 6 个槽位，排成 3 列 × 2 行：
            // 网格本身是原版装备面板的克隆，这里只改约束，
            // 格子尺寸与间距仍沿用原版，外观保持一致。
            GridLayoutGroup gridLayout = grid.GetComponent<GridLayoutGroup>();
            if (gridLayout == null)
                gridLayout = grid.GetComponentInChildren<GridLayoutGroup>(true);

            if (gridLayout != null)
            {
                gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                gridLayout.constraintCount = 3;
            }

            // 清掉复制出来的模板格子（原版里是隐藏的示例对象）
            for (int i = grid.childCount - 1; i >= 0; i--)
                Destroy(grid.GetChild(i).gameObject);

            _treasurePanel = treasurePanel;
            _treasurePanel.SetActive(false);

            _built = true;
            Diag.Log("装备页「法宝」页签已创建");
        }

        /// <summary>在横幅里找到原版标题文字（「装备」）。</summary>
        private static TextMeshProUGUI FindTitleLabel(Transform titleBar)
        {
            for (int i = 0; i < titleBar.childCount; i++)
            {
                TextMeshProUGUI label = titleBar.GetChild(i).GetComponent<TextMeshProUGUI>();
                if (label != null)
                    return label;
            }

            return null;
        }

        /// <summary>确保槽位存在，并把格子绑定到当前角色的法宝槽位上。</summary>
        private void Refresh()
        {
            // 场景/预制体重载后，克隆出来的面板可能已经被销毁，需要重建
            if (!_built || _equipmentPanel == null || _treasurePanel == null || _grid == null)
            {
                _built = false;
                _buildFailed = false;
                if (_view == null)
                    return;
                EnsureBuilt(_view);
                if (!_built)
                    return;
            }

            try
            {
                if (_treasureActive)
                    RefreshTreasurePanel();

                ApplyTabVisual();
                ForceLayoutRebuild();
            }
            catch (Exception e)
            {
                Diag.Report("刷新法宝页签失败", e);
            }
        }

        /// <summary>法宝页签里的格子：确保槽位存在并绑定格子。</summary>
        private void RefreshTreasurePanel()
        {
            List<Slot> slots = Slots.TreasureSlots.EnsureSlots();
            if (slots == null)
            {
                Diag.Warn("法宝槽位不可用（主角色的角色物品尚未就绪），本次跳过刷新");
                return;
            }

            CharacterMainControl main = CharacterMainControl.Main;
            Item characterItem = main != null ? main.CharacterItem : null;

            if (_boundCharacterItem != characterItem || !CellsMatch(slots))
            {
                RebindCells(slots);
                _boundCharacterItem = characterItem;
            }
        }

        /// <summary>
        /// 让 Content 的纵向布局立刻重算：面板激活 / 隐藏、格子增删后，
        /// 父级布局不会自动在同一帧内给出正确高度，需要手动重建。
        /// </summary>
        private void ForceLayoutRebuild()
        {
            if (_treasurePanel != null)
            {
                RectTransform panelRect = _treasurePanel.transform as RectTransform;
                if (panelRect != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
            }

            if (_equipmentPanel != null)
            {
                RectTransform parentRect = _equipmentPanel.transform.parent as RectTransform;
                if (parentRect != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(parentRect);
            }
        }

        private bool CellsMatch(List<Slot> slots)
        {
            if (_cells.Count != slots.Count)
                return false;

            for (int i = 0; i < slots.Count; i++)
            {
                if (_cells[i] == null || _cells[i].Target != slots[i])
                    return false;
            }

            return true;
        }

        /// <summary>用原版 SlotDisplay 对象池重建全部格子，外观与原版装备格完全一致。</summary>
        private void RebindCells(List<Slot> slots)
        {
            for (int i = 0; i < _cells.Count; i++)
            {
                if (_cells[i] != null)
                    SlotDisplay.Release(_cells[i]);
            }

            _cells.Clear();

            if (_grid == null)
                return;

            foreach (Slot slot in slots)
            {
                SlotDisplay display = SlotDisplay.Get();
                if (display == null)
                    continue;

                display.Setup(slot);
                display.Editable = true;
                display.ContentSelectable = true;
                display.ShowOperationMenu = true;
                display.Movable = true;
                display.transform.SetParent(_grid, false);
                display.transform.localScale = Vector3.one;
                display.transform.SetAsLastSibling();
                display.gameObject.SetActive(true);
                _cells.Add(display);
            }

            Diag.Log($"法宝页签：已绑定 {_cells.Count} 个格子（槽位：{string.Join("、", slots.ConvertAll(s => s.DisplayName).ToArray())}）");

            ForceLayoutRebuild();
        }

        /// <summary>页签切换。</summary>
        internal void SwitchTo(bool treasure)
        {
            if (!_built || _treasureActive == treasure)
                return;

            _treasureActive = treasure;

            try
            {
                // 隐藏页里的物品不应该继续占用详情面板
                ItemUIUtilities.Select(null);
            }
            catch (Exception)
            {
                // 选中状态清理失败不影响切换
            }

            // 先切换面板显隐：格子要在激活的面板里创建 / 显示，
            // 这样 SlotDisplay 的 OnEnable 才会注册拖放、点击等事件。
            ApplyTabVisual();

            if (treasure)
                RefreshTreasurePanel();

            ForceLayoutRebuild();
        }

        private void ApplyTabVisual()
        {
            if (!_built)
                return;

            if (_equipmentPanel != null)
                _equipmentPanel.SetActive(!_treasureActive);
            if (_treasurePanel != null)
                _treasurePanel.SetActive(_treasureActive);

            SetLabelActive(_equipmentLabel, !_treasureActive);
            SetLabelActive(_treasureLabel, _treasureActive);
        }

        private static void SetLabelActive(TextMeshProUGUI label, bool active)
        {
            if (label == null)
                return;

            Color color = label.color;
            color.a = active ? ActiveAlpha : InactiveAlpha;
            label.color = color;
        }
    }

    /// <summary>页签点击响应（挂在原版标题文字与克隆出来的文字上）。</summary>
    internal sealed class TreasureTabButton : MonoBehaviour, IPointerClickHandler
    {
        private TreasureTabUI _owner;
        private bool _treasure;

        internal void Initialize(TreasureTabUI owner, bool treasure)
        {
            _owner = owner;
            _treasure = treasure;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData != null && eventData.button != PointerEventData.InputButton.Left)
                return;

            _owner?.SwitchTo(_treasure);
        }
    }
}
