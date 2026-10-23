using System;
using Duckov;
using HarmonyLib;
using ItemStatsSystem;
using MagicTreasurePower.Slots;
using MagicTreasurePower.Treasures;
using MagicTreasurePower.UI;
using UnityEngine;

namespace MagicTreasurePower.Patches
{
    /// <summary>
    /// 阴阳镜：常驻 **20% 最终伤害减免**（乘算），即所有护甲、元素抗性算完之后，
    /// 最终伤害再 ×0.8；减免后不足 1 点的伤害按 1 点算。
    ///
    /// <para><b>为什么拦在 <c>CurrentHealth</c> 的 setter：</b></para>
    /// 原版在 <c>Health.Hurt</c> 内部依次完成「暴击 → 护甲 → 元素拆分」才得到
    /// <c>finalDamage</c>，方法中间没有可挂的公开接缝。而扣血最后一定会落到
    /// <c>CurrentHealth -= finalDamage</c>，即调用这个 setter——在这里对「本次掉血量」
    /// 再乘一次 0.8，正好等价于「最终伤害 × 0.8」，且不会影响护甲公式本身。
    ///
    /// <para><b>为什么要限定在 Hurt 内部：</b></para>
    /// setter 也被回血、初始化、剧情处死等路径调用。用 <see cref="YinYangJingHurtScopePatch"/>
    /// 记录「当前是否处于 Hurt 调用中」，只有 Hurt 造成的掉血才走减免。
    /// </summary>
    [HarmonyPatch(typeof(Health), nameof(Health.Hurt))]
    internal static class YinYangJingHurtScopePatch
    {
        private static int _depth;

        internal static bool Inside => _depth > 0;

        [HarmonyPrefix]
        private static void Before(out int __state)
        {
            __state = _depth;
            _depth++;
        }

        [HarmonyFinalizer]
        private static Exception After(Exception __exception, int __state)
        {
            _depth = __state;
            return __exception;
        }
    }

    /// <summary>在真正扣血的那一刻把掉血量乘以 0.8（最低 1 点）。</summary>
    [HarmonyPatch(typeof(Health), "set_CurrentHealth")]
    internal static class YinYangJingReducePatch
    {
        /// <summary>最终伤害减免比例按法宝等级取值（见 <see cref="TreasureBalance"/>）。</summary>

        /// <summary>任何伤害的最低值。</summary>
        internal const float MinimumDamage = 1f;

        /// <summary>
        /// 优先级设为最高：必须在定海珠的免死钳制之前把伤害算完，
        /// 否则「锁 1 点血」会把阴阳镜的减免效果掩盖掉。
        /// </summary>
        [HarmonyPriority(Priority.First)]
        [HarmonyPrefix]
        private static void BeforeSetCurrentHealth(Health __instance, ref float value)
        {
            try
            {
                if (!YinYangJingHurtScopePatch.Inside)
                    return;

                if (__instance == null || !__instance.IsMainCharacterHealth)
                    return;

                Item treasure = TreasureSlots.GetEquippedItem(TreasureIds.YinYangJing);
                if (treasure == null)
                    return;

                float reduction = TreasureBalance.YinYangJingReductionOf(
                    TreasureProgression.GetLevel(treasure));

                float current = __instance.CurrentHealth;
                if (value >= current)
                    return; // 不是掉血（回血 / 初始化 / 处死归零）

                float damage = current - value;
                if (damage <= 0f)
                    return;

                float reduced = damage * (1f - reduction);
                if (reduced < MinimumDamage)
                    reduced = MinimumDamage;

                float saved = damage - reduced;
                value = current - reduced;

                if (saved > 0f)
                {
                    TreasureNotice.Show(
                        TreasureIds.YinYangJing,
                        string.Format("减免 <b>{0:0.#}</b> 点伤害", saved));

                }
            }
            catch (Exception e)
            {
                Diag.Report("阴阳镜减伤失败", e);
            }
        }
    }
}
