using System;
using Duckov;
using Duckov.Utilities;
using HarmonyLib;
using ItemStatsSystem;
using MagicTreasurePower.Treasures;
using SodaCraft.Localizations;
using UnityEngine;

namespace MagicTreasurePower.Quests
{
    /// <summary>
    /// 「法宝宝箱」物品：任务奖励，使用后打开七选一界面。
    ///
    /// 物品用动态注册（与法宝同一套流程），克隆一件法宝作为模板，
    /// 清掉法宝自带的 Effect / 标签，再挂上自定义使用行为。
    /// </summary>
    internal static class TreasureBoxItem
    {
        /// <summary>宝箱物品 ID（17356，登记在 <see cref="TreasureIds.TreasureBox"/>，法宝不要占用）。</summary>
        internal const int TypeId = TreasureIds.TreasureBox;

        /// <summary>宝箱原型的 GameObject 名称前缀（Instantiate 后可能带 (Clone)）。</summary>
        internal const string PrototypeName = "MagicTreasurePower_TreasureBox";

        private const string NameKey = "MagicTreasurePower_TreasureBox";
        private const string DescKey = NameKey + "_Desc";

        private static bool _registered;

        internal static void Register()
        {
            if (_registered)
                return;

            try
            {
                Item existing = ItemAssetsCollection.GetPrefab(TypeId);
                if (existing != null)
                {
                    // 重新启用 mod 时，上一次注册的宝箱还在动态表里，直接复用
                    if (existing.gameObject != null &&
                        existing.gameObject.name.StartsWith(PrototypeName, StringComparison.Ordinal))
                    {
                        _registered = true;
                        return;
                    }

                    Diag.Warn($"法宝宝箱：ID {TypeId} 已被其它物品占用（{existing.name}），未注册");
                    return;
                }

                Item template = ItemAssetsCollection.GetPrefab(TreasureIds.FanTianYin);
                if (template == null)
                {
                    Diag.Warn("法宝宝箱：未找到克隆模板（需先注册法宝）");
                    return;
                }

                Item created = UnityEngine.Object.Instantiate(template);
                GameObject clone = created.gameObject;
                clone.name = "MagicTreasurePower_TreasureBox";

                Item item = clone.GetComponent<Item>();
                if (item == null)
                {
                    Diag.Warn("法宝宝箱：模板缺少 Item 组件");
                    UnityEngine.Object.Destroy(clone);
                    return;
                }

                item.SetPrivateField("typeID", TypeId);
                item.SetPrivateField("displayName", NameKey);
                item.SetPrivateField("weight", 0.5f);
                // 用户指定：法宝宝箱售价 50 万
                item.SetPrivateField("value", 500000);
                item.SetPrivateField("quality", 7);
                item.SetPrivateField("displayQuality", DisplayQuality.Q7);
                item.SetPrivateField("order", 0);

                // 优先用内嵌的宝箱图片，加载失败时退回运行时生成的占位图标
                Sprite icon = TreasureRegistrar.LoadEmbeddedSprite("bxtm.png", TypeId) ?? CreateIcon();
                item.SetPrivateField("icon", icon);

                item.Tags.Clear();
                Tag luxury = TreasureRegistrar.FindTag("Luxury");
                if (luxury != null)
                    item.Tags.Add(luxury);

                // 宝箱用独立的「法宝宝箱」标签区分归属：
                // 不能打「法宝」标签，否则会被法宝槽位当成法宝装备上去
                Tag boxTag = TreasureRegistrar.BoxTag;
                if (boxTag != null)
                    item.Tags.Add(boxTag);

                // 去掉法宝自带的展示 Effect，宝箱不需要词条。
                // 光销毁 GameObject 不够：Item.effects 列表里的引用仍指向已销毁对象，
                // 详情面板还会把它们当成词条显示（会显示成翻天印的效果文案）。
                foreach (Effect effect in clone.GetComponentsInChildren<Effect>(true))
                {
                    if (effect != null)
                        UnityEngine.Object.Destroy(effect.gameObject);
                }

                if (item.Effects != null)
                    item.Effects.Clear();

                // 使用行为：打开七选一界面
                item.AddUsageUtilitiesComponent();
                UsageUtilities usage = item.UsageUtilities;
                if (usage != null)
                {
                    TreasureBoxUsageBehavior behavior = clone.AddComponent<TreasureBoxUsageBehavior>();
                    usage.behaviors.Add(behavior);
                }

                UnityEngine.Object.DontDestroyOnLoad(clone);

                LocalizationManager.SetOverrideText(NameKey, "法宝宝箱");
                LocalizationManager.SetOverrideText(DescKey, "使用后可从七件法宝中任选一件。");

                if (ItemAssetsCollection.AddDynamicEntry(item))
                {
                    _registered = true;
                    Diag.Log("已注册法宝宝箱物品：ID " + TypeId);
                }
                else
                {
                    Diag.Warn("法宝宝箱：AddDynamicEntry 返回 false");
                    UnityEngine.Object.Destroy(clone);
                }
            }
            catch (Exception e)
            {
                Diag.Report("注册法宝宝箱失败", e);
            }
        }

        /// <summary>
        /// 判断一个物品是不是本 mod 的法宝宝箱。
        ///
        /// 只看 TypeID 不够：如果其它 mod 先占了 17356，这里会把对方的物品也当成宝箱。
        /// 因此优先看自定义「法宝」标签，标签拿不到时退回原型名判断。
        /// </summary>
        internal static bool IsOurBox(Item item)
        {
            try
            {
                if (item == null || item.TypeID != TypeId)
                    return false;

                Tag tag = TreasureRegistrar.BoxTag;
                if (tag != null && item.Tags != null && item.Tags.Contains(tag))
                    return true;

                return item.gameObject != null &&
                       item.gameObject.name.StartsWith(PrototypeName, StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>运行时生成一个简单的金色宝箱图标（不使用额外美术资源）。</summary>
        private static Sprite CreateIcon()
        {
            const int size = 128;
            const int radius = 18;

            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            Color gold = new Color(0.91f, 0.75f, 0.36f, 1f);
            Color dark = new Color(0.22f, 0.14f, 0.05f, 1f);
            Color lid = new Color(0.78f, 0.61f, 0.25f, 1f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float half = size * 0.5f;
                    float inner = half - radius;
                    float dx = Mathf.Max(Mathf.Abs(x + 0.5f - half) - inner, 0f);
                    float dy = Mathf.Max(Mathf.Abs(y + 0.5f - half) - inner, 0f);
                    float distance = Mathf.Sqrt(dx * dx + dy * dy) - radius;

                    Color color = y > 78 ? lid : gold;

                    // 横向锁扣带
                    if (y >= 52 && y <= 70)
                        color = dark;

                    // 竖向锁扣带
                    if (x >= 56 && x <= 72)
                        color = dark;

                    // 锁芯
                    if (x >= 52 && x <= 76 && y >= 50 && y <= 74)
                        color = gold;

                    color.a = Mathf.Clamp01(-distance + 0.5f);
                    texture.SetPixel(x, y, color);
                }
            }

            texture.Apply();

            GameObject holder = new GameObject("MagicTreasurePower_BoxIcon");
            UnityEngine.Object.DontDestroyOnLoad(holder);
            holder.AddComponent<BoxIconHolder>().Texture = texture;

            return Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                100f);
        }

        private sealed class BoxIconHolder : MonoBehaviour
        {
            internal Texture2D Texture;
        }
    }

    /// <summary>
    /// 宝箱没有 3D 模型 / ItemAgent，「拿在手上使用」的流程可能起不来，
    /// 所以在 <c>UseItem</c> 入口直接接管：打开界面并跳过原版动作。
    /// </summary>
    [HarmonyPatch(typeof(CharacterMainControl), nameof(CharacterMainControl.UseItem))]
    internal static class TreasureBoxUsePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Item item)
        {
            try
            {
                if (!TreasureBoxItem.IsOurBox(item))
                    return true;

                TreasureBoxUI.Open(item);
                return false;
            }
            catch (System.Exception e)
            {
                Diag.Report("打开法宝宝箱失败", e);
                return true;
            }
        }
    }

    /// <summary>宝箱的使用行为：打开七选一界面。</summary>
    public class TreasureBoxUsageBehavior : UsageBehavior
    {
        public override DisplaySettingsData DisplaySettings => new DisplaySettingsData
        {
            display = true,
            description = "开启后选择一件法宝",
        };

        public override bool CanBeUsed(Item item, object user)
        {
            return true;
        }

        protected override void OnUse(Item item, object user)
        {
            try
            {
                TreasureBoxUI.Open(item);
            }
            catch (Exception e)
            {
                Diag.Report("打开法宝宝箱界面失败", e);
            }
        }
    }
}
