using System;
using Duckov.UI;
using HarmonyLib;
using ItemStatsSystem;
using MagicTreasurePower.Treasures;

namespace MagicTreasurePower.Patches
{
    /// <summary>
    /// 详情面板显示法宝时，把 Effect 词条改写为「当前等级」对应的文案，
    /// 其中会随等级变化的数值用金色富文本显示。
    ///
    /// 用前缀而不是后置：原版 <c>Setup</c> 会用 <c>Effect.Description</c> 构建词条，
    /// 必须在它读取之前改写。
    /// </summary>
    [HarmonyPatch(typeof(ItemDetailsDisplay), "Setup")]
    internal static class TreasureEffectTextPatch
    {
        [HarmonyPrefix]
        private static void BeforeSetup(Item target)
        {
            try
            {
                if (target == null || !TreasureProgression.IsTreasure(target))
                    return;

                TreasureBalance.ApplyEffectText(target);
            }
            catch (Exception e)
            {
                Diag.Report("更新法宝等级词条失败", e);
            }
        }
    }
}
