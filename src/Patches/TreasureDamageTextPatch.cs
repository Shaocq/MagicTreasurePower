using System;
using System.Collections.Generic;
using FX;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace MagicTreasurePower.Patches
{
    /// <summary>触发型法宝命中时给伤害数字套用的样式。</summary>
    internal enum DamageTextStyle
    {
        None = 0,

        /// <summary>混元金斗：正红字 + 黑色描边。</summary>
        HunYuanJinDou = 1,

        /// <summary>翻天印：金色字 + 深棕描边。</summary>
        FanTianYin = 2,
    }

    /// <summary>各样式的具体配色与字号。</summary>
    internal static class DamageTextStyles
    {
        /// <summary>字号倍数（相对普通伤害数字）。</summary>
        internal const float SizeMultiplier = 2f;

        /// <summary>TMP 描边宽度（0~1，0.2 约等于明显的一圈边）。</summary>
        internal const float OutlineWidth = 0.2f;

        /// <summary>混元金斗：正红字。</summary>
        private static readonly Color HunYuanJinDouColor = new Color(1f, 0.15f, 0.15f, 1f);

        /// <summary>混元金斗：黑色描边。</summary>
        private static readonly Color HunYuanJinDouOutline = Color.black;

        /// <summary>翻天印：金色字。</summary>
        private static readonly Color FanTianYinColor = new Color(1f, 0.78f, 0.18f, 1f);

        /// <summary>翻天印：深棕描边（配金字，且与混元金斗的黑边区分开）。</summary>
        private static readonly Color FanTianYinOutline = new Color(0.24f, 0.12f, 0.02f, 1f);

        internal static Color NumberColor(DamageTextStyle style)
        {
            switch (style)
            {
                case DamageTextStyle.HunYuanJinDou:
                    return HunYuanJinDouColor;
                case DamageTextStyle.FanTianYin:
                    return FanTianYinColor;
                default:
                    return Color.white;
            }
        }

        internal static Color OutlineColor(DamageTextStyle style)
        {
            switch (style)
            {
                case DamageTextStyle.HunYuanJinDou:
                    return HunYuanJinDouOutline;
                case DamageTextStyle.FanTianYin:
                    return FanTianYinOutline;
                default:
                    return Color.black;
            }
        }
    }

    /// <summary>
    /// 在弹丸 <c>UpdateMoveAndCheck</c>（真正命中并结算伤害的方法）执行期间
    /// 打开作用域，记录这一发属于哪件法宝的触发样式。
    ///
    /// <para><b>弹丸来源如何区分：</b></para>
    /// 各法宝在自己的弹丸 Init 补丁里打标记：混元金斗加成过的弹丸、
    /// 翻天印追加的弹丸。翻天印的额外弹丸不会被混元金斗打标记（两者互斥），
    /// 这里仍然先判翻天印，保证样式唯一。
    ///
    /// <para><b>为什么还要在 <c>Health.Hurt</c> 再判一次：</b></para>
    /// 命中结算期间可能触发连锁伤害（例如敌人死亡爆炸），它们的弹出数字不应被染色，
    /// 因此用 <c>isExplosion</c> + 角色 / 武器比对把它们排除掉。
    /// </summary>
    [HarmonyPatch(typeof(Projectile), "UpdateMoveAndCheck")]
    internal static class TreasureHitScopePatch
    {
        /// <summary>一次命中结算的作用域现场（结构体承载，避免每次结算都分配对象）。</summary>
        private struct ScopeState
        {
            internal bool Active;
            internal DamageTextStyle Style;
            internal CharacterMainControl FromCharacter;
            internal int WeaponItemId;
        }

        private static ScopeState _current;

        /// <summary>当前命中结算对应的样式。</summary>
        internal static DamageTextStyle CurrentStyle => _current.Style;

        [HarmonyPrefix]
        private static void Before(Projectile __instance, out ScopeState __state)
        {
            __state = _current;
            _current = new ScopeState();

            try
            {
                DamageTextStyle style = DamageTextStyle.None;

                if (FanTianYinProjectilePatch.IsExtra(__instance))
                    style = DamageTextStyle.FanTianYin;
                else if (HunYuanJinDouProjectilePatch.IsBoosted(__instance))
                    style = DamageTextStyle.HunYuanJinDou;

                if (style == DamageTextStyle.None)
                    return;

                // ProjectileContext 是 struct，直接取值即可（无需判空）
                ProjectileContext context = __instance.context;

                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null || context.fromCharacter != main)
                    return;

                _current = new ScopeState
                {
                    Active = true,
                    Style = style,
                    FromCharacter = context.fromCharacter,
                    WeaponItemId = context.fromWeaponItemID,
                };
            }
            catch (Exception e)
            {
                _current = new ScopeState();
                Diag.Report("法宝伤害数字作用域判定失败", e);
            }
        }

        [HarmonyFinalizer]
        private static Exception After(Exception __exception, ScopeState __state)
        {
            _current = __state;
            return __exception;
        }

        /// <summary>判断这次结算的伤害是否来自触发型法宝的弹丸。</summary>
        internal static bool Matches(DamageInfo damageInfo)
        {
            try
            {
                if (!_current.Active)
                    return false;

                // 爆炸伤害、连锁伤害不染色
                if (damageInfo.isExplosion)
                    return false;

                // 混元金斗改成「结算后真实伤害」：直击那一下按原版白字，
                // 只有补的那一次真实伤害数字用它的红色样式
                if (_current.Style == DamageTextStyle.HunYuanJinDou
                    && !TreasureTrueDamageScope.Applying)
                {
                    return false;
                }

                return damageInfo.fromCharacter == _current.FromCharacter
                    && damageInfo.fromWeaponItemID == _current.WeaponItemId;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// 在 <c>Health.Hurt</c> 里判定本次伤害数字要用的样式；
    /// 作用域只在本次 <c>Hurt</c> 调用内部有效，异常时由终结器还原。
    /// </summary>
    [HarmonyPatch(typeof(Health), nameof(Health.Hurt))]
    internal static class TreasureDamagePopupPatch
    {
        private static DamageTextStyle _style;

        internal static DamageTextStyle Styling => _style;

        [HarmonyPrefix]
        private static void Before(DamageInfo damageInfo, out DamageTextStyle __state)
        {
            __state = _style;
            _style = DamageTextStyle.None;

            try
            {
                if (TreasureHitScopePatch.Matches(damageInfo))
                    _style = TreasureHitScopePatch.CurrentStyle;
            }
            catch (Exception e)
            {
                _style = DamageTextStyle.None;
                Diag.Report("法宝伤害数字标记失败", e);
            }
        }

        [HarmonyFinalizer]
        private static Exception After(Exception __exception, DamageTextStyle __state)
        {
            _style = __state;
            return __exception;
        }
    }

    /// <summary>
    /// 按样式改伤害数字的颜色与字号。原版在 <c>Health.Hurt</c> 里调用
    /// <c>PopText.Pop(text, pos, color, size, sprite)</c>，这里直接替换参数。
    /// </summary>
    [HarmonyPatch(typeof(PopText), nameof(PopText.Pop))]
    internal static class TreasurePopTextPatch
    {
        private static DamageTextStyle _applyToSetup;

        /// <summary>供 <see cref="TreasurePopTextOutlinePatch"/> 在实体初始化时读取。</summary>
        internal static DamageTextStyle ApplyToSetup => _applyToSetup;

        [HarmonyPrefix]
        private static void Before(ref Color color, ref float size, out DamageTextStyle __state)
        {
            __state = _applyToSetup;
            _applyToSetup = DamageTextStyle.None;

            try
            {
                DamageTextStyle style = TreasureDamagePopupPatch.Styling;
                if (style == DamageTextStyle.None)
                    return;

                color = DamageTextStyles.NumberColor(style);
                size *= DamageTextStyles.SizeMultiplier;
                _applyToSetup = style;
            }
            catch (Exception e)
            {
                _applyToSetup = DamageTextStyle.None;
                Diag.Report("法宝伤害数字改色失败", e);
            }
        }

        [HarmonyFinalizer]
        private static Exception After(Exception __exception, DamageTextStyle __state)
        {
            _applyToSetup = __state;
            return __exception;
        }
    }

    /// <summary>
    /// 给特殊伤害数字加描边（颜色随样式）。描边是 TMP 的材质属性，
    /// 只能在弹字实体创建（<c>SetupContent</c>）时设置。
    /// </summary>
    [HarmonyPatch(typeof(PopTextEntity), nameof(PopTextEntity.SetupContent))]
    internal static class TreasurePopTextOutlinePatch
    {
        /// <summary>被本 mod 加过描边的实体 → 所用样式；复用到普通伤害时负责清除。</summary>
        private static readonly Dictionary<int, DamageTextStyle> OutlinedEntities =
            new Dictionary<int, DamageTextStyle>();

        [HarmonyPostfix]
        private static void After(PopTextEntity __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                TextMeshPro tmp = __instance.Tmp;
                if (tmp == null)
                    return;

                int id = __instance.GetInstanceID();
                DamageTextStyle style = TreasurePopTextPatch.ApplyToSetup;

                if (style != DamageTextStyle.None)
                {
                    ApplyOutline(tmp, style);
                    OutlinedEntities[id] = style;
                }
                else if (OutlinedEntities.Remove(id))
                {
                    ClearOutline(tmp);
                }
            }
            catch (Exception e)
            {
                Diag.Report("法宝伤害数字描边失败", e);
            }
        }

        private static void ApplyOutline(TextMeshPro tmp, DamageTextStyle style)
        {
            // 先设颜色和宽度：TMP 的 outlineWidth 设置器会把材质实例化并同步 padding
            tmp.outlineColor = DamageTextStyles.OutlineColor(style);
            tmp.outlineWidth = DamageTextStyles.OutlineWidth;

            Material material = tmp.fontMaterial;
            if (material != null)
            {
                // 部分 TMP 着色器用关键字控制描边分支，开不开都不影响普通文字
                material.EnableKeyword(ShaderUtilities.Keyword_Outline);
            }

            tmp.UpdateMeshPadding();
            tmp.ForceMeshUpdate();
        }

        private static void ClearOutline(TextMeshPro tmp)
        {
            tmp.outlineWidth = 0f;
            tmp.ForceMeshUpdate();
        }
    }
}
