using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace MagicTreasurePower
{
    /// <summary>
    /// 统一日志出口：任何日志失败都不允许抛回游戏或其它 mod。
    /// 同时写两份：
    /// <list type="bullet">
    /// <item>Unity 日志（搜索 <c>[MagicTreasurePower]</c>）；</item>
    /// <item>游戏根目录下的 <c>MagicTreasurePower.log</c>——独立落盘，
    /// 用来确认 mod 是否真的被加载、补丁是否安装成功（不受 Unity 日志轮转影响）。</item>
    /// </list>
    /// </summary>
    internal static class Diag
    {
        internal const string Tag = "[MagicTreasurePower]";

        private const string FileName = "MagicTreasurePower.log";

        private static string _filePath;
        private static bool _fileFailed;

        public static void Log(string message)
        {
            try
            {
                Debug.Log(Tag + " " + message);
            }
            catch
            {
                // 日志失败不影响游戏
            }

            WriteFile("信息", message);
        }

        public static void Warn(string message)
        {
            try
            {
                Debug.LogWarning(Tag + " " + message);
            }
            catch
            {
            }

            WriteFile("警告", message);
        }

        public static void Report(string context, Exception e)
        {
            try
            {
                Debug.LogError(Tag + " " + context + "：" + e);
            }
            catch
            {
            }

            WriteFile("错误", context + "：" + (e != null ? e.ToString() : ""));
        }

        private static void WriteFile(string level, string message)
        {
            if (_fileFailed)
                return;

            try
            {
                lock (typeof(Diag))
                {
                    File.AppendAllText(
                        GetFilePath(),
                        string.Format("{0} [{1}] {2}{3}", DateTime.Now.ToString("HH:mm:ss.fff"), level, message, Environment.NewLine),
                        Encoding.UTF8);
                }
            }
            catch
            {
                // 写文件失败就停用文件日志，绝不影响游戏
                _fileFailed = true;
            }
        }

        private static string GetFilePath()
        {
            if (_filePath != null)
                return _filePath;

            try
            {
                string root = Path.GetDirectoryName(Application.dataPath);
                _filePath = Path.Combine(string.IsNullOrEmpty(root) ? "." : root, FileName);
            }
            catch
            {
                _filePath = FileName;
            }

            return _filePath;
        }
    }
}
