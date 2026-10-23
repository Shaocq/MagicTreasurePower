using System;
using System.Reflection;
using Duckov.UI;
using HarmonyLib;
using ItemStatsSystem;
using MagicTreasurePower.Treasures;
using TMPro;

namespace MagicTreasurePower.Patches
{
    /// <summary>
    /// 在悬停提示（<see cref="ItemHoveringUI"/>）里补上法宝的效果词条。
    ///
    /// <para><b>为什么要单独补：</b></para>
    /// 原版悬停提示的结构是「名称 / 重量 / 描述 / 使用信息 / 属性词条」，
    /// 其中属性词条来自 <see cref="ItemUIUtilities.GetPropertyValueTextPair"/>，
    /// 只收 Variables / Constants / Stats / Modifiers，**不包含 Effects**。
    /// 法宝的专属效果挂在 Effect 上（详情面板会显示），所以悬停时看不到。
    ///
    /// <para><b>做法：</b></para>
    /// 在原版描述之后追加一段「法宝效果」，颜色沿用自建「法宝」标签的金色，
    /// 不新增 UI 节点、不改原版描述文本（每次悬停原版都会重设描述，不存在残留）。
    /// </summary>
    [HarmonyPatch(typeof(ItemHoveringUI), "SetupAndShow")]
    internal static class TreasureHoverEffectPatch
    {
        /// <summary>与「法宝」标签一致的淡金色。</summary>
        private const string HeaderColor = "#E8BF5C";

        private static FieldInfo _descriptionField;

        [HarmonyPostfix]
        private static void AfterSetupAndShow(ItemHoveringUI __instance, ItemDisplay display)
        {
            try
            {
                Item item = display != null ? display.Target : null;
                string effect = GetEffectText(item);
                if (string.IsNullOrEmpty(effect))
                    return;

                TextMeshProUGUI description = GetDescription(__instance);
                if (description == null)
                {
                    Diag.Warn("悬停提示里没有找到描述文本，法宝效果词条未显示");
                    return;
                }

                string original = description.text ?? string.Empty;
                description.text = original +
                                   "\n\n<color=" + HeaderColor + ">【法宝效果】</color>\n" +
                                   effect;
            }
            catch (Exception e)
            {
                Diag.Report("悬停提示追加法宝效果词条失败", e);
            }
        }

        /// <summary>按 TypeID 取法宝的效果文案；非法宝返回 null。</summary>
        private static string GetEffectText(Item item)
        {
            if (item == null || !TreasureIds.IsInRange(item.TypeID))
                return null;

            // 按物品实例的等级生成：可随等级变化的数值用金色显示
            string text = TreasureBalance.FormatEffectText(
                item.TypeID, TreasureProgression.GetLevel(item));
            if (!string.IsNullOrEmpty(text))
                return text;

            foreach (TreasureDefinition definition in TreasureCatalog.All)
            {
                if (definition.TypeId == item.TypeID)
                    return definition.EffectText;
            }

            return null;
        }

        private static TextMeshProUGUI GetDescription(ItemHoveringUI ui)
        {
            try
            {
                if (_descriptionField == null)
                    _descriptionField = AccessTools.Field(typeof(ItemHoveringUI), "itemDescription");

                return _descriptionField?.GetValue(ui) as TextMeshProUGUI;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
