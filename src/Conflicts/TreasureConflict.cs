namespace MagicTreasurePower.Conflicts
{
    /// <summary>加载时 ID 冲突的来源。</summary>
    internal enum TreasureConflictKind
    {
        /// <summary>与原版游戏的自带物品撞号。</summary>
        Vanilla,

        /// <summary>与「法宝之力」的另一个副本（本地 + 工坊重复安装、旧版本）撞号。</summary>
        DuplicateCopy,

        /// <summary>与其它 mod 已注册的动态物品撞号。</summary>
        OtherMod,
    }

    /// <summary>一次 ID 冲突的上下文；发生冲突的法宝不会注册。</summary>
    internal sealed class TreasureConflict
    {
        internal TreasureConflictKind Kind;
        internal int TypeId;
        internal string TreasureName;
        internal string PrefabName;

        /// <summary>冲突归属的 mod 显示名，解析失败时为空（日志里退回物品原型名）。</summary>
        internal string Owner;
    }

    internal static class ConflictDescriber
    {
        /// <summary>日志用描述。</summary>
        internal static string Describe(TreasureConflict conflict)
        {
            if (conflict == null)
                return "未知物品";

            switch (conflict.Kind)
            {
                case TreasureConflictKind.Vanilla:
                    return $"原版物品「{conflict.PrefabName}」";

                case TreasureConflictKind.DuplicateCopy:
                    return "另一个「法宝之力」副本（重复安装 / 旧版本）";

                default:
                    if (!string.IsNullOrEmpty(conflict.Owner))
                        return $"Mod「{conflict.Owner}」";

                    return string.IsNullOrEmpty(conflict.PrefabName)
                        ? "其它 Mod"
                        : $"其它 Mod（物品原型「{conflict.PrefabName}」）";
            }
        }

        /// <summary>进入游戏后弹出的 3 秒提示文本。</summary>
        internal static string BuildNotification(TreasureConflict conflict)
        {
            switch (conflict.Kind)
            {
                case TreasureConflictKind.Vanilla:
                    return $"法宝之力：ID {conflict.TypeId} 与原版游戏物品冲突，" +
                           $"「{conflict.TreasureName}」未加载";

                case TreasureConflictKind.DuplicateCopy:
                    return $"法宝之力：ID {conflict.TypeId} 与另一个「法宝之力」副本冲突，" +
                           $"「{conflict.TreasureName}」未加载";

                default:
                    string owner = string.IsNullOrEmpty(conflict.Owner) ? "其它 Mod" : conflict.Owner;
                    return $"法宝之力：ID {conflict.TypeId} 与 Mod「{owner}」冲突，" +
                           $"「{conflict.TreasureName}」未加载";
            }
        }
    }
}
