using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Duckov.Modding;
using Duckov.Utilities;
using ItemStatsSystem;
using MagicTreasurePower.Conflicts;
using SodaCraft.Localizations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MagicTreasurePower.Treasures
{
    /// <summary>
    /// 把 <see cref="TreasureCatalog"/> 里的法宝注册进游戏的动态物品表。
    ///
    /// 流程与存储拓展验证过的一致：
    ///   GetPrefab(模板) → Instantiate → 改私有字段 / 图标 / 标签 → AddDynamicEntry。
    ///
    /// 模板使用原版 ID 0：这是原版物品表里存在的占位条目（存储拓展的「木头模特」已验证
    /// GetPrefab(0) 不会返回 null），适合做无槽位、无额外组件的干净底子。
    /// </summary>
    internal static class TreasureRegistrar
    {
        /// <summary>克隆用的原版模板 ID（占位条目，见类注释）。</summary>
        private const int TemplateItemId = 0;

        /// <summary>自建标签名；显示名取自本地化 key Tag_MagicTreasure。</summary>
        private const string TreasureTagName = "MagicTreasure";

        private static Tag _treasureTag;

        /// <summary>注册成功的法宝 TypeID（ID 冲突被跳过的不会进来）。</summary>
        private static readonly HashSet<int> RegisteredTypeIds = new HashSet<int>();

        /// <summary>该 TypeID 的法宝是否已经成功注册。</summary>
        internal static bool IsRegistered(int typeId)
        {
            return RegisteredTypeIds.Contains(typeId);
        }

        /// <summary>注册原型 GameObject 的前缀，用于识别「本 mod 的另一个副本」。</summary>
        internal const string PrototypeNamePrefix = "MagicTreasurePower_";

        /// <summary>加载时检测到的 ID 冲突（发生冲突的法宝不会注册）。</summary>
        private static readonly List<TreasureConflict> Conflicts = new List<TreasureConflict>();

        internal static IReadOnlyList<TreasureConflict> ConflictList => Conflicts;

        private sealed class IconHolder : MonoBehaviour
        {
            internal Texture2D Texture;
            internal Sprite Sprite;
        }

        /// <summary>已注册法宝的图标（TypeID → Sprite），供提示框等 UI 复用。</summary>
        private static readonly Dictionary<int, Sprite> IconCache = new Dictionary<int, Sprite>();

        /// <summary>取法宝图标；未注册 / 加载失败时返回 null。</summary>
        internal static Sprite GetIcon(int typeId)
        {
            Sprite sprite;
            return IconCache.TryGetValue(typeId, out sprite) ? sprite : null;
        }

        internal static void RegisterAll()
        {
            foreach (TreasureDefinition definition in TreasureCatalog.All)
            {
                try
                {
                    RegisterOne(definition);
                }
                catch (Exception e)
                {
                    Diag.Report($"注册法宝失败：{definition.DisplayName}（ID {definition.TypeId}）", e);
                }
            }
        }

        private static void RegisterOne(TreasureDefinition definition)
        {
            if (!TreasureIds.IsInRange(definition.TypeId))
            {
                Diag.Warn(
                    $"法宝「{definition.DisplayName}」的 ID {definition.TypeId} " +
                    $"不在本 mod 的 {TreasureIds.RangeStart}~{TreasureIds.RangeEnd} 区间，已跳过");
                return;
            }

            // 游戏内「禁用 → 重新启用」时，上一次注册的动态条目还在表里，
            // 原型名带本 mod 前缀说明就是自己，直接复用而不是当作「另一个副本」报冲突
            if (ItemAssetsCollection.TryGetDynamicEntry(
                    definition.TypeId, out ItemAssetsCollection.DynamicEntry existing) &&
                existing != null && existing.prefab != null &&
                existing.prefab.name.StartsWith(PrototypeNamePrefix, StringComparison.Ordinal))
            {
                ApplyIcon(existing.prefab, definition);
                ApplyLocalization(definition);
                RegisteredTypeIds.Add(definition.TypeId);
                Diag.Log($"法宝「{definition.DisplayName}」（ID {definition.TypeId}）已存在，复用旧实例");
                return;
            }

            // ID 冲突检测：与其它 mod 已注册的动态物品、或原版物品撞号时，
            // 该法宝直接不加载，并记录冲突以便进入游戏后提示玩家
            if (TryDetectConflict(definition, out TreasureConflict conflict))
            {
                Conflicts.Add(conflict);
                Diag.Warn(
                    $"ID 冲突：法宝「{definition.DisplayName}」（ID {definition.TypeId}）与 " +
                    $"{ConflictDescriber.Describe(conflict)} 冲突，已跳过加载");
                return;
            }

            Item template = ItemAssetsCollection.GetPrefab(TemplateItemId);
            if (template == null)
            {
                Diag.Report(
                    $"原版模板 ID {TemplateItemId} 不存在，无法创建「{definition.DisplayName}」",
                    new InvalidOperationException("模板物品为空"));
                return;
            }

            GameObject clone = Object.Instantiate(template.gameObject);
            clone.name = $"MagicTreasurePower_{definition.TypeId}_{definition.ConstantName}";
            Object.DontDestroyOnLoad(clone);

            Item item = clone.GetComponent<Item>();
            if (item == null)
            {
                Diag.Warn($"模板 {TemplateItemId} 上没有 Item 组件，「{definition.DisplayName}」注册取消");
                Object.Destroy(clone);
                return;
            }

            ApplyProperties(item, definition);
            ApplyEffectEntry(item, definition);
            ApplyIcon(item, definition);
            ApplyLocalization(definition);

            if (ItemAssetsCollection.AddDynamicEntry(item))
            {
                RegisteredTypeIds.Add(definition.TypeId);
                Diag.Log(
                    $"已注册法宝：ID {definition.TypeId}，{definition.DisplayName}，" +
                    $"素材 {definition.ResourceName}");
            }
            else
            {
                Diag.Warn($"注册法宝失败（AddDynamicEntry 返回 false）：{definition.DisplayName}");
                Object.Destroy(clone);
            }
        }

        /// <summary>
        /// 注册前检测 ID 冲突。
        ///
        /// 判定顺序：
        ///   1. 动态物品表里已经有这个 ID —— 说明其它 mod（或本 mod 的另一个副本）
        ///      先注册过，继续注册会直接覆盖对方的物品；
        ///   2. 原版物品表里已有这个 ID —— 会覆盖原版物品。
        /// 两者都算冲突，调用方会跳过该法宝的注册。
        /// </summary>
        private static bool TryDetectConflict(
            TreasureDefinition definition,
            out TreasureConflict conflict)
        {
            conflict = null;

            int typeId = definition.TypeId;

            if (ItemAssetsCollection.TryGetDynamicEntry(
                    typeId, out ItemAssetsCollection.DynamicEntry dynamic) && dynamic != null)
            {
                string prefabName = dynamic.prefab != null ? dynamic.prefab.name : "";
                bool duplicateCopy = !string.IsNullOrEmpty(prefabName) &&
                                     prefabName.StartsWith(PrototypeNamePrefix, StringComparison.Ordinal);

                conflict = new TreasureConflict
                {
                    Kind = duplicateCopy ? TreasureConflictKind.DuplicateCopy : TreasureConflictKind.OtherMod,
                    TypeId = typeId,
                    TreasureName = definition.DisplayName,
                    PrefabName = prefabName,
                    Owner = duplicateCopy ? "法宝之力（另一个副本 / 旧版本）" : null,
                };
                return true;
            }

            ItemAssetsCollection collection = ItemAssetsCollection.Instance;
            if (collection != null && collection.entries != null)
            {
                foreach (ItemAssetsCollection.Entry entry in collection.entries)
                {
                    if (entry == null || entry.typeID != typeId)
                        continue;

                    conflict = new TreasureConflict
                    {
                        Kind = TreasureConflictKind.Vanilla,
                        TypeId = typeId,
                        TreasureName = definition.DisplayName,
                        PrefabName = entry.prefab != null ? entry.prefab.name : "",
                        Owner = "原版游戏",
                    };
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 尽量解析冲突所属的其它 mod 名称。
        ///
        /// 动态物品表只记录原型，不记录注册者，所以只能反查：用前置 0Harmony 内嵌的
        /// Mono.Cecil 读取其它已启用 mod 的 DLL，查找是否有方法加载了该 ID 常量。
        /// 属于启发式匹配（数值恰好相同也会命中），解析不到时退回物品原型名。
        /// 调用时机在进入游戏后，HarmonyLib 通常已经加载。
        /// </summary>
        internal static void ResolveUnknownConflictOwners()
        {
            var unknown = new List<TreasureConflict>();
            foreach (TreasureConflict conflict in Conflicts)
            {
                if (conflict.Kind == TreasureConflictKind.OtherMod &&
                    string.IsNullOrEmpty(conflict.Owner))
                {
                    unknown.Add(conflict);
                }
            }

            if (unknown.Count == 0)
                return;

            try
            {
                if (HarmonyLoad.Load0Harmony() == null)
                {
                    foreach (TreasureConflict conflict in unknown)
                        conflict.Owner = FallbackOwner(conflict);
                    return;
                }

                var typeIds = new List<int>();
                foreach (TreasureConflict conflict in unknown)
                {
                    if (!typeIds.Contains(conflict.TypeId))
                        typeIds.Add(conflict.TypeId);
                }

                Dictionary<int, string> owners = FindModOwnersByTypeIds(typeIds);
                foreach (TreasureConflict conflict in unknown)
                {
                    string owner;
                    conflict.Owner = owners.TryGetValue(conflict.TypeId, out owner) &&
                                     !string.IsNullOrEmpty(owner)
                        ? owner
                        : FallbackOwner(conflict);
                }
            }
            catch (Exception e)
            {
                Diag.Report("解析 ID 冲突所属 Mod 失败", e);
                foreach (TreasureConflict conflict in unknown)
                {
                    if (string.IsNullOrEmpty(conflict.Owner))
                        conflict.Owner = FallbackOwner(conflict);
                }
            }
        }

        private static string FallbackOwner(TreasureConflict conflict)
        {
            return string.IsNullOrEmpty(conflict.PrefabName)
                ? "其它 Mod"
                : $"其它 Mod（物品原型「{conflict.PrefabName}」）";
        }

        /// <summary>
        /// 逐个扫描已启用 mod 的 DLL，找出加载了目标 ID 常量的 mod。
        /// 0Harmony 里内嵌的 Mono.Cecil 类型是 internal 的，只能全反射调用。
        /// </summary>
        private static Dictionary<int, string> FindModOwnersByTypeIds(List<int> typeIds)
        {
            var result = new Dictionary<int, string>();
            var remaining = new HashSet<int>(typeIds);

            Assembly harmony = HarmonyLoad.Load0Harmony();
            Type assemblyDefinitionType = harmony?.GetType("Mono.Cecil.AssemblyDefinition");
            MethodInfo readAssembly = assemblyDefinitionType?.GetMethod(
                "ReadAssembly", new[] { typeof(string) });

            if (readAssembly == null)
            {
                Diag.Warn("未能定位 Mono.Cecil 的 ReadAssembly，无法解析冲突 Mod 的名称");
                return result;
            }

            foreach (ModInfo info in ModManager.modInfos)
            {
                if (remaining.Count == 0)
                    break;

                if (string.IsNullOrEmpty(info.name) || info.name == "MagicTreasurePower")
                    continue;

                try
                {
                    if (!ModManager.IsModActive(info, out _))
                        continue;

                    string dllPath = info.dllPath;
                    if (string.IsNullOrEmpty(dllPath) || !File.Exists(dllPath))
                        continue;

                    object assembly = readAssembly.Invoke(null, new object[] { dllPath });
                    HashSet<int> hits;
                    try
                    {
                        hits = assembly == null
                            ? new HashSet<int>()
                            : FindConstantsInAssembly(assembly, remaining);
                    }
                    finally
                    {
                        (assembly as IDisposable)?.Dispose();
                    }

                    if (hits.Count == 0)
                        continue;

                    string displayName = string.IsNullOrEmpty(info.displayName)
                        ? info.name
                        : info.displayName.Trim();

                    foreach (int typeId in hits)
                    {
                        result[typeId] = displayName;
                        remaining.Remove(typeId);
                    }
                }
                catch (Exception e)
                {
                    Diag.Warn($"扫描 Mod「{info.name}」的 DLL 以解析 ID 冲突时失败：{e.Message}");
                }
            }

            return result;
        }

        private static HashSet<int> FindConstantsInAssembly(
            object assembly,
            HashSet<int> targets)
        {
            var hits = new HashSet<int>();

            IEnumerable modules = GetProperty(assembly, "Modules") as IEnumerable;
            if (modules == null)
                return hits;

            foreach (object module in modules)
            {
                CollectModuleConstants(module, targets, hits);
                if (hits.Count == targets.Count)
                    return hits;
            }

            return hits;
        }

        private static void CollectModuleConstants(
            object module,
            HashSet<int> targets,
            HashSet<int> hits)
        {
            IEnumerable types = GetProperty(module, "Types") as IEnumerable;
            if (types == null)
                return;

            foreach (object type in types)
            {
                CollectTypeConstants(type, targets, hits);
                if (hits.Count == targets.Count)
                    return;
            }
        }

        private static void CollectTypeConstants(
            object type,
            HashSet<int> targets,
            HashSet<int> hits)
        {
            IEnumerable methods = GetProperty(type, "Methods") as IEnumerable;
            if (methods != null)
            {
                foreach (object method in methods)
                {
                    if (!(GetProperty(method, "HasBody") is bool hasBody) || !hasBody)
                        continue;

                    object body = GetProperty(method, "Body");
                    IEnumerable instructions = body == null
                        ? null
                        : GetProperty(body, "Instructions") as IEnumerable;

                    if (instructions == null)
                        continue;

                    foreach (object instruction in instructions)
                    {
                        int? constant = TryGetInt32Constant(instruction);
                        if (constant.HasValue && targets.Contains(constant.Value))
                            hits.Add(constant.Value);
                    }

                    if (hits.Count == targets.Count)
                        return;
                }
            }

            IEnumerable nested = GetProperty(type, "NestedTypes") as IEnumerable;
            if (nested == null)
                return;

            foreach (object nestedType in nested)
            {
                CollectTypeConstants(nestedType, targets, hits);
                if (hits.Count == targets.Count)
                    return;
            }
        }

        /// <summary>指令如果是 ldc.i4 系列，返回它压栈的 int 值；否则返回 null。</summary>
        private static int? TryGetInt32Constant(object instruction)
        {
            object opcode = GetProperty(instruction, "OpCode");
            string code = GetProperty(opcode, "Code")?.ToString();
            if (string.IsNullOrEmpty(code))
                return null;

            switch (code)
            {
                case "Ldc_I4_M1": return -1;
                case "Ldc_I4_0": return 0;
                case "Ldc_I4_1": return 1;
                case "Ldc_I4_2": return 2;
                case "Ldc_I4_3": return 3;
                case "Ldc_I4_4": return 4;
                case "Ldc_I4_5": return 5;
                case "Ldc_I4_6": return 6;
                case "Ldc_I4_7": return 7;
                case "Ldc_I4_8": return 8;
                case "Ldc_I4_S":
                    return GetProperty(instruction, "Operand") is sbyte small ? small : (int?)null;
                case "Ldc_I4":
                    return GetProperty(instruction, "Operand") is int number ? number : (int?)null;
                default:
                    return null;
            }
        }

        private static readonly Dictionary<string, PropertyInfo> CecilPropertyCache =
            new Dictionary<string, PropertyInfo>();

        /// <summary>带缓存地读取 Cecil 对象的公开属性（Cecil 类型是 internal，不能再加访问限定）。</summary>
        private static object GetProperty(object instance, string name)
        {
            if (instance == null)
                return null;

            Type type = instance.GetType();
            string key = type.FullName + "|" + name;

            PropertyInfo property;
            if (!CecilPropertyCache.TryGetValue(key, out property))
            {
                property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                CecilPropertyCache[key] = property;
            }

            return property?.GetValue(instance);
        }

        private static void ApplyProperties(Item item, TreasureDefinition definition)
        {
            item.SetPrivateField("typeID", definition.TypeId);
            item.SetPrivateField("weight", definition.Weight);
            item.SetPrivateField("value", definition.Value);
            item.SetPrivateField("quality", definition.Quality);
            // displayQuality 是独立字段：只改 quality 不会改变图标光效，两者都要写
            item.SetPrivateField("displayQuality", definition.DisplayQuality);
            item.SetPrivateField("displayName", definition.LocalizationKey);
            item.SetPrivateField("order", 0);

            // 模板 ID 0 是原版「Character」占位物品（玩家属性面板里的假人）：
            // 自带 10 个装备槽和 1000 格背包，法宝只需要物品本体，必须剥掉，
            // 否则详情面板会显示一身装备格、还能当背包用。
            StripCharacterComponents(item);

            // 模板自带的「枪械伤害倍率 +100%」等显示词条只是假人占位数据，
            // 法宝的词条改由 ApplyEffectEntry 单独挂载，这里全部隐藏。
            HideTemplateStats(item);

            // 先清掉模板自带的标签，再按配置添加（法宝 = 自定义「法宝」标签 + Luxury）
            item.Tags.Clear();

            Tag treasureTag = GetOrCreateTreasureTag();
            if (treasureTag != null)
                item.Tags.Add(treasureTag);

            // 与图腾一致：装在槽位里时死亡不掉落。
            // 原版 CharacterMainControl 的死亡掉落逻辑会跳过带该标签的槽位物品。
            Tag keepOnDeathTag = GameplayDataSettings.Tags.DontDropOnDeadInSlot;
            if (keepOnDeathTag != null)
                item.Tags.Add(keepOnDeathTag);
            else
                Diag.Warn("未找到原版标签 DontDropOnDeadInSlot，法宝死亡时仍会掉落");

            if (definition.Tags == null || definition.Tags.Length == 0)
                return;

            foreach (string tagName in definition.Tags)
            {
                Tag target = FindTag(tagName);
                if (target == null)
                {
                    Diag.Warn($"法宝「{definition.DisplayName}」的标签 {tagName} 不存在，已跳过");
                    continue;
                }

                item.Tags.Add(target);
            }
        }

        /// <summary>
        /// 剥离「Character」模板自带的角色组件。
        /// slots.list 是公开字段，直接清空即可让详情面板不再显示装备格；
        /// Inventory / Stats 是私有字段，置空后游戏里的空值检查会跳过它们。
        /// </summary>
        private static void StripCharacterComponents(Item item)
        {
            try
            {
                if (item.Slots != null)
                    item.Slots.list.Clear();

                item.SetPrivateField("inventory", null);
            }
            catch (Exception e)
            {
                Diag.Report("剥离模板装备槽 / 背包失败", e);
            }
        }

        /// <summary>
        /// 隐藏模板自带的所有显示词条（Character 占位物品的 71 条属性里，
        /// 只有「枪械伤害倍率」是 display=true）。属性数据保留，仅关闭显示。
        /// </summary>
        private static void HideTemplateStats(Item item)
        {
            try
            {
                if (item.Stats == null)
                    return;

                foreach (Stat stat in item.Stats)
                {
                    if (stat == null || !stat.Display)
                        continue;

                    if (_statDisplayField == null)
                    {
                        _statDisplayField = typeof(Stat).GetField(
                            "display", BindingFlags.Instance | BindingFlags.NonPublic);
                    }

                    if (_statDisplayField == null)
                    {
                        Diag.Warn("Stat 未找到私有字段「display」，模板词条无法隐藏");
                        return;
                    }

                    _statDisplayField.SetValue(stat, false);
                }
            }
            catch (Exception e)
            {
                Diag.Report("隐藏模板词条失败", e);
            }
        }

        private static FieldInfo _statDisplayField;

        private static readonly Dictionary<string, FieldInfo> EffectFieldCache =
            new Dictionary<string, FieldInfo>();

        /// <summary>
        /// 给法宝挂一条原版 Effect 词条：详情面板的「属性区」会以单行文本显示
        /// Effect.Description。Effect 的 triggers / filters / actions 全部为空，
        /// 因此它只是展示用，不会触发任何游戏行为。
        ///
        /// 概率与数值等法宝升级系统做好后再填进文本里。
        /// </summary>
        private static void ApplyEffectEntry(Item item, TreasureDefinition definition)
        {
            if (string.IsNullOrEmpty(definition.EffectText))
                return;

            try
            {
                var effectGo = new GameObject($"MagicTreasurePower_Effect_{definition.TypeId}");
                effectGo.transform.SetParent(item.transform, false);

                Effect effect = effectGo.AddComponent<Effect>();
                SetEffectField(effect, "display", true);
                SetEffectField(effect, "description", definition.EffectText);
                SetEffectField(effect, "item", item);

                // SetItem 会把 effect 绑定到物品并注册刷新事件
                effect.SetItem(item);

                if (item.Effects != null && !item.Effects.Contains(effect))
                    item.Effects.Add(effect);
            }
            catch (Exception e)
            {
                Diag.Report($"给法宝「{definition.DisplayName}」添加效果词条失败", e);
            }
        }

        private static void SetEffectField(Effect effect, string fieldName, object value)
        {
            FieldInfo field;
            if (!EffectFieldCache.TryGetValue(fieldName, out field))
            {
                field = typeof(Effect).GetField(
                    fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
                EffectFieldCache[fieldName] = field;
            }

            if (field == null)
            {
                Diag.Warn($"Effect 未找到私有字段「{fieldName}」，可能游戏结构已变化");
                return;
            }

            field.SetValue(effect, value);
        }

        /// <summary>
        /// 按名字查找原版标签。
        /// 优先用 <see cref="GameplayDataSettings.Tags"/> 的完整列表：TagsData 是
        /// 原版标签的唯一索引，访问时会把所有 Tag 资源一并加载；
        /// 早期 mod 加载阶段 Resources.FindObjectsOfTypeAll 可能还看不到它们。
        /// </summary>
        internal static Tag FindTag(string name)
        {
            try
            {
                foreach (Tag tag in GameplayDataSettings.Tags.AllTags)
                {
                    if (tag != null && tag.name == name)
                        return tag;
                }
            }
            catch (Exception)
            {
                // GameplayDataSettings 尚未就绪时退回运行时查找
            }

            foreach (Tag tag in Resources.FindObjectsOfTypeAll<Tag>())
            {
                if (tag != null && tag.name == name)
                    return tag;
            }

            return null;
        }

        /// <summary>
        /// 供槽位 / UI 复用的「法宝」标签（与法宝物品上的标签是同一个）。
        /// 槽位用 requireTags 限制只能放入带该标签的物品。
        /// </summary>
        internal static Tag TreasureTag => GetOrCreateTreasureTag();

        /// <summary>法宝宝箱专用的标签（不参与法宝槽位，避免宝箱被装备）。</summary>
        internal static Tag BoxTag => GetOrCreateBoxTag();

        private const string BoxTagName = "MagicTreasureBox";

        private static Tag _boxTag;

        private static Tag GetOrCreateBoxTag()
        {
            if (_boxTag != null)
                return _boxTag;

            try
            {
                _boxTag = FindTag(BoxTagName);

                if (_boxTag == null)
                {
                    _boxTag = ScriptableObject.CreateInstance<Tag>();
                    _boxTag.name = BoxTagName;
                    _boxTag.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    SetTagField(_boxTag, "show", true);
                    SetTagField(_boxTag, "showDescription", true);
                    SetTagField(_boxTag, "color", new Color(0.91f, 0.75f, 0.36f, 1f));
                    SetTagField(_boxTag, "priority", 99);
                    Diag.Log("已创建自定义标签：MagicTreasureBox（法宝宝箱）");
                }

                LocalizationManager.SetOverrideText("Tag_" + BoxTagName, "法宝宝箱");
                LocalizationManager.SetOverrideText(
                    "Tag_" + BoxTagName + "_Desc",
                    "装着法宝的箱子，打开后可从七件法宝中任选其一。");
                return _boxTag;
            }
            catch (Exception e)
            {
                Diag.Report("创建「法宝宝箱」标签失败", e);
                return null;
            }
        }

        /// <summary>
        /// 自建「法宝」标签。游戏里的 Tag 是 ScriptableObject，可以在运行时创建；
        /// 显示名走 Tag_MagicTreasure 的本地化 override，颜色 / 是否显示等私有字段用反射写入。
        /// 若同名的 Tag 已存在（例如重载或其它 mod 也建过），直接复用，避免重复。
        /// </summary>
        private static Tag GetOrCreateTreasureTag()
        {
            if (_treasureTag != null)
                return _treasureTag;

            try
            {
                _treasureTag = FindTag(TreasureTagName);

                if (_treasureTag == null)
                {
                    _treasureTag = ScriptableObject.CreateInstance<Tag>();
                    _treasureTag.name = TreasureTagName;
                    // 无资源引用的运行时 ScriptableObject 可能被 UnloadUnusedAssets 回收
                    _treasureTag.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    SetTagField(_treasureTag, "show", true);
                    SetTagField(_treasureTag, "showDescription", true);
                    SetTagField(_treasureTag, "color", new Color(0.91f, 0.75f, 0.36f, 1f));
                    SetTagField(_treasureTag, "priority", 100);
                    Diag.Log("已创建自定义标签：MagicTreasure（法宝）");
                }

                LocalizationManager.SetOverrideText("Tag_" + TreasureTagName, "法宝");
                LocalizationManager.SetOverrideText(
                    "Tag_" + TreasureTagName + "_Desc",
                    "传说中蕴含超乎常理之力的宝物。");
                return _treasureTag;
            }
            catch (Exception e)
            {
                Diag.Report("创建「法宝」标签失败", e);
                return null;
            }
        }

        private static void SetTagField(Tag tag, string fieldName, object value)
        {
            FieldInfo field = typeof(Tag).GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                Diag.Warn($"Tag 未找到私有字段「{fieldName}」，可能游戏结构已变化");
                return;
            }

            field.SetValue(tag, value);
        }

        private static void ApplyIcon(Item item, TreasureDefinition definition)
        {
            Sprite sprite = LoadEmbeddedSprite(definition.ResourceName, definition.TypeId);
            if (sprite == null)
            {
                Diag.Warn($"法宝「{definition.DisplayName}」没有可用图标，将沿用模板图标");
                return;
            }

            item.SetPrivateField("icon", sprite);
            IconCache[definition.TypeId] = sprite;
        }

        internal static Sprite LoadEmbeddedSprite(string resourceName, int typeId)
        {
            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        Diag.Warn($"嵌入资源未找到：{resourceName}；现有资源：{string.Join(", ", assembly.GetManifestResourceNames())}");
                        return null;
                    }

                    byte[] data = new byte[stream.Length];
                    int read = 0;
                    while (read < data.Length)
                    {
                        int count = stream.Read(data, read, data.Length - read);
                        if (count <= 0)
                            break;
                        read += count;
                    }

                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!ImageConversion.LoadImage(texture, data))
                    {
                        Diag.Warn($"图标加载失败（不是有效 PNG）：{resourceName}");
                        Object.Destroy(texture);
                        return null;
                    }

                    texture.filterMode = FilterMode.Bilinear;
                    texture.Apply();
                    Sprite sprite = Sprite.Create(
                        texture,
                        new Rect(0f, 0f, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f),
                        100f);

                    // 纹理 / Sprite 必须挂在常驻对象上，否则切换场景后被 GC 回收
                    var holderGo = new GameObject($"MagicTreasurePower_Icon_{typeId}");
                    Object.DontDestroyOnLoad(holderGo);
                    IconHolder holder = holderGo.AddComponent<IconHolder>();
                    holder.Texture = texture;
                    holder.Sprite = sprite;
                    return sprite;
                }
            }
            catch (Exception e)
            {
                Diag.Report($"加载法宝图标失败：{resourceName}", e);
                return null;
            }
        }

        private static void ApplyLocalization(TreasureDefinition definition)
        {
            LocalizationManager.SetOverrideText(definition.LocalizationKey, definition.DisplayName);
            LocalizationManager.SetOverrideText(
                definition.DescriptionKey,
                definition.Description + "\n\n<color=grey>[Mod:法宝之力]</color>");
        }
    }
}
