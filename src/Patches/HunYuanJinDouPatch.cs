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
    /// 命中则在**护甲结算之前**给本次射击的子弹额外附加
    /// 「枪械基础伤害 × 1.5」的伤害（爆炸伤害不加成）。
    ///
    /// <para><b>加的是固定值，不是倍率：</b></para>
    /// 「枪械基础伤害」取枪的 <c>Stat.BaseValue</c>，即不含配件加成、
    /// 子弹倍率与角色伤害加成的本体数值；霰弹枪按 <c>ShotCount</c> 均摊、
    /// 穿透后每一段命中都带这笔值。这笔值与普通直击伤害一样会被暴击、
    /// 距离衰减和目标护甲影响。
    ///
    /// <para><b>为什么把掷骰放在 <c>ItemAgent_Gun.TransToFire</c>：</b></para>
    /// 游戏里「弹匣里的子弹」只是一个整数计数（<c>ItemSetting_Gun.BulletCount</c>），
    /// 没有逐颗的子弹对象；而 <c>TransToFire</c> 正好是「一次扳机 = 消耗一发弹」的入口，
    /// 内层 <c>for (ShotCount)</c> 循环只是霰弹枪的多颗弹丸。所以在它的前缀里掷一次骰，
    /// 本次射击的所有弹丸共享同一个结果，语义与「这一发子弹触发」一致，
    /// 换弹 / 退弹 / 读档 / 其它换弹 mod 都不会让判定错位。
    ///
    /// <para><b>为什么加成放在 <c>Projectile.Init</c>：</b></para>
    /// 直击伤害在开火瞬间按弹丸算好并写进 <c>ProjectileContext.damage</c>，
    /// 命中时才由 <c>Health.Hurt</c> 依次乘暴击系数与护甲系数。在这里把固定值加到
    /// <c>context.damage</c> 上，正好是「护甲结算前叠加、再一起被护甲削减」；
    /// 既不会影响爆炸伤害，也不会碰到别处产生的弹道（按枪械设置实例逐一比对）。
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
    /// 子弹初始化时套用混元金斗加成。只改直击伤害 <c>context.damage</c>，
    /// 不碰 <c>context.explosionDamage</c>。
    /// </summary>
    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Init), new[] { typeof(ProjectileContext) })]
    internal static class HunYuanJinDouProjectilePatch
    {
        /// <summary>一颗弹丸的加成信息。</summary>
        internal sealed class BonusData
        {
            /// <summary>整次射击的附加伤害总量（命中提示用）。</summary>
            internal float TotalBonus;
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

                // 固定值叠加，不乘倍率：命中时才会被暴击系数与护甲系数影响
                __instance.context.damage += bonus;

                // 登记加成信息：伤害数字上色与命中提示都靠它
                BoostedProjectiles.Add(__instance, new BonusData
                {
                    TotalBonus = HunYuanJinDouShotPatch.TotalBonus,
                });
            }
            catch (Exception e)
            {
                Diag.Report("混元金斗加成子弹伤害失败", e);
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
