using System;
using System.Reflection;
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
    /// 翻天印：每次射击（每消耗一发弹匣子弹）有 20% 概率同时触发两个效果——
    /// 本次不消耗弹药，并额外再射出一发子弹。
    ///
    /// <para><b>掷骰位置</b>：与混元金斗一致，放在 <c>ItemAgent_Gun.TransToFire</c> 前缀，
    /// 「一次扳机 = 一次判定」，霰弹枪的多颗弹丸共享同一个结果。</para>
    ///
    /// <para><b>不消耗弹药</b>：<c>ItemSetting_Gun.UseABullet</c> 全游戏只有
    /// <c>TransToFire</c> 一个调用点，触发时用前缀返回 false 直接跳过，
    /// <c>BulletCount</c> 保持不变，也不会误触发自动换弹。</para>
    ///
    /// <para><b>额外一发</b>：不复制瞄准与散射逻辑，而是在 <c>ShootOneBullet</c>
    /// 前缀里记录本次射击的枪口位置 / 方向 / 首发检测点，再由 <c>TransToFire</c>
    /// 后置用同样的参数调用一次原版 <c>ShootOneBullet</c>。额外那发走完整原版
    /// 射击管线（自己掷散射、命中、暴击、护甲），普通枪就是实打实多一发完整伤害，
    /// 霰弹枪按原版规则仍是「总伤害 ÷ ShotCount」。</para>
    /// </summary>
    [HarmonyPatch(typeof(ItemAgent_Gun), "TransToFire")]
    internal static class FanTianYinShotPatch
    {
        /// <summary>额外那发子弹的伤害系数（按法宝等级取值）。</summary>
        private static float _firingExtraFactor = 1f;

        /// <summary>供 <see cref="FanTianYinProjectilePatch"/> 读取。</summary>
        internal static float FiringExtraFactor => _firingExtraFactor;

        /// <summary>原版 <c>ShootOneBullet</c>（私有方法，只在这里反射调用）。</summary>
        private static readonly MethodInfo ShootOneBulletMethod =
            AccessTools.Method(
                typeof(ItemAgent_Gun),
                "ShootOneBullet",
                new[] { typeof(Vector3), typeof(Vector3), typeof(Vector3) });

        /// <summary>一次射击的触发状态；null 表示本次未触发。</summary>
        private sealed class ShotState
        {
            /// <summary>触发的枪（用来比对弹药消耗与弹丸来源）。</summary>
            internal ItemSetting_Gun Gun;

            /// <summary>本次射击是否真的发出过弹丸（弹药耗尽时原版会提前返回）。</summary>
            internal bool Fired;

            internal Vector3 MuzzlePoint;
            internal Vector3 Direction;
            internal Vector3 FirstFrameStart;
        }

        private static ShotState _current;

        /// <summary>是否正在补射翻天印的那一发（期间产出的弹丸只归翻天印，不再吃混元金斗加成）。</summary>
        private static bool _firingExtra;

        /// <summary>供混元金斗与伤害数字样式判断「这颗弹丸是不是翻天印追加的」。</summary>
        internal static bool IsFiringExtraShot => _firingExtra;

        /// <summary><c>UseABullet</c> 前缀用：本次射击是否要跳过弹药消耗。</summary>
        internal static bool ShouldSaveAmmo(ItemSetting_Gun gun)
        {
            try
            {
                return _current != null && gun != null && _current.Gun == gun;
            }
            catch
            {
                return false;
            }
        }

        /// <summary><c>ShootOneBullet</c> 前缀用：记录本次射击的真实参数（只记第一颗弹丸）。</summary>
        internal static void RecordShot(
            ItemAgent_Gun gun,
            Vector3 muzzlePoint,
            Vector3 direction,
            Vector3 firstFrameStart)
        {
            try
            {
                ShotState shot = _current;
                if (shot == null || shot.Fired || gun == null || shot.Gun != gun.GunItemSetting)
                    return;

                shot.Fired = true;
                shot.MuzzlePoint = muzzlePoint;
                shot.Direction = direction;
                shot.FirstFrameStart = firstFrameStart;
            }
            catch (Exception e)
            {
                Diag.Report("翻天印记录射击参数失败", e);
            }
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

                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null || __instance == null || __instance.Holder != main)
                    return;

                Item treasure = TreasureSlots.GetEquippedItem(TreasureIds.FanTianYin);
                if (treasure == null)
                    return;

                int level = TreasureProgression.GetLevel(treasure);
                if (UnityEngine.Random.value >= TreasureBalance.FanTianYinChanceOf(level))
                    return;

                _firingExtraFactor = TreasureBalance.FanTianYinExtraDamageOf(level);

                ItemSetting_Gun gun = __instance.GunItemSetting;
                if (gun == null)
                    return;

                _current = new ShotState { Gun = gun };
            }
            catch (Exception e)
            {
                // 掷骰失败按「未触发」处理，绝不影响射击本身
                _current = null;
                Diag.Report("翻天印射击掷骰失败", e);
            }
        }

        /// <summary>原版射击结束后补一发；参数取自本次射击的第一颗弹丸。</summary>
        [HarmonyPostfix]
        private static void AfterTransToFire(ItemAgent_Gun __instance)
        {
            try
            {
                ShotState shot = _current;
                if (shot == null || !shot.Fired || __instance == null)
                    return;

                if (ShootOneBulletMethod == null)
                {
                    Diag.Warn("未找到 ItemAgent_Gun.ShootOneBullet，翻天印的额外射击无法生效");
                    return;
                }

                // 补射期间打标记：额外那发只归翻天印（不叠加混元金斗），
                // 伤害数字用翻天印自己的金色样式
                _firingExtra = true;

                try
                {
                    ShootOneBulletMethod.Invoke(
                        __instance,
                        new object[] { shot.MuzzlePoint, shot.Direction, shot.FirstFrameStart });
                }
                finally
                {
                    _firingExtra = false;
                }

                TreasureNotice.Show(TreasureIds.FanTianYin, "节省弹药并额外射出一发");
            }
            catch (Exception e)
            {
                Diag.Report("翻天印追加射击失败", e);
            }
        }

        /// <summary>无论原方法正常结束还是抛异常，都恢复上一次的现场。</summary>
        [HarmonyFinalizer]
        private static Exception AfterTransToFireFinalizer(Exception __exception, ShotState __state)
        {
            _current = __state;
            return __exception;
        }
    }

    /// <summary>翻天印触发时跳过弹药消耗。</summary>
    [HarmonyPatch(typeof(ItemSetting_Gun), nameof(ItemSetting_Gun.UseABullet))]
    internal static class FanTianYinAmmoPatch
    {
        [HarmonyPrefix]
        private static bool BeforeUseABullet(ItemSetting_Gun __instance)
        {
            try
            {
                return !FanTianYinShotPatch.ShouldSaveAmmo(__instance);
            }
            catch
            {
                return true;
            }
        }
    }

    /// <summary>记录原版每次 <c>ShootOneBullet</c> 的参数，供翻天印的额外射击复用。</summary>
    [HarmonyPatch(typeof(ItemAgent_Gun), "ShootOneBullet")]
    internal static class FanTianYinShotRecorder
    {
        [HarmonyPrefix]
        private static void BeforeShootOneBullet(
            ItemAgent_Gun __instance,
            Vector3 __0,
            Vector3 __1,
            Vector3 __2)
        {
            FanTianYinShotPatch.RecordShot(__instance, __0, __1, __2);
        }
    }

    /// <summary>
    /// 给翻天印追加的那颗弹丸打标记：
    /// 命中时用它套用金色伤害数字，同时让混元金斗跳过这颗弹丸（不叠加加成）。
    ///
    /// <para>弹丸来自对象池，用弱引用表登记，被复用 / 销毁后自动出表。</para>
    /// </summary>
    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Init), new[] { typeof(ProjectileContext) })]
    internal static class FanTianYinProjectilePatch
    {
        private static readonly ConditionalWeakTable<Projectile, object> ExtraProjectiles =
            new ConditionalWeakTable<Projectile, object>();

        private static readonly object Marker = new object();

        /// <summary>该弹丸是否由翻天印补射产生。</summary>
        internal static bool IsExtra(Projectile projectile)
        {
            try
            {
                return projectile != null && ExtraProjectiles.TryGetValue(projectile, out _);
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

                // 每次开火都会对池里的弹丸重新 Init，先清掉上一次的标记
                ExtraProjectiles.Remove(__instance);

                if (!FanTianYinShotPatch.IsFiringExtraShot)
                    return;

                ProjectileContext context = __instance.context;

                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null || context.fromCharacter != main)
                    return;

                // 额外那发的强度按法宝等级缩放
                float factor = FanTianYinShotPatch.FiringExtraFactor;
                if (factor > 0f && factor < 1f)
                    __instance.context.damage *= factor;

                ExtraProjectiles.Add(__instance, Marker);
            }
            catch (Exception e)
            {
                Diag.Report("翻天印标记追加弹丸失败", e);
            }
        }
    }
}
