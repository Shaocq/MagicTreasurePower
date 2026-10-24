using System;
using System.Collections.Generic;
using System.Reflection;
using Duckov;
using Duckov.Buffs;
using HarmonyLib;
using ItemStatsSystem;
using MagicTreasurePower.Slots;
using MagicTreasurePower.Treasures;
using MagicTreasurePower.UI;
using UnityEngine;

namespace MagicTreasurePower.Patches
{
    /// <summary>
    /// 金蛟剪：每次射击（每消耗一发弹匣子弹）掷一次骰，30% 概率让这一发子弹携带效果，
    /// **首次命中敌人**时生效：
    /// <list type="bullet">
    /// <item>自身带有异常状态 → 随机移除其中一种的**一层**；</item>
    /// <item>自身没有异常状态 → 随机获得一个增益，持续 8 秒。
    /// 不同增益可以同时存在；若随机到的是已经生效的同一个增益，
    /// 则把它的持续时间累加 8 秒（不重置计时、不额外叠层）。</item>
    /// </list>
    ///
    /// <para><b>为什么掷骰在开火、生效在命中：</b></para>
    /// 弹丸的命中结算发生在开枪之后的若干帧，所以开火前缀里只记下「这一发带效果」，
    /// 要等弹丸真正打中敌人时才消耗掉这个标记——同一发子弹只生效一次，
    /// 霰弹枪的多颗弹丸共享同一次判定。
    ///
    /// <para><b>白名单：</b></para>
    /// 只从明确安全的原版异常 / 增益里随机，避开剧情机制（变异病毒 / 变异）、
    /// 依赖装备或系统维护的内部 Buff（融合充能、伞之力、图腾诅咒等），
    /// 以及由状态 / 环境系统持续重新施加、移除后立刻回来的负面（饥饿、负重、寒冷等）。
    /// </summary>
    [HarmonyPatch(typeof(ItemAgent_Gun), "TransToFire")]
    internal static class JinJiaoJianShotPatch
    {
        /// <summary>触发概率。等法宝升级系统上线后改为按等级取值。</summary>
        /// <summary>触发概率按法宝等级取值（见 <see cref="TreasureBalance"/>）。</summary>

        /// <summary>本次射击携带效果的枪械；null 表示未触发。</summary>
        private static ItemSetting_Gun _armedGun;

        /// <summary>
        /// 命中时调用：标记属于这把枪、且尚未消耗时返回 true 并清掉标记
        /// （保证一发子弹只生效一次）。
        /// </summary>
        internal static bool TryConsume(ItemSetting_Gun gun)
        {
            try
            {
                if (_armedGun == null || gun == null || _armedGun != gun)
                    return false;

                _armedGun = null;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 一次射击开始时掷骰。只处理玩家本人的枪械，敌人 / 宠物 / 队友一律走原版。
        /// 这里**不做现场恢复**：命中发生在之后的帧，标记必须保留到命中结算。
        /// </summary>
        [HarmonyPrefix]
        private static void BeforeTransToFire(ItemAgent_Gun __instance)
        {
            try
            {
                // 每次扣扳机都重置：上一次没命中的标记不会带到这一发
                _armedGun = null;

                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null || __instance == null || __instance.Holder != main)
                    return;

                Item treasure = TreasureSlots.GetEquippedItem(TreasureIds.JinJiaoJian);
                if (treasure == null)
                    return;

                int level = TreasureProgression.GetLevel(treasure);
                if (UnityEngine.Random.value >= TreasureBalance.JinJiaoJianChanceOf(level))
                    return;

                _armedGun = __instance.GunItemSetting;
            }
            catch (Exception e)
            {
                _armedGun = null;
                Diag.Report("金蛟剪射击掷骰失败", e);
            }
        }
    }

    /// <summary>
    /// 记录「当前正在结算命中的弹丸」，供 <see cref="JinJiaoJianHitPatch"/>
    /// 判断这次伤害是不是射击打出来的（排除近战等非弹丸伤害）。
    /// </summary>
    [HarmonyPatch(typeof(Projectile), "UpdateMoveAndCheck")]
    internal static class JinJiaoJianHitScopePatch
    {
        private static Projectile _current;

        internal static Projectile Current => _current;

        [HarmonyPrefix]
        private static void Before(Projectile __instance, out Projectile __state)
        {
            __state = _current;
            _current = __instance;
        }

        [HarmonyFinalizer]
        private static Exception After(Exception __exception, Projectile __state)
        {
            _current = __state;
            return __exception;
        }
    }

    /// <summary>
    /// 命中判定：只认「玩家本人枪械发出的弹丸打中敌人」的第一次命中。
    /// </summary>
    [HarmonyPatch(typeof(DamageReceiver), nameof(DamageReceiver.Hurt))]
    internal static class JinJiaoJianHitPatch
    {
        [HarmonyPrefix]
        private static void BeforeHurt(DamageReceiver __instance, DamageInfo __0)
        {
            try
            {
                Projectile projectile = JinJiaoJianHitScopePatch.Current;
                if (projectile == null || __instance == null)
                    return;

                // 爆炸伤害不算「射击命中」
                if (__0.isExplosion)
                    return;

                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null || __0.fromCharacter != main)
                    return;

                // 打自己 / 队友 / 中立目标不触发
                if (__instance.IsMainCharacter || !Team.IsEnemy(main.Team, __instance.Team))
                    return;

                ProjectileContext context = projectile.context;
                if (context.fromCharacter != main)
                    return;

                if (!JinJiaoJianShotPatch.TryConsume(context.fromGunItemSetting))
                    return;

                JinJiaoJianEffect.Apply(main);
            }
            catch (Exception e)
            {
                Diag.Report("金蛟剪命中触发失败", e);
            }
        }
    }

    /// <summary>
    /// 金蛟剪的实际效果：优先移除一层异常状态，没有异常时给一个 8 秒增益。
    /// 只从白名单里随机，避免碰到剧情 Buff 与依赖外部逻辑的内部 Buff。
    /// </summary>
    internal static class JinJiaoJianEffect
    {
        /// <summary>随机获得的增益时长按法宝等级取值（见 <see cref="TreasureBalance"/>）。</summary>

        // 可移除的异常状态清单在 TreasureDebuffs.Ids（与定海珠共用，见 src/Treasures/TreasureDebuffs.cs）。

        /// <summary>
        /// 可随机获得的增益（原版 ID）。已排除：
        /// 1205「1000%」（自带眩晕）、1206/1207 Tagilla / 米诺陶（持续掉血）、
        /// 1404/1405 伞之力 / 红色圆球（依赖装备逻辑）、1408~1412 融合充能、
        /// 1305/1306/1401 干枯（装备吸取）、1021 轻盈、以及全部隐藏条目；
        /// 1101/1102 高兴、1202 伪装（开火或攻击时自动移除，对射击流几乎无效）；
        /// 2301 抗寒（环境向、收益太低）；
        /// 1083/1084 镇静、1115/1116 空间减伤（与保留项效果完全重复，
        /// 统一压成 8 秒后连时长差异也没有了）。
        /// </summary>
        private static readonly int[] AddableBuffs =
        {
            1011,                                 // 加速
            1012,                                 // 负重提升
            1013,                                 // 硬化
            1014,                                 // 持久（水分消耗 +200%）
            1015,                                 // 力量
            1017,                                 // 强翅
            1018,                                 // 回复
            1019,                                 // 出血免疫
            1072, 1074, 1075, 1076,               // 抗电 / 抗火 / 抗毒 / 抗空间
            1082,                                 // 镇静（疼痛免疫）
            1091,                                 // 热血（水分消耗 +200%）
            1093,                                 // 愤怒
            1113,                                 // 弱效空间抵抗（风暴防护 +1）
            1114,                                 // 强效空间抵抗（风暴防护 +2）
            1201,                                 // 明视
            1402,                                 // 高手
        };

        private static FieldInfo _totalLifeTimeField;
        private static FieldInfo _limitedLifeTimeField;
        private static FieldInfo _timeWhenStartedField;

        internal static void Apply(CharacterMainControl character)
        {
            CharacterBuffManager manager = character.GetBuffManager();
            if (manager == null)
                return;

            Item treasure = TreasureSlots.GetEquippedItem(TreasureIds.JinJiaoJian);
            int level = TreasureProgression.GetLevel(treasure);
            float duration = TreasureBalance.JinJiaoJianBuffTimeOf(level);

            List<Buff> debuffs = new List<Buff>();
            foreach (Buff buff in manager.Buffs)
            {
                if (buff != null && TreasureDebuffs.Contains(buff.ID))
                    debuffs.Add(buff);
            }

            if (debuffs.Count > 0)
            {
                // 有异常状态：随机挑一种，只移除一层
                Buff target = debuffs[UnityEngine.Random.Range(0, debuffs.Count)];
                string name = target.DisplayName;
                character.RemoveBuff(target.ID, true);
                TreasureNotice.Show(TreasureIds.JinJiaoJian, string.Format("移除「{0}」1 层", name));
                return;
            }

            ApplyRandomBuff(character, manager, duration);
        }

        private static void ApplyRandomBuff(
            CharacterMainControl character, CharacterBuffManager manager, float duration)
        {
            Dictionary<int, Buff> prefabs = VanillaBuffs.All;
            if (prefabs.Count == 0)
                return;

            // 随机一个增益：已有同名 → 持续时间累加；没有 → 新挂 8 秒。
            // 被抗性/同标签互斥挡掉时换一个再试。
            for (int attempt = 0; attempt < 5; attempt++)
            {
                int id = AddableBuffs[UnityEngine.Random.Range(0, AddableBuffs.Length)];

                Buff prefab;
                if (!prefabs.TryGetValue(id, out prefab) || prefab == null)
                    continue;

                Buff active = FindBuff(manager, id);
                if (active != null)
                {
                    // 同一个增益已在生效：只把总时长累加，不重置起始时间、不额外叠层
                    AddLifeTime(active, duration);
                    TreasureNotice.Show(
                        TreasureIds.JinJiaoJian,
                        string.Format("「{0}」持续时间 +{1:0.#} 秒", active.DisplayName, duration));
                    return;
                }

                character.AddBuff(prefab, character);

                Buff instance = FindBuff(manager, id);
                if (instance == null)
                    continue;

                SetLifeTime(instance, duration);
                TreasureNotice.Show(
                    TreasureIds.JinJiaoJian,
                    string.Format("获得「{0}」（{1:0.#} 秒）", instance.DisplayName, duration));
                return;
            }
        }

        private static Buff FindBuff(CharacterBuffManager manager, int id)
        {
            foreach (Buff buff in manager.Buffs)
            {
                if (buff != null && buff.ID == id)
                    return buff;
            }

            return null;
        }

        /// <summary>
        /// 把 Buff 实例改成「从此刻起限时 8 秒」。
        /// <c>totalLifeTime</c> / <c>limitedLifeTime</c> / <c>timeWhenStarted</c>
        /// 都是私有序列化字段，只能反射写；重置起始时间才能保证剩余时间是完整的 8 秒。
        /// </summary>
        private static void SetLifeTime(Buff buff, float seconds)
        {
            if (!EnsureFields())
                return;

            try
            {
                _totalLifeTimeField.SetValue(buff, seconds);
                _limitedLifeTimeField.SetValue(buff, true);
                _timeWhenStartedField?.SetValue(buff, Time.time);
            }
            catch (Exception e)
            {
                Diag.Report("金蛟剪修改 Buff 持续时间失败", e);
            }
        }

        /// <summary>
        /// 在已有增益的基础上把总时长 +8 秒：不重置起始时间，
        /// 因此剩余时间会自然增加 8 秒，也不会额外叠层。
        /// </summary>
        private static void AddLifeTime(Buff buff, float seconds)
        {
            if (!EnsureFields())
                return;

            try
            {
                float total = (float)_totalLifeTimeField.GetValue(buff);
                _totalLifeTimeField.SetValue(buff, total + seconds);
                _limitedLifeTimeField.SetValue(buff, true);
            }
            catch (Exception e)
            {
                Diag.Report("金蛟剪延长 Buff 持续时间失败", e);
            }
        }

        private static bool EnsureFields()
        {
            if (_totalLifeTimeField == null)
                _totalLifeTimeField = typeof(Buff).GetField(
                    "totalLifeTime", BindingFlags.Instance | BindingFlags.NonPublic);

            if (_limitedLifeTimeField == null)
                _limitedLifeTimeField = typeof(Buff).GetField(
                    "limitedLifeTime", BindingFlags.Instance | BindingFlags.NonPublic);

            if (_timeWhenStartedField == null)
                _timeWhenStartedField = typeof(Buff).GetField(
                    "timeWhenStarted", BindingFlags.Instance | BindingFlags.NonPublic);

            if (_totalLifeTimeField != null && _limitedLifeTimeField != null)
                return true;

            Diag.Warn("金蛟剪未能修改 Buff 持续时间（字段未找到）");
            return false;
        }
    }
}
