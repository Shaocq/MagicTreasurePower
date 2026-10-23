using System;
using System.Collections.Generic;
using System.Reflection;
using Duckov.Buffs;
using Duckov.Utilities;

namespace MagicTreasurePower.Treasures
{
    /// <summary>
    /// 原版 Buff 预设表：`GameplayDataSettings.Buffs` 只公开了少数几个命名属性，
    /// 全部预设藏在私有字段 <c>allBuffs</c> 里，需要反射取一次并缓存成字典。
    /// </summary>
    internal static class VanillaBuffs
    {
        private static Dictionary<int, Buff> _map;

        internal static Dictionary<int, Buff> All
        {
            get
            {
                if (_map != null)
                    return _map;

                _map = new Dictionary<int, Buff>();

                try
                {
                    FieldInfo allBuffsField = typeof(GameplayDataSettings.BuffsData)
                        .GetField("allBuffs", BindingFlags.Instance | BindingFlags.NonPublic);

                    List<Buff> list = allBuffsField != null
                        ? allBuffsField.GetValue(GameplayDataSettings.Buffs) as List<Buff>
                        : null;

                    if (list == null)
                    {
                        Diag.Warn("读取原版 Buff 预设失败：allBuffs 未找到");
                        return _map;
                    }

                    foreach (Buff buff in list)
                    {
                        if (buff != null && !_map.ContainsKey(buff.ID))
                            _map[buff.ID] = buff;
                    }
                }
                catch (Exception e)
                {
                    Diag.Report("读取原版 Buff 预设失败", e);
                }

                return _map;
            }
        }

        /// <summary>按 ID 取原版预设；找不到返回 null。</summary>
        internal static Buff Get(int id)
        {
            Buff buff;
            return All.TryGetValue(id, out buff) ? buff : null;
        }
    }
}
