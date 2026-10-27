using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using MagicTreasurePower.Conflicts;
using MagicTreasurePower.Patches;
using MagicTreasurePower.Quests;
using MagicTreasurePower.Treasures;
using MagicTreasurePower.UI;
using UnityEngine;

namespace MagicTreasurePower
{
    /// <summary>
    /// 「法宝之力」mod 入口。
    ///
    /// 当前为前期骨架：负责日志、Harmony 前置检测与补丁安装流程，
    /// 具体的法宝物品注册 / 能力逻辑在 <c>src/Treasures/</c> 目录下逐步补充。
    ///
    /// 前置：HarmonyLib（工坊 3589088839），只做编译期引用，不内嵌 0Harmony。
    /// 自定义物品 ID 段：17350~17399，登记表见 <see cref="TreasureIds"/>。
    /// </summary>
    public class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private const string HarmonyId = "com.magictreasurepower";
        private const float RetryInterval = 2f;

        private static ModBehaviour _instance;
        private static bool _patchAttempted;

        /// <summary>当前生效的 mod 实例（后续物品种类注册用）。</summary>
        internal static ModBehaviour Instance => _instance;

        private Harmony _harmony;
        private Coroutine _patchCoroutine;

        /// <summary>关卡初始化监听是否已经订阅（避免 mod 重载时重复挂）。</summary>
        private static bool _levelHookInstalled;

        /// <summary>下次检查法宝装备加成的时刻（每 0.5 秒一次）。</summary>
        private float _nextEquipmentCheck;

        private void Update()
        {
            try
            {
                if (Time.unscaledTime < _nextEquipmentCheck)
                    return;

                _nextEquipmentCheck = Time.unscaledTime + 0.5f;

                // 九龙离火罩等「装备期间生效」的法宝加成
                JiuLongLiHuoZhaoEffect.Reconcile();
            }
            catch (Exception e)
            {
                Diag.Report("法宝装备加成刷新失败", e);
            }
        }

        protected override void OnAfterSetup()
        {
            try
            {
                _instance = this;

                Diag.Log($"已加载：版本 {info.version}，目录 {info.path}");
                Diag.Log(TreasureIds.Describe());

                TreasureRegistrar.RegisterAll();

                // ID 冲突时不注册对应法宝；进游戏后用原版提示条显示 3 秒
                ConflictNotifier.Schedule(this);

                // 击杀给法宝加经验（订阅原版死亡事件）
                TreasureKillExp.Subscribe();

                // 六个法宝任务 + 法宝宝箱
                TreasureQuestRegistrar.RegisterAll();

                // 提前建好提示框（字体扫描 / 贴图生成），避免首次触发时卡一下
                UI.TreasureNotice.Prewarm();

                // 火眼金睛：进入战局后扫描全场物资箱（只在本局装备了该法宝时生效）
                HuoYanJinJingEffect.Subscribe();

                // 换图 / 新关卡时重置定海珠的「每局一次免死」
                if (!_levelHookInstalled)
                {
                    LevelManager.OnLevelInitialized += DingHaiZhuDeathSavePatch.ResetForNewRaid;
                    _levelHookInstalled = true;
                }

                _patchCoroutine = StartCoroutine(InstallHarmonyWhenReady());
            }
            catch (Exception e)
            {
                Diag.Report("初始化失败", e);
            }
        }

        protected override void OnBeforeDeactivate()
        {
            try
            {
                TreasureKillExp.Unsubscribe();

                // 把任务模板从 QuestCollection 里摘掉，避免重载后集合里留下已销毁的引用
                TreasureQuestRegistrar.UnregisterAll();

                // 宝箱界面 / 扫描面板是独立覆盖层，停用时要主动关闭
                TreasureBoxUI.ForceClose();
                TreasureScanUI.ForceClose();

                HuoYanJinJingEffect.Unsubscribe();

                // 带路绿线是独立的世界空间对象，停用时要清掉
                TreasureRouteLine.Clear();

                // 卸掉装备期间生效的属性加成
                JiuLongLiHuoZhaoEffect.RemoveAll();

                if (_levelHookInstalled)
                {
                    LevelManager.OnLevelInitialized -= DingHaiZhuDeathSavePatch.ResetForNewRaid;
                    _levelHookInstalled = false;
                }

                if (_patchCoroutine != null)
                {
                    StopCoroutine(_patchCoroutine);
                    _patchCoroutine = null;
                }

                if (_harmony != null)
                {
                    _harmony.UnpatchAll(HarmonyId);
                    _harmony = null;
                }

                _patchAttempted = false;
                Diag.Log("已停用，补丁已清理");
            }
            catch (Exception e)
            {
                Diag.Report("停用清理失败", e);
            }
        }

        /// <summary>
        /// 等待前置 HarmonyLib 把 0Harmony 载入后再安装补丁。
        /// 单独拆成方法：0Harmony 未载入时不能让包含 Harmony 类型的方法被 JIT 解析。
        /// </summary>
        private IEnumerator InstallHarmonyWhenReady()
        {
            while (true)
            {
                try
                {
                    if (HarmonyLoad.Load0Harmony() != null)
                    {
                        InstallPatches();
                        yield break;
                    }

                    if (!_patchAttempted)
                    {
                        _patchAttempted = true;
                        Diag.Warn("未找到前置 HarmonyLib（工坊 3589088839），每 2 秒重试一次");
                    }
                }
                catch (Exception e)
                {
                    Diag.Report("安装 Harmony 补丁失败，稍后重试", e);
                }

                yield return new WaitForSecondsRealtime(RetryInterval);
            }
        }

        private void InstallPatches()
        {
            try
            {
                _harmony = new Harmony(HarmonyId);

                // 法宝相关补丁放在 src/Patches 目录并标注 [HarmonyPatch]，
                // PatchAll 会自动发现（例如隐藏「来源」词条）。
                _harmony.PatchAll(Assembly.GetExecutingAssembly());

                Diag.Log($"Harmony 补丁已就绪（0Harmony {HarmonyLoad.HarmonyVersion}）");
            }
            catch (Exception e)
            {
                Diag.Report("PatchAll 失败", e);
            }
        }
    }
}
