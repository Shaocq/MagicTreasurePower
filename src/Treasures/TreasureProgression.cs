using ItemStatsSystem;

namespace MagicTreasurePower.Treasures
{
    /// <summary>
    /// 法宝等级与经验。
    ///
    /// <para><b>存储：</b></para>
    /// 存在 <c>Item.Variables</c>（原版 <c>ItemTreeData.variables</c> 会随存档保存），
    /// 因此**每件法宝实例各自独立升级**，复制 / 掉落 / 放背包都不会丢失。
    ///
    /// <para><b>规则：</b></para>
    /// 默认 1 级，上限 7 级；升级经验依次为 132 / 168 / 228 / 312 / 420 / 552。
    /// 击杀普通敌人 +1、精英 +3、BOSS +5（见 <see cref="Patches.TreasureKillExpPatch"/>）。
    /// </summary>
    internal static class TreasureProgression
    {
        internal const int MaxLevel = 7;
        internal const int DefaultLevel = 1;

        /// <summary>
        /// 升到下一级所需经验（下标 = 当前等级 - 1）。
        /// 曲线为二次递增，二阶差值固定 28：100 / 135 / 198 / 289 / 408 / 555，累计 1685。
        /// </summary>
        private static readonly int[] RequiredExp = { 100, 135, 198, 289, 408, 555 };

        private const string LevelKey = "MagicTreasureLevel";
        private const string ExpKey = "MagicTreasureExp";

        /// <summary>该物品是否是法宝。</summary>
        internal static bool IsTreasure(Item item)
        {
            if (item == null)
                return false;

            for (int i = 0; i < TreasureCatalog.All.Length; i++)
            {
                if (TreasureCatalog.All[i].TypeId == item.TypeID)
                    return true;
            }

            return false;
        }

        internal static int GetLevel(Item item)
        {
            if (item == null || item.Variables == null)
                return DefaultLevel;

            int level = item.Variables.GetInt(LevelKey, DefaultLevel);
            if (level < DefaultLevel)
                return DefaultLevel;
            if (level > MaxLevel)
                return MaxLevel;
            return level;
        }

        /// <summary>当前等级内的经验；满级返回 0。</summary>
        internal static int GetExp(Item item)
        {
            if (item == null || item.Variables == null)
                return 0;

            int level = GetLevel(item);
            if (level >= MaxLevel)
                return 0;

            int need = GetRequiredExp(level);
            int exp = item.Variables.GetInt(ExpKey, 0);
            if (exp < 0)
                return 0;
            return exp >= need ? need - 1 : exp;
        }

        /// <summary>升到下一级需要的总经验；满级返回 0。</summary>
        internal static int GetRequiredExp(int level)
        {
            if (level < 1 || level >= MaxLevel)
                return 0;

            return RequiredExp[level - 1];
        }

        /// <summary>增加经验并按需升级（可连升多级）。</summary>
        internal static void AddExp(Item item, int amount)
        {
            if (item == null || amount <= 0 || item.Variables == null)
                return;

            int level = GetLevel(item);
            if (level >= MaxLevel)
                return;

            int exp = item.Variables.GetInt(ExpKey, 0) + amount;
            bool leveledUp = false;

            while (level < MaxLevel)
            {
                int need = GetRequiredExp(level);
                if (need <= 0 || exp < need)
                    break;

                exp -= need;
                level++;
                leveledUp = true;
            }

            if (level >= MaxLevel)
                exp = 0;

            item.Variables.SetInt(LevelKey, level);
            item.Variables.SetInt(ExpKey, exp);

            if (leveledUp)
                Diag.Log($"法宝升级：{item.DisplayName} → Lv.{level}");
        }
    }
}
