using System;
using System.Runtime.CompilerServices;
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
    /// 混元金斗：每次射击（每消耗一发弹匣子弹）投掷一次，
    /// 命中后额外造成「枪械基础伤害 × N」的真实伤害（爆炸伤害不加成）。
    ///
    /// <para><b>真实伤害的定义：</b></para>
    /// 原版这一下伤害先正常结算（暴击、护甲、元素抗性、距离衰减全按原版）；
    /// 结算完成后，再单独补一次 <c>DamageTypes.realDamage</c> 的伤害。
    /// 这笔伤害在 <c>Health.Hurt</c> 里跳过护甲公式，同时关掉暴击与难度系数，
    /// 因此不受护甲、元素抗性、暴击、距离衰减影响，也不会被目标防御类词条削减。
    ///
    /// <para><b>加的是固定值，不是倍率：</b></para>
    /// 「枪械基础伤害」取枪的 <c>Stat.BaseValue</c>，即不含配件加成、
    /// 子弹倍率与角色伤害加成的本体数值；霰弹枪按 <c>ShotCount</c> 均摊、
    /// 穿透后每一段命中都补一次。
    ///
    /// <para><b>为什么把掷骰放在 <c>ItemAgent_Gun.TransToFire</c>：</b></para>
    /// 游戏里「弹匣里的子弹」只是一个整数计数（<c>ItemSetting_Gun.BulletCount</c>），
    /// 没有逐颗的子弹对象；而 <c>TransToFire</c> 正好是「一次扳机 = 消耗一发弹」的入口，
    /// 内层 <c>for (ShotCount)</c> 循环只是霰弹枪的多颗弹丸。所以在它的前缀里掷一次骰，
    /// 本次射击的所有弹丸共享同一个结果，语义与「这一发子弹触发」一致，
    /// 换弹 / 退弹 / 读档 / 其它换弹 mod 都不会让判定错位。
    ///
    /// <para><b>为什么在 <c>Projectile.Init</c> 只登记、不改进伤害：</b></para>
    /// 直击伤害在开火瞬间算好并写进 <c>ProjectileContext.damage</c>，命中前改它
    /// 只能做到「护甲结算前叠加」。改成结算后补真实伤害就只能在命中流程里做：
    /// <c>DamageReceiver.Hurt</c> 的后置拿到「这一下已经结算完」的时机，
    /// 再按登记的信息补一次真实伤害（见 <see cref="HunYuanJinDouTrueDamagePatch"/>）。
    /// </summary>
    [HarmonyPatch(typeof(ItemAgent_Gun), "TransToFire")]
    internal static class HunYuanJinDouShotPatch
    {
        /// <summary>触发概率。等法宝升级系统上线后改为按等级取值。</summary>
        /// <summary>枪械伤害属性的键（与原版 <c>ItemAgent_Gun</c> 保持一致）。</summary>
        private const string DamageStatKey = "Damage";

        /// <summary>一次射击的加成状态；null 表示本次未触发。</summary>
        private sealed class ShotState
        {
            /// <summary>触发的枪（用来比对弹丸来源）。</summary>
            internal ItemSetting_Gun Gun;

            /// <summary>每颗弹丸追加的固定伤害（霰弹枪已按 ShotCount 均摊）。</summary>
            internal float BonusPerProjectile;

            /// <summary>整次射击的附加伤害总量（提示框显示用）。</summary>
            internal float TotalBonus;
        }

        private static ShotState _current;

        /// <summary>本次射击是否还没弹出「额外造成伤害」提示（命中敌人才提示）。</summary>
        private static bool _noticePending;

        /// <summary>供 <see cref="HunYuanJinDouProjectilePatch"/> 读取。</summary>
        internal static ItemSetting_Gun BoostedGun => _current?.Gun;

        /// <summary>供 <see cref="HunYuanJinDouProjectilePatch"/> 读取的每颗弹丸附加伤害。</summary>
        internal static float BonusPerProjectile => _current?.BonusPerProjectile ?? 0f;

        /// <summary>整次射击的附加伤害总量（提示框用）。</summary>
        internal static float TotalBonus => _current?.TotalBonus ?? 0f;

        /// <summary>命中时消耗一次提示资格（同一发只提示一次）。</summary>
        internal static bool TryConsumeNotice()
        {
            if (!_noticePending)
                return false;

            _noticePending = false;
            return true;
        }

        /// <summary>
        /// 一次射击开始时掷骰。只处理玩家本人的枪械，敌人 / 宠物 / 队友一律走原版。
        /// 用 <c>__state</c> 保存并恢复现场，避免重入时把外层的状态冲掉。
        /// </summary>
        [HarmonyPrefix]
        private static void BeforeTransToFire(ItemAgent_Gun __instance, out ShotState __state)
        {
            __state = _current;

            try
            {
                _current = null;
                _noticePending = false;

                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null || __instance == null || __instance.Holder != main)
                    return;

                Item treasure = TreasureSlots.GetEquippedItem(TreasureIds.HunYuanJinDou);
                if (treasure == null)
                    return;

                int level = TreasureProgression.GetLevel(treasure);
                if (UnityEngine.Random.value >= TreasureBalance.HunYuanJinDouChanceOf(level))
                    return;

                float multiplier = TreasureBalance.HunYuanJinDouMultiplierOf(level);

                ItemSetting_Gun gun = __instance.GunItemSetting;
                if (gun == null)
                    return;

                // 霰弹枪一次开火产生 ShotCount 颗弹丸，加成按弹丸数均摊，
                // 与原版「总伤害除以 ShotCount」的算法保持一致
                int shotCount = Mathf.Max(1, __instance.ShotCount);
                float baseDamage = GetBaseDamage(gun);
                float bonus = baseDamage * multiplier / shotCount;

                if (bonus <= 0f)
                    return;

                // 提示改到「子弹真正命中敌人」时再弹（见 HunYuanJinDouHitNoticePatch）
                _noticePending = true;

                _current = new ShotState
                {
                    Gun = gun,
                    BonusPerProjectile = bonus,
                    TotalBonus = baseDamage * multiplier,
                };
            }
            catch (Exception e)
            {
                // 掷骰失败按「未触发」处理，绝不影响射击本身
                _current = null;
                Diag.Report("混元金斗射击掷骰失败", e);
            }
        }

        /// <summary>无论原方法正常结束还是抛异常，都恢复上一次的现场。</summary>
        [HarmonyFinalizer]
        private static Exception AfterTransToFire(Exception __exception, ShotState __state)
        {
            _current = __state;
            return __exception;
        }

        /// <summary>
        /// 取枪械本体伤害：<c>Stat.BaseValue</c> 是物品模板里的基础值，
        /// 配件加成、子弹倍率、角色伤害加成都不在里面。
        /// </summary>
        private static float GetBaseDamage(ItemSetting_Gun gun)
        {
            Item item = gun.Item;
            Stat stat = item != null ? item.GetStat(DamageStatKey) : null;
            return stat != null ? stat.BaseValue : 0f;
        }
    }

    /// <summary>
    /// 子弹初始化时登记混元金斗的加成信息。
    ///
    /// <para>这里**不再改** <c>context.damage</c>：加成改成「结算后的真实伤害」，
    /// 命中时由 <see cref="HunYuanJinDouTrueDamagePatch"/> 在本次伤害结算完成后
    /// 单独补一次 <c>DamageTypes.realDamage</c> 伤害，因此不吃护甲、元素抗性、
    /// 暴击与距离衰减。</para>
    /// </summary>
    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Init), new[] { typeof(ProjectileContext) })]
    internal static class HunYuanJinDouProjectilePatch
    {
        /// <summary>一颗弹丸的加成信息。</summary>
        internal sealed class BonusData
        {
            /// <summary>整次射击的附加伤害总量（命中提示用）。</summary>
            internal float TotalBonus;

            /// <summary>这颗弹丸命中后要补的真实伤害（霰弹枪已按 ShotCount 均摊）。</summary>
            internal float TrueDamage;

            /// <summary>开火武器 TypeID，补伤害时沿用，便于伤害数字识别。</summary>
            internal int WeaponItemId;
        }

        /// <summary>
        /// 被混元金斗加成的弹丸。用弱引用表而不是普通集合：
        /// 弹丸来自对象池，被复用/销毁后自动出表，不会长期累积。
        /// </summary>
        private static readonly ConditionalWeakTable<Projectile, BonusData> BoostedProjectiles =
            new ConditionalWeakTable<Projectile, BonusData>();

        /// <summary>该弹丸的直击伤害是否被混元金斗加成过（供伤害数字上色用）。</summary>
        internal static bool IsBoosted(Projectile projectile)
        {
            return TryGetBonus(projectile, out _);
        }

        /// <summary>取该弹丸的加成信息；未被加成时返回 false。</summary>
        internal static bool TryGetBonus(Projectile projectile, out BonusData data)
        {
            data = null;

            try
            {
                return projectile != null && BoostedProjectiles.TryGetValue(projectile, out data);
            }
            catch
            {
                return false;
            }
        }

        [HarmonyPostfix]
        private static void AfterInit(Projectile __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                ItemSetting_Gun boostedGun = HunYuanJinDouShotPatch.BoostedGun;

                // 每次开火都会对池里的弹丸重新 Init，先清掉上一次的标记
                BoostedProjectiles.Remove(__instance);

                // 翻天印追加的那一发只归翻天印：不加混元金斗的伤害，也不套红色数字
                if (FanTianYinShotPatch.IsFiringExtraShot)
                    return;

                if (boostedGun == null)
                    return;

                ProjectileContext context = __instance.context;

                // 只认本次射击的那把枪发出的弹丸，其余弹道（其它枪、投掷物、爆炸物）保持原版
                if (context.fromGunItemSetting != boostedGun)
                    return;

                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null || context.fromCharacter != main)
                    return;

                float bonus = HunYuanJinDouShotPatch.BonusPerProjectile;
                if (bonus <= 0f)
                    return;

                // 只登记、不改 context.damage：加成改成命中结算后单独补的真实伤害
                BoostedProjectiles.Add(__instance, new BonusData
                {
                    TotalBonus = HunYuanJinDouShotPatch.TotalBonus,
                    TrueDamage = bonus,
                    WeaponItemId = context.fromWeaponItemID,
                });
            }
            catch (Exception e)
            {
                Diag.Report("混元金斗加成子弹伤害失败", e);
            }
        }
    }

    /// <summary>
    /// 标记「当前正在补真实伤害」。补伤害本身会再走一次 <c>DamageReceiver.Hurt</c>，
    /// 用这个标记避免再次补伤害、也避免把命中类效果重复触发一遍。
    /// </summary>
    internal static class TreasureTrueDamageScope
    {
        [ThreadStatic]
        private static bool _applying;

        internal static bool Applying => _applying;

        internal static void Enter()
        {
            _applying = true;
        }

        internal static void Exit()
        {
            _applying = false;
        }
    }

    /// <summary>
    /// 混元金斗的真实伤害：原版这一下的伤害正常结算完之后，
    /// 再单独补一次 <c>DamageTypes.realDamage</c> 伤害。
    /// 真实伤害在 <c>Health.Hurt</c> 里跳过护甲公式；同时关掉暴击与难度系数，
    /// 所以它既不吃对方护甲 / 元素抗性，也不被暴击、距离衰减放大。
    /// </summary>
    [HarmonyPatch(typeof(DamageReceiver), nameof(DamageReceiver.Hurt))]
    internal static class HunYuanJinDouTrueDamagePatch
    {
        [HarmonyPostfix]
        private static void AfterHurt(DamageReceiver __instance, DamageInfo __0, bool __result)
        {
            if (!__result || TreasureTrueDamageScope.Applying)
                return;

            try
            {
                if (__instance == null || __0.isExplosion)
                    return;

                Projectile projectile = JinJiaoJianHitScopePatch.Current;
                if (projectile == null)
                    return;

                if (!HunYuanJinDouProjectilePatch.TryGetBonus(projectile, out var bonus))
                    return;

                if (bonus.TrueDamage <= 0f)
                    return;

                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null || __0.fromCharacter != main)
                    return;

                if (__instance.IsMainCharacter || !Team.IsEnemy(main.Team, __instance.Team))
                    return;

                // 这一下已经把目标打死就不用再补，避免打尸体
                if (__instance.IsDead || __instance.health == null)
                    return;

                DamageInfo info = new DamageInfo(main)
                {
                    damageType = DamageTypes.realDamage,
                    ignoreArmor = true,
                    ignoreDifficulty = true,
                    damageValue = bonus.TrueDamage,
                    fromWeaponItemID = bonus.WeaponItemId,
                    damagePoint = __0.damagePoint,
                    damageNormal = __0.damageNormal,
                    isExplosion = false,
                    critRate = 0f,
                    critDamageFactor = 1f,
                };

                TreasureTrueDamageScope.Enter();
                try
                {
                    __instance.Hurt(info);
                }
                finally
                {
                    TreasureTrueDamageScope.Exit();
                }
            }
            catch (Exception e)
            {
                Diag.Report("混元金斗真实伤害结算失败", e);
            }
        }
    }

    /// <summary>
    /// 混元金斗的命中提示：只有被加成的弹丸真正打中敌人时才弹
    /// 「额外造成 X 点伤害」；空枪 / 打墙 / 打队友都不提示。同一发只提示一次。
    /// </summary>
    [HarmonyPatch(typeof(DamageReceiver), nameof(DamageReceiver.Hurt))]
    internal static class HunYuanJinDouHitNoticePatch
    {
        [HarmonyPrefix]
        private static void BeforeHurt(DamageReceiver __instance, DamageInfo __0)
        {
            try
            {
                if (__instance == null || __0.isExplosion)
                    return;

                Projectile projectile = JinJiaoJianHitScopePatch.Current;
                if (projectile == null)
                    return;

                if (!HunYuanJinDouProjectilePatch.TryGetBonus(projectile, out var bonus))
                    return;

                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null || __0.fromCharacter != main)
                    return;

                if (__instance.IsMainCharacter || !Team.IsEnemy(main.Team, __instance.Team))
                    return;

                if (!HunYuanJinDouShotPatch.TryConsumeNotice())
                    return;

                TreasureNotice.Show(
                    TreasureIds.HunYuanJinDou,
                    string.Format("额外造成 <b>{0:0.#}</b> 点伤害", bonus.TotalBonus));

            }
            catch (Exception e)
            {
                Diag.Report("混元金斗命中提示失败", e);
            }
        }
    }
}
