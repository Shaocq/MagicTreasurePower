using System;
using System.Collections.Generic;
using System.Reflection;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Items;

namespace MagicTreasurePower.Slots
{
    /// <summary>
    /// 装备页「法宝」页签里的法宝槽位。
    ///
    /// <para><b>为什么挂在角色物品上：</b></para>
    /// 槽位直接加到 <c>CharacterMainControl.Main.CharacterItem.Slots</c>，这样：
    /// <list type="bullet">
    ///   <item>放进槽位的法宝会随原版存档（ItemTreeData）按槽位 key 一起保存 / 读档；</item>
    ///   <item>法宝成为角色物品树的一部分，原版的统计与效果判定都能看到它。</item>
    /// </list>
    ///
    /// <para><b>为什么装备页看不到它们：</b></para>
    /// 原版 <see cref="Duckov.UI.ItemSlotCollectionDisplay.Setup"/> 会渲染角色物品的全部槽位，
    /// 由 <c>HideTreasureSlotsPatch</c> 在渲染前临时移除、渲染后恢复，
    /// 所以「装备」页签只显示原版装备槽，「法宝」页签由 <see cref="UI.TreasureTabUI"/> 单独渲染。
    ///
    /// <para><b>存档兼容：</b></para>
    /// 槽位 key 一经发布不能改（存档按 key 记录槽位内容）。读档时若存档里有法宝，
    /// 原版会自动补建同名槽位，<see cref="EnsureSlots"/> 再补上「只允许法宝」的限制。
    /// </summary>
    internal static class TreasureSlots
    {
        /// <summary>槽位 key 前缀（存档依据，不要修改）。</summary>
        internal const string KeyPrefix = "MagicTreasureSlot_";

        /// <summary>六个法宝槽位的 key（key 即存档依据，只能追加、不能改名或复用）。</summary>
        internal static readonly string[] Keys =
        {
            KeyPrefix + "1",
            KeyPrefix + "2",
            KeyPrefix + "3",
            KeyPrefix + "4",
            KeyPrefix + "5",
            KeyPrefix + "6",
        };

        /// <summary>页签与槽位显示名。</summary>
        internal const string DisplayName = "法宝";

        /// <summary>判断某个槽位是否为本 mod 的法宝槽位。</summary>
        internal static bool IsTreasureSlot(Slot slot)
        {
            return slot != null && !string.IsNullOrEmpty(slot.Key) &&
                   slot.Key.StartsWith(KeyPrefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// 判断指定 TypeID 的法宝是否装备在任意一个法宝槽位里。
        /// 槽位里没有物品或换下的瞬间就返回 false，不需要任何事件通知。
        /// </summary>
        internal static bool IsEquipped(int typeId)
        {
            try
            {
                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null)
                    return false;

                return IsEquipped(main.CharacterItem, typeId);
            }
            catch (Exception e)
            {
                Diag.Report("查询法宝是否已装备失败", e);
                return false;
            }
        }

        /// <summary>取主角装备的指定法宝实例；没装备返回 null。</summary>
        internal static Item GetEquippedItem(int typeId)
        {
            try
            {
                CharacterMainControl main = CharacterMainControl.Main;
                Item characterItem = main != null ? main.CharacterItem : null;
                if (characterItem == null || characterItem.Slots == null)
                    return null;

                foreach (string key in Keys)
                {
                    Slot slot = characterItem.Slots.GetSlot(key);
                    Item content = slot != null ? slot.Content : null;
                    if (content != null && content.TypeID == typeId)
                        return content;
                }
            }
            catch (Exception e)
            {
                Diag.Report("查询已装备法宝失败", e);
            }

            return null;
        }

        /// <summary>在指定角色物品的法宝槽位里查找指定 TypeID。</summary>
        internal static bool IsEquipped(Item characterItem, int typeId)
        {
            if (characterItem == null || characterItem.Slots == null)
                return false;

            SlotCollection collection = characterItem.Slots;
            foreach (string key in Keys)
            {
                Slot slot = collection.GetSlot(key);
                Item content = slot != null ? slot.Content : null;
                if (content != null && content.TypeID == typeId)
                    return true;
            }

            return false;
        }

        /// <summary>主角色的角色物品上确保存在全部法宝槽位，返回它们。</summary>
        internal static List<Slot> EnsureSlots()
        {
            CharacterMainControl main = CharacterMainControl.Main;
            if (main == null)
                return null;

            return EnsureSlots(main.CharacterItem);
        }

        /// <summary>在指定角色物品上确保存在全部法宝槽位，返回它们。</summary>
        internal static List<Slot> EnsureSlots(Item characterItem)
        {
            if (characterItem == null)
                return null;

            try
            {
                if (characterItem.Slots == null)
                    characterItem.CreateSlotsComponent();

                SlotCollection collection = characterItem.Slots;
                if (collection == null)
                    return null;

                Tag treasureTag = Treasures.TreasureRegistrar.TreasureTag;
                if (treasureTag == null)
                {
                    Diag.Warn("「法宝」标签不可用，法宝槽位暂不限制放入物品");
                }

                List<Slot> result = new List<Slot>(Keys.Length);
                foreach (string key in Keys)
                {
                    Slot slot = collection.GetSlot(key);
                    if (slot == null)
                    {
                        slot = new Slot(key);
                        collection.Add(slot);
                        Diag.Log($"已在角色物品上添加法宝槽位：{key}");
                    }

                    ApplyRestriction(slot, treasureTag);
                    result.Add(slot);
                }

                return result;
            }
            catch (Exception e)
            {
                Diag.Report("创建法宝槽位失败", e);
                return null;
            }
        }

        /// <summary>把槽位限制为「只接受法宝」，并清掉可能残留的其它限制。</summary>
        private static void ApplyRestriction(Slot slot, Tag treasureTag)
        {
            if (slot.requireTags == null)
                slot.requireTags = new List<Tag>();
            if (slot.excludeTags == null)
                slot.excludeTags = new List<Tag>();

            // 所有法宝槽都不允许出现相同 TypeID 的法宝：
            // 原版 Slot.CheckAbleToPlug 里，只要双方都开了该标记就会互相排斥。
            SetForbidItemsWithSameID(slot, true);

            bool alreadyValid = treasureTag != null &&
                                slot.requireTags.Count == 1 &&
                                slot.requireTags[0] != null &&
                                slot.requireTags[0].Hash == treasureTag.Hash;

            if (alreadyValid)
                return;

            slot.requireTags.Clear();
            slot.excludeTags.Clear();
            if (treasureTag != null)
                slot.requireTags.Add(treasureTag);
        }

        /// <summary>
        /// 写 <c>Slot.forbidItemsWithSameID</c>（私有序列化字段）。
        /// 开启后，原版 <c>CheckAbleToPlug</c> 会拒绝与另一个开启了同标记的槽位里
        /// 已有物品 TypeID 相同的东西——正好用于「同一个法宝不能装两个槽」。
        /// </summary>
        private static void SetForbidItemsWithSameID(Slot slot, bool value)
        {
            try
            {
                FieldInfo field = typeof(Slot).GetField(
                    "forbidItemsWithSameID", BindingFlags.Instance | BindingFlags.NonPublic);

                if (field == null)
                {
                    Diag.Warn("Slot 未找到私有字段「forbidItemsWithSameID」，重复装备限制未生效");
                    return;
                }

                if ((bool)field.GetValue(slot) == value)
                    return;

                field.SetValue(slot, value);
            }
            catch (Exception e)
            {
                Diag.Report("设置法宝槽同 ID 限制失败", e);
            }
        }
    }
}
