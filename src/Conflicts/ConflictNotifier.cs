using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Duckov.UI;
using MagicTreasurePower.Treasures;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MagicTreasurePower.Conflicts
{
    /// <summary>
    /// 把加载时检测到的 ID 冲突，在进入游戏后以原版提示条（NotificationText）显示 3 秒。
    ///
    /// 检测发生在 mod 加载阶段（通常还在主菜单），那时玩家看不到任何 UI，
    /// 所以先记下来，等关卡初始化完成、提示条实例出现后再依次弹出。
    /// </summary>
    internal static class ConflictNotifier
    {
        /// <summary>每条提示的显示时长（用户要求 3 秒）。</summary>
        private const float DisplaySeconds = 3f;

        private const float PollInterval = 0.5f;

        /// <summary>等待进入游戏的最长时间；超时只写日志，不阻塞游戏。</summary>
        private const float WaitTimeoutSeconds = 600f;

        private static bool _scheduled;

        internal static void Schedule(ModBehaviour host)
        {
            if (host == null || _scheduled)
                return;

            if (TreasureRegistrar.ConflictList.Count == 0)
                return;

            _scheduled = true;
            host.StartCoroutine(ShowWhenReady(host));
        }

        private static IEnumerator ShowWhenReady(ModBehaviour host)
        {
            float startedAt = Time.realtimeSinceStartup;
            List<NotificationText> targets = null;

            while (true)
            {
                if (host == null)
                    yield break;

                try
                {
                    if (LevelManager.LevelInited)
                    {
                        targets = FindActiveNotifications();
                        if (targets.Count > 0)
                            break;
                    }
                }
                catch (Exception e)
                {
                    Diag.Report("等待 ID 冲突提示 UI 时出错", e);
                    yield break;
                }

                if (Time.realtimeSinceStartup - startedAt > WaitTimeoutSeconds)
                {
                    Diag.Warn("等待游戏内提示 UI 超时，ID 冲突提示未能显示（冲突详情见加载日志）");
                    yield break;
                }

                yield return new WaitForSecondsRealtime(PollInterval);
            }

            var conflicts = new List<TreasureConflict>(TreasureRegistrar.ConflictList);

            try
            {
                // 此时 HarmonyLib 通常已经加载，可以反查冲突归属的其它 mod
                TreasureRegistrar.ResolveUnknownConflictOwners();
            }
            catch (Exception e)
            {
                Diag.Report("显示 ID 冲突提示前解析 Mod 归属失败", e);
            }

            // 临时把原版提示条的显示时长改成 3 秒，提示结束后恢复
            var originalDurations = new List<float>();
            foreach (NotificationText notification in targets)
                originalDurations.Add(SetDuration(notification, DisplaySeconds));

            foreach (TreasureConflict conflict in conflicts)
            {
                string text = ConflictDescriber.BuildNotification(conflict);
                NotificationText.Push(text);
                Diag.Warn(text);
            }

            // 每条 3 秒，末尾多等 1 秒再恢复原版的显示时长
            yield return new WaitForSecondsRealtime(DisplaySeconds * conflicts.Count + 1f);

            for (int i = 0; i < targets.Count && i < originalDurations.Count; i++)
            {
                NotificationText notification = targets[i];
                if (notification == null || originalDurations[i] < 0f)
                    continue;

                SetDuration(notification, originalDurations[i]);
            }
        }

        /// <summary>找到当前真正显示在场景里的提示条（排除资源包里的预制体）。</summary>
        private static List<NotificationText> FindActiveNotifications()
        {
            var list = new List<NotificationText>();

            foreach (NotificationText notification in Resources.FindObjectsOfTypeAll<NotificationText>())
            {
                if (notification == null)
                    continue;

                GameObject go = notification.gameObject;
                if (!go.scene.IsValid() || !go.activeInHierarchy || !notification.enabled)
                    continue;

                list.Add(notification);
            }

            return list;
        }

        /// <summary>反射修改提示条的私有 duration 字段，返回原值（失败返回 -1）。</summary>
        private static float SetDuration(NotificationText notification, float seconds)
        {
            try
            {
                FieldInfo field = typeof(NotificationText).GetField(
                    "duration", BindingFlags.Instance | BindingFlags.NonPublic);

                if (field == null || field.FieldType != typeof(float))
                {
                    Diag.Warn("NotificationText 未找到 duration 字段，ID 冲突提示将沿用原版显示时长");
                    return -1f;
                }

                float oldValue = (float)field.GetValue(notification);
                field.SetValue(notification, seconds);
                return oldValue;
            }
            catch (Exception e)
            {
                Diag.Report("设置 ID 冲突提示显示时长失败", e);
                return -1f;
            }
        }
    }
}
