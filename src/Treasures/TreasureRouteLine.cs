using System;
using Duckov.Utilities;
using UnityEngine;
using UnityEngine.Rendering;

namespace MagicTreasurePower.Treasures
{
    /// <summary>
    /// 火眼金睛的「带路绿线」：从角色脚下到选中的物资箱画一条**贴地直线**。
    ///
    /// <para><b>为什么不用 A*：</b>游戏自带的 A* 路径会绕路，线看起来杂乱；
    /// 这里只做直连指引，简单直观。线沿地面采样（向下打射线贴地），不做避障。</para>
    ///
    /// <para><b>生命周期：</b>同一时刻只保留一条线；再点同一个箱子会取消；
    /// 角色走到箱子 3 米内自动消失；换图 / 回基地 / 停用 mod 也会清掉。</para>
    /// </summary>
    internal static class TreasureRouteLine
    {
        private static RouteLineHost _host;

        /// <summary>当前带路的目标箱子实例 ID；没有线时为 0。</summary>
        internal static int ActiveBoxId => _host != null ? _host.TargetBoxId : 0;

        /// <summary>点同一个箱子取消，点别的箱子切换。</summary>
        internal static void Toggle(InteractableLootbox box)
        {
            if (box == null)
                return;

            // config.ini 的 ShowRouteLine=false 时完全不画线
            if (!ModConfig.Current.ShowRouteLine)
                return;

            int id = box.GetInstanceID();
            if (_host != null && _host.TargetBoxId == id)
            {
                Clear();
                return;
            }

            SetTarget(box);
        }

        internal static void SetTarget(InteractableLootbox box)
        {
            try
            {
                if (box == null || !ModConfig.Current.ShowRouteLine)
                    return;

                EnsureHost().SetTarget(box);
            }
            catch (Exception e)
            {
                Diag.Report("火眼金睛：绘制带路线失败", e);
            }
        }

        internal static void Clear()
        {
            if (_host != null)
                _host.ClearTarget();
        }

        private static RouteLineHost EnsureHost()
        {
            if (_host != null && !_host)
                _host = null;

            if (_host == null)
            {
                GameObject go = new GameObject("MagicTreasurePower_RouteLine");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _host = go.AddComponent<RouteLineHost>();
            }

            return _host;
        }
    }

    /// <summary>绿色带路线的宿主：管 LineRenderer、贴地采样与自动清除。</summary>
    internal sealed class RouteLineHost : MonoBehaviour
    {
        /// <summary>走到这个距离内就认为「到了」，线自动消失（米）。</summary>
        private const float ArriveDistance = 3f;

        /// <summary>角色移动超过这个距离才重新采样（米）。</summary>
        private const float RefreshMoveDistance = 1f;

        /// <summary>重新采样的最小间隔（秒）。</summary>
        private const float RefreshInterval = 0.25f;

        /// <summary>直线采样点数（越大越贴合起伏地形）。</summary>
        private const int Samples = 32;

        /// <summary>线离地高度（米），防止被地面遮住。</summary>
        private const float GroundOffset = 0.08f;

        private static readonly Color LineColor = new Color(0.25f, 1f, 0.4f, 0.95f);

        private InteractableLootbox _box;
        private LevelManager _level;
        private LineRenderer _line;
        private Vector3 _lastStart;
        private float _nextRefresh;

        internal int TargetBoxId { get; private set; }

        private void Awake()
        {
            try
            {
                BuildLine();
            }
            catch (Exception e)
            {
                Diag.Report("火眼金睛：初始化带路线失败", e);
            }
        }

        private void BuildLine()
        {
            GameObject lineGo = new GameObject("Line");
            lineGo.transform.SetParent(transform, false);

            _line = lineGo.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.positionCount = 0;
            _line.startWidth = 0.14f;
            _line.endWidth = 0.14f;
            _line.numCapVertices = 4;
            _line.numCornerVertices = 2;
            _line.textureMode = LineTextureMode.Stretch;
            _line.alignment = LineAlignment.View;
            _line.shadowCastingMode = ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.startColor = LineColor;
            _line.endColor = LineColor;
            _line.enabled = false;

            // 优先用能吃到顶点色（LineRenderer 的 startColor / endColor）的无光照着色器；
            // 全部找不到时保留 LineRenderer 自带的默认材质，至少不会是粉色报错材质
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");

            if (shader != null)
            {
                Material material = new Material(shader);
                material.hideFlags = HideFlags.DontSave;

                // URP/Unlit 之类不吃顶点色的着色器，靠材质颜色兜底
                if (material.HasProperty("_BaseColor"))
                    material.SetColor("_BaseColor", LineColor);
                if (material.HasProperty("_Color"))
                    material.SetColor("_Color", LineColor);
                if (material.HasProperty("_TintColor"))
                    material.SetColor("_TintColor", LineColor);

                _line.material = material;
            }
        }

        internal void SetTarget(InteractableLootbox box)
        {
            _box = box;
            TargetBoxId = box != null ? box.GetInstanceID() : 0;
            _level = LevelManager.Instance;
            _lastStart = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            _nextRefresh = 0f;

            if (_box != null)
                Refresh(true);
        }

        internal void ClearTarget()
        {
            _box = null;
            TargetBoxId = 0;
            _level = null;

            if (_line != null)
            {
                _line.positionCount = 0;
                _line.enabled = false;
            }
        }

        private void Update()
        {
            try
            {
                // 箱子被销毁（换图 / 清场）时也要把线收掉：
                // 否则 _box 变成 Unity 假 null 后会一直停在旧世界坐标上
                if (_box == null)
                {
                    if (TargetBoxId != 0)
                        ClearTarget();
                    return;
                }

                CharacterMainControl main = CharacterMainControl.Main;
                LevelManager manager = LevelManager.Instance;

                if (main == null || manager == null || !manager.IsRaidMap ||
                    !ReferenceEquals(manager, _level))
                {
                    ClearTarget();
                    return;
                }

                Vector3 start = main.transform.position;
                Vector3 end = _box.transform.position;

                // 走到箱子附近就自动结束带路
                if ((start - end).sqrMagnitude <= ArriveDistance * ArriveDistance)
                {
                    ClearTarget();
                    return;
                }

                if (Time.unscaledTime < _nextRefresh)
                    return;

                _nextRefresh = Time.unscaledTime + RefreshInterval;

                // 角色没怎么动就不重采样，省掉射线开销
                if ((start - _lastStart).sqrMagnitude < RefreshMoveDistance * RefreshMoveDistance)
                    return;

                Refresh(false);
            }
            catch (Exception e)
            {
                Diag.Report("火眼金睛：带路线刷新失败", e);
                ClearTarget();
            }
        }

        private void Refresh(bool force)
        {
            if (_box == null || _line == null)
                return;

            CharacterMainControl main = CharacterMainControl.Main;
            if (main == null)
                return;

            Vector3 start = main.transform.position;
            Vector3 end = _box.transform.position;

            if (!force && (start - _lastStart).sqrMagnitude < RefreshMoveDistance * RefreshMoveDistance)
                return;

            _lastStart = start;
            DrawStraightLine(start, end);
        }

        /// <summary>
        /// 贴地直线：沿直线采样，每个采样点向下打射线找地面高度；
        /// 找不到地面就用两端插值的高度。不做避障。
        /// </summary>
        private void DrawStraightLine(Vector3 start, Vector3 end)
        {
            if (_line == null)
                return;

            int mask = 0;
            try
            {
                mask = GameplayDataSettings.Layers.groundLayerMask;
            }
            catch
            {
            }

            var points = new Vector3[Samples];
            for (int i = 0; i < Samples; i++)
            {
                float t = i / (float)(Samples - 1);
                Vector3 point = Vector3.Lerp(start, end, t);
                point.y += GroundOffset;

                if (mask != 0)
                {
                    Ray ray = new Ray(new Vector3(point.x, point.y + 6f, point.z), Vector3.down);
                    RaycastHit hit;
                    if (Physics.Raycast(ray, out hit, 20f, mask, QueryTriggerInteraction.Ignore))
                        point.y = hit.point.y + GroundOffset;
                }

                points[i] = point;
            }

            _line.positionCount = points.Length;
            _line.SetPositions(points);
            _line.enabled = true;
        }
    }
}
