using System;
using System.Reflection;
using Duckov;
using Duckov.Buffs;
using HarmonyLib;
using ItemStatsSystem;
using MagicTreasurePower.Slots;
using MagicTreasurePower.Treasures;
using MagicTreasurePower.UI;
using SodaCraft.Localizations;
using UnityEngine;

namespace MagicTreasurePower.Patches
{
    /// <summary>
    /// 定海珠：
    /// <list type="bullet">
    /// <item>受到伤害时按等级概率完全免疫本次伤害（Lv.1~7：8% / 10% / 12% / 14% / 16% / 18% / 20%）；</item>
    /// <item>免死：每场战局可触发 1 次，Lv.4 起 2 次，Lv.7 起 3 次；受到致命伤害时免疫死亡，
    /// 生命值锁定为 1 点，持续 5 秒（全等级固定）；换图（关卡初始化）后次数重置。</item>
    /// <item>免死触发瞬间清空身上全部「金蛟剪可清除」类目的异常状态，护体期间也不会再被挂上
    /// 同类异常（类目见 <see cref="TreasureDebuffs"/>）。</item>
    /// </list>
    ///
    /// <para><b>免死为什么做成 Buff：</b></para>
    /// 「5 秒内生命值不可低于 1」需要一个可视化状态，所以触发时挂一个自定义的
    /// 5 秒 Buff（克隆原版无效果 Buff 模板，自定 ID / 图标 / 文案）。
    /// 拦截本身仍然由 <c>CurrentHealth</c> 的 setter 补丁完成——Buff 只负责展示与计时，
    /// 不会被驱散 / 抵抗等机制干扰触发条件。
    /// </summary>
    [HarmonyPatch(typeof(Health), nameof(Health.Hurt))]
    internal static class DingHaiZhuNegatePatch
    {
        /// <summary>免伤概率按法宝等级取值（见 <see cref="TreasureBalance"/>）。</summary>

        /// <summary>返回 false 表示整次伤害作废（连伤害数字都不会弹）。</summary>
        [HarmonyPrefix]
        private static bool BeforeHurt(Health __instance, DamageInfo __0)
        {
            try
            {
                if (__instance == null || !__instance.IsMainCharacterHealth)
                    return true;

                if (__0.damageValue <= 0f)
                    return true;

                Item treasure = TreasureSlots.GetEquippedItem(TreasureIds.DingHaiZhu);
                if (treasure == null)
                    return true;

                int level = TreasureProgression.GetLevel(treasure);
                if (UnityEngine.Random.value >= TreasureBalance.DingHaiZhuNegateOf(level))
                    return true;

                TreasureNotice.Show(TreasureIds.DingHaiZhu, "免疫本次伤害");
                return false;
            }
            catch (Exception e)
            {
                Diag.Report("定海珠免伤判定失败", e);
                return true;
            }
        }
    }

    /// <summary>
    /// 免死：拦在 <c>CurrentHealth</c> 的 setter 上。
    /// 任何来源把血量压到 1 以下时：
    /// 护体 Buff 生效中 → 直接锁回 1；否则还有剩余次数 → 触发免死（血锁 1 + 挂 Buff + 次数 -1）。
    /// 次数上限按法宝等级取（Lv.1~3：1 次；Lv.4~6：2 次；Lv.7：3 次）。
    /// </summary>
    [HarmonyPatch(typeof(Health), "set_CurrentHealth")]
    internal static class DingHaiZhuDeathSavePatch
    {
        /// <summary>本场战局已经触发过的免死次数。</summary>
        private static int _usedThisRaid;

        /// <summary>换图 / 新关卡时重置（由 ModBehaviour 订阅关卡初始化事件调用）。</summary>
        internal static void ResetForNewRaid()
        {
            _usedThisRaid = 0;
        }

        [HarmonyPrefix]
        private static void BeforeSetCurrentHealth(Health __instance, ref float value)
        {
            try
            {
                if (value >= 1f)
                    return;

                if (__instance == null || !__instance.IsMainCharacterHealth)
                    return;

                Item treasure = TreasureSlots.GetEquippedItem(TreasureIds.DingHaiZhu);
                if (treasure == null)
                    return;

                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null)
                    return;

                // 护体期间：任何伤害都不能把生命值压到 1 以下
                if (main.HasBuff(DingHaiZhuBuffs.DeathWardId))
                {
                    value = 1f;
                    return;
                }

                int level = TreasureProgression.GetLevel(treasure);
                int allowed = TreasureBalance.DingHaiZhuSavesOf(level);
                if (_usedThisRaid >= allowed)
                    return;

                _usedThisRaid++;
                value = 1f;

                // 触发瞬间清空全部异常状态（类目与金蛟剪共用 TreasureDebuffs）
                int cleared = TreasureDebuffs.ClearAll(main);

                float duration = TreasureBalance.DingHaiZhuWardOf(level);

                Buff ward = DingHaiZhuBuffs.GetDeathWardPrefab(duration);
                if (ward != null)
                    main.AddBuff(ward, main);

                TreasureNotice.Show(
                    TreasureIds.DingHaiZhu,
                    cleared > 0
                        ? string.Format("免疫本次死亡，清除 {0} 种异常（本局剩余 {1} 次）",
                            cleared, allowed - _usedThisRaid)
                        : string.Format("免疫本次死亡（本局剩余 {0} 次）", allowed - _usedThisRaid));
            }
            catch (Exception e)
            {
                Diag.Report("定海珠免死触发失败", e);
            }
        }
    }

    /// <summary>
    /// 护体期间免疫异常状态：所有「金蛟剪可清除」类目的 Buff（<see cref="TreasureDebuffs"/>）
    /// 在护体 Buff 生效时一律加不上去。类目与金蛟剪完全共用同一张表。
    /// </summary>
    [HarmonyPatch(typeof(CharacterBuffManager), nameof(CharacterBuffManager.AddBuff))]
    internal static class DingHaiZhuWardDebuffBlockPatch
    {
        /// <summary>返回 false 表示本次添加作废（连层数刷新都不做）。</summary>
        [HarmonyPrefix]
        private static bool BeforeAddBuff(CharacterBuffManager __instance, Buff buffPrefab)
        {
            try
            {
                if (__instance == null || buffPrefab == null)
                    return true;

                if (!TreasureDebuffs.Contains(buffPrefab.ID))
                    return true;

                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null || __instance.Master != main)
                    return true;

                // 只有护体窗口内才拦截；平时照常挂异常
                return !main.HasBuff(DingHaiZhuBuffs.DeathWardId);
            }
            catch (Exception e)
            {
                Diag.Report("定海珠护体期间拦截异常状态失败", e);
                return true;
            }
        }
    }

    /// <summary>
    /// 定海珠「护体」Buff：克隆原版一个无 Effect 的限时 Buff 作为模板，
    /// 改 ID / 名称 / 描述 / 图标 / 时长，仅用于展示 5 秒免死窗口。
    /// </summary>
    internal static class DingHaiZhuBuffs
    {
        /// <summary>自定义 Buff ID（位于本 mod 独占段，避免与原版冲突）。</summary>
        internal const int DeathWardId = 17460;

        /// <summary>免死窗口默认时长（秒）；实际按法宝等级覆盖。</summary>
        internal const float DeathWardDuration = 5f;

        private const string NameKey = "MagicTreasurePower_Buff_DeathWard";
        private const string DescKey = "MagicTreasurePower_Buff_DeathWard_Desc";

        /// <summary>克隆用的原版模板：1019 出血免疫（无 Effect，限时 60 秒）。</summary>
        private const int TemplateBuffId = 1019;

        private static Buff _prefab;

        internal static Buff GetDeathWardPrefab(float duration)
        {
            if (duration <= 0f)
                duration = DeathWardDuration;

            if (_prefab != null)
            {
                // 时长按法宝等级变化：每次添加前改写模板，AddBuff 实例化时会带上
                SetField(_prefab, "totalLifeTime", duration);
                return _prefab;
            }

            try
            {
                Buff template = VanillaBuffs.Get(TemplateBuffId);
                if (template == null)
                {
                    Diag.Warn("定海珠：未找到 Buff 模板，免死没有可视化状态");
                    return null;
                }

                Buff clone = UnityEngine.Object.Instantiate(template);
                clone.name = "MagicTreasurePower_DeathWard";
                UnityEngine.Object.DontDestroyOnLoad(clone.gameObject);
                clone.gameObject.SetActive(false);

                SetField(clone, "id", DeathWardId);
                SetField(clone, "displayName", NameKey);
                SetField(clone, "description", DescKey);
                SetField(clone, "icon", TreasureRegistrar.GetIcon(TreasureIds.DingHaiZhu));
                SetField(clone, "exclusiveTag", Buff.BuffExclusiveTags.NotExclusive);
                SetField(clone, "exclusiveTagPriority", 0);
                SetField(clone, "maxLayers", 1);
                SetField(clone, "totalLifeTime", duration);
                SetField(clone, "limitedLifeTime", true);
                SetField(clone, "hide", false);
                SetField(clone, "displayInExtraView", true);

                LocalizationManager.SetOverrideText(NameKey, "定海珠·护体");
                LocalizationManager.SetOverrideText(
                    DescKey,
                    "受到致命伤害时触发：清除自身全部异常状态，生命值锁定为 1 点；持续期间不会受到任何异常状态影响。");

                _prefab = clone;
                return _prefab;
            }
            catch (Exception e)
            {
                Diag.Report("定海珠：创建护体 Buff 失败", e);
                return null;
            }
        }

        private static void SetField(Buff buff, string fieldName, object value)
        {
            FieldInfo field = typeof(Buff).GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            if (field == null)
            {
                Diag.Warn($"定海珠：Buff 未找到私有字段「{fieldName}」");
                return;
            }

            field.SetValue(buff, value);
        }
    }
}
