using ItemStatsSystem;

namespace MagicTreasurePower.Treasures
{
    /// <summary>
    /// 单件法宝的注册配置。
    /// ID 与常量名必须与 <see cref="TreasureIds"/> 的登记表保持一致。
    /// </summary>
    internal sealed class TreasureDefinition
    {
        /// <summary>自定义 TypeID（17350 起，登记表见 TreasureIds.cs）。</summary>
        internal int TypeId;

        /// <summary>代码里的常量名，仅用于日志与排查。</summary>
        internal string ConstantName;

        /// <summary>游戏内中文名称。</summary>
        internal string DisplayName;

        /// <summary>物品描述（会显示在详情面板）。</summary>
        internal string Description;

        /// <summary>
        /// 法宝效果词条（显示在详情面板的属性区，走原版 Effect 条目）。
        /// 概率 / 数值等由后续法宝升级系统填入，这里只写效果本身的说明。
        /// </summary>
        internal string EffectText;

        /// <summary>内嵌 PNG 资源名（大小写与文件名一致）。</summary>
        internal string ResourceName;

        /// <summary>重量，单位与游戏一致。</summary>
        internal float Weight;

        /// <summary>基础价值。</summary>
        internal int Value;

        /// <summary>品质数值（quality 字段）。</summary>
        internal int Quality;

        /// <summary>品质显示档位（displayQuality 字段，决定图标光效；需与 Quality 对应）。</summary>
        internal DisplayQuality DisplayQuality;

        /// <summary>物品标签；为空表示不加入任何品类限制。</summary>
        internal string[] Tags;

        /// <summary>名称的本地化 key；描述的 key 由游戏自动追加 "_Desc"。</summary>
        internal string LocalizationKey => $"MagicTreasurePower_{TypeId}";

        /// <summary>描述的本地化 key。</summary>
        internal string DescriptionKey => LocalizationKey + "_Desc";
    }
}
