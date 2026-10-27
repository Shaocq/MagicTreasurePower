using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine.InputSystem;

namespace MagicTreasurePower
{
    /// <summary>
    /// 用户配置：mod 目录下的 <c>config.ini</c>。
    ///
    /// <para>解析规则与工作区其它 mod 一致：逐行 <c>key=value</c>，<c>#</c> / <c>;</c> 开头为注释，
    /// 读不到 / 解析失败一律回落到默认值；文件不存在时自动生成一份带注释的默认配置。</para>
    ///
    /// <para>更好的更好的mod管理器（BBMM）的 mod 条目菜单里可以直接编辑这个文件，
    /// 也就是玩家侧的「模组设置」入口。</para>
    /// </summary>
    internal sealed class ModConfig
    {
        internal const string FileName = "config.ini";

        private const string DefaultHotkey = "F8";

        /// <summary>扫描结果面板的重开热键（原始文本，用于界面提示与日志）。</summary>
        internal string ScanHotkeyText = DefaultHotkey;

        /// <summary>扫描结果面板的重开热键（解析后的 Input System 按键）。</summary>
        internal Key ScanKey = Key.F8;

        /// <summary>是否允许「带路绿线」。关闭后点箱子行不会画线。</summary>
        internal bool ShowRouteLine = true;

        private static ModConfig _current;

        /// <summary>当前 config.ini 的完整路径；用于把 ModSetting 里改的值写回文件。</summary>
        private static string _path;

        internal static ModConfig Current
        {
            get
            {
                if (_current == null)
                    _current = new ModConfig();
                return _current;
            }
        }

        /// <summary>读取 mod 目录下的 config.ini；文件不存在时生成默认文件。</summary>
        internal static void Load(string modDirectory)
        {
            ModConfig config = new ModConfig();

            try
            {
                string path = string.IsNullOrEmpty(modDirectory)
                    ? null
                    : Path.Combine(modDirectory, FileName);

                if (string.IsNullOrEmpty(path))
                {
                    Diag.Warn("mod 目录为空，config.ini 使用默认值");
                    _current = config;
                    return;
                }

                _path = path;

                if (!File.Exists(path))
                {
                    WriteDefault(path);
                }
                else
                {
                    Parse(path, config);
                }
            }
            catch (Exception e)
            {
                Diag.Report("读取 config.ini 失败，使用默认值", e);
            }

            _current = config;

            Diag.Log(
                $"配置：重开热键={config.ScanHotkeyText}，" +
                $"透视标记={(config.ShowRouteLine ? "开" : "关")}");
        }

        private static void Parse(string path, ModConfig config)
        {
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';')
                    continue;

                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;

                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string value = line.Substring(eq + 1).Trim();

                switch (key)
                {
                    case "scanhotkey":
                        ApplyHotkey(config, value);
                        break;
                    case "showrouteline":
                        config.ShowRouteLine = ParseBool(value, config.ShowRouteLine);
                        break;
                }
            }
        }

        private static void ApplyHotkey(ModConfig config, string value)
        {
            if (string.IsNullOrEmpty(value))
                return;

            Key parsed;
            if (Enum.TryParse(value, true, out parsed) && parsed != Key.None)
            {
                config.ScanKey = parsed;
                config.ScanHotkeyText = parsed.ToString();
                return;
            }

            Diag.Warn($"config.ini 里的 ScanHotkey「{value}」不是有效按键名，继续使用 F8");
            config.ScanKey = Key.F8;
            config.ScanHotkeyText = DefaultHotkey;
        }

        private static bool ParseBool(string value, bool fallback)
        {
            if (bool.TryParse(value, out bool result))
                return result;

            if (value == "1")
                return true;
            if (value == "0")
                return false;

            return fallback;
        }

        /// <summary>
        /// 把当前值写回 config.ini（只改这三行，保留用户自己加的注释与其它键）。
        /// 供 ModSetting 设置页改完值后调用，保证 config.ini 与游戏内设置一致。
        /// </summary>
        internal static void Persist()
        {
            try
            {
                if (string.IsNullOrEmpty(_path))
                    return;

                ModConfig config = Current;
                var lines = File.Exists(_path)
                    ? new List<string>(File.ReadAllLines(_path))
                    : new List<string>();

                SetLine(lines, "ScanHotkey", config.ScanHotkeyText);
                SetLine(lines, "ShowRouteLine", config.ShowRouteLine ? "true" : "false");

                File.WriteAllLines(_path, lines, new UTF8Encoding(true));
            }
            catch (Exception e)
            {
                Diag.Warn($"写回 config.ini 失败：{e.Message}");
            }
        }

        /// <summary>把 key=value 写进对应的行；没有这一行就追加到末尾。</summary>
        private static void SetLine(List<string> lines, string key, string value)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i].TrimStart();
                if (line.Length > key.Length &&
                    line.StartsWith(key, StringComparison.OrdinalIgnoreCase) &&
                    line[key.Length] == '=')
                {
                    lines[i] = key + "=" + value;
                    return;
                }
            }

            lines.Add(key + "=" + value);
        }

        /// <summary>生成默认配置文件。写 UTF-8 BOM，Windows 记事本 / BBMM 编辑器打开都不会乱码。</summary>
        private static void WriteDefault(string path)
        {
            const string content =
                "# 法宝之力 配置文件\r\n" +
                "# 修改后需要重新加载 mod（或在 BBMM 里禁用/启用本 mod）才会生效。\r\n" +
                "\r\n" +
                "# 扫描结果面板的重开热键。\r\n" +
                "# 取值是 Unity Input System 的按键名，例如：F8 / F7 / H / PageUp / Tab。\r\n" +
                "ScanHotkey=F8\r\n" +
                "\r\n" +
                "# 是否允许「透视标记」：在扫描面板里点箱子行时，在目标头顶显示图标与浮动箭头。\r\n" +
                "# 改成 false 后点箱子行不再显示标记（面板其它功能不受影响）。\r\n" +
                "ShowRouteLine=true\r\n";

            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            File.WriteAllText(path, content, new UTF8Encoding(true));
            Diag.Log("已生成默认配置文件：" + path);
        }
    }
}
