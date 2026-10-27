using System;
using System.Collections;
using System.Collections.Generic;
using Duckov.MasterKeys;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using MagicTreasurePower.Slots;
using MagicTreasurePower.UI;
using UnityEngine;

namespace MagicTreasurePower.Treasures
{
    /// <summary>
    /// 火眼金睛：进入战局后扫描全场物资箱，列出其中价值最高的 N 件物品。
    ///
    /// <para><b>可开箱判定（复用原版逻辑）：</b></para>
    /// 战局里的箱子用原版 <see cref="InteractableBase.requireItem"/> / <c>requireItemId</c>
    /// 表示「需要某件钥匙类物品」。能不能开直接调原版
    /// <see cref="InteractableBase.TryGetRequiredItem"/>：它已经覆盖了
    /// 「钥匙录入机解锁过（<see cref="MasterKeysManager.IsActive"/>）→ 角色装备槽 / 主背包 / 容器 /
    /// 宠物背包里带着这把钥匙」三种情况，和玩家真正去开箱时的判定完全一致。
    /// 原版方法在宠物背包缺失等早期状态下可能抛异常，此时退回本地等价实现
    /// <see cref="FallbackHasKey"/>，避免把本来能开的箱子误判成打不开。
    ///
    /// <para><b>扫描时机：</b></para>
    /// 订阅 <c>LevelManager.OnLevelInitialized</c>，只在战局（<c>IsRaidMap</c>）里、
    /// 且法宝装备在法宝槽位时扫描一次；回基地 / 撤离后重置，下一局重新扫描。
    /// 箱子物品是 <c>LootBoxLoader.Setup()</c> 异步生成的，所以要先等
    /// <c>Inventory.Loading</c> 全部结束再统计，否则会漏掉后生成的物品。
    /// </summary>
    internal static class HuoYanJinJingEffect
    {
        /// <summary>等待箱子物品生成完毕的上限（秒）。</summary>
        private const float LoadWaitSeconds = 15f;

        /// <summary>等待期间的轮询间隔（秒）。</summary>
        private const float LoadPollInterval = 0.25f;

        /// <summary>关卡初始化后再等这么久才开始统计，避免箱子还没建出 Inventory。</summary>
        private const float MinimumSettleSeconds = 2f;

        private static bool _subscribed;

        /// <summary>本局是否已经扫描过（回基地 / 撤离后重置）。</summary>
        private static bool _scannedThisRaid;

        /// <summary>正在等待 / 扫描中，避免同一局重复起协程。</summary>
        private static bool _scanning;

        internal static void Subscribe()
        {
            if (_subscribed)
                return;

            try
            {
                LevelManager.OnLevelInitialized += OnLevelInitialized;
                LevelManager.OnEvacuated += OnEvacuated;
                _subscribed = true;
            }
            catch (Exception e)
            {
                Diag.Report("火眼金睛：订阅关卡事件失败", e);
            }
        }

        internal static void Unsubscribe()
        {
            if (!_subscribed)
                return;

            try
            {
                LevelManager.OnLevelInitialized -= OnLevelInitialized;
                LevelManager.OnEvacuated -= OnEvacuated;
            }
            catch (Exception e)
            {
                Diag.Report("火眼金睛：退订关卡事件失败", e);
            }

            _subscribed = false;
            _scannedThisRaid = false;
            _scanning = false;
        }

        private static void OnEvacuated(EvacuationInfo info)
        {
            // 撤离后本局结束，下一局进战局重新扫描
            _scannedThisRaid = false;
        }

        private static void OnLevelInitialized()
        {
            try
            {
                LevelManager manager = LevelManager.Instance;
                if (manager == null || !manager.IsRaidMap)
                {
                    // 回基地 / 主菜单：重置，下一局重新扫描
                    _scannedThisRaid = false;
                    return;
                }

                // 新一局开始：上一局的扫描结果作废，F8 不该再打开旧列表
                if (!_scannedThisRaid)
                    TreasureScanUI.ClearLastResult();

                if (_scannedThisRaid || _scanning)
                    return;

                Item treasure = TreasureSlots.GetEquippedItem(TreasureIds.HuoYanJinJing);
                if (treasure == null)
                {
                    // 没装备就什么都不做；等下一局
                    return;
                }

                ModBehaviour host = ModBehaviour.Instance;
                if (host == null)
                    return;

                _scannedThisRaid = true;
                _scanning = true;
                host.StartCoroutine(ScanRoutine(treasure));
            }
            catch (Exception e)
            {
                _scanning = false;
                Diag.Report("火眼金睛：进入战局扫描失败", e);
            }
        }

        private static IEnumerator ScanRoutine(Item treasure)
        {
            LevelManager expected = LevelManager.Instance;
            int level = TreasureProgression.GetLevel(treasure);
            int want = TreasureBalance.HuoYanJinJingCountOf(level);

            float deadline = Time.unscaledTime + LoadWaitSeconds;
            float earliest = Time.unscaledTime + MinimumSettleSeconds;

            // 等：关卡初始化完成、主角就位、箱子物品异步生成结束
            while (Time.unscaledTime < deadline)
            {
                if (LevelManager.Instance == null || !ReferenceEquals(LevelManager.Instance, expected))
                {
                    _scanning = false;
                    yield break;
                }

                bool ready = CharacterMainControl.Main != null &&
                             LevelManager.LevelInited &&
                             Time.unscaledTime >= earliest &&
                             BoxesSettled();

                if (ready)
                    break;

                yield return new WaitForSecondsRealtime(LoadPollInterval);
            }

            // 再让一帧，确保最后一笔异步生成落库
            yield return null;

            List<TreasureScanGroup> groups;
            int total;
            try
            {
                groups = Scan(want, out total);
            }
            catch (Exception e)
            {
                Diag.Report("火眼金睛：扫描物资箱失败", e);
                _scanning = false;
                yield break;
            }

            _scanning = false;

            if (groups.Count == 0)
            {
                Diag.Log("火眼金睛：本局没有扫描到可获取的高价值物资");
                TreasureNotice.Show(TreasureIds.HuoYanJinJing, "本局没有扫描到可获取的高价值物资");
                yield break;
            }

            Diag.Log($"火眼金睛：本局共扫到 {total} 件可获取物品，展示价值最高的 {want} 件");
            TreasureScanUI.Show(groups, want, level);
        }

        /// <summary>所有战局物资箱的物品是否都已经生成完毕。</summary>
        private static bool BoxesSettled()
        {
            try
            {
                Dictionary<int, Inventory> inventories = LevelManager.LootBoxInventories;
                if (inventories == null)
                    return true;

                foreach (Inventory inventory in inventories.Values)
                {
                    if (inventory != null && inventory.Loading)
                        return false;
                }

                return true;
            }
            catch
            {
                // 状态取不到时不再等，按「已就绪」处理，避免永远扫描不出来
                return true;
            }
        }

        /// <summary>
        /// 扫描全场物资箱，返回按箱子分组的结果（箱子按其中最高价值物品的排名出现）。
        /// </summary>
        private static List<TreasureScanGroup> Scan(int want, out int total)
        {
            total = 0;

            var groups = new List<TreasureScanGroup>();
            var all = new List<TreasureScanEntry>();

            CharacterMainControl main = CharacterMainControl.Main;
            if (main == null || want <= 0)
                return groups;

            Vector3 origin = main.transform.position;

            InteractableLootbox[] boxes;
            try
            {
                boxes = UnityEngine.Object.FindObjectsOfType<InteractableLootbox>();
            }
            catch (Exception e)
            {
                Diag.Report("火眼金睛：查找物资箱失败", e);
                return groups;
            }

            foreach (InteractableLootbox box in boxes)
            {
                try
                {
                    if (box == null || !box.gameObject.activeInHierarchy)
                        continue;

                    Inventory inventory = box.Inventory;
                    if (inventory == null || inventory.IsEmpty())
                        continue;

                    // 刚被建出来、物品还在异步生成的箱子先跳过，
                    // 否则会把「生成到一半」的内容当成完整结果（下一次扫描自然不会有这个问题）
                    if (inventory.Loading)
                    {
                        Diag.Log($"火眼金睛：物资箱「{BoxNameOf(box)}」仍在生成物品，本次跳过");
                        continue;
                    }

                    // 只扫地图上的固定物资箱：
                    //   - 玩家 / 宠物身上、被他们搬运的容器不算；
                    //   - 尸体 / 掉落物（CreateFromItem 生成的容器）不算。
                    if (IsCarriedOrOwned(box, main, inventory))
                    {
                        Diag.Log($"火眼金睛：跳过随身 / 搬运中的容器「{BoxNameOf(box)}」");
                        continue;
                    }

                    // 上锁且打不开的箱子直接跳过：展示了也拿不到
                    if (!CanOpen(main, box))
                    {
                        Diag.Log($"火眼金睛：跳过上锁物资箱「{BoxNameOf(box)}」（缺少钥匙 {box.requireItemId}）");
                        continue;
                    }

                    string boxName = BoxNameOf(box);
                    float distance = Vector3.Distance(origin, box.transform.position);
                    int boxId = box.GetInstanceID();

                    foreach (Item item in inventory)
                    {
                        if (item == null)
                            continue;

                        long value = ValueOf(item);
                        if (value <= 0)
                            continue;

                        total++;

                        string itemName = item.DisplayName;
                        if (item.StackCount > 1)
                            itemName += " ×" + item.StackCount;

                        all.Add(new TreasureScanEntry
                        {
                            Item = item,
                            Box = box,
                            BoxId = boxId,
                            BoxName = boxName,
                            Distance = distance,
                            Value = value,
                            CachedName = itemName,
                            CachedIcon = item.Icon,
                        });
                    }
                }
                catch (Exception e)
                {
                    Diag.Warn($"火眼金睛：统计物资箱失败（{box}）：{e.Message}");
                }
            }

            if (all.Count == 0)
                return groups;

            // 价值从高到低；同价按名称排序，保证每次结果稳定
            all.Sort((a, b) =>
            {
                int byValue = b.Value.CompareTo(a.Value);
                if (byValue != 0)
                    return byValue;

                return string.CompareOrdinal(a.DisplayName, b.DisplayName);
            });

            if (all.Count > want)
                all.RemoveRange(want, all.Count - want);

            // 按箱子分组，组的顺序 = 组内最高价值物品在榜单上的先后
            var byBox = new Dictionary<int, TreasureScanGroup>();
            foreach (TreasureScanEntry entry in all)
            {
                TreasureScanGroup group;
                if (!byBox.TryGetValue(entry.BoxId, out group))
                {
                    group = new TreasureScanGroup(entry.Box, entry.BoxName, entry.Distance);
                    byBox.Add(entry.BoxId, group);
                    groups.Add(group);
                }

                group.Entries.Add(entry);
            }

            return groups;
        }

        /// <summary>
        /// 是否是「玩家 / 宠物身上」或「他们正在搬运」的容器，以及从角色尸体上掉出来的容器。
        /// 这些都不属于地图上的固定物资箱，扫描要排除。
        /// </summary>
        private static bool IsCarriedOrOwned(
            InteractableLootbox box, CharacterMainControl main, Inventory inventory)
        {
            try
            {
                // CreateFromItem 生成的容器（尸体 / 掉落物）Inventory 挂在箱子自己身上，
                // 地图固定物资箱的 Inventory 是由 Game 动态建在 LootBoxInventoriesParent 下。
                if (box.GetComponent<Inventory>() != null)
                    return true;

                if (main != null)
                {
                    if (box.transform.IsChildOf(main.transform))
                        return true;

                    Item characterItem = main.CharacterItem;
                    Inventory selfInventory = characterItem != null ? characterItem.Inventory : null;
                    if (selfInventory != null)
                    {
                        if (ReferenceEquals(inventory, selfInventory))
                            return true;

                        // 从自己背包里拿出来的容器（AttachedToItem 指向背包内的物品）
                        Item attached = inventory.AttachedToItem;
                        if (attached != null && IsInside(attached, selfInventory))
                            return true;
                    }
                }

                PetProxy pet = null;
                try
                {
                    LevelManager manager = LevelManager.Instance;
                    pet = manager != null ? manager.PetProxy : null;
                }
                catch
                {
                }

                if (pet != null)
                {
                    if (pet.transform != null && box.transform.IsChildOf(pet.transform))
                        return true;

                    Inventory petInventory = pet.Inventory;
                    if (petInventory != null)
                    {
                        if (ReferenceEquals(inventory, petInventory))
                            return true;

                        Item attached = inventory.AttachedToItem;
                        if (attached != null && IsInside(attached, petInventory))
                            return true;
                    }
                }
            }
            catch
            {
                // 判断失败时按「不是随身物品」处理，避免整张图都扫不出来
            }

            return false;
        }

        /// <summary>物品是否在指定背包里（含嵌套容器）。</summary>
        private static bool IsInside(Item item, Inventory inventory)
        {
            Item current = item;

            for (int depth = 0; depth < 8 && current != null; depth++)
            {
                if (ReferenceEquals(current.InInventory, inventory))
                    return true;

                current = current.ParentItem;
            }

            return false;
        }

        /// <summary>物品价值口径：单件价值 × 堆叠数量（不把容器里装着的东西算进去）。</summary>
        private static long ValueOf(Item item)
        {
            try
            {
                return (long)item.Value * Mathf.Max(1, item.StackCount);
            }
            catch
            {
                return 0L;
            }
        }

        private static string BoxNameOf(InteractableLootbox box)
        {
            try
            {
                string name = box.InteractName;
                if (!string.IsNullOrEmpty(name))
                    return name;
            }
            catch
            {
            }

            return "物资箱";
        }

        // ------------------------------------------------------------ 上锁判定

        /// <summary>箱子是否需要钥匙；需要时玩家当前是否满足条件。</summary>
        private static bool CanOpen(CharacterMainControl main, InteractableLootbox box)
        {
            if (!box.requireItem)
                return true;

            try
            {
                return box.TryGetRequiredItem(main).hasItem;
            }
            catch (Exception e)
            {
                Diag.Warn($"火眼金睛：原版开锁判定失败，改用本地判定（{e.GetType().Name}）");
                return FallbackHasKey(main, box.requireItemId);
            }
        }

        /// <summary>
        /// 与 <see cref="InteractableBase.TryGetRequiredItem"/> 等价的本地实现，
        /// 只在整个方法抛异常时才用；逻辑顺序保持一致（录入机 → 装备槽 → 主背包 / 容器 → 宠物背包）。
        /// </summary>
        private static bool FallbackHasKey(CharacterMainControl main, int keyId)
        {
            if (keyId <= 0 || main == null)
                return false;

            try
            {
                if (MasterKeysManager.IsActive(keyId))
                    return true;
            }
            catch
            {
            }

            try
            {
                Item characterItem = main.CharacterItem;
                if (characterItem == null)
                    return false;

                if (characterItem.Slots != null)
                {
                    foreach (Slot slot in characterItem.Slots)
                    {
                        Item content = slot != null ? slot.Content : null;
                        if (content != null && content.TypeID == keyId)
                            return true;
                    }
                }

                if (ContainsKey(characterItem.Inventory, keyId, 0))
                    return true;
            }
            catch
            {
            }

            try
            {
                LevelManager manager = LevelManager.Instance;
                PetProxy pet = manager != null ? manager.PetProxy : null;
                if (pet != null && ContainsKey(pet.Inventory, keyId, 0))
                    return true;
            }
            catch
            {
            }

            return false;
        }

        /// <summary>背包里（含容器槽位与嵌套背包）是否有指定 TypeID 的钥匙。</summary>
        private static bool ContainsKey(Inventory inventory, int keyId, int depth)
        {
            if (inventory == null || depth > 3)
                return false;

            try
            {
                foreach (Item item in inventory)
                {
                    if (item == null)
                        continue;

                    if (item.TypeID == keyId)
                        return true;

                    if (item.Slots != null)
                    {
                        foreach (Slot slot in item.Slots)
                        {
                            Item content = slot != null ? slot.Content : null;
                            if (content != null && content.TypeID == keyId)
                                return true;
                        }
                    }

                    if (item.Inventory != null && ContainsKey(item.Inventory, keyId, depth + 1))
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }
    }

    /// <summary>扫描到的一件物品（只用于界面展示，不持有额外状态）。</summary>
    internal sealed class TreasureScanEntry
    {
        internal Item Item;
        internal InteractableLootbox Box;
        internal int BoxId;
        internal string BoxName;
        internal float Distance;
        internal long Value;

        /// <summary>
        /// 显示用名称与图标的快照。物品可能被拿走 / 销毁，而面板还能用 F8 重新打开，
        /// 所以展示数据必须在扫描当时就缓存下来，不能每次去读 Item。
        /// </summary>
        internal string CachedName;
        internal Sprite CachedIcon;

        internal string DisplayName => CachedName ?? string.Empty;
    }

    /// <summary>同一个物资箱里的若干件物品。</summary>
    internal sealed class TreasureScanGroup
    {
        /// <summary>箱子本体（点行画带路线时要用它的位置）。</summary>
        internal readonly InteractableLootbox Box;

        internal readonly int BoxId;
        internal readonly string BoxName;
        internal readonly float Distance;
        internal readonly List<TreasureScanEntry> Entries = new List<TreasureScanEntry>();

        internal TreasureScanGroup(InteractableLootbox box, string boxName, float distance)
        {
            Box = box;
            BoxId = box != null ? box.GetInstanceID() : 0;
            BoxName = boxName;
            Distance = distance;
        }

        /// <summary>
        /// 这个箱子是否已经被打开过（本次战局内）。
        /// 原版有两处记录：<c>LootView</c> 的「曾经打开过」集合，以及箱子 Inventory 上的
        /// <c>hasBeenInspectedInLootBox</c>（交互结束就置 true），任一为真即算已获取。
        /// </summary>
        internal bool IsLooted()
        {
            try
            {
                if (Box == null)
                    return false;

                if (Box.Looted)
                    return true;

                Inventory inventory = Box.Inventory;
                return inventory != null && inventory.hasBeenInspectedInLootBox;
            }
            catch
            {
                return false;
            }
        }
    }
}
