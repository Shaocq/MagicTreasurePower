using System;
using System.Reflection;

namespace MagicTreasurePower
{
    /// <summary>
    /// 获取前置「HarmonyLib」（工坊 3589088839）已载入的 0Harmony。
    /// 本 mod 不内嵌 0Harmony；前置未启用时返回 null，由 ModBehaviour 定时重试。
    /// </summary>
    internal static class HarmonyLoad
    {
        private static Assembly _harmonyAssembly;

        public static string HarmonyVersion { get; private set; } = "未加载";

        public static Assembly Load0Harmony()
        {
            if (_harmonyAssembly != null)
                return _harmonyAssembly;

            try
            {
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        if (assembly.GetName().Name != "0Harmony")
                            continue;

                        Version version = assembly.GetName().Version;
                        if (version != null && version.Major >= 2)
                        {
                            _harmonyAssembly = assembly;
                            HarmonyVersion = version.ToString();
                            Diag.Log($"已找到前置提供的 0Harmony，版本 {HarmonyVersion}");
                            return _harmonyAssembly;
                        }
                    }
                    catch
                    {
                        // 跳过异常程序集
                    }
                }
            }
            catch (Exception e)
            {
                Diag.Report("查找前置 Harmony 失败", e);
            }

            return null;
        }
    }
}
