using System;
using System.Collections.Generic;
using System.Reflection;
using Duckov.Economy;
using Duckov.Utilities;
using ItemStatsSystem;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MagicTreasurePower.Treasures
{
    /// <summary>
    /// 【测试用】把「火眼金睛」按 1 元上架到基地售货机。
    ///
    /// <para><b>开关：</b>config.ini 的 <c>ShopSellHuoYanJinJing</c>，默认开；
    /// 正式发布前必须改为 false。</para>
    ///
    /// <para><b>商人：</b>基地售货机是 <see cref="StockShop"/>，merchantID 固定为
    /// <c>"Merchant_Normal"</c>（本地化 key：MerchantName_Merchant_Normal = 售货机）。</para>
    ///
    /// <para><b>价格：</b>售货机最终价 = <c>floor(物品 raw value × entry.priceFactor)</c>，
    /// 这里按法宝实际 raw value 反推系数，保证显示价与扣款都是 1 元；
    /// 回购价同样压到 1 元，避免「1 元买进、几十万卖出」的刷钱漏洞。</para>
    ///
    /// <para><b>上架时机：</b></para>
    /// <list type="number">
    ///   <item>mod 加载时先尝试一次（支持中途启用）；</item>
    ///   <item>每次场景加载后再同步一次——基地售货机实例由 BuildingArea 在 Start 阶段生成，
    ///         sceneLoaded 早于 Start，这时写进数据库的条目才能在售货机 Awake 时被读进去；</item>
    ///   <item>关卡初始化完成后再兜底同步一次。</item>
    /// </list>
    /// </summary>
    internal static class TreasureShopStock
    {
        /// <summary>基地售货机的商人 ID。</summary>
        private const string MerchantId = "Merchant_Normal";

        /// <summary>每次补货的上架数量（测试用给足）。</summary>
        private const int MaxStock = 99;

        /// <summary>目标售价（元）。</summary>
        private const int TargetPrice = 1;

        private static bool _installed;

        private static MethodInfo _cacheItemInstancesMethod;

        internal static void Install()
        {
            if (_installed)
                return;

            _installed = true;

            try
            {
                SceneManager.sceneLoaded += OnSceneLoaded;
                LevelManager.OnLevelInitialized += OnLevelInitialized;
                SafeApply();
            }
            catch (Exception e)
            {
                Diag.Report("售货机上架：订阅场景事件失败", e);
            }
        }

        internal static void Uninstall()
        {
            if (!_installed)
                return;

            _installed = false;

            try
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                LevelManager.OnLevelInitialized -= OnLevelInitialized;
            }
            catch (Exception e)
            {
                Diag.Report("售货机上架：退订场景事件失败", e);
            }
        }

        /// <summary>
        /// ModSetting 里切换「测试上架」开关时调用：
        /// 打开 → 重新上架；关闭 → 退订事件并把条目从数据库与现有售货机里撤掉。
        /// </summary>
        internal static void SetEnabled(bool enabled)
        {
            try
            {
                if (enabled)
                {
                    Install();
                    SafeApply();
                    return;
                }

                Uninstall();
                RemoveFromDatabaseAndShops();
            }
            catch (Exception e)
            {
                Diag.Report("切换售货机测试上架失败", e);
            }
        }

        private static void RemoveFromDatabaseAndShops()
        {
            int typeId = TreasureIds.HuoYanJinJing;

            StockShopDatabase database = GetDatabase();
            if (database != null && database.merchantProfiles != null)
            {
                StockShopDatabase.MerchantProfile profile =
                    database.merchantProfiles.Find(p => p != null && p.merchantID == MerchantId);

                profile?.entries?.RemoveAll(e => e != null && e.typeID == typeId);
            }

            foreach (StockShop shop in Object.FindObjectsOfType<StockShop>())
            {
                if (shop == null || shop.MerchantID != MerchantId)
                    continue;

                shop.entries?.RemoveAll(e => e != null && e.ItemTypeID == typeId);
                shop.overrideSellingPrice?.RemoveAll(e => e != null && e.typeID == typeId);
            }

            Diag.Log("已从基地售货机撤下火眼金睛");
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // sceneLoaded 早于场景对象的 Start：先写商店数据库，
            // 基地里后生成的售货机 Awake 时才能读到法宝条目
            SafeApply();
        }

        private static void OnLevelInitialized()
        {
            SafeApply();
        }

        private static void SafeApply()
        {
            try
            {
                Apply();
            }
            catch (Exception e)
            {
                Diag.Report("向基地售货机上架火眼金睛失败", e);
            }
        }

        private static void Apply()
        {
            if (!TreasureRegistrar.IsRegistered(TreasureIds.HuoYanJinJing))
                return;

            StockShopDatabase database = GetDatabase();
            if (database == null)
            {
                Diag.Warn("向售货机上架：商店数据库尚未加载，稍后重试");
                return;
            }

            StockShopDatabase.MerchantProfile profile = GetOrCreateProfile(database);
            StockShopDatabase.ItemEntry dbEntry = EnsureProfileEntry(profile);
            if (dbEntry == null)
                return;

            int syncedShops = 0;
            int addedEntries = 0;

            foreach (StockShop shop in Object.FindObjectsOfType<StockShop>())
            {
                if (shop == null || shop.MerchantID != MerchantId)
                    continue;

                // 蓝图 / 建造预览里的未激活实例不参与上架
                if (!shop.gameObject.activeInHierarchy)
                    continue;

                bool added = SyncShop(shop, dbEntry);
                syncedShops++;

                if (added)
                {
                    // 只有真的补了新条目时才重建物品实例缓存
                    TryCacheItemInstances(shop);
                    addedEntries++;
                }
            }

            // 每个场景都会调用一次 Apply：没有新增条目就不刷日志，避免日志被刷屏
            if (addedEntries > 0)
            {
                Diag.Log(
                    $"售货机上架完成：火眼金睛（ID {TreasureIds.HuoYanJinJing}，" +
                    $"单价 {TargetPrice} 元，库存 {MaxStock}），已同步 {syncedShops} 台售货机");
            }
        }

        private static StockShopDatabase GetDatabase()
        {
            try
            {
                return GameplayDataSettings.StockshopDatabase;
            }
            catch
            {
                // GameplayDataSettings 尚未加载时访问静态属性会抛 NRE
                return null;
            }
        }

        private static StockShopDatabase.MerchantProfile GetOrCreateProfile(StockShopDatabase database)
        {
            if (database.merchantProfiles == null)
                database.merchantProfiles = new List<StockShopDatabase.MerchantProfile>();

            StockShopDatabase.MerchantProfile profile =
                database.merchantProfiles.Find(p => p != null && p.merchantID == MerchantId);

            if (profile == null)
            {
                profile = new StockShopDatabase.MerchantProfile { merchantID = MerchantId };
                database.merchantProfiles.Add(profile);
                Diag.Warn($"商店数据库里原本没有商人「{MerchantId}」，已新建档案");
            }

            if (profile.entries == null)
                profile.entries = new List<StockShopDatabase.ItemEntry>();

            return profile;
        }

        /// <summary>把火眼金睛写进商店数据库的商人物品表（已存在则更新配置）。</summary>
        private static StockShopDatabase.ItemEntry EnsureProfileEntry(StockShopDatabase.MerchantProfile profile)
        {
            int typeId = TreasureIds.HuoYanJinJing;
            float priceFactor = ComputePriceFactor(typeId);

            StockShopDatabase.ItemEntry entry =
                profile.entries.Find(e => e != null && e.typeID == typeId);

            if (entry == null)
            {
                entry = new StockShopDatabase.ItemEntry { typeID = typeId };
                profile.entries.Add(entry);
            }

            entry.maxStock = Mathf.Max(1, MaxStock);
            entry.forceUnlock = true;
            entry.priceFactor = priceFactor;
            // -1：跳过 DoRefreshStock 里 0~1 的随机上架概率判断
            entry.possibility = -1f;
            entry.lockInDemo = false;

            return entry;
        }

        /// <summary>同步一台已经存在的售货机：补上缺失的条目、设置回购价。</summary>
        private static bool SyncShop(StockShop shop, StockShopDatabase.ItemEntry dbEntry)
        {
            if (shop.entries == null)
                shop.entries = new List<StockShop.Entry>();

            if (shop.overrideSellingPrice == null)
                shop.overrideSellingPrice = new List<StockShop.OverrideSellingPriceEntry>();

            int typeId = dbEntry.typeID;

            StockShop.Entry entry = shop.entries.Find(e => e != null && e.ItemTypeID == typeId);
            bool added = entry == null;

            if (entry == null)
            {
                // 售货机已经在场景里（例如中途启用 mod）：手动补条目并给满初始库存，
                // 否则 UI 打开时会因为没触发刷新而显示 0 库存
                entry = new StockShop.Entry(dbEntry) { Show = true };
                entry.CurrentStock = entry.MaxStock;
                shop.entries.Add(entry);
            }
            else
            {
                // 已有条目保留当前库存（可能是玩家买剩下的），只确保可见
                entry.Show = true;
            }

            EnsureSellingPrice(shop, typeId, dbEntry.priceFactor);
            return added;
        }

        /// <summary>
        /// 回购价系数。默认 sellFactor = 0.5，法宝 raw value 极高，
        /// 不覆盖的话 1 元买入能卖几十万 —— 这里同样压到 1 元。
        /// </summary>
        private static void EnsureSellingPrice(StockShop shop, int typeId, float factor)
        {
            StockShop.OverrideSellingPriceEntry entry =
                shop.overrideSellingPrice.Find(e => e != null && e.typeID == typeId);

            if (entry == null)
            {
                shop.overrideSellingPrice.Add(
                    new StockShop.OverrideSellingPriceEntry { typeID = typeId, factor = factor });
            }
            else
            {
                entry.factor = factor;
            }
        }

        /// <summary>
        /// 由物品 raw value 反推价格系数，使 floor(raw × factor) 正好等于目标价。
        /// 用 1.5 而不是 1：给浮点误差留余量，且不会因为多出一点而进位到 2。
        /// </summary>
        private static float ComputePriceFactor(int typeId)
        {
            int rawValue = 0;

            try
            {
                Item prefab = ItemAssetsCollection.GetPrefab(typeId);
                if (prefab != null)
                    rawValue = prefab.GetTotalRawValue();
            }
            catch (Exception)
            {
                // 取不到实际价值时退回配置里的 Value
            }

            if (rawValue <= 0)
            {
                for (int i = 0; i < TreasureCatalog.All.Length; i++)
                {
                    if (TreasureCatalog.All[i].TypeId == typeId)
                    {
                        rawValue = TreasureCatalog.All[i].Value;
                        break;
                    }
                }
            }

            if (rawValue <= 0)
                rawValue = 1;

            return (TargetPrice + 0.5f) / rawValue;
        }

        /// <summary>
        /// 触发私有方法 CacheItemInstances：售货机 UI 的 Setup 直接使用
        /// GetItemInstanceDirect 取展示用实例，缓存没建好会 NRE 或买不了。
        /// </summary>
        private static void TryCacheItemInstances(StockShop shop)
        {
            try
            {
                if (_cacheItemInstancesMethod == null)
                {
                    _cacheItemInstancesMethod = typeof(StockShop).GetMethod(
                        "CacheItemInstances",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                }

                _cacheItemInstancesMethod?.Invoke(shop, null);
            }
            catch (Exception e)
            {
                Exception inner = e.InnerException ?? e;
                Diag.Warn($"预处理售货机物品实例失败：{inner.Message}");
            }
        }
    }
}
