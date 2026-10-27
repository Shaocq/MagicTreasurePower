namespace MagicTreasurePower
{
    /// <summary>
    /// 「法宝之力」自定义物品 ID 段（用户指定，2026-10-20）。
    ///
    /// <para><b>区间约定：17350 ~ 17399，由本 mod 独占。</b>
    /// 其它 mod 不要使用该区间；本 mod 也不要越界申请，以免与后续 mod 冲突。</para>
    ///
    /// <para><b>分配规则：</b></para>
    /// <list type="number">
    ///   <item>按登记表从上到下依次占用，不复用、不跳号；</item>
    ///   <item>物品一旦随 mod 发布，ID 永远不能改——玩家存档按 TypeID 记录物品，改 ID 会让旧存档里的法宝变成错误物品或直接消失；</item>
    ///   <item>每新增一件法宝，必须在下方「物品登记表」补一行：ID / 代码常量名 / 游戏内名称 / 克隆用原版模板 / 备注；</item>
    ///   <item>区间用满之前先与用户确认是否扩展，不要私自改成其它数字。</item>
    /// </list>
    ///
    /// <para><b>物品登记表（2026-10-20 首批六件 + 火眼金睛）：</b></para>
    /// <code>
    /// ID    | 常量名             | 游戏内名称 | 原版模板 | 素材      | 备注
    /// ------+--------------------+------------+----------+-----------+--------------------------
    /// 17350 | FanTianYin         | 翻天印     | 0        | fty.png   | 注册顺序 1，暂定数值
    /// 17351 | YinYangJing        | 阴阳镜     | 0        | yyj.png   | 注册顺序 2，暂定数值
    /// 17352 | DingHaiZhu         | 定海珠     | 0        | dhz.png   | 注册顺序 3，暂定数值
    /// 17353 | HunYuanJinDou      | 混元金斗   | 0        | hyjd.png  | 注册顺序 4，暂定数值
    /// 17354 | JinJiaoJian        | 金蛟剪     | 0        | jjj.png   | 注册顺序 5，暂定数值
    /// 17355 | JiuLongLiHuoZhao   | 九龙离火罩 | 0        | jllhz.png | 注册顺序 6，暂定数值
    /// 17356 | HuoYanJinJing      | 火眼金睛   | 0        | hyjj.png  | 注册顺序 7，战局物资扫描
    /// 17357 | （尚未分配）        | —          | —        | —         | 后续扩展
    /// ...   | ...                | ...        | ...      | ...       | ...
    /// 17399 | 区间末尾            | —          | —        | —         | 暂时保留
    /// </code>
    /// </summary>
    internal static class TreasureIds
    {
        /// <summary>法宝 ID 段起点（含）。用户指定从此开始。</summary>
        internal const int RangeStart = 17350;

        /// <summary>法宝 ID 段终点（含）。先预留 50 个，用满前需与用户确认扩展。</summary>
        internal const int RangeEnd = 17399;

        /// <summary>翻天印。</summary>
        internal const int FanTianYin = 17350;

        /// <summary>阴阳镜。</summary>
        internal const int YinYangJing = 17351;

        /// <summary>定海珠。</summary>
        internal const int DingHaiZhu = 17352;

        /// <summary>混元金斗。</summary>
        internal const int HunYuanJinDou = 17353;

        /// <summary>金蛟剪。</summary>
        internal const int JinJiaoJian = 17354;

        /// <summary>九龙离火罩。</summary>
        internal const int JiuLongLiHuoZhao = 17355;

        /// <summary>火眼金睛。</summary>
        internal const int HuoYanJinJing = 17356;

        /// <summary>判断某个 TypeID 是否属于本 mod 的法宝 ID 段。</summary>
        internal static bool IsInRange(int typeId)
        {
            return typeId >= RangeStart && typeId <= RangeEnd;
        }

        /// <summary>ID 段说明，供启动日志输出。</summary>
        internal static string Describe()
        {
            return $"法宝物品 ID 段 {RangeStart}~{RangeEnd}（独占）";
        }
    }
}
