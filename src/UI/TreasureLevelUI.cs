using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Duckov.UI;
using HarmonyLib;
using ItemStatsSystem;
using MagicTreasurePower.Treasures;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MagicTreasurePower.UI
{
    /// <summary>
    /// 法宝详情页的等级展示：
    ///   - 图标左上角一个等级徽标（Lv.N）；
    ///   - 属性区里**克隆原版耐久度条**，改成法宝经验条（同样的版式与配色风格）。
    ///
    /// 挂在原版 <c>ItemDetailsDisplay.Setup(Item)</c> 后置上，每次选中物品都会刷新；
    /// 非法宝会把这两个节点隐藏。
    /// </summary>
    [HarmonyPatch(typeof(ItemDetailsDisplay), "Setup")]
    internal static class TreasureLevelDetailsPatch
    {
        private static readonly ConditionalWeakTable<ItemDetailsDisplay, LevelView> Views =
            new ConditionalWeakTable<ItemDetailsDisplay, LevelView>();

        private static LevelView _lastView;
        private static Item _lastItem;

        [HarmonyPostfix]
        private static void AfterSetup(ItemDetailsDisplay __instance, Item target)
        {
            try
            {
                if (__instance == null)
                    return;

                LevelView view;
                if (!Views.TryGetValue(__instance, out view))
                {
                    view = LevelView.Create(__instance);
                    if (view == null)
                        return;

                    Views.Add(__instance, view);
                }

                view.Refresh(target);

                _lastView = view;
                _lastItem = target;
            }
            catch (Exception e)
            {
                Diag.Report("法宝等级界面刷新失败", e);
            }
        }

        /// <summary>经验变化时刷新正在显示的详情页（没有显示该物品则忽略）。</summary>
        internal static void RefreshIfShowing(Item item)
        {
            try
            {
                if (item == null || _lastView == null || _lastItem != item)
                    return;

                TreasureBalance.ApplyEffectText(item);
                _lastView.Refresh(item);
            }
            catch (Exception e)
            {
                Diag.Report("刷新法宝经验条失败", e);
            }
        }
    }

    /// <summary>一个详情面板对应的等级节点（徽标 + 经验条）。</summary>
    internal sealed class LevelView
    {
        private static readonly Color Gold = new Color(0.91f, 0.75f, 0.36f, 1f);
        private static readonly Color TextColor = new Color(0.93f, 0.92f, 0.88f, 1f);

        private static readonly FieldInfo IconField = typeof(ItemDetailsDisplay).GetField(
            "icon", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo NameField = typeof(ItemDetailsDisplay).GetField(
            "displayName", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo DurabilityContainerField = typeof(ItemDetailsDisplay).GetField(
            "durabilityContainer", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo DurabilityTextField = typeof(ItemDetailsDisplay).GetField(
            "durabilityText", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo DurabilityFillField = typeof(ItemDetailsDisplay).GetField(
            "durabilityFill", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo DurabilityLossField = typeof(ItemDetailsDisplay).GetField(
            "durabilityLoss", BindingFlags.Instance | BindingFlags.NonPublic);

        private GameObject _badge;
        private TextMeshProUGUI _badgeText;

        private GameObject _bar;
        private TextMeshProUGUI _barLabel;
        private TextMeshProUGUI _barValue;
        private Image _barFill;
        private Image _barLoss;

        internal static LevelView Create(ItemDetailsDisplay panel)
        {
            try
            {
                var view = new LevelView();

                Image icon = GetField(IconField, panel) as Image;
                TextMeshProUGUI nameText = GetField(NameField, panel) as TextMeshProUGUI;
                TMP_FontAsset font = nameText != null && nameText.font != null
                    ? nameText.font
                    : TMP_Settings.defaultFontAsset;

                if (icon != null)
                    view.BuildBadge(icon, font);

                view.BuildExperienceBar(panel);

                if (view._badge == null && view._bar == null)
                    return null;

                return view;
            }
            catch (Exception e)
            {
                Diag.Report("创建法宝等级节点失败", e);
                return null;
            }
        }

        internal void Refresh(Item item)
        {
            bool treasure = TreasureProgression.IsTreasure(item);

            if (_badge != null)
                _badge.SetActive(treasure);
            if (_bar != null)
                _bar.SetActive(treasure);

            if (!treasure)
                return;

            int level = TreasureProgression.GetLevel(item);
            int experience = TreasureProgression.GetExp(item);
            int required = TreasureProgression.GetRequiredExp(level);

            if (_badgeText != null)
                _badgeText.text = "Lv." + level;

            if (_barLabel != null)
                _barLabel.text = "法宝经验";

            bool maxed = level >= TreasureProgression.MaxLevel;

            if (_barValue != null)
                _barValue.text = maxed ? "MAX" : experience + " / " + required;

            if (_barFill != null)
            {
                _barFill.fillAmount = maxed
                    ? 1f
                    : (required > 0 ? Mathf.Clamp01((float)experience / required) : 1f);
                _barFill.color = Gold;
            }

            if (_barLoss != null)
                _barLoss.enabled = false;
        }

        private void BuildBadge(Image icon, TMP_FontAsset font)
        {
            _badge = new GameObject("MagicTreasureLevelBadge", typeof(RectTransform), typeof(Image));
            _badge.transform.SetParent(icon.rectTransform, false);

            var rt = (RectTransform)_badge.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(46f, 22f);
            rt.anchoredPosition = new Vector2(3f, -3f);

            Image background = _badge.GetComponent<Image>();
            background.sprite = RoundedSprite.Get();
            background.type = Image.Type.Sliced;
            background.color = new Color(0.05f, 0.06f, 0.07f, 0.85f);
            background.raycastTarget = false;

            _badgeText = CreateText(_badge.transform, "Text", "Lv.1", 15f, Gold, font);
            StretchFull(_badgeText.rectTransform);
            _badgeText.fontStyle = FontStyles.Bold;
            _badgeText.alignment = TextAlignmentOptions.Center;
        }

        /// <summary>
        /// 克隆原版耐久度条，改成经验条：
        /// 版式、字体、背景都直接复用原版资源，只替换文字和填充值。
        /// </summary>
        private void BuildExperienceBar(ItemDetailsDisplay panel)
        {
            GameObject container = GetField(DurabilityContainerField, panel) as GameObject;
            TextMeshProUGUI durabilityText = GetField(DurabilityTextField, panel) as TextMeshProUGUI;
            Image durabilityFill = GetField(DurabilityFillField, panel) as Image;
            Image durabilityLoss = GetField(DurabilityLossField, panel) as Image;

            if (container == null || container.transform.parent == null)
            {
                Diag.Warn("详情面板缺少耐久度条，法宝经验条无法克隆");
                return;
            }

            Transform parent = container.transform.parent;
            _bar = UnityEngine.Object.Instantiate(container, parent);
            _bar.name = "MagicTreasureExpBar";
            _bar.SetActive(true);
            _bar.transform.SetSiblingIndex(container.transform.GetSiblingIndex() + 1);

            // 按相对路径找回克隆出来的「数值文本 / 填充条 / 损失条」
            if (durabilityText != null)
            {
                string path = RelativePath(container.transform, durabilityText.transform);
                Transform node = _bar.transform.Find(path);
                if (node != null)
                    _barValue = node.GetComponent<TextMeshProUGUI>();
            }

            if (durabilityFill != null)
            {
                string path = RelativePath(container.transform, durabilityFill.transform);
                Transform node = _bar.transform.Find(path);
                if (node != null)
                    _barFill = node.GetComponent<Image>();
            }

            if (durabilityLoss != null)
            {
                string path = RelativePath(container.transform, durabilityLoss.transform);
                Transform node = _bar.transform.Find(path);
                if (node != null)
                    _barLoss = node.GetComponent<Image>();
            }

            // 找一个不是数值文本的 TMP 当标签（原版耐久度条的「耐久度」）
            TextMeshProUGUI[] texts = _bar.GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (TextMeshProUGUI text in texts)
            {
                if (text == null || text == _barValue)
                    continue;

                _barLabel = text;
                break;
            }

            if (_barValue == null && _barLabel != null)
            {
                _barValue = _barLabel;
                _barLabel = null;
            }

            if (_barLabel != null)
                _barLabel.text = "法宝经验";

            if (_barValue != null)
                _barValue.color = TextColor;

            // 经验条不需要原版耐久度的悬停提示
            foreach (MonoBehaviour behaviour in _bar.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour != null &&
                    behaviour.GetType().Name.IndexOf("Tooltip", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    behaviour.enabled = false;
                }
            }
        }

        private static object GetField(FieldInfo field, object target)
        {
            if (field == null || target == null)
                return null;

            try
            {
                return field.GetValue(target);
            }
            catch
            {
                return null;
            }
        }

        private static string RelativePath(Transform root, Transform child)
        {
            if (root == null || child == null || child == root)
                return string.Empty;

            string path = child.name;
            Transform node = child.parent;
            while (node != null && node != root)
            {
                path = node.name + "/" + path;
                node = node.parent;
            }

            return path;
        }

        private static TextMeshProUGUI CreateText(
            Transform parent, string name, string text, float size, Color color, TMP_FontAsset font)
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

            return tmp;
        }

        private static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }

    /// <summary>运行时生成的白色圆角九宫格贴图（由 Image.color 着色）。</summary>
    internal static class RoundedSprite
    {
        private static Sprite _sprite;

        internal static Sprite Get()
        {
            if (_sprite != null)
                return _sprite;

            const int size = 32;
            const int radius = 8;

            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float half = size * 0.5f;
                    float inner = half - radius;
                    float dx = Mathf.Max(Mathf.Abs(x + 0.5f - half) - inner, 0f);
                    float dy = Mathf.Max(Mathf.Abs(y + 0.5f - half) - inner, 0f);
                    float distance = Mathf.Sqrt(dx * dx + dy * dy) - radius;
                    float alpha = Mathf.Clamp01(-distance + 0.5f);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();

            GameObject holder = new GameObject("MagicTreasurePower_RoundedSprite");
            UnityEngine.Object.DontDestroyOnLoad(holder);
            holder.AddComponent<SpriteHolder>().Texture = texture;

            _sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(radius, radius, radius, radius));

            return _sprite;
        }

        private sealed class SpriteHolder : MonoBehaviour
        {
            internal Texture2D Texture;
        }
    }
}
