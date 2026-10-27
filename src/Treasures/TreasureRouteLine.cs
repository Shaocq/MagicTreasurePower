using System;
using System.Collections.Generic;
using Duckov.Utilities;
using Pathfinding;
using UnityEngine;
using UnityEngine.Rendering;

namespace MagicTreasurePower.Treasures
{
    /// <summary>
    /// 火眼金睛的「带路绿线」：从角色脚下沿可行走地面画一条绿线，连到选中的物资箱。
    ///
    /// <para><b>寻路：</b>直接用游戏自带的 A*（<c>AstarPathfindingProject</c> 的
    /// <see cref="Seeker"/>），与 AI 用的是同一套图，所以线会绕开墙体和建筑。
    /// 寻路失败（缺寻路图 / 箱子不在网格上）时退化成「贴地直线」，至少保证有指引。</para>
    ///
    /// <para><b>生命周期：</b>同一时刻只保留一条线；再点同一个箱子会取消；
    /// 角色走到箱子 3 米内自动消失；换图 / 回基地 / 停用 mod 也会清掉。</para>
    ///
    /// <para><b>刷新：</b>角色移动超过 4 米时每 1.5 秒按当前位置重算一次，
    /// 保证线的起点始终跟着角色。</para>
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
                if (box == null)
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

    /// <summary>绿色带路线的宿主：管 LineRenderer、A* 请求与自动清除。</summary>
    internal sealed class RouteLineHost : MonoBehaviour
    {
        /// <summary>走到这个距离内就认为「到了」，线自动消失（米）。</summary>
        private const float ArriveDistance = 3f;

        /// <summary>角色移动超过这个距离才重算路径（米）。</summary>
        private const float RepathMoveDistance = 4f;

        /// <summary>重算路径的最小间隔（秒）。</summary>
        private const float RepathInterval = 1.5f;

        /// <summary>贴地直线采样点数。</summary>
        private const int StraightSamples = 48;

        /// <summary>线离地高度（米），防止被地面遮住。</summary>
        private const float GroundOffset = 0.08f;

        private static readonly Color LineColor = new Color(0.25f, 1f, 0.4f, 0.95f);

        private InteractableLootbox _box;
        private LevelManager _level;
        private LineRenderer _line;
        private Seeker _seeker;
        private Vector3 _lastPathStart;
        private float _nextRepath;

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
            _line.numCornerVertices = 4;
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
            _lastPathStart = Vector3.zero;
            _nextRepath = 0f;

            if (_box != null)
                RequestPath();
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

                if (Time.unscaledTime < _nextRepath)
                    return;

                _nextRepath = Time.unscaledTime + RepathInterval;

                // 角色没怎么动就不重算，省掉不必要的寻路开销
                if ((start - _lastPathStart).sqrMagnitude < RepathMoveDistance * RepathMoveDistance)
                    return;

                RequestPath();
            }
            catch (Exception e)
            {
                Diag.Report("火眼金睛：带路线刷新失败", e);
                ClearTarget();
            }
        }

        private void RequestPath()
        {
            if (_box == null)
                return;

            CharacterMainControl main = CharacterMainControl.Main;
            if (main == null)
                return;

            Vector3 start = main.transform.position;
            Vector3 end = _box.transform.position;
            _lastPathStart = start;

            EnsureSeeker();

            if (_seeker == null)
            {
                // 没有可用寻路图（或 A* 还没初始化）：退化成贴地直线
                DrawStraightLine(start, end);
                return;
            }

            try
            {
                _seeker.CancelCurrentPathRequest();
                _seeker.StartPath(start, end, OnPathComplete);
            }
            catch (Exception e)
            {
                Diag.Warn($"火眼金睛：A* 寻路失败，改用贴地直线（{e.GetType().Name}）");
                DrawStraightLine(start, end);
            }
        }

        private void EnsureSeeker()
        {
            if (_seeker != null)
                return;

            try
            {
                if (AstarPath.active == null)
                    return;

                _seeker = gameObject.GetComponent<Seeker>();
                if (_seeker == null)
                    _seeker = gameObject.AddComponent<Seeker>();
            }
            catch (Exception e)
            {
                Diag.Warn($"火眼金睛：创建寻路组件失败（{e.GetType().Name}）");
                _seeker = null;
            }
        }

        private void OnPathComplete(Path p)
        {
            try
            {
                if (_box == null || _line == null)
                    return;

                if (p != null && !p.error && p.vectorPath != null && p.vectorPath.Count >= 2)
                {
                    DrawPath(p.vectorPath);
                    return;
                }

                // 寻路失败（目标不可达等）：贴地直线兜底
                Vector3 start = _lastPathStart;
                Vector3 end = _box.transform.position;
                DrawStraightLine(start, end);
            }
            catch (Exception e)
            {
                Diag.Report("火眼金睛：处理寻路结果失败", e);
            }
        }

        private void DrawPath(List<Vector3> points)
        {
            if (_line == null || points == null || points.Count < 2)
                return;

            var array = new Vector3[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 point = points[i];
                point.y += GroundOffset;
                array[i] = point;
            }

            _line.positionCount = array.Length;
            _line.SetPositions(array);
            _line.enabled = true;
        }

        /// <summary>
        /// 贴地直线：沿直线采样，每个采样点向下打射线找地面高度；
        /// 找不到地面就用两端插值的高度。不做避障，只作为寻路失败时的兜底。
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

            var array = new Vector3[StraightSamples];
            for (int i = 0; i < StraightSamples; i++)
            {
                float t = i / (float)(StraightSamples - 1);
                Vector3 point = Vector3.Lerp(start, end, t);
                point.y += GroundOffset;

                if (mask != 0)
                {
                    Ray ray = new Ray(new Vector3(point.x, point.y + 6f, point.z), Vector3.down);
                    RaycastHit hit;
                    if (Physics.Raycast(ray, out hit, 20f, mask, QueryTriggerInteraction.Ignore))
                        point.y = hit.point.y + GroundOffset;
                }

                array[i] = point;
            }

            _line.positionCount = array.Length;
            _line.SetPositions(array);
            _line.enabled = true;
        }
    }
}
