using System.Reflection;
using ItemStatsSystem;

namespace MagicTreasurePower
{
    /// <summary>
    /// Item 的私有字段读写：物品的 typeID / displayName / icon 等没有公开 setter，
    /// 沿用存储拓展已验证的做法通过反射写入。
    /// </summary>
    internal static class ItemExtensions
    {
        internal static bool SetPrivateField(this Item item, string fieldName, object value)
        {
            FieldInfo field = typeof(Item).GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null)
            {
                field.SetValue(item, value);
                return true;
            }

            Diag.Warn($"Item 未找到私有字段「{fieldName}」，可能游戏结构已变化");
            return false;
        }
    }
}
