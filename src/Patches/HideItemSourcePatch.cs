using System;
using Duckov.UI;
using Duckov.Utilities;
using HarmonyLib;
using ItemStatsSystem;
using ItemStatsSystem.Items;

namespace MagicTreasurePower.Patches
{
    /// <summary>
    /// 隐藏法宝物品上的「来源」词条（例如从售货机购买后显示的「来源：交易」）。
    ///
    /// 来源并不是物品描述的一部分，而是 <see cref="Item.FromInfoKey"/> 写入的
    /// `ItemFromInfo` 变量，详情面板的 SetupVariables 会把 Display = true 的变量逐条显示出来。
    /// 这里只处理本 mod 的法宝 ID 段，不影响原版物品。
    ///
    /// 两个补丁配合：
    /// 1. FromInfoKey setter 后置：物品刚被售货机 / 掉落等流程打上来源时，立即把显示关掉；
    /// 2. SetupVariables 前缀：兼容旧存档里已经把 Display 存成 true 的法宝。
    /// </summary>
    internal static class HideItemSourcePatch
    {
        private const string FromInfoVariableKey = "ItemFromInfo";

        /// <summary>物品设置来源时（售货机 = UI_Trade「交易」等），法宝直接隐藏该词条。</summary>
        [HarmonyPatch(typeof(Item), nameof(Item.FromInfoKey), MethodType.Setter)]
        [HarmonyPostfix]
        private static void AfterSetFromInfoKey(Item __instance)
        {
            Hide(__instance);
        }

        /// <summary>详情面板刷新变量前兜底：旧存档里 Display 可能仍是 true。</summary>
        [HarmonyPatch(typeof(ItemDetailsDisplay), "SetupVariables")]
        [HarmonyPrefix]
        private static void BeforeSetupVariables(ItemDetailsDisplay __instance)
        {
            Hide(__instance.Target);
        }

        /// <summary>
        /// 悬停提示（ItemHoveringUI → ItemPropertiesDisplay）用的是
        /// <see cref="ItemUIUtilities.GetPropertyValueTextPair"/>，与详情面板不是同一条路径，
        /// 必须单独拦一次，否则「来源：交易」只会在悬停提示里残留。
        /// </summary>
        [HarmonyPatch(typeof(ItemUIUtilities), nameof(ItemUIUtilities.GetPropertyValueTextPair))]
        [HarmonyPrefix]
        private static void BeforeGetPropertyValueTextPair(Item item)
        {
            Hide(item);
        }

        /// <summary>文本形式的属性汇总（供其它 UI / 其它 mod 读取）。</summary>
        [HarmonyPatch(typeof(ItemUIUtilities), nameof(ItemUIUtilities.GetPropertiesDisplayText))]
        [HarmonyPrefix]
        private static void BeforeGetPropertiesDisplayText(Item item)
        {
            Hide(item);
        }

        /// <summary>悬停属性面板刷新前兜底。</summary>
        [HarmonyPatch(typeof(ItemPropertiesDisplay), "Setup")]
        [HarmonyPrefix]
        private static void BeforePropertiesDisplaySetup(Item targetItem)
        {
            Hide(targetItem);
        }

        /// <summary>递归隐藏整棵物品树（角色物品 + 槽位 + 背包）里所有法宝的来源词条。</summary>
        internal static void HideFromTree(Item root)
        {
            try
            {
                if (root == null)
                    return;

                Hide(root);

                if (root.Slots != null && root.Slots.list != null)
                {
                    foreach (Slot slot in root.Slots.list)
                    {
                        if (slot != null && slot.Content != null)
                            HideFromTree(slot.Content);
                    }
                }

                if (root.Inventory != null && root.Inventory.Content != null)
                {
                    foreach (Item child in root.Inventory.Content)
                    {
                        if (child != null)
                            HideFromTree(child);
                    }
                }
            }
            catch (Exception e)
            {
                Diag.Report("清理法宝「来源」词条失败", e);
            }
        }

        private static void Hide(Item item)
        {
            try
            {
                if (item == null || !TreasureIds.IsInRange(item.TypeID))
                {
                    return;
                }

                CustomData entry = item.Variables?.GetEntry(FromInfoVariableKey);
                if (entry != null && entry.Display)
                {
                    entry.Display = false;
                }
            }
            catch (Exception e)
            {
                Diag.Report("隐藏法宝「来源」词条失败", e);
            }
        }
    }
}
