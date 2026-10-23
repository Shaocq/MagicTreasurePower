using System;
using System.Collections.Generic;
using Duckov.UI;
using HarmonyLib;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using MagicTreasurePower.Slots;
using MagicTreasurePower.UI;

namespace MagicTreasurePower.Patches
{
    /// <summary>
    /// 装备页打开时的两个时机：
    /// <list type="number">
    ///   <item>前缀：确保主角色的角色物品上有法宝槽位（早于原版渲染装备槽）；</item>
    ///   <item>后缀：创建 / 刷新「装备 / 法宝」页签 UI。</item>
    /// </list>
    /// </summary>
    [HarmonyPatch(typeof(LootView), "OnOpen")]
    internal static class LootViewOpenPatch
    {
        [HarmonyPrefix]
        private static void BeforeOpen()
        {
            try
            {
                TreasureSlots.EnsureSlots();
            }
            catch (Exception e)
            {
                Diag.Report("打开装备页前准备法宝槽位失败", e);
            }
        }

        [HarmonyPostfix]
        private static void AfterOpen(LootView __instance)
        {
            try
            {
                // 旧存档里法宝的「来源」可能已经以 Display = true 存下来，
                // 打开装备页时顺带扫一遍主角色的整棵物品树。
                CharacterMainControl main = CharacterMainControl.Main;
                if (main != null && main.CharacterItem != null)
                    HideItemSourcePatch.HideFromTree(main.CharacterItem);

                TreasureTabUI.EnsureAttached(__instance);
            }
            catch (Exception e)
            {
                Diag.Report("打开装备页后挂接法宝页签失败", e);
            }
        }
    }

    /// <summary>
    /// 原版 <see cref="ItemSlotCollectionDisplay.Setup"/> 会渲染角色物品的全部槽位。
    /// 法宝槽位挂在角色物品上（为了随存档保存），但不应该出现在「装备」页签里：
    /// 渲染前临时从槽位列表移除，渲染后（无论成功与否）再按原位置恢复。
    /// </summary>
    [HarmonyPatch(typeof(ItemSlotCollectionDisplay), nameof(ItemSlotCollectionDisplay.Setup))]
    internal static class HideTreasureSlotsPatch
    {
        private sealed class RemovedSlot
        {
            internal int Index;
            internal Slot Slot;
        }

        private sealed class FilterState
        {
            internal SlotCollection Collection;
            internal List<RemovedSlot> Removed;
        }

        [HarmonyPrefix]
        private static void Prefix(Item target, ref FilterState __state)
        {
            try
            {
                if (target == null || target.Slots == null)
                    return;

                // 只处理主角色的角色物品；其它物品（详情面板里的物品等）保持原版行为
                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null || main.CharacterItem != target)
                    return;

                SlotCollection collection = target.Slots;
                List<RemovedSlot> removed = null;

                for (int i = collection.list.Count - 1; i >= 0; i--)
                {
                    Slot slot = collection.list[i];
                    if (!TreasureSlots.IsTreasureSlot(slot))
                        continue;

                    if (removed == null)
                        removed = new List<RemovedSlot>();
                    removed.Add(new RemovedSlot { Index = i, Slot = slot });
                    collection.list.RemoveAt(i);
                }

                if (removed == null)
                    return;

                removed.Sort((a, b) => a.Index.CompareTo(b.Index));
                __state = new FilterState { Collection = collection, Removed = removed };
            }
            catch (Exception e)
            {
                Diag.Report("渲染装备页时隐藏法宝槽位失败", e);
            }
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, ref FilterState __state)
        {
            try
            {
                if (__state != null && __state.Collection != null && __state.Removed != null)
                {
                    List<Slot> list = __state.Collection.list;
                    foreach (RemovedSlot removed in __state.Removed)
                    {
                        if (removed.Slot == null || list.Contains(removed.Slot))
                            continue;

                        if (removed.Index >= 0 && removed.Index <= list.Count)
                            list.Insert(removed.Index, removed.Slot);
                        else
                            list.Add(removed.Slot);
                    }
                }
            }
            catch (Exception e)
            {
                Diag.Report("恢复法宝槽位失败", e);
            }
            finally
            {
                __state = null;
            }

            // 保留原始异常
            return __exception;
        }
    }
}
