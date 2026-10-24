using System.Collections.Generic;
using Duckov.Buffs;
using ItemStatsSystem;

namespace MagicTreasurePower.Treasures
{
    /// <summary>
    /// 法宝共用的「异常状态」类目。
    ///
    /// 金蛟剪（命中时随机移除一种异常的一层）与定海珠（免死触发时清空、护体期间免疫）
    /// **共用这同一张表**，改这里两边同时生效。
    ///
    /// 只收原版里明确可以安全移除的负面状态，刻意排除：
    /// 1002 出血变体、1130 食物中毒、2001 缺氧、1129 机器入侵、1032 饥饿、
    /// 2101 寒冷、2102 失温、2201 冻伤、1131 浸水（环境 / 系统持续施加）、
    /// 1 脱水、1022/1023/1024 负重三档，
    /// 1111/1112/1117 空间系、1127 冻结、1128 纳米机器（连锁上游），
    /// 以及 1062/1063 变异病毒与变异（剧情机制）。
    /// </summary>
    internal static class TreasureDebuffs
    {
        /// <summary>可清除 / 可免疫的异常状态（原版 Buff ID）。</summary>
        internal static readonly int[] Ids =
        {
            1001,                                 // 出血
            1003,                                 // 骨折
            1004,                                 // 创伤
            1016,                                 // 萎靡
            1041, 1042,                           // 震慑（长 / 短）
            1061,                                 // 中毒
            1071,                                 // 触电
            1081,                                 // 疼痛
            1121, 1125,                           // 点燃 / 燃烧
            1122,                                 // 弱毒
            1123,                                 // 恶心
            1124,                                 // 害怕
            1126,                                 // 冰冻
        };

        /// <summary>该 Buff ID 是否属于本类目的异常状态。</summary>
        internal static bool Contains(int buffId)
        {
            for (int i = 0; i < Ids.Length; i++)
            {
                if (Ids[i] == buffId)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 清空角色身上所有属于本类目的异常状态（整条移除，不是只掉一层）。
        /// 返回实际清掉的个数，供提示文案使用。
        /// </summary>
        internal static int ClearAll(CharacterMainControl character)
        {
            if (character == null)
                return 0;

            CharacterBuffManager manager = character.GetBuffManager();
            if (manager == null)
                return 0;

            // 先收集 ID 再移除：RemoveBuff 会改动 manager.Buffs，不能在遍历中直接删
            List<int> remove = new List<int>();
            foreach (Buff buff in manager.Buffs)
            {
                if (buff != null && Contains(buff.ID))
                    remove.Add(buff.ID);
            }

            for (int i = 0; i < remove.Count; i++)
                character.RemoveBuff(remove[i], false);

            return remove.Count;
        }
    }
}
