using System;
using Duckov.Modding;
using MagicTreasurePower.Treasures;
using UnityEngine.InputSystem;

namespace MagicTreasurePower.ModSettingIntegration
{
    /// <summary>
    /// 把本 mod 的设置项挂到 ModSetting 的「模组设置」页。
    ///
    /// <para><b>松耦合（用户指定方案 1）：</b>只通过 <see cref="ModSettingAPI"/> 的反射封装调用
    /// <c>ModSetting.ModBehaviour</c>，不引用 ModSetting.dll。ModSetting 未启用、或加载顺序不同，
    /// 本 mod 都照常运行，只是设置页里没有本 mod 的条目（此时仍可用 config.ini）。</para>
    ///
    /// <para><b>设置项：</b></para>
    /// <list type="bullet">
    ///   <item>ScanHotkey：呼出 / 关闭扫描面板的快捷键（按键绑定）；</item>
    ///   <item>ShowRouteLine：是否允许「透视标记」（开关）。</item>
    /// </list>
    ///
    /// <para><b>持久化：</b>ModSetting 自己会把值存进 <c>persistentDataPath/ModSetting/ModSetting.json</c>，
    /// 但不会在加载时回调 mod；所以启动时要先用 <c>GetSavedValue</c> 读回来应用，
    /// 再把界面注册进去。改动时同步写回 config.ini，两个入口保持一致。</para>
    /// </summary>
    internal static class TreasureModSettings
    {
        private const string KeyHotkey = "ScanHotkey";
        private const string KeyRoute = "ShowRouteLine";

        private static bool _registered;

        /// <summary>
        /// 尝试初始化并注册设置页。ModSetting 还没加载时返回 false，由调用方定时重试。
        /// </summary>
        internal static bool TryRegister(ModInfo info)
        {
            if (_registered)
                return true;

            bool initialized;
            try
            {
                initialized = ModSettingAPI.Init(info);
            }
            catch (Exception e)
            {
                Diag.Report("ModSetting 初始化失败", e);
                return false;
            }

            if (!initialized)
                return false;

            // ModSetting 还没读完自己的存档时，GetSavedValue 会拿不到值：等下一轮重试
            if (!ModSettingAPI.IsReady)
                return false;

            ApplySavedValues();
            RegisterUi();

            _registered = true;
            Diag.Log("ModSetting 设置页已注册：呼出热键 / 透视标记");
            return true;
        }

        /// <summary>mod 停用时重置标记（ModSetting 那边保留条目，重新启用时不会重复添加）。</summary>
        internal static void Reset()
        {
            _registered = false;
        }

        /// <summary>读取 ModSetting 上次保存的值，覆盖 config.ini 里的默认值。</summary>
        private static void ApplySavedValues()
        {
            try
            {
                ModConfig config = ModConfig.Current;

                bool route;
                if (ModSettingAPI.GetSavedValue(KeyRoute, out route))
                    config.ShowRouteLine = route;

                Key key;
                if (ModSettingAPI.GetSavedValue(KeyHotkey, out key) && key != Key.None)
                {
                    config.ScanKey = key;
                    config.ScanHotkeyText = key.ToString();
                }

                Diag.Log(
                    $"ModSetting 存档值：重开热键={config.ScanHotkeyText}，" +
                    $"透视标记={(config.ShowRouteLine ? "开" : "关")}");
            }
            catch (Exception e)
            {
                Diag.Report("读取 ModSetting 存档值失败", e);
            }
        }

        private static void RegisterUi()
        {
            ModConfig config = ModConfig.Current;

            // 1) 重开扫描面板的快捷键
            ModSettingAPI.AddKeybinding(
                KeyHotkey,
                "重开物资扫描面板的快捷键",
                config.ScanKey,
                Key.F8,
                key =>
                {
                    try
                    {
                        if (ModBehaviour.Instance == null)
                            return;

                        if (key == Key.None)
                            return;

                        ModConfig.Current.ScanKey = key;
                        ModConfig.Current.ScanHotkeyText = key.ToString();
                        ModConfig.Persist();
                        Diag.Log($"ModSetting：重开热键改为 {key}");
                    }
                    catch (Exception e)
                    {
                        Diag.Report("应用重开热键失败", e);
                    }
                });

            // 2) 透视标记开关
            ModSettingAPI.AddToggle(
                KeyRoute,
                "允许透视标记（点箱子行时高亮箱子）",
                config.ShowRouteLine,
                value =>
                {
                    try
                    {
                        if (ModBehaviour.Instance == null)
                            return;

                        ModConfig.Current.ShowRouteLine = value;
                        if (!value)
                            TreasureTargetMarker.Clear();

                        ModConfig.Persist();
                        Diag.Log($"ModSetting：透视标记 {(value ? "开" : "关")}");
                    }
                    catch (Exception e)
                    {
                        Diag.Report("应用透视标记开关失败", e);
                    }
                });

        }
    }
}
