using System;
using System.Collections.Generic;
using System.Reflection;
using Duckov;
using Duckov.Quests;
using Duckov.Utilities;
using HarmonyLib;
using SodaCraft.Localizations;
using UnityEngine;

namespace MagicTreasurePower.Quests
{
    /// <summary>
    /// 注册七个法宝任务（全部挂在 Jeff 名下、同时可接可做），
    /// 每个任务奖励 1 个「法宝宝箱」。
    ///
    /// 原版任务来自 <c>QuestCollection</c> 里的预制体，且 <c>Quest.MeetsPrerequisit</c>
    /// 要求任务在关系图里有节点。这里：
    ///   1. 运行时按原版结构构造任务预制体（Quest 在根节点，Task / Reward 在子节点）并追加进任务集合；
    ///   2. 只跳过原版「必须存在关系图节点」这一条，等级 / Demo / 条件检查仍照原版执行。
    ///
    /// 与 <c>QuestManager.IsQuestAvaliable</c> 的关系：可用性（已接取 / 已完成不再出现）
    /// 完全交给原版判断，本 mod 不干预。
    /// </summary>
    internal static class TreasureQuestRegistrar
    {
        private const int FirstQuestId = 900001;
        private const int QuestCount = 7;

        /// <summary>「坚忍之守」单局承受伤害阈值（原版要求）。</summary>
        private const float DamageTakenThreshold = 400f;

        /// <summary>「满载而归」单局撤离物资价值阈值（用户指定 60 万）。</summary>
        private const float InventoryValueThreshold = 600000f;

        /// <summary>本 mod 任务的 GameObject 名称前缀（用于重载时识别自己的模板）。</summary>
        private const string QuestObjectPrefix = "MagicTreasurePower_Quest_";

        private static readonly HashSet<int> QuestIds = new HashSet<int>();
        private static bool _registered;

        /// <summary>已创建的任务模板（停用时统一销毁）。</summary>
        private static readonly List<GameObject> Templates = new List<GameObject>();

        internal static bool IsTreasureQuest(int questId)
        {
            return QuestIds.Contains(questId);
        }

        /// <summary>注册失败后的重试次数上限（数据资源启动瞬间可能还没就绪）。</summary>
        private const int MaxRetryAttempts = 3;

        private static bool _retryHooked;
        private static int _retryAttempts;

        internal static void RegisterAll()
        {
            if (_registered)
                return;

            try
            {
                TreasureBoxItem.Register();

                QuestCollection collection = GameplayDataSettings.QuestCollection;
                if (collection == null)
                {
                    Diag.Warn("法宝任务：找不到 QuestCollection，稍后在进入关卡时重试");
                    HookRetry();
                    return;
                }

                FieldInfo listField = typeof(QuestCollection).GetField(
                    "list", BindingFlags.Instance | BindingFlags.NonPublic);

                if (listField == null)
                {
                    Diag.Warn("法宝任务：QuestCollection.list 字段未找到");
                    return;
                }

                var list = listField.GetValue(collection) as List<Quest>;
                if (list == null)
                {
                    Diag.Warn("法宝任务：QuestCollection 列表为空，稍后重试");
                    HookRetry();
                    return;
                }

                int registered = 0;
                for (int i = 0; i < QuestCount; i++)
                {
                    Quest quest = BuildQuest(i, list);
                    if (quest == null)
                        continue;

                    QuestIds.Add(quest.ID);
                    registered++;
                    Diag.Log($"已注册法宝任务：{quest.ID}");
                }

                // 只有真的成功注册过才落标记；全失败时留给下次重试
                if (registered > 0)
                    _registered = true;
                else
                    HookRetry();
            }
            catch (Exception e)
            {
                Diag.Report("注册法宝任务失败", e);
                HookRetry();
            }
        }

        /// <summary>数据资源还没就绪时，挂一次「进入关卡」重试（有次数上限，避免刷屏）。</summary>
        private static void HookRetry()
        {
            if (_retryHooked || _registered)
                return;

            _retryHooked = true;

            try
            {
                LevelManager.OnLevelInitialized += RetryRegister;
            }
            catch (Exception e)
            {
                Diag.Report("法宝任务：挂载注册重试失败", e);
            }
        }

        private static void RetryRegister()
        {
            try
            {
                if (_registered)
                {
                    LevelManager.OnLevelInitialized -= RetryRegister;
                    return;
                }

                if (_retryAttempts >= MaxRetryAttempts)
                {
                    LevelManager.OnLevelInitialized -= RetryRegister;
                    Diag.Warn("法宝任务：重试注册达到上限，本局不再尝试");
                    return;
                }

                _retryAttempts++;
                RegisterAll();
            }
            catch (Exception e)
            {
                Diag.Report("法宝任务：重试注册失败", e);
            }
        }

        /// <summary>停用 mod 时把任务模板从集合里摘掉并销毁，避免重载后集合里留下空引用。</summary>
        internal static void UnregisterAll()
        {
            try
            {
                QuestCollection collection = GameplayDataSettings.QuestCollection;
                FieldInfo listField = typeof(QuestCollection).GetField(
                    "list", BindingFlags.Instance | BindingFlags.NonPublic);

                var list = listField != null
                    ? listField.GetValue(collection) as List<Quest>
                    : null;

                if (list != null)
                {
                    list.RemoveAll(q => IsOwnQuestObject(q));
                }

                foreach (GameObject go in Templates)
                {
                    if (go != null)
                        UnityEngine.Object.Destroy(go);
                }

                // 停用后不能再让重试钩子把任务重新塞回去
                LevelManager.OnLevelInitialized -= RetryRegister;
                _retryHooked = false;
                _retryAttempts = 0;

                Templates.Clear();
                QuestIds.Clear();
                _registered = false;
            }
            catch (Exception e)
            {
                Diag.Report("注销法宝任务失败", e);
            }
        }

        private static bool IsOwnQuestObject(Quest quest)
        {
            return quest != null &&
                   quest.gameObject != null &&
                   quest.gameObject.name.StartsWith(QuestObjectPrefix, StringComparison.Ordinal);
        }

        private static Quest BuildQuest(int index, List<Quest> collection)
        {
            int questId = FirstQuestId + index;

            // 重复初始化（mod 重载）时不重复添加；
            // 若 ID 被其它来源的任务占用（其它 mod），跳过而不是覆盖/接管
            for (int i = 0; i < collection.Count; i++)
            {
                Quest existing = collection[i];
                if (existing == null || existing.ID != questId)
                    continue;

                if (IsOwnQuestObject(existing))
                {
                    Templates.Add(existing.gameObject);
                    return existing;
                }

                Diag.Warn($"法宝任务 ID {questId} 已被其它任务占用（{existing.gameObject?.name}），跳过注册");
                return null;
            }

            var root = new GameObject(QuestObjectPrefix + questId);
            root.SetActive(false);

            // 与原生任务一致：Quest 在根节点，Task / Reward 各占一个子节点。
            // 子节点随父物体一起被 Instantiate 深拷贝并重映射 master 引用。
            Quest quest = root.AddComponent<Quest>();
            SetField(quest, "id", questId);
            SetField(quest, "displayName", "TreasureQuest_" + questId);
            SetField(quest, "description", "TreasureQuest_" + questId + "_Desc");
            SetField(quest, "requireLevel", 0);
            SetField(quest, "lockInDemo", false);
            SetField(quest, "requiredItemID", 0);
            SetField(quest, "requiredItemCount", 1);
            SetField(quest, "questGiverID", QuestGiverID.Jeff);

            LocalizationManager.SetOverrideText(GetNameKey(index), GetName(index));
            LocalizationManager.SetOverrideText(GetDescriptionKey(index), GetDescription(index));

            AddTasks(quest, index);
            AddReward(quest);

            UnityEngine.Object.DontDestroyOnLoad(root);
            Templates.Add(root);

            // 模板保持激活：Clone 会继承激活状态，否则任务组件的 OnEnable 不会执行。
            root.SetActive(true);

            collection.Add(quest);
            return quest;
        }

        /// <summary>单个任务目标配置：任务详情里的文字也由它生成，保证描述与实际判定一致。</summary>
        private struct QuestTaskSpec
        {
            internal TreasureObjective Objective;
            internal int Count;
            internal float Threshold;
            internal string SceneId;
            internal int BuffId;
            internal float LowHpThreshold;

            /// <summary>BOSS 目标是否要求不同种类。</summary>
            internal bool DistinctBosses;
        }

        private static QuestTaskSpec Spec(
            TreasureObjective objective, int count, float threshold = 0f,
            string sceneId = null, int buffId = 0, float lowHpThreshold = 0.1f,
            bool distinctBosses = false)
        {
            return new QuestTaskSpec
            {
                Objective = objective,
                Count = count,
                Threshold = threshold,
                SceneId = sceneId,
                BuffId = buffId,
                LowHpThreshold = lowHpThreshold,
                DistinctBosses = distinctBosses,
            };
        }

        /// <summary>各任务的目标配置（目标类型与数值都在这里，描述文案由它自动生成）。</summary>
        private static QuestTaskSpec[] GetTaskSpecs(int index)
        {
            switch (index)
            {
                case 0:
                    // 【测试期】原版：累计击杀 10 精英 + 成功撤离
                    // return new[] {
                    //     Spec(TreasureObjective.EliteKillsTotal, 10),
                    //     Spec(TreasureObjective.EvacuateOnce, 1),
                    // };
                    // 击杀 5 种不同的 BOSS（同一种 BOSS 重复击杀只算一次）
                    return new[] { Spec(TreasureObjective.BossKillsTotal, 5, distinctBosses: true) };

                case 1:
                    return new[] { Spec(TreasureObjective.NoAmmoRunKillsEvac, 7) };

                case 2:
                    // 单局内受到 3 种不同的异常（不含出血/骨折/创伤，点燃/恶心/燃烧也算），
                    // 并且撤离时身上还带着其中一种异常
                    return new[] { Spec(TreasureObjective.DebuffHitsEvac, 3) };

                case 3:
                    // 单局血量降到 10% 以下两次、不阵亡并撤离
                    return new[] { Spec(TreasureObjective.LowHpNoDeathEvac, 2) };

                case 4:
                    // 单局累计承受伤害 400，活着撤离
                    return new[] { Spec(TreasureObjective.DamageTakenEvac, 1, DamageTakenThreshold) };

                case 5:
                    // 单局撤离带出物资 60 万，并成功撤离
                    return new[] { Spec(TreasureObjective.InventoryValueEvac, 1, InventoryValueThreshold) };

                case 6:
                    // 按前缀匹配：零号区的子场景有 Main / 1 / Cave，实测撤离时拿到的是 _1
                    return new[] { Spec(TreasureObjective.EvacuateSceneOnce, 1, 0f, "Level_GroundZero") };

                default:
                    return new QuestTaskSpec[0];
            }
        }

        private static void AddTasks(Quest quest, int index)
        {
            QuestTaskSpec[] specs = GetTaskSpecs(index);

            for (int i = 0; i < specs.Length; i++)
            {
                QuestTaskSpec spec = specs[i];
                AddTask(quest, spec.Objective, spec.Count, spec.Threshold, i + 1,
                    spec.SceneId, spec.BuffId, spec.LowHpThreshold, spec.DistinctBosses);
            }
        }

        /// <summary>把目标配置翻译成一句人话，任务详情和任务条目里显示同一份语义。</summary>
        private static string DescribeTask(QuestTaskSpec spec)
        {
            switch (spec.Objective)
            {
                case TreasureObjective.EliteKillsTotal:
                    return $"任意对局累计击杀 {spec.Count} 个精英敌人，并成功撤离。";
                case TreasureObjective.EvacuateOnce:
                    return "成功撤离一次。";
                case TreasureObjective.NoAmmoRunKillsEvac:
                    return $"本局开局不带备用弹药，击杀至少 {spec.Count} 名敌人，并成功撤离。";
                case TreasureObjective.DebuffHitsEvac:
                    return $"单局内受到 {spec.Count} 种不同的异常状态（不包含出血、骨折、创伤）。";
                case TreasureObjective.LowHpNoDeathEvac:
                    return $"单局血量降到 {spec.LowHpThreshold * 100f:0}% 以下至少 {spec.Count} 次，本局不阵亡，并成功撤离。";
                case TreasureObjective.DamageTakenEvac:
                    return $"单局累计承受伤害达到 {spec.Threshold:0}，活着成功撤离。";
                case TreasureObjective.InventoryValueEvac:
                    return $"单局撤离带出的背包物资总价值达到 {FormatAmount(spec.Threshold)}，并成功撤离。";
                case TreasureObjective.EvacuateSceneOnce:
                    return "成功从零号区撤离一次。";
                case TreasureObjective.BossKillsTotal:
                    return spec.DistinctBosses
                        ? $"击杀 {spec.Count} 种不同的 BOSS 生物（同一种 BOSS 只算一次）。"
                        : $"击杀 {spec.Count} 个 BOSS 生物。";
                case TreasureObjective.SpecificDebuffOnce:
                    return "受到一次骨折。";
                default:
                    return "完成法宝试炼。";
            }
        }

        private static void AddTask(
            Quest quest, TreasureObjective objective, int count, float threshold, int taskId,
            string sceneId = null, int buffId = 0, float lowHpThreshold = 0.1f,
            bool distinctBosses = false)
        {
            var go = new GameObject("Task_" + taskId);
            go.transform.SetParent(quest.transform, false);

            TreasureQuestTask task = go.AddComponent<TreasureQuestTask>();
            task.Objective = objective;
            task.RequireCount = count;
            task.Threshold = threshold;
            task.RequiredSceneId = sceneId;
            task.RequiredBuffId = buffId;
            task.LowHpThreshold = lowHpThreshold;
            task.RequireDistinctBosses = distinctBosses;

            SetField(task, "master", quest);
            SetField(task, "id", taskId);

            quest.Tasks.Add(task);
        }

        private static void AddReward(Quest quest)
        {
            var go = new GameObject("Reward_1");
            go.transform.SetParent(quest.transform, false);

            RewardItem reward = go.AddComponent<RewardItem>();
            reward.itemTypeID = TreasureBoxItem.TypeId;
            reward.amount = 1;

            SetField(reward, "master", quest);
            SetField(reward, "id", 1);

            // rewards 是 Quest 的私有 List<Reward>，只能反射取出来追加
            FieldInfo rewardsField = typeof(Quest).GetField(
                "rewards", BindingFlags.Instance | BindingFlags.NonPublic);

            var rewards = rewardsField != null
                ? rewardsField.GetValue(quest) as List<Reward>
                : null;

            rewards?.Add(reward);
        }

        private static string GetNameKey(int index)
        {
            return "TreasureQuest_" + (FirstQuestId + index);
        }

        private static string GetDescriptionKey(int index)
        {
            return GetNameKey(index) + "_Desc";
        }

        private static string GetName(int index)
        {
            switch (index)
            {
                case 0: return "锋芒试炼";
                case 1: return "续航突围";
                case 2: return "清浊除厄";
                case 3: return "濒死生还";
                case 4: return "坚忍之守";
                case 5: return "满载而归";
                case 6: return "初来乍到";
                default: return "法宝任务";
            }
        }

        /// <summary>大额数值按「万」显示，读起来更直观。</summary>
        private static string FormatAmount(float value)
        {
            return value >= 10000f
                ? (value / 10000f).ToString("0.#") + " 万"
                : value.ToString("0");
        }

        /// <summary>任务详情里的描述：直接由目标配置生成，改阈值不会再出现「文字与判定不一致」。</summary>
        private static string GetDescription(int index)
        {
            QuestTaskSpec[] specs = GetTaskSpecs(index);

            if (specs.Length == 0)
                return "完成法宝试炼。";

            var builder = new System.Text.StringBuilder();

            for (int i = 0; i < specs.Length; i++)
            {
                if (i > 0)
                    builder.Append(' ');

                builder.Append(DescribeTask(specs[i]));
            }

            return builder.ToString();
        }

        private static void SetField(object target, string fieldName, object value)
        {
            try
            {
                // Task.master / Reward.master 等字段声明在基类里，
                // Type.GetField 不会返回私有基类字段，必须逐级向上找
                const BindingFlags Flags =
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

                Type type = target.GetType();
                FieldInfo field = null;

                while (type != null && field == null)
                {
                    field = type.GetField(fieldName, Flags);
                    type = type.BaseType;
                }

                if (field == null)
                {
                    Diag.Warn($"法宝任务：{target.GetType().Name} 未找到字段「{fieldName}」");
                    return;
                }

                field.SetValue(target, value);
            }
            catch (Exception e)
            {
                Diag.Report($"法宝任务：写入字段「{fieldName}」失败", e);
            }
        }
    }

    /// <summary>
    /// 让法宝任务不受 NodeCanvas 关系图约束。
    ///
    /// 原版 <c>MeetsPrerequisit</c> 的唯一额外要求是「任务在关系图里有节点」，
    /// 其余（等级、Demo 锁、prerequisite 条件）这里照原版逐条执行，
    /// 保证行为与原生任务一致。
    /// </summary>
    [HarmonyPatch(typeof(Quest), nameof(Quest.MeetsPrerequisit))]
    internal static class TreasureQuestAvailabilityPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Quest __instance, ref bool __result)
        {
            try
            {
                if (__instance == null || !TreasureQuestRegistrar.IsTreasureQuest(__instance.ID))
                    return true;

                // 原版第一段：等级要求
                if (__instance.RequireLevel > EXPManager.Level)
                {
                    __result = false;
                    return false;
                }

                // 原版第二段：Demo 锁
                if (__instance.LockInDemo && GameMetaData.Instance.IsDemo)
                {
                    __result = false;
                    return false;
                }

                // 原版第三段：prerequisite 条件（本 mod 不添加条件，保持空列表）
                var prerequisits = __instance.Prerequisits;
                if (prerequisits != null)
                {
                    for (int i = 0; i < prerequisits.Count; i++)
                    {
                        Condition condition = prerequisits[i];
                        if (condition != null && !condition.Evaluate())
                        {
                            __result = false;
                            return false;
                        }
                    }
                }

                // 唯一放行的部分：原版要求关系图里存在节点
                __result = true;
                return false;
            }
            catch (Exception e)
            {
                Diag.Report("法宝任务：可用性检查失败", e);
                return true;
            }
        }
    }
}
