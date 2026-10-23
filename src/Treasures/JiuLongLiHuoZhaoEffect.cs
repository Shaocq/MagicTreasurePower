using System;
using System.Collections.Generic;
using System.Reflection;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using MagicTreasurePower.Slots;

namespace MagicTreasurePower.Treasures
{
    /// <summary>
    /// 九龙离火罩：装备时给角色物品挂一组属性修饰，卸下时移除。
    /// <list type="bullet">
    /// <item>身体护甲 / 头部护甲 +5；</item>
    /// <item>火、毒、电、空间、幽灵、冰 六种元素抗性各 +20%（即元素承受 ×0.8）；</item>
    /// <item>背包容量 +20 格。</item>
    /// </list>
    ///
    /// <para><b>为什么改属性而不是直接改数值：</b></para>
    /// 原版护甲、元素抗性、背包容量全部从角色物品的属性（<c>BodyArmor</c> /
    /// <c>HeadArmor</c> / <c>ElementFactor_*</c> / <c>InventoryCapacity</c>）读取，
    /// 背包容量在 <c>CharacterMainControl.LateUpdate</c> 里每帧按属性重算。
    /// 只要往这些属性上加修饰符，原版逻辑会自动生效，也不会被频率刷新覆盖。
    ///
    /// <para><b>刷新方式：</b></para>
    /// 由 <see cref="ModBehaviour"/> 每 0.5 秒调用一次 <see cref="Reconcile"/>，
    /// 比对「是否装备」与「当前已应用的修饰符」，避免依赖装备槽事件的触发时机
    /// （读档、换图、槽位重建都能覆盖）。
    /// </summary>
    internal static class JiuLongLiHuoZhaoEffect
    {
        /// <summary>当前已应用的等级；等级变化时需要重新挂加成。</summary>
        private static int _appliedLevel = -1;

        /// <summary>六种非物理元素（物理本质是普通伤害，减它等于再加一层护甲）。</summary>
        private static readonly string[] ElementKeys =
        {
            "ElementFactor_Fire",
            "ElementFactor_Poison",
            "ElementFactor_Electricity",
            "ElementFactor_Space",
            "ElementFactor_Ghost",
            "ElementFactor_Ice",
        };

        /// <summary>修饰符来源标记，便于区分本 mod 加的那几条。</summary>
        private static readonly object Source = new object();

        private static Item _item;
        private static readonly List<Stat> _stats = new List<Stat>();
        private static readonly List<Modifier> _modifiers = new List<Modifier>();

        /// <summary>由 <see cref="ModBehaviour"/> 定时调用：让加成与装备状态保持一致。</summary>
        internal static void Reconcile()
        {
            try
            {
                Item treasure = TreasureSlots.GetEquippedItem(TreasureIds.JiuLongLiHuoZhao);
                if (treasure == null)
                {
                    RemoveAll();
                    return;
                }

                int level = TreasureProgression.GetLevel(treasure);

                CharacterMainControl main = CharacterMainControl.Main;
                Item item = main != null ? main.CharacterItem : null;
                if (item == null)
                    return;

                if (_item == item && _modifiers.Count > 0 && _appliedLevel == level)
                    return;

                RemoveAll();
                ApplyAll(item, level);
            }
            catch (Exception e)
            {
                Diag.Report("九龙离火罩加成刷新失败", e);
            }
        }

        /// <summary>移除所有加成（停用 mod 时调用）。</summary>
        internal static void RemoveAll()
        {
            for (int i = 0; i < _modifiers.Count; i++)
            {
                try
                {
                    Stat stat = _stats[i];
                    if (stat != null)
                        stat.RemoveModifier(_modifiers[i]);
                }
                catch (Exception e)
                {
                    Diag.Report("九龙离火罩移除加成失败", e);
                }
            }

            _modifiers.Clear();
            _stats.Clear();
            _item = null;
            _appliedLevel = -1;
        }

        private static void ApplyAll(Item item, int level)
        {
            _item = item;
            _appliedLevel = level;

            float armor = TreasureBalance.JiuLongArmorOf(level);
            float resistance = -TreasureBalance.JiuLongResistOf(level);
            float slots = TreasureBalance.JiuLongSlotsOf(level);

            AddModifier(item, "BodyArmor", armor);
            AddModifier(item, "HeadArmor", armor);
            AddModifier(item, "InventoryCapacity", slots);

            foreach (string key in ElementKeys)
                AddModifier(item, key, resistance);
        }

        private static void AddModifier(Item item, string key, float value)
        {
            try
            {
                StatCollection collection = item.Stats;
                if (collection == null)
                {
                    Diag.Warn($"九龙离火罩：角色物品没有属性集合，{key} 加成未生效");
                    return;
                }

                Stat stat = collection[key];
                if (stat == null)
                {
                    // 理论上原版角色物品都有这些属性；缺失时补一个中性基础值。
                    // Stat.Initialize 是 internal，只能反射调用。
                    float baseValue = key.StartsWith("ElementFactor_", StringComparison.Ordinal) ? 1f : 0f;
                    stat = new Stat(key, baseValue);
                    collection.Add(stat);

                    MethodInfo initialize = typeof(Stat).GetMethod(
                        "Initialize", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                    if (initialize == null)
                    {
                        Diag.Warn($"九龙离火罩：属性 {key} 不存在且无法补建，加成未生效");
                        return;
                    }

                    initialize.Invoke(stat, new object[] { collection });
                }

                Modifier modifier = new Modifier(ModifierType.Add, value, Source);
                stat.AddModifier(modifier);

                _stats.Add(stat);
                _modifiers.Add(modifier);
            }
            catch (Exception e)
            {
                Diag.Report($"九龙离火罩：添加属性 {key} 失败", e);
            }
        }
    }
}
