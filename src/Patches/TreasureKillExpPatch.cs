using System;
using System.Collections.Generic;
using System.Reflection;
using Duckov;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using MagicTreasurePower.Slots;
using MagicTreasurePower.Treasures;
using MagicTreasurePower.UI;

namespace MagicTreasurePower.Patches
{
    /// <summary>
    /// 击杀敌人时给已装备的法宝增加经验：
    /// 普通敌人 +1、精英 +4、BOSS +9；只有玩家本人的最后一击才算。
    ///
    /// <para><b>为什么用 <c>Health.OnDead</c> 静态事件：</b></para>
    /// 它是原版在角色死亡时统一触发的回调（HitMarker 等系统也用它），
    /// 比自己挂 <c>Health.Hurt</c> 前后缀更可靠，也不需要每次受伤都做判断。
    /// 由 <see cref="ModBehaviour"/> 在初始化时订阅、停用时退订。
    ///
    /// <para><b>怪物分类依据：</b></para>
    /// BOSS = <c>isBossCharacter</c> / <c>preset.isBoss</c>；
    /// 精英 = <c>CharacterRandomPreset.characterIconType == elete</c>；
    /// 商人 / 宠物不计经验；其余敌对单位为普通。
    /// </summary>
    internal static class TreasureKillExp
    {
        private static readonly FieldInfo IconTypeField =
            typeof(CharacterRandomPreset).GetField(
                "characterIconType", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// 原版里算普通、但按精英给经验（+4）的敌人。
        /// 用预设的本地化 key 判定，避免依赖显示名。
        /// </summary>
        private static readonly HashSet<string> EliteExperienceKeys =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "Cname_DengWolf",          // 劳登的狗
                "Cname_RobSpider",         // 机械蜘蛛
                "Cname_Boss_Fly_Child",    // 蝇蝇队员
                "Cname_Mushroom",          // 行走菇
                "Cname_Usec",              // 雇佣兵
            };

        private static bool _subscribed;

        internal static void Subscribe()
        {
            if (_subscribed)
                return;

            Health.OnDead += OnCharacterDead;
            _subscribed = true;
            Diag.Log("法宝经验：已订阅击杀事件");
        }

        internal static void Unsubscribe()
        {
            if (!_subscribed)
                return;

            Health.OnDead -= OnCharacterDead;
            _subscribed = false;
        }

        private static void OnCharacterDead(Health health, DamageInfo damageInfo)
        {
            try
            {
                if (health == null || health.IsMainCharacterHealth)
                    return;

                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null || damageInfo.fromCharacter != main)
                    return;

                CharacterMainControl victim = health.TryGetCharacter();
                if (victim == null || victim == main)
                    return;

                if (!Team.IsEnemy(main.Team, victim.Team))
                    return;

                int experience = ClassifyExperience(victim);
                if (experience <= 0)
                    return;

                Award(experience, victim);
            }
            catch (Exception e)
            {
                Diag.Report("击杀获得法宝经验失败", e);
            }
        }

        internal static int ClassifyExperience(CharacterMainControl victim)
        {
            CharacterRandomPreset preset = victim.characterPreset;

            if (victim.isBossCharacter || (preset != null && preset.isBoss))
                return 9;

            // 指定普通怪按精英给经验
            if (preset != null && !string.IsNullOrEmpty(preset.nameKey) &&
                EliteExperienceKeys.Contains(preset.nameKey))
            {
                return 4;
            }

            if (preset != null && IconTypeField != null)
            {
                try
                {
                    object raw = IconTypeField.GetValue(preset);
                    if (raw is CharacterIconTypes type)
                    {
                        if (type == CharacterIconTypes.merchant || type == CharacterIconTypes.pet)
                            return 0;

                        if (type == CharacterIconTypes.elete)
                            return 4;
                    }
                }
                catch
                {
                    // 读不到分类时按普通敌人处理
                }
            }

            return 1;
        }

        private static void Award(int experience, CharacterMainControl victim)
        {
            List<Slot> slots = TreasureSlots.EnsureSlots();
            if (slots == null)
                return;

            var awarded = new List<Item>(slots.Count);

            foreach (Slot slot in slots)
            {
                if (slot == null)
                    continue;

                Item item = slot.Content;
                if (item == null || !TreasureProgression.IsTreasure(item))
                    continue;

                TreasureProgression.AddExp(item, experience);
                awarded.Add(item);
            }

            if (awarded.Count == 0)
                return;

            string victimName = GetVictimName(victim);
            Diag.Log($"击杀 {victimName}：法宝经验 +{experience}（{awarded.Count} 件）");

            // 详情页正显示其中一件时立即刷新经验条
            foreach (Item item in awarded)
                TreasureLevelDetailsPatch.RefreshIfShowing(item);
        }

        private static string GetVictimName(CharacterMainControl victim)
        {
            try
            {
                CharacterRandomPreset preset = victim.characterPreset;
                if (preset != null && !string.IsNullOrEmpty(preset.nameKey))
                    return preset.nameKey;
            }
            catch
            {
            }

            return victim.name;
        }
    }
}
