using System;
using ItemStatsSystem;
using MagicTreasurePower.UI;
using UnityEngine;
using UnityEngine.UI;

namespace MagicTreasurePower.Treasures
{
    /// <summary>
    /// 火眼金睛的「透视标记」：选中物资箱 / 地面物品后，
    /// 在目标头顶显示一个火眼金睛图标，图标下方带一个上下浮动的红色箭头。
    ///
    /// <para><b>为什么不再画红边：</b>用户 2026-10-28 反馈穿墙红边太花，去掉红边，
    /// 只保留图标 + 箭头作为指引。</para>
    ///
    /// <para>再点同一行取消；走到 3 米内自动结束；目标被捡走 / 销毁、换图 / 回基地、
    /// 停用 mod 时自动清除。</para>
    /// </summary>
    internal static class TreasureTargetMarker
    {
        private static TargetMarkerHost _host;

        /// <summary>当前标记的目标实例 ID；没有标记时为 0。</summary>
        internal static int ActiveTargetId => _host != null ? _host.TargetId : 0;

        internal static void Toggle(Transform target)
        {
            if (target == null)
                return;

            // 透视标记的总开关（config.ini 的 ShowRouteLine）
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

    /// <summary>透视标记的宿主：屏幕空间图标 + 浮动箭头。</summary>
    internal sealed class TargetMarkerHost : MonoBehaviour
    {
        /// <summary>走到这个距离内认为「到了」，标记自动消失（米）。</summary>
        private const float ArriveDistance = 3f;

        /// <summary>图标尺寸（UI 参考分辨率下的像素）。</summary>
        private const float IconSize = 46f;

        /// <summary>图标相对目标包围盒顶部的抬高（米）。</summary>
        private const float IconLift = 0.45f;

        private static readonly Color MarkerColor = new Color(1f, 0.18f, 0.18f, 1f);

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

        private Bounds _bounds;

        internal int TargetId { get; private set; }

        internal void SetTarget(Transform target)
        {
            _target = target;
            TargetId = target != null ? target.GetInstanceID() : 0;
            _level = LevelManager.Instance;

            EnsureIcon();
            RefreshBounds();
            UpdateIconTransform();
            UpdateArrowBob();
        }

        internal void ClearTarget()
        {
            _target = null;
            TargetId = 0;
            _level = null;

            if (_canvas != null)
                _canvas.gameObject.SetActive(false);
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

                // 目标是地面物品、已经被捡进背包 / 槽位时，标记也该收掉
                Item targetItem = _target.GetComponent<Item>();
                if (targetItem != null &&
                    (targetItem.InInventory != null || targetItem.PluggedIntoSlot != null))
                {
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

                // 走到目标附近就自动结束标记
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
                _iconBackground.color = MarkerColor;
            }

            // 图标下方的红色箭头：朝下、上下浮动，提示「就是下面这个东西」
            GameObject arrowGo = new GameObject("Arrow", typeof(RectTransform), typeof(Image));
            arrowGo.transform.SetParent(backgroundGo.transform, false);

            _arrowImage = arrowGo.GetComponent<Image>();
            _arrowImage.sprite = GetArrowSprite();
            _arrowImage.preserveAspect = true;
            _arrowImage.raycastTarget = false;
            _arrowImage.color = MarkerColor;

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

        // ------------------------------------------------------------ 包围盒（图标定位用）

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
    }
}
