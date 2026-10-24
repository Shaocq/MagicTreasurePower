using System;
using System.Reflection;
using ItemStatsSystem;

namespace MagicTreasurePower.Treasures
{
    /// <summary>
    /// 法宝各等级的概率与强度（**Lv.7 = 当前正式数值**，低等级向下拆分）。
    ///
    /// 所有可随等级变化的数值集中在这里，后续调平衡只改这一张表。
    /// 详情页 / 悬停提示里的可变数值会用金色显示（见 <see cref="FormatEffectText"/>）。
    /// </summary>
    internal static class TreasureBalance
    {
        // ---------------------------------------------------------------- 翻天印

        /// <summary>触发概率（%）。</summary>
        private static readonly int[] FanTianYinChance = { 8, 10, 12, 14, 16, 18, 20 };

        /// <summary>额外那发子弹的伤害系数。</summary>
        private static readonly float[] FanTianYinExtraDamage = { 0.6f, 0.65f, 0.7f, 0.8f, 0.85f, 0.9f, 1f };

        // ---------------------------------------------------------------- 阴阳镜

        /// <summary>最终伤害减免（%）。</summary>
        private static readonly int[] YinYangJingReduction = { 8, 10, 12, 14, 16, 18, 20 };

        // ---------------------------------------------------------------- 定海珠

        /// <summary>免伤概率（%）：8% 起，每级 +2%。</summary>
        private static readonly int[] DingHaiZhuNegate = { 8, 10, 12, 14, 16, 18, 20 };

        /// <summary>免死锁血时长（秒）：全等级统一 5 秒。</summary>
        internal const float DingHaiZhuWardSeconds = 5f;

        /// <summary>每场战局的免死次数：基础 1 次，Lv.4 起 2 次，Lv.7 起 3 次。</summary>
        private static readonly int[] DingHaiZhuSaves = { 1, 1, 1, 2, 2, 2, 3 };

        // ---------------------------------------------------------------- 混元金斗

        /// <summary>触发概率（%）。</summary>
        private static readonly int[] HunYuanJinDouChance = { 12, 15, 18, 21, 24, 27, 30 };

        /// <summary>附加倍率（枪械基础伤害 × N）。</summary>
        private static readonly float[] HunYuanJinDouMultiplier = { 1.0f, 1.1f, 1.2f, 1.2f, 1.3f, 1.4f, 1.5f };

        // ---------------------------------------------------------------- 金蛟剪

        /// <summary>触发概率（%）。</summary>
        private static readonly int[] JinJiaoJianChance = { 12, 15, 18, 21, 24, 27, 30 };

        /// <summary>获得增益的持续时间（秒）。</summary>
        private static readonly float[] JinJiaoJianBuffTime = { 4f, 5f, 5f, 6f, 6f, 7f, 8f };

        // ---------------------------------------------------------------- 九龙离火罩

        /// <summary>身体 / 头部护甲加成。</summary>
        private static readonly float[] JiuLongArmor = { 2f, 2f, 3f, 3f, 4f, 4f, 5f };

        /// <summary>六种元素抗性（%）。</summary>
        private static readonly int[] JiuLongResist = { 8, 10, 12, 14, 16, 18, 20 };

        /// <summary>背包容量加成（格）。</summary>
        private static readonly int[] JiuLongSlots = { 8, 10, 12, 14, 16, 18, 20 };

        private const string GoldColor = "#E8BF5C";

        private static FieldInfo _effectDescriptionField;

        // ---------------------------------------------------------------- 取值

        private static int Index(int level)
        {
            if (level < 1)
                return 0;
            if (level > TreasureProgression.MaxLevel)
                return TreasureProgression.MaxLevel - 1;
            return level - 1;
        }

        internal static float FanTianYinChanceOf(int level) => FanTianYinChance[Index(level)] / 100f;
        internal static float FanTianYinExtraDamageOf(int level) => FanTianYinExtraDamage[Index(level)];
        internal static float YinYangJingReductionOf(int level) => YinYangJingReduction[Index(level)] / 100f;
        internal static float DingHaiZhuNegateOf(int level) => DingHaiZhuNegate[Index(level)] / 100f;
        internal static float DingHaiZhuWardOf(int level) => DingHaiZhuWardSeconds;
        internal static int DingHaiZhuSavesOf(int level) => DingHaiZhuSaves[Index(level)];
        internal static float HunYuanJinDouChanceOf(int level) => HunYuanJinDouChance[Index(level)] / 100f;
        internal static float HunYuanJinDouMultiplierOf(int level) => HunYuanJinDouMultiplier[Index(level)];
        internal static float JinJiaoJianChanceOf(int level) => JinJiaoJianChance[Index(level)] / 100f;
        internal static float JinJiaoJianBuffTimeOf(int level) => JinJiaoJianBuffTime[Index(level)];
        internal static float JiuLongArmorOf(int level) => JiuLongArmor[Index(level)];
        internal static float JiuLongResistOf(int level) => JiuLongResist[Index(level)] / 100f;
        internal static float JiuLongSlotsOf(int level) => JiuLongSlots[Index(level)];

        // ---------------------------------------------------------------- 文案

        /// <summary>
        /// 按等级生成法宝效果词条；可变数值用金色富文本标记。
        /// 非法宝 / 未定义时返回 null。
        /// </summary>
        internal static string FormatEffectText(int typeId, int level)
        {
            switch (typeId)
            {
                case TreasureIds.FanTianYin:
                    return "射击时，有 " + Gold(FanTianYinChance[Index(level)] + "%") +
                           " 概率不消耗弹药，并额外射出 1 发子弹（该子弹造成 " +
                           Gold(Mathf_Round(FanTianYinExtraDamage[Index(level)] * 100f) + "%") + " 伤害）。";

                case TreasureIds.YinYangJing:
                    return "常驻减伤：最终承受的伤害降低 " + Gold(YinYangJingReduction[Index(level)] + "%") +
                           "（乘算），且每次受到的伤害最低为 1 点。";

                case TreasureIds.DingHaiZhu:
                    return "受到伤害时有 " + Gold(DingHaiZhuNegate[Index(level)] + "%") +
                           " 概率免疫本次伤害。每场战局中，受到致命伤害时可免疫死亡 " +
                           Gold(DingHaiZhuSaves[Index(level)] + " 次") +
                           "：生命值锁定为 1 点，持续 " +
                           Gold(DingHaiZhuWardSeconds + " 秒") +
                           "，触发时清除自身全部异常状态，护体期间免疫异常状态；换图后重新可用。";

                case TreasureIds.HunYuanJinDou:
                    return "每次射击有 " + Gold(HunYuanJinDouChance[Index(level)] + "%") +
                           " 概率使该次射击的子弹在护甲结算前额外附加「枪械基础伤害 × " +
                           Gold(HunYuanJinDouMultiplier[Index(level)].ToString("0.0")) + "」的伤害。";

                case TreasureIds.JinJiaoJian:
                    return "每次射击有 " + Gold(JinJiaoJianChance[Index(level)] + "%") +
                           " 概率，命中敌人时随机移除自身一种异常状态的一层；自身没有异常状态时，获得一个随机增益（持续 " +
                           Gold(JinJiaoJianBuffTime[Index(level)] + " 秒") + "，不同增益可共存，重复获得时时间累加）。";

                case TreasureIds.JiuLongLiHuoZhao:
                    return "装备时：身体与头部护甲 " + Gold("+" + JiuLongArmor[Index(level)]) +
                           "，火、毒、电、空间、幽灵、冰六种元素抗性各 " + Gold("+" + JiuLongResist[Index(level)] + "%") +
                           "，背包容量 " + Gold("+" + JiuLongSlots[Index(level)] + " 格") + "。";
            }

            return null;
        }

        /// <summary>
        /// 把法宝实例的 Effect 描述更新为当前等级对应的文案。
        /// 详情面板读取的是 Effect.Description，所以要在它 Setup 之前更新。
        /// </summary>
        internal static void ApplyEffectText(Item item)
        {
            try
            {
                if (item == null || item.Effects == null)
                    return;

                string text = FormatEffectText(item.TypeID, TreasureProgression.GetLevel(item));
                if (string.IsNullOrEmpty(text))
                    return;

                if (_effectDescriptionField == null)
                    _effectDescriptionField = typeof(Effect).GetField(
                        "description", BindingFlags.Instance | BindingFlags.NonPublic);

                if (_effectDescriptionField == null)
                    return;

                foreach (Effect effect in item.Effects)
                {
                    if (effect == null)
                        continue;

                    _effectDescriptionField.SetValue(effect, text);
                    break;
                }
            }
            catch (Exception e)
            {
                Diag.Report("更新法宝等级词条失败", e);
            }
        }

        private static string Gold(string value)
        {
            return "<color=" + GoldColor + ">" + value + "</color>";
        }

        private static int Mathf_Round(float value)
        {
            return (int)Math.Round(value, MidpointRounding.AwayFromZero);
        }
    }
}
