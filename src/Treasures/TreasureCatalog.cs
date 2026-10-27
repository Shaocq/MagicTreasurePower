using ItemStatsSystem;

namespace MagicTreasurePower.Treasures
{
    /// <summary>
    /// 七件法宝（首批六件 + 火眼金睛）。
    ///
    /// 素材文件名与法宝的对应关系（用户指定）：
    ///   fty  = 翻天印        yyj  = 阴阳镜
    ///   dhz  = 定海珠        hyjd = 混元金斗
    ///   jjj  = 金蛟剪        jllhz= 九龙离火罩
    ///
    /// 当前数值（重量 0.2 / 价值 999999 / 品质 7）：重量 0.2 由用户 2026-10-24 指定，
    /// 其余数值由用户 2026-10-20 指定，后续可调整；
    /// 调整这些数值不影响存档（ID 不变即可），但**不要改 TypeId**。
    /// </summary>
    internal static class TreasureCatalog
    {
        /// <summary>按 TypeID 取法宝显示名；找不到时返回空串。</summary>
        internal static string GetDisplayName(int typeId)
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].TypeId == typeId)
                    return All[i].DisplayName;
            }

            return string.Empty;
        }

        internal static readonly TreasureDefinition[] All =
        {
            new TreasureDefinition
            {
                TypeId = TreasureIds.FanTianYin,
                ConstantName = nameof(TreasureIds.FanTianYin),
                DisplayName = "翻天印",
                Description = "广成子镇洞之宝。祭出时迎风而长，如山岳倾压，寻常血肉之躯难挡一击。",
                EffectText = "射击时，有 20% 概率不消耗弹药，并额外射出 3 发子弹（每发造成 100% 伤害）。",
                ResourceName = "fty.png",
                Weight = 0.2f,
                Value = 999999,
                Quality = 7,
                DisplayQuality = DisplayQuality.Q7,
                Tags = new[] { "Luxury" },
            },
            new TreasureDefinition
            {
                TypeId = TreasureIds.YinYangJing,
                ConstantName = nameof(TreasureIds.YinYangJing),
                DisplayName = "阴阳镜",
                Description = "赤精子之宝。镜分阴阳两面，白光照生、黑光定死，照定魂魄便由持镜人做主。",
                EffectText = "常驻减伤：最终承受的伤害降低 20%（乘算，即结算完成后 ×0.8），且每次受到的伤害最低为 1 点。",
                ResourceName = "yyj.png",
                Weight = 0.2f,
                Value = 999999,
                Quality = 7,
                DisplayQuality = DisplayQuality.Q7,
                Tags = new[] { "Luxury" },
            },
            new TreasureDefinition
            {
                TypeId = TreasureIds.DingHaiZhu,
                ConstantName = nameof(TreasureIds.DingHaiZhu),
                DisplayName = "定海珠",
                Description = "赵公明之宝，共二十四颗。祭起时碧海翻涛，宝珠化作流光，专打顶上三花。",
                EffectText = "受到伤害时，有 20% 概率免疫本次伤害。每场战局中，受到致命伤害时可免疫死亡 3 次：生命值锁定为 1 点，持续 5 秒，触发时清除自身全部异常状态，护体期间免疫异常状态；换图后重新可用。",
                ResourceName = "dhz.png",
                Weight = 0.2f,
                Value = 999999,
                Quality = 7,
                DisplayQuality = DisplayQuality.Q7,
                Tags = new[] { "Luxury" },
            },
            new TreasureDefinition
            {
                TypeId = TreasureIds.HunYuanJinDou,
                ConstantName = nameof(TreasureIds.HunYuanJinDou),
                DisplayName = "混元金斗",
                Description = "三霄娘娘之宝。金光一落，仙凡皆收，任你道行高深也难脱斗中之厄。",
                EffectText = "射击时，有 30% 概率，命中后额外造成「枪械基础伤害 × 1.5」的真实伤害：在本次伤害结算完成后单独生效，不受护甲、元素抗性、暴击与距离衰减影响。",
                ResourceName = "hyjd.png",
                Weight = 0.2f,
                Value = 999999,
                Quality = 7,
                DisplayQuality = DisplayQuality.Q7,
                Tags = new[] { "Luxury" },
            },
            new TreasureDefinition
            {
                TypeId = TreasureIds.JinJiaoJian,
                ConstantName = nameof(TreasureIds.JinJiaoJian),
                DisplayName = "金蛟剪",
                Description = "金蛟化剪，双龙交剪。祭在空中只一剪，利可分金断玉、剪断法宝灵光。",
                EffectText = "每次射击有 30% 概率触发；命中敌人时：若自身带有异常状态则随机移除其中一种的一层，否则获得一个随机增益（持续 8 秒）；不同增益可同时存在，重复获得同一增益时持续时间累加。",
                ResourceName = "jjj.png",
                Weight = 0.2f,
                Value = 999999,
                Quality = 7,
                DisplayQuality = DisplayQuality.Q7,
                Tags = new[] { "Luxury" },
            },
            new TreasureDefinition
            {
                TypeId = TreasureIds.JiuLongLiHuoZhao,
                ConstantName = nameof(TreasureIds.JiuLongLiHuoZhao),
                DisplayName = "九龙离火罩",
                Description = "九条火龙盘旋而成。罩落之处烈焰焚天，任你腾挪变化也难逃火网。",
                EffectText = "装备时：身体与头部护甲 +5，火、毒、电、空间、幽灵、冰六种元素抗性各 +20%，背包容量 +20 格。",
                ResourceName = "jllhz.png",
                Weight = 0.2f,
                Value = 999999,
                Quality = 7,
                DisplayQuality = DisplayQuality.Q7,
                Tags = new[] { "Luxury" },
            },
            new TreasureDefinition
            {
                TypeId = TreasureIds.HuoYanJinJing,
                ConstantName = nameof(TreasureIds.HuoYanJinJing),
                DisplayName = "火眼金睛",
                Description = "八卦炉中炼出的眼目，能看破虚妄、直抵本源。凡尘障眼之物，在它面前无处遁形。",
                EffectText = "进入战局时自动扫描全场物资箱，列出其中价值最高的 1 件物品；上锁的物资箱仅在已用钥匙录入机解锁或随身携带对应钥匙时才会被扫描。",
                ResourceName = "hyjj.png",
                Weight = 0.2f,
                Value = 999999,
                Quality = 7,
                DisplayQuality = DisplayQuality.Q7,
                Tags = new[] { "Luxury" },
            },
        };
    }
}
