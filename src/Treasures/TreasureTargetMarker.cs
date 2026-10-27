using System;
using Duckov.Utilities;
using MagicTreasurePower.UI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace MagicTreasurePower.Treasures
{
    /// <summary>
    /// 火眼金睛的「透视标记」：选中物资箱后，
    /// <list type="bullet">
    ///   <item>箱子头顶始终挂一个火眼金睛图标（屏幕空间，永远可见）；</item>
    ///   <item>箱子被墙/遮挡物挡住时，给它画一圈**穿墙可见的红边**；</item>
    ///   <item>走到能直接看到箱子时，红边消失，只保留图标。</item>
    /// </list>
    ///
    /// <para>再点同一行取消；走到 3 米内自动结束；换图 / 回基地 / 停用 mod 自动清除。</para>
    /// </summary>
    internal static class TreasureTargetMarker
    {
        private static TargetMarkerHost _host;

        /// <summary>当前标记的目标箱子实例 ID；没有标记时为 0。</summary>
        internal static int ActiveTargetId => _host != null ? _host.TargetId : 0;

        internal static void Toggle(Transform target)
        {
            if (target == null)
                return;

            // 带路/透视标记的总开关（config.ini 的 ShowRouteLine）
            if (!ModConfig.Current.ShowRouteLine)
                return;

            int id = target.GetInstanceID();
            if (_host != null && _host.TargetId == id)
            {
                Clear();
                return;
            }

            SetTarget(target);
        }

        internal static void SetTarget(Transform target)
        {
            try
            {
                if (target == null || !ModConfig.Current.ShowRouteLine)
                    return;

                EnsureHost().SetTarget(target);
            }
            catch (Exception e)
            {
                Diag.Report("火眼金睛：创建透视标记失败", e);
            }
        }

        internal static void Clear()
        {
            if (_host != null)
                _host.ClearTarget();
        }

        private static TargetMarkerHost EnsureHost()
        {
            if (_host != null && !_host)
                _host = null;

            if (_host == null)
            {
                GameObject go = new GameObject("MagicTreasurePower_TargetMarker");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _host = go.AddComponent<TargetMarkerHost>();
            }

            return _host;
        }
    }

    /// <summary>透视标记的宿主：屏幕图标 + 穿墙红边。</summary>
    internal sealed class TargetMarkerHost : MonoBehaviour
    {
        /// <summary>走到这个距离内认为「到了」，标记自动消失（米）。</summary>
        private const float ArriveDistance = 3f;

        /// <summary>图标尺寸（UI 参考分辨率下的像素）。</summary>
        private const float IconSize = 46f;

        /// <summary>图标相对箱子包围盒顶部的抬高（米）。</summary>
        private const float IconLift = 0.45f;

        /// <summary>红边线宽（米）。</summary>
        private const float OutlineWidth = 0.025f;

        /// <summary>遮挡检测间隔（秒）。</summary>
        private const float VisibilityInterval = 0.2f;

        private static readonly Color OutlineColor = new Color(1f, 0.18f, 0.18f, 1f);

        private Transform _target;
        private LevelManager _level;
        private Camera _camera;

        private Canvas _canvas;
        private RectTransform _iconRect;
        private Image _iconImage;
        private Image _iconBackground;

        /// <summary>图标下方的红色指示箭头（上下浮动）。</summary>
        private RectTransform _arrowRect;
        private Image _arrowImage;
        private static Sprite _arrowSprite;

        private Material _outlineMaterial;
        private LineRenderer[] _edges;
        private bool _outlineVisible;

        private Bounds _bounds;
        private float _nextVisibilityCheck;

        internal int TargetId { get; private set; }

        internal void SetTarget(Transform target)
        {
            _target = target;
            TargetId = target != null ? target.GetInstanceID() : 0;
            _level = LevelManager.Instance;
            _nextVisibilityCheck = 0f;
            _outlineVisible = false;

            EnsureIcon();
            RefreshBounds();
            RefreshOutlinePositions();
            ApplyOutlineVisibility();
            UpdateIconTransform();
        }

        internal void ClearTarget()
        {
            _target = null;
            TargetId = 0;
            _level = null;
            _outlineVisible = false;

            if (_canvas != null)
                _canvas.gameObject.SetActive(false);

            ApplyOutlineVisibility();
        }

        private void Update()
        {
            try
            {
                if (_target == null)
                {
                    if (TargetId != 0)
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

                // 走到箱子附近就自动结束标记
                if ((main.transform.position - _target.position).sqrMagnitude <=
                    ArriveDistance * ArriveDistance)
                {
                    ClearTarget();
                    return;
                }

                if (_canvas != null && !_canvas.gameObject.activeSelf)
                    _canvas.gameObject.SetActive(true);

                UpdateIconTransform();
                UpdateArrowBob();

                if (Time.unscaledTime < _nextVisibilityCheck)
                    return;

                _nextVisibilityCheck = Time.unscaledTime + VisibilityInterval;

                bool visible = IsDirectlyVisible();
                if (visible != !_outlineVisible)
                {
                    _outlineVisible = !visible;
                    ApplyOutlineVisibility();
                }
            }
            catch (Exception e)
            {
                Diag.Report("火眼金睛：透视标记刷新失败", e);
                ClearTarget();
            }
        }

        // ------------------------------------------------------------ 图标

        private void EnsureIcon()
        {
            if (_canvas != null && _canvas)
                return;

            GameObject canvasGo = new GameObject("MagicTreasurePower_TargetIcon");
            canvasGo.transform.SetParent(transform, false);

            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 31000;

            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject backgroundGo = new GameObject("Background", typeof(RectTransform), typeof(Image));
            backgroundGo.transform.SetParent(canvasGo.transform, false);
            _iconBackground = backgroundGo.GetComponent<Image>();
            _iconBackground.sprite = RoundedSprite.Get();
            _iconBackground.type = Image.Type.Sliced;
            _iconBackground.color = new Color(0f, 0f, 0f, 0.45f);
            _iconBackground.raycastTarget = false;

            _iconRect = (RectTransform)backgroundGo.transform;
            _iconRect.sizeDelta = new Vector2(IconSize + 10f, IconSize + 10f);

            GameObject iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(backgroundGo.transform, false);

            _iconImage = iconGo.GetComponent<Image>();
            _iconImage.sprite = TreasureRegistrar.GetIcon(TreasureIds.HuoYanJinJing);
            _iconImage.preserveAspect = true;
            _iconImage.raycastTarget = false;

            var iconRect = (RectTransform)iconGo.transform;
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = new Vector2(5f, 5f);
            iconRect.offsetMax = new Vector2(-5f, -5f);

            if (_iconImage.sprite == null)
            {
                // 图标没注册成功（ID 冲突等）时至少给个红点，别让标记彻底消失
                _iconBackground.color = OutlineColor;
            }

            // 图标下方的红色箭头：朝下、上下浮动，提示「就是下面这个东西」
            GameObject arrowGo = new GameObject("Arrow", typeof(RectTransform), typeof(Image));
            arrowGo.transform.SetParent(backgroundGo.transform, false);

            _arrowImage = arrowGo.GetComponent<Image>();
            _arrowImage.sprite = GetArrowSprite();
            _arrowImage.preserveAspect = true;
            _arrowImage.raycastTarget = false;
            _arrowImage.color = OutlineColor;

            _arrowRect = (RectTransform)arrowGo.transform;
            _arrowRect.anchorMin = new Vector2(0.5f, 0f);
            _arrowRect.anchorMax = new Vector2(0.5f, 0f);
            _arrowRect.pivot = new Vector2(0.5f, 1f);
            _arrowRect.sizeDelta = new Vector2(20f, 16f);
            _arrowRect.anchoredPosition = new Vector2(0f, -2f);

            _canvas.gameObject.SetActive(false);
        }

        /// <summary>箭头上下浮动：正弦，±4 像素，看起来是活动的指示。</summary>
        private void UpdateArrowBob()
        {
            if (_arrowRect == null)
                return;

            float offset = Mathf.Sin(Time.unscaledTime * 6f) * 4f;
            _arrowRect.anchoredPosition = new Vector2(0f, -2f + offset);
        }

        /// <summary>运行时生成一个朝下的红色三角形箭头（不依赖字体字形）。</summary>
        private static Sprite GetArrowSprite()
        {
            if (_arrowSprite != null)
                return _arrowSprite;

            const int width = 28;
            const int height = 20;

            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            for (int y = 0; y < height; y++)
            {
                // 底尖上宽：y=0 是尖端，y=height 是宽边
                float half = (y / (float)(height - 1)) * (width * 0.5f);

                for (int x = 0; x < width; x++)
                {
                    float dx = Mathf.Abs(x + 0.5f - width * 0.5f);
                    bool inside = dx <= half;
                    texture.SetPixel(x, y, inside
                        ? new Color(1f, 1f, 1f, 1f)
                        : new Color(1f, 1f, 1f, 0f));
                }
            }

            texture.Apply();

            _arrowSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, width, height),
                new Vector2(0.5f, 1f),
                100f);

            return _arrowSprite;
        }

        private void UpdateIconTransform()
        {
            if (_canvas == null || _iconRect == null || _target == null)
                return;

            if (_camera == null || !_camera)
                _camera = Camera.main;

            if (_camera == null)
            {
                _canvas.gameObject.SetActive(false);
                return;
            }

            Vector3 world = _bounds.max + Vector3.up * IconLift;
            Vector3 screen = _camera.WorldToScreenPoint(world);

            if (screen.z <= 0f)
            {
                // 在摄像机背后：隐藏，避免图标出现在反方向
                _canvas.gameObject.SetActive(false);
                return;
            }

            _canvas.gameObject.SetActive(true);

            // 贴边：超出屏幕时把图标压在边缘，仍然指示方向
            float margin = IconSize * 0.5f + 8f;
            float x = Mathf.Clamp(screen.x, margin, Screen.width - margin);
            float y = Mathf.Clamp(screen.y, margin, Screen.height - margin);

            _iconRect.position = new Vector3(x, y, 0f);
        }

        // ------------------------------------------------------------ 红边

        private void RefreshBounds()
        {
            if (_target == null)
                return;

            bool has = false;
            Bounds bounds = new Bounds(_target.position, Vector3.one);

            Renderer[] renderers = _target.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled)
                    continue;

                if (!has)
                {
                    bounds = renderer.bounds;
                    has = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (!has)
            {
                // 没有可渲染网格（纯碰撞体）时，用碰撞盒兜底
                Collider[] colliders = _target.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < colliders.Length; i++)
                {
                    Collider collider = colliders[i];
                    if (collider == null || !collider.enabled)
                        continue;

                    if (!has)
                    {
                        bounds = collider.bounds;
                        has = true;
                    }
                    else
                    {
                        bounds.Encapsulate(collider.bounds);
                    }
                }
            }

            if (!has)
                bounds = new Bounds(_target.position + Vector3.up * 0.5f, Vector3.one);

            bounds.Expand(0.06f);
            _bounds = bounds;
        }

        private void RefreshOutlinePositions()
        {
            if (_target == null)
                return;

            EnsureOutline();

            Vector3 c = _bounds.center;
            Vector3 e = _bounds.extents;

            Vector3[] p =
            {
                c + new Vector3(-e.x, -e.y, -e.z),
                c + new Vector3(e.x, -e.y, -e.z),
                c + new Vector3(-e.x, -e.y, e.z),
                c + new Vector3(e.x, -e.y, e.z),
                c + new Vector3(-e.x, e.y, -e.z),
                c + new Vector3(e.x, e.y, -e.z),
                c + new Vector3(-e.x, e.y, e.z),
                c + new Vector3(e.x, e.y, e.z),
            };

            int[,] edges =
            {
                { 0, 1 }, { 1, 3 }, { 3, 2 }, { 2, 0 },
                { 4, 5 }, { 5, 7 }, { 7, 6 }, { 6, 4 },
                { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 },
            };

            for (int i = 0; i < _edges.Length; i++)
            {
                LineRenderer line = _edges[i];
                if (line == null)
                    continue;

                line.SetPosition(0, p[edges[i, 0]]);
                line.SetPosition(1, p[edges[i, 1]]);
            }
        }

        private void EnsureOutline()
        {
            if (_edges != null)
                return;

            if (_outlineMaterial == null)
                _outlineMaterial = CreateOutlineMaterial();

            GameObject root = new GameObject("Outline");
            root.transform.SetParent(transform, false);

            _edges = new LineRenderer[12];
            for (int i = 0; i < _edges.Length; i++)
            {
                GameObject lineGo = new GameObject("Edge_" + i);
                lineGo.transform.SetParent(root.transform, false);

                LineRenderer line = lineGo.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.positionCount = 2;
                line.startWidth = OutlineWidth;
                line.endWidth = OutlineWidth;
                line.numCapVertices = 0;
                line.numCornerVertices = 0;
                line.textureMode = LineTextureMode.Stretch;
                line.alignment = LineAlignment.View;
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.startColor = OutlineColor;
                line.endColor = OutlineColor;

                if (_outlineMaterial != null)
                    line.material = _outlineMaterial;

                line.enabled = false;

                _edges[i] = line;
            }
        }

        /// <summary>
        /// 穿墙材质：优先 Hidden/Internal-Colored + ZTest Always（URP 下也能穿墙）；
        /// 没有这个着色器就退回 Sprites/Default（只做普通描边，不穿墙）。
        /// </summary>
        private static Material CreateOutlineMaterial()
        {
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            bool throughWall = shader != null;

            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");

            if (shader == null)
                return null;

            Material material = new Material(shader);
            material.hideFlags = HideFlags.DontSave;

            if (material.HasProperty("_Color"))
                material.SetColor("_Color", OutlineColor);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", OutlineColor);

            if (throughWall)
            {
                if (material.HasProperty("_ZTest"))
                    material.SetInt("_ZTest", (int)CompareFunction.Always);
                if (material.HasProperty("_ZWrite"))
                    material.SetInt("_ZWrite", 0);
                if (material.HasProperty("_Cull"))
                    material.SetInt("_Cull", 0);
                if (material.HasProperty("_SrcBlend"))
                    material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                if (material.HasProperty("_DstBlend"))
                    material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            }

            return material;
        }

        private void ApplyOutlineVisibility()
        {
            if (_edges == null)
                return;

            for (int i = 0; i < _edges.Length; i++)
            {
                LineRenderer line = _edges[i];
                if (line != null)
                    line.enabled = _outlineVisible;
            }
        }

        /// <summary>
        /// 玩家是否「能直接看到」这个箱子：从摄像机向包围盒的几个采样点连线，
        /// 被墙 / 半高障碍挡住就算看不到（这时才显示红边）。
        /// </summary>
        private bool IsDirectlyVisible()
        {
            if (_target == null)
                return true;

            if (_camera == null || !_camera)
                _camera = Camera.main;

            if (_camera == null)
                return true;

            int mask = 0;
            try
            {
                mask = GameplayDataSettings.Layers.wallLayerMask |
                       GameplayDataSettings.Layers.halfObsticleLayer;
            }
            catch
            {
            }

            if (mask == 0)
                return true;

            Vector3 origin = _camera.transform.position;
            Vector3 c = _bounds.center;
            Vector3 e = _bounds.extents;

            Vector3[] samples =
            {
                c,
                c + new Vector3(0f, e.y, 0f),
                c + new Vector3(e.x, 0f, 0f),
                c + new Vector3(-e.x, 0f, 0f),
                c + new Vector3(0f, 0f, e.z),
            };

            int clear = 0;

            for (int i = 0; i < samples.Length; i++)
            {
                RaycastHit hit;
                if (!Physics.Linecast(origin, samples[i], out hit, mask, QueryTriggerInteraction.Ignore))
                {
                    clear++;
                    continue;
                }

                // 打到的是箱子自己的碰撞体也算看得到
                if (hit.collider != null && hit.collider.transform.IsChildOf(_target))
                    clear++;
            }

            return clear >= 2;
        }
    }
}
