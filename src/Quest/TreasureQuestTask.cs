using System;
using System.Collections.Generic;
using Duckov;
using Duckov.Buffs;
using Duckov.Quests;
using Duckov.Utilities;
using HarmonyLib;
using ItemStatsSystem;
using MagicTreasurePower.Patches;
using UnityEngine;

namespace MagicTreasurePower.Quests
{
    /// <summary>自定义任务目标类型。</summary>
    internal enum TreasureObjective
    {
        /// <summary>任意对局累计击杀精英敌人。</summary>
        EliteKillsTotal = 0,

        /// <summary>成功撤离一次。</summary>
        EvacuateOnce = 1,

        /// <summary>单局开局不带备用弹药、击杀 N 名敌人并成功撤离。</summary>
        NoAmmoRunKillsEvac = 2,

        /// <summary>单局内受到 N 种不同的异常状态（不要求撤离时仍带着异常）。</summary>
        DebuffKinds = 3,

        /// <summary>单局血量降到 10% 以下 N 次、不阵亡并撤离。</summary>
        LowHpNoDeathEvac = 4,

        /// <summary>单局累计承受伤害达到阈值并活着撤离。</summary>
        DamageTakenEvac = 5,

        /// <summary>单局撤离带出的物资总价值达到阈值。</summary>
        InventoryValueEvac = 6,

        /// <summary>从指定子场景成功撤离一次。</summary>
        EvacuateSceneOnce = 7,

        /// <summary>击杀指定数量的 BOSS（测试用）。</summary>
        BossKillsTotal = 8,

        /// <summary>受到指定异常状态一次（测试用）。</summary>
        SpecificDebuffOnce = 9,

        /// <summary>单局开启 N 个不同的物资箱并成功撤离。</summary>
        LootBoxesOpenedEvac = 10,
    }

    /// <summary>
    /// 法宝任务的目标实现。
    ///
    /// <para><b>生命周期（与原版 Task 一致）：</b></para>
    /// 任务模板与克隆都由原版流程创建；OnEnable / OnDisable 订阅、退订全局事件。
    /// 模板本身不在「活跃任务」里，所有回调先检查 <c>Master.Active</c>，模板上不计数。
    /// 目标打勾后由玩家在任务发布者处（或 QuestBoard 的按钮）调 <c>Quest.TryComplete</c> 交任务。
    ///
    /// <para><b>本局统计：</b></para>
    /// 不再依赖「进关卡那一刻正好是活跃任务」——只有任务在进入关卡前就接好时才成立。
    /// 现在改为<b>惰性开一局</b>：任何一次计数回调（受伤 / 击杀 / 上 Buff）在战局中发现本局尚未开始，
    /// 就地开新局；撤离时也补一次判断，保证「在战局里接取任务」（QuestBoard 随处接取）同样能结算。
    ///
    /// <para><b>状态保存：</b></para>
    /// 只保存「已完成」与「累计精英 / BOSS 击杀数」，本局临时计数不落盘。
    /// 存档值必须是 ES3 能识别类型的基元（这里用 int 位打包）——返回 <c>int[]</c> 会被写成
    /// 没有 <c>__type</c> 的裸数组，读档时 ES3 还原不出类型，导致整个任务存档加载失败。
    /// </summary>
    internal class TreasureQuestTask : Task
    {
        /// <summary>
        /// 「清浊除厄」计入的异常状态。
        ///
        /// 口径（2026-10-22 用户确认）：同一局内受到 <b>3 种不同</b>的异常即可，
        /// 不包含出血（1001/1002）、骨折（1003）、创伤（1004）；
        /// 点燃 / 恶心 / 燃烧 也算。
        /// </summary>
        private static readonly HashSet<int> DebuffIds = new HashSet<int>
        {
            1061, 1122, 1130,               // 中毒 / 弱毒 / 食物中毒
            1071, 1126, 1127, 1041, 1042,   // 触电 / 冰冻 / 冻结 / 震慑
            1124, 1111, 1112, 1117,         // 害怕 / 扰动 / 扭曲 / 碎裂
            1121, 1123, 1125,               // 点燃 / 恶心 / 燃烧
        };

        [SerializeField]
        private TreasureObjective objective;

        [SerializeField]
        private int requireCount = 1;

        [SerializeField]
        private float threshold = 100f;

        [SerializeField]
        private string requiredSceneId;

        [SerializeField]
        private int requiredBuffId;

        [SerializeField]
        private float lowHpThreshold = 0.1f;

        /// <summary>BOSS 目标是否要求「不同种类」（同一种 BOSS 只算一次）。</summary>
        [SerializeField]
        private bool requireDistinctBosses;

        /// <summary>
        /// 全部 BOSS 预设的 nameKey（共 49 种，取自游戏 CharacterRandomPreset）。
        ///
        /// 这张表决定「击杀过哪些 BOSS」的位掩码索引，**只能追加、不能重排或删除**，
        /// 否则老存档里已记录的 BOSS 会错位。
        /// </summary>
        private static readonly string[] BossKeys =
        {
            "Character_Alex", "Character_Fo", "Character_Jeff", "Character_Ming",
            "Character_Mud", "Character_Orange", "Character_Xavier",
            "Cname_BALeader", "Cname_Boss_3Shot", "Cname_Boss_Arcade",
            "Cname_Boss_Blue", "Cname_Boss_Fly", "Cname_Boss_Fly_Alone",
            "Cname_Boss_LionHead", "Cname_Boss_Red", "Cname_Boss_Shot",
            "Cname_Boss_Sniper", "Cname_Grenade", "Cname_Hunter",
            "Cname_IslandBoss", "Cname_Killa", "Cname_PMCLeader",
            "Cname_Prison_Boss", "Cname_RaiderIce", "Cname_Roadblock",
            "Cname_RPG", "Cname_SchoolBully", "Cname_SenorEngineer",
            "Cname_ServerGuardian", "Cname_ShortEagle", "Cname_Snow_BigIce",
            "Cname_Snow_Fleeze", "Cname_Snow_Igny", "Cname_SnowMan",
            "Cname_SnowMan_Island", "Cname_Speedy", "Cname_Speedy_Ice",
            "Cname_StormBoss1", "Cname_StormBoss1_Child", "Cname_StormBoss2",
            "Cname_StormBoss3", "Cname_StormBoss4", "Cname_StormBoss5",
            "Cname_Tagilla", "Cname_UltraMan", "Cname_Usec", "Cname_Vida",
            "Cname_WolfKing_Ice", "Cname_XING",
        };

        private static readonly Dictionary<string, int> BossKeyIndex = BuildBossKeyIndex();

        private static Dictionary<string, int> BuildBossKeyIndex()
        {
            var map = new Dictionary<string, int>(BossKeys.Length, StringComparer.Ordinal);
            for (int i = 0; i < BossKeys.Length; i++)
                map[BossKeys[i]] = i;
            return map;
        }

        /// <summary>跨局完成的进度（会存档）。</summary>
        private bool _done;

        private int _eliteKills;
        private int _bossKills;

        /// <summary>已击杀过的 BOSS 种类位掩码（配合 <see cref="BossKeys"/>）。</summary>
        private long _bossMask;

        /// <summary>本局（一次战局）统计，进图或首次计数时清零。</summary>
        private bool _runActive;

        private int _runKills;

        /// <summary>本局已受到的异常<b>种类数</b>（同一种异常重复给只算一次）。</summary>
        private int _runDebuffHits;

        /// <summary>本局已经算过的异常 Buff ID。</summary>
        private readonly HashSet<int> _runDebuffKinds = new HashSet<int>();

        private int _runLowHpCount;
        private bool _wasLowHp;
        private float _runDamage;
        private bool _runNoSpareAmmo;
        private bool _runDied;

        /// <summary>本局已开启的物资箱数量（同一个箱子反复开关只算一次）。</summary>
        private int _runLootBoxes;

        /// <summary>本局已经算过的物资箱实例 ID。</summary>
        private readonly HashSet<int> _runLootBoxIds = new HashSet<int>();

        private bool _hooked;

        /// <summary>最近一次主角受伤前的生命值，由 <see cref="TreasureHealthHurtProbe"/> 写入。</summary>
        internal static float _mainHealthBeforeHurt;

        internal TreasureObjective Objective
        {
            get { return objective; }
            set { objective = value; }
        }

        internal int RequireCount
        {
            get { return requireCount; }
            set { requireCount = value; }
        }

        internal float Threshold
        {
            get { return threshold; }
            set { threshold = value; }
        }

        internal string RequiredSceneId
        {
            get { return requiredSceneId; }
            set { requiredSceneId = value; }
        }

        internal int RequiredBuffId
        {
            get { return requiredBuffId; }
            set { requiredBuffId = value; }
        }

        internal float LowHpThreshold
        {
            get { return lowHpThreshold; }
            set { lowHpThreshold = value; }
        }

        internal bool RequireDistinctBosses
        {
            get { return requireDistinctBosses; }
            set { requireDistinctBosses = value; }
        }

        public override string Description
        {
            get
            {
                string progress;
                switch (objective)
                {
                    case TreasureObjective.EliteKillsTotal:
                        progress = $"击杀精英敌人（{_eliteKills}/{requireCount}）";
                        break;
                    case TreasureObjective.EvacuateOnce:
                        progress = "成功撤离一次";
                        break;
                    case TreasureObjective.NoAmmoRunKillsEvac:
                        progress = $"单局不带备用弹药击杀 {_runKills}/{requireCount} 名敌人并成功撤离";
                        break;
                    case TreasureObjective.DebuffKinds:
                        progress = $"单局受到 {_runDebuffHits}/{requireCount} 种不同的异常";
                        break;
                    case TreasureObjective.LowHpNoDeathEvac:
                        progress = $"单局血量降至 {lowHpThreshold * 100f:0}% 以下 {_runLowHpCount}/{requireCount} 次，不阵亡并撤离";
                        break;
                    case TreasureObjective.DamageTakenEvac:
                        progress = $"单局累计承受伤害 {_runDamage:0}/{threshold:0}，活着撤离";
                        break;
                    case TreasureObjective.InventoryValueEvac:
                        progress = $"单局撤离带出的物资总价值达到 {FormatAmount(threshold)}";
                        break;
                    case TreasureObjective.EvacuateSceneOnce:
                        progress = "成功从零号区撤离一次";
                        break;
                    case TreasureObjective.BossKillsTotal:
                        progress = requireDistinctBosses
                            ? $"击杀不同种类的 BOSS（{_bossKills}/{requireCount}）"
                            : $"击杀 BOSS 生物（{_bossKills}/{requireCount}）";
                        break;
                    case TreasureObjective.SpecificDebuffOnce:
                        progress = "受到一次骨折";
                        break;
                    case TreasureObjective.LootBoxesOpenedEvac:
                        progress = $"单局已开启物资箱 {_runLootBoxes}/{requireCount} 个，成功撤离后达成";
                        break;
                    default:
                        progress = "未知目标";
                        break;
                }

                return IsFinished() ? "✔ " + progress : progress;
            }
        }

        private void OnEnable()
        {
            Hook(true);
        }

        private void OnDisable()
        {
            Hook(false);
        }

        private void Hook(bool on)
        {
            if (_hooked == on)
                return;
            _hooked = on;

            try
            {
                if (on)
                {
                    Health.OnDead += OnHealthDead;
                    Health.OnHurt += OnHealthHurt;
                    LevelManager.OnLevelInitialized += OnLevelInitialized;
                    LevelManager.OnEvacuated += OnEvacuated;
                    TreasureQuestEvents.MainBuffAdded += OnMainBuffAdded;
                    InteractableLootbox.OnStartLoot += OnLootBoxOpened;
                }
                else
                {
                    Health.OnDead -= OnHealthDead;
                    Health.OnHurt -= OnHealthHurt;
                    LevelManager.OnLevelInitialized -= OnLevelInitialized;
                    LevelManager.OnEvacuated -= OnEvacuated;
                    TreasureQuestEvents.MainBuffAdded -= OnMainBuffAdded;
                    InteractableLootbox.OnStartLoot -= OnLootBoxOpened;
                }
            }
            catch (Exception e)
            {
                Diag.Report("法宝任务订阅事件失败", e);
            }
        }

        private bool IsLive
        {
            get
            {
                try
                {
                    return Master != null && Master.Active;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>任务 ID（日志用）：Task.ID 在同一任务里都是 1，光看它分不清是哪个任务。</summary>
        private int QuestId
        {
            get
            {
                try
                {
                    return Master != null ? Master.ID : ID;
                }
                catch
                {
                    return ID;
                }
            }
        }

        /// <summary>当前是否在战局里（基地不算）。</summary>
        /// <summary>大额数值按「万」显示，和任务详情文案保持一致。</summary>
        private static string FormatAmount(float value)
        {
            return value >= 10000f
                ? (value / 10000f).ToString("0.#") + " 万"
                : value.ToString("0");
        }

        private static bool InRaid
        {
            get
            {
                try
                {
                    LevelManager manager = LevelManager.Instance;
                    return manager != null && manager.IsRaidMap;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// 惰性开一局：任务可能是在战局里才接的（QuestBoard 随处接取），
        /// 这时 <see cref="OnLevelInitialized"/> 早已过去，必须在一第一次计数时就地开新局。
        /// </summary>
        private void EnsureRunStarted()
        {
            if (_runActive || !InRaid)
                return;

            ResetRun();
            _runActive = true;
            CaptureSpareAmmo();
            Diag.Log($"法宝任务 {QuestId}：开始统计本局");
        }

        private void OnLevelInitialized()
        {
            try
            {
                if (!IsLive)
                    return;

                // 换图一律作废上一局的统计，即便上一局没有正常撤离
                _runActive = false;

                if (InRaid)
                {
                    EnsureRunStarted();
                    Diag.Log($"法宝任务 {QuestId}：进入战局，本局统计已重置");
                }
            }
            catch (Exception e)
            {
                Diag.Report("法宝任务：进入关卡处理失败", e);
            }
        }

        private void ResetRun()
        {
            _runKills = 0;
            _runDebuffHits = 0;
            _runDebuffKinds.Clear();
            _runLowHpCount = 0;
            _wasLowHp = false;
            _runDamage = 0f;
            _runNoSpareAmmo = false;
            _runDied = false;
            _runLootBoxes = 0;
            _runLootBoxIds.Clear();
        }

        private void CaptureSpareAmmo()
        {
            _runNoSpareAmmo = true;

            try
            {
                CharacterMainControl main = CharacterMainControl.Main;
                Inventory inventory = main != null && main.CharacterItem != null
                    ? main.CharacterItem.Inventory
                    : null;

                if (inventory == null)
                {
                    _runNoSpareAmmo = false;
                    return;
                }

                Tag bulletTag = GameplayDataSettings.Tags.Bullet;

                if (bulletTag == null)
                {
                    _runNoSpareAmmo = false;
                    return;
                }

                if (ContainsBullet(inventory, bulletTag, 0))
                    _runNoSpareAmmo = false;
            }
            catch (Exception e)
            {
                _runNoSpareAmmo = false;
                Diag.Report("法宝任务：检查备用弹药失败", e);
            }
        }

        /// <summary>背包里（含容器内）是否有带「弹药」标签的物品。</summary>
        private static bool ContainsBullet(Inventory inventory, Tag bulletTag, int depth)
        {
            if (inventory == null || depth > 4)
                return false;

            try
            {
                foreach (Item item in inventory)
                {
                    if (item == null)
                        continue;

                    if (item.Tags != null && item.Tags.Contains(bulletTag))
                        return true;

                    if (item.Inventory != null && ContainsBullet(item.Inventory, bulletTag, depth + 1))
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private void OnHealthDead(Health health, DamageInfo damageInfo)
        {
            try
            {
                if (!IsLive || health == null)
                    return;

                if (health.IsMainCharacterHealth)
                {
                    _runDied = true;
                    return;
                }

                if (!InRaid)
                    return;

                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null || damageInfo.fromCharacter != main)
                    return;

                CharacterMainControl victim = health.TryGetCharacter();
                if (victim == null)
                    return;

                // 只统计敌对单位，避免打死友方 / 中立单位也计数
                if (!Team.IsEnemy(main.Team, victim.Team))
                    return;

                if (objective == TreasureObjective.EliteKillsTotal)
                {
                    if (TreasureKillExp.ClassifyExperience(victim) == 4)
                    {
                        bool wasFinished = CheckFinished();
                        _eliteKills++;

                        if (!wasFinished)
                            ReportStatusChanged();
                    }
                }
                else if (objective == TreasureObjective.BossKillsTotal)
                {
                    if (TreasureKillExp.ClassifyExperience(victim) == 9)
                    {
                        // 「不同种类」模式下，同一种 BOSS 重复击杀不计数
                        if (!requireDistinctBosses || TryRecordBossKind(victim))
                        {
                            bool wasFinished = CheckFinished();
                            _bossKills++;

                            if (!wasFinished)
                                ReportStatusChanged();
                        }
                    }
                }
                else if (objective == TreasureObjective.NoAmmoRunKillsEvac)
                {
                    EnsureRunStarted();
                    if (!_runActive)
                        return;

                    _runKills++;
                    ReportStatusChanged();
                }
            }
            catch (Exception e)
            {
                Diag.Report("法宝任务：击杀计数失败", e);
            }
        }

        /// <summary>记录一种被击杀的 BOSS；返回 true 表示这是新种类（同种类重复击杀返回 false）。</summary>
        private bool TryRecordBossKind(CharacterMainControl victim)
        {
            try
            {
                CharacterRandomPreset preset = victim != null ? victim.characterPreset : null;
                string key = preset != null ? preset.nameKey : null;

                if (string.IsNullOrEmpty(key))
                    return false;

                int index;
                if (!BossKeyIndex.TryGetValue(key, out index))
                {
                    Diag.Warn($"法宝任务：BOSS「{key}」不在种类表里，本次击杀不计入");
                    return false;
                }

                long bit = 1L << index;
                if ((_bossMask & bit) != 0)
                    return false;

                _bossMask |= bit;
                Diag.Log($"法宝任务 {QuestId}：新 BOSS 种类 {key}（{_bossKills + 1}/{requireCount}）");
                return true;
            }
            catch (Exception e)
            {
                Diag.Report("法宝任务：记录 BOSS 种类失败", e);
                return false;
            }
        }

        private void OnHealthHurt(Health health, DamageInfo damageInfo)
        {
            try
            {
                if (!IsLive || health == null || !health.IsMainCharacterHealth)
                    return;

                if (!InRaid)
                    return;

                EnsureRunStarted();
                if (!_runActive)
                    return;

                // 用「受伤前后生命值差」统计实际承受伤害：
                // damageInfo.finalDamage 是护甲结算后的值，但阴阳镜的减伤是在
                // Health.set_CurrentHealth 里对实际扣血做乘算的，不会回写到它上面。
                float taken = damageInfo.finalDamage;

                if (_mainHealthBeforeHurt > 0f && _mainHealthBeforeHurt >= health.CurrentHealth)
                    taken = _mainHealthBeforeHurt - health.CurrentHealth;

                _runDamage += Mathf.Max(0f, taken);

                if (objective != TreasureObjective.LowHpNoDeathEvac)
                    return;

                float max = health.MaxHealth;
                if (max <= 0f)
                    return;

                float ratio = health.CurrentHealth / max;
                float thresholdRatio = Mathf.Clamp(lowHpThreshold, 0.01f, 0.99f);

                if (!_wasLowHp && ratio <= thresholdRatio)
                {
                    bool wasFinished = CheckFinished();
                    _runLowHpCount++;
                    _wasLowHp = true;

                    if (!wasFinished)
                        ReportStatusChanged();
                }
                else if (_wasLowHp && ratio >= thresholdRatio + 0.05f)
                {
                    _wasLowHp = false;
                }
            }
            catch (Exception e)
            {
                Diag.Report("法宝任务：受伤计数失败", e);
            }
        }

        private void OnMainBuffAdded(int buffId)
        {
            try
            {
                if (!IsLive || !InRaid)
                    return;

                if (objective == TreasureObjective.SpecificDebuffOnce)
                {
                    if (buffId == requiredBuffId && !_done)
                    {
                        _done = true;
                        Diag.Log($"法宝任务 {QuestId}：目标达成（受到 Buff {buffId}）");
                        ReportStatusChanged();
                    }
                    return;
                }

                if (objective != TreasureObjective.DebuffKinds)
                    return;

                if (!DebuffIds.Contains(buffId))
                    return;

                EnsureRunStarted();
                if (!_runActive)
                    return;

                // 同一种异常重复给只算一次，按「不同种类」计数
                if (!_runDebuffKinds.Add(buffId))
                    return;

                bool wasFinished = CheckFinished();

                _runDebuffHits++;
                Diag.Log($"法宝任务 {QuestId}：新异常种类 {buffId}（{_runDebuffHits}/{requireCount}）");

                // 只要求「这一局内受到过 N 种」，不要求撤离时仍然带着异常：
                // 计数够了当场打勾，之后是否撤离都不影响
                if (_runDebuffHits >= requireCount && !_done)
                {
                    _done = true;
                    Diag.Log($"法宝任务 {QuestId}：目标达成（本局已受到 {_runDebuffHits} 种异常）");
                }

                // 已经完成过的任务不再重复上报，避免刷「任务目标完成」通知和音效
                if (!wasFinished)
                    ReportStatusChanged();
            }
            catch (Exception e)
            {
                Diag.Report("法宝任务：异常状态计数失败", e);
            }
        }

        /// <summary>
        /// 物资箱计数：原版 <see cref="InteractableLootbox.OnStartLoot"/> 在战利品界面真正打开的瞬间触发。
        /// 同一个箱子反复开关只算一次（按实例 ID 去重）。
        /// </summary>
        private void OnLootBoxOpened(InteractableLootbox box)
        {
            try
            {
                if (!IsLive || !InRaid)
                    return;

                if (objective != TreasureObjective.LootBoxesOpenedEvac)
                    return;

                EnsureRunStarted();
                if (!_runActive)
                    return;

                if (box == null)
                    return;

                // 宠物背包、尸体掉落等「挂在对象自己身上的容器」不算物资箱
                if (box.GetComponent<Inventory>() != null)
                    return;

                CharacterMainControl main = CharacterMainControl.Main;
                if (main != null && box.transform.IsChildOf(main.transform))
                    return;

                try
                {
                    LevelManager manager = LevelManager.Instance;
                    PetProxy pet = manager != null ? manager.PetProxy : null;
                    if (pet != null && pet.transform != null &&
                        box.transform.IsChildOf(pet.transform))
                        return;
                }
                catch
                {
                }

                if (!_runLootBoxIds.Add(box.GetInstanceID()))
                    return;

                _runLootBoxes++;
                Diag.Log($"法宝任务 {QuestId}：本局已开启物资箱 {_runLootBoxes}/{requireCount}");
                ReportStatusChanged();
            }
            catch (Exception e)
            {
                Diag.Report("法宝任务：物资箱计数失败", e);
            }
        }

        private void OnEvacuated(EvacuationInfo info)
        {
            try
            {
                if (!IsLive)
                    return;

                // 战局中途接取的任务，OnLevelInitialized 早已过去，这里补开一局
                EnsureRunStarted();

                bool run = _runActive;
                bool wasDone = _done;
                CharacterMainControl main = CharacterMainControl.Main;

                switch (objective)
                {
                    case TreasureObjective.EvacuateOnce:
                        _done = true;
                        break;

                    case TreasureObjective.NoAmmoRunKillsEvac:
                        if (run && _runNoSpareAmmo && _runKills >= requireCount)
                            _done = true;
                        break;

                    case TreasureObjective.DebuffKinds:
                        // 只要本局受过 N 种异常即可，不要求撤离时仍带着异常
                        if (run && _runDebuffHits >= requireCount)
                            _done = true;
                        break;

                    case TreasureObjective.LowHpNoDeathEvac:
                        if (run && !_runDied && _runLowHpCount >= requireCount)
                            _done = true;
                        break;

                    case TreasureObjective.DamageTakenEvac:
                        if (run && !_runDied && _runDamage >= threshold)
                            _done = true;
                        break;

                    case TreasureObjective.InventoryValueEvac:
                        if (run && !_runDied && InventoryValue(main) >= threshold)
                            _done = true;
                        break;

                    case TreasureObjective.EvacuateSceneOnce:
                        if (SceneMatches(info.subsceneID))
                            _done = true;
                        break;

                    case TreasureObjective.LootBoxesOpenedEvac:
                        if (run && !_runDied && _runLootBoxes >= requireCount)
                            _done = true;
                        break;
                }

                Diag.Log(
                    $"法宝任务 {QuestId}：撤离结算 → {(_done ? "完成" : "未完成")}" +
                    $"（本局统计={run}，承伤={_runDamage:0}，击杀={_runKills}，" +
                    $"低血={_runLowHpCount}，异常={_runDebuffHits}，物资箱={_runLootBoxes}，" +
                    $"物资={InventoryValue(main)}，场景={info.subsceneID}）");

                _runActive = false;

                // 只在「这次撤离刚好把任务打完」时上报一次；
                // 已经完成但还没交任务时再撤离不会重复通知
                if (_done && !wasDone)
                    ReportStatusChanged();
            }
            catch (Exception e)
            {
                Diag.Report("法宝任务：撤离结算失败", e);
            }
        }

        /// <summary>
        /// 撤离场景判定。
        ///
        /// <c>EvacuationInfo.subsceneID</c> 给的是<b>子场景</b> ID，同一张图会有多个：
        /// 零号区实测撤离时拿到的是 <c>Level_GroundZero_1</c>，而不是 <c>Level_GroundZero_Main</c>。
        /// 所以这里按前缀匹配，<c>Level_GroundZero</c> 同时覆盖 Main / 1 / Cave。
        /// </summary>
        private bool SceneMatches(string subsceneId)
        {
            if (string.IsNullOrEmpty(requiredSceneId) || string.IsNullOrEmpty(subsceneId))
                return false;

            return subsceneId.StartsWith(requiredSceneId, StringComparison.OrdinalIgnoreCase);
        }


        private static long InventoryValue(CharacterMainControl main)
        {
            Item characterItem = main != null ? main.CharacterItem : null;
            Inventory inventory = characterItem != null ? characterItem.Inventory : null;
            return inventory != null ? SumValue(inventory, 0) : 0L;
        }

        private static long SumValue(Inventory inventory, int depth)
        {
            if (inventory == null || depth > 4)
                return 0L;

            long sum = 0L;

            try
            {
                foreach (Item item in inventory)
                {
                    if (item == null)
                        continue;

                    sum += (long)item.Value * Mathf.Max(1, item.StackCount);

                    if (item.Inventory != null)
                        sum += SumValue(item.Inventory, depth + 1);
                }
            }
            catch
            {
            }

            return sum;
        }

        protected override bool CheckFinished()
        {
            if (_done)
                return true;

            if (objective == TreasureObjective.EliteKillsTotal)
                return _eliteKills >= requireCount;

            if (objective == TreasureObjective.BossKillsTotal)
                return _bossKills >= requireCount;

            return false;
        }

        public override object GenerateSaveData()
        {
            // 位打包成一个 64 位整数：ES3 只对基元值写 __type，读档时才能还原；
            // 返回 int[] / List 会写成无类型的裸数组，读档时 ES3 抛异常，
            // 整个任务存档（包括原版任务）都会被丢弃。
            //
            // 布局：bit0 = 已完成，bit1-8 = 精英击杀，
            //       bit9-57 = 已击杀 BOSS 种类掩码（49 位），bit58-63 = BOSS 击杀总数（非去重模式用）
            long packed = (_done ? 1L : 0L)
                          | ((long)Mathf.Clamp(_eliteKills, 0, 0xFF) << 1)
                          | (_bossMask << 9)
                          | ((long)Mathf.Clamp(_bossKills, 0, 0x3F) << 58);
            return packed;
        }

        public override void SetupSaveData(object data)
        {
            try
            {
                // 兼容最早那版 mod 写下的 int[]（[done, elite, boss]）
                if (data is int[] legacy)
                {
                    _done = legacy.Length > 0 && legacy[0] != 0;
                    _eliteKills = legacy.Length > 1 ? legacy[1] : 0;
                    _bossKills = legacy.Length > 2 ? legacy[2] : 0;
                    return;
                }

                // 当前版本：long 位打包（含 BOSS 种类掩码）
                if (data is long current)
                {
                    ApplyPacked(current);
                    return;
                }

                // 上一版：int 位打包（只有 done / elite / bossKills）
                int legacyPacked;
                if (data is int i)
                    legacyPacked = i;
                else if (data is short s)
                    legacyPacked = s;
                else if (data is byte b)
                    legacyPacked = b;
                else if (data is string text && int.TryParse(text, out int parsed))
                    legacyPacked = parsed;
                else
                    return;

                _done = (legacyPacked & 1) != 0;
                _eliteKills = (legacyPacked >> 1) & 0xFF;
                _bossKills = (legacyPacked >> 9) & 0xFF;
            }
            catch (Exception e)
            {
                Diag.Report("法宝任务：读取存档失败", e);
            }
        }

        private const long BossMaskBits = (1L << 49) - 1;

        private void ApplyPacked(long packed)
        {
            _done = (packed & 1L) != 0;
            _eliteKills = (int)((packed >> 1) & 0xFF);
            _bossMask = (packed >> 9) & BossMaskBits;

            if (!requireDistinctBosses)
            {
                _bossKills = (int)((packed >> 58) & 0x3F);
                return;
            }

            // 去重模式：完成数 = 掩码里 1 的个数
            _bossKills = 0;
            long mask = _bossMask;

            while (mask != 0)
            {
                _bossKills += (int)(mask & 1L);
                mask >>= 1;
            }
        }
    }

    /// <summary>任务与其它系统之间的轻量事件桥。</summary>
    internal static class TreasureQuestEvents
    {
        internal static event Action<int> MainBuffAdded;

        internal static void NotifyMainBuffAdded(int buffId)
        {
            try
            {
                MainBuffAdded?.Invoke(buffId);
            }
            catch (Exception e)
            {
                Diag.Report("法宝任务：广播异常状态失败", e);
            }
        }
    }

    /// <summary>主角获得 Buff 时通知任务（用于「清浊除厄」计数）。</summary>
    [HarmonyPatch(typeof(CharacterBuffManager), nameof(CharacterBuffManager.AddBuff))]
    internal static class TreasureQuestBuffHook
    {
        [HarmonyPostfix]
        private static void AfterAddBuff(CharacterBuffManager __instance, Buff buffPrefab)
        {
            try
            {
                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null || __instance == null || __instance.Master != main || buffPrefab == null)
                    return;

                TreasureQuestEvents.NotifyMainBuffAdded(buffPrefab.ID);
            }
            catch (Exception e)
            {
                Diag.Report("法宝任务：通知异常状态失败", e);
            }
        }
    }

    /// <summary>
    /// 主角受伤前记录生命值。
    ///
    /// 「坚忍之守」要统计玩家**实际承受**的伤害，而 <c>DamageInfo.finalDamage</c>
    /// 只是护甲结算后的值——阴阳镜的减伤是在 <c>Health.set_CurrentHealth</c> 里
    /// 对实际扣血再乘算的，不会回写这个字段。用受伤前后的生命值差最准确。
    /// </summary>
    [HarmonyPatch(typeof(Health), nameof(Health.Hurt))]
    internal static class TreasureHealthHurtProbe
    {
        [HarmonyPrefix]
        private static void Prefix(Health __instance)
        {
            try
            {
                CharacterMainControl main = CharacterMainControl.Main;
                if (main != null && __instance != null && __instance == main.Health)
                    TreasureQuestTask._mainHealthBeforeHurt = __instance.CurrentHealth;
            }
            catch (Exception e)
            {
                Diag.Report("法宝任务：记录受伤前生命值失败", e);
            }
        }
    }

    /// <summary>记录接取 / 交任务，方便在日志里确认任务状态流转。</summary>
    [HarmonyPatch(typeof(QuestManager), nameof(QuestManager.ActivateQuest))]
    internal static class TreasureQuestActivateLogPatch
    {
        [HarmonyPostfix]
        private static void Postfix(int id)
        {
            try
            {
                if (TreasureQuestRegistrar.IsTreasureQuest(id))
                    Diag.Log($"法宝任务 {id}：已接取");
            }
            catch (Exception e)
            {
                Diag.Report("法宝任务：记录接取状态失败", e);
            }
        }
    }

    /// <summary>记录任务交掉（TalkComplete）。</summary>
    [HarmonyPatch(typeof(Quest), nameof(Quest.TryComplete))]
    internal static class TreasureQuestCompleteLogPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Quest __instance, bool __result)
        {
            try
            {
                if (__instance != null && __result && TreasureQuestRegistrar.IsTreasureQuest(__instance.ID))
                    Diag.Log($"法宝任务 {__instance.ID}：已交任务");
            }
            catch (Exception e)
            {
                Diag.Report("法宝任务：记录交任务状态失败", e);
            }
        }
    }
}
