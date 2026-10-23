using System;
using System.Collections;
using System.Collections.Generic;
using MagicTreasurePower.Treasures;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MagicTreasurePower.UI
{
    /// <summary>
    /// 法宝触发提示：屏幕中上方悬浮一行短提示（图标 + 法宝名 + 具体效果）。
    ///
    /// <para><b>滚动窗口：</b></para>
    /// 最多同时显示 3 条，新提示出现时直接顶掉最旧的一条（符合「只显示最后三条」）。
    /// 提示对象复用池化，不会每次触发都新建 / 销毁 GameObject 与 TMP 文本。
    ///
    /// <para><b>风格：</b></para>
    /// 深色半透明圆角底 + 金色描边 + 法宝金色名称，字体直接从游戏内正在使用的
    /// TextMeshPro 组件上取，保证中文字形与字号观感与原版一致。
    /// 圆角底图没有可复用的原版资源，运行时生成九宫格贴图（与 ItemDetailsDisplay 的圆角风格一致）。
    /// </summary>
    internal static class TreasureNotice
    {
        private static TreasureNoticeHost _host;

        /// <summary>
        /// 预热：在 mod 初始化阶段就把 Canvas / 字体 / 底图建好，
        /// 避免第一次触发时才做字体扫描（<c>FindObjectsOfTypeAll</c>）和贴图生成而卡一下。
        /// </summary>
        internal static void Prewarm()
        {
            try
            {
                EnsureHost();
            }
            catch (Exception e)
            {
                Diag.Report("法宝提示框预热失败", e);
            }
        }

        private static TreasureNoticeHost EnsureHost()
        {
            if (_host != null && !_host)
                _host = null;

            if (_host == null)
            {
                GameObject hostGo = new GameObject("MagicTreasurePower_NoticeHost");
                UnityEngine.Object.DontDestroyOnLoad(hostGo);
                _host = hostGo.AddComponent<TreasureNoticeHost>();
            }

            return _host;
        }

        /// <summary>显示一条法宝触发提示。<paramref name="detail"/> 支持 Unity 富文本。</summary>
        internal static void Show(int typeId, string detail)
        {
            try
            {
                EnsureHost().Enqueue(typeId, detail);
            }
            catch (Exception e)
            {
                Diag.Report("法宝提示显示失败", e);
            }
        }
    }

    /// <summary>提示条的宿主：建 Canvas、维护滚动窗口、播放淡入淡出。</summary>
    internal sealed class TreasureNoticeHost : MonoBehaviour
    {
        // 单条提示总时长 = 0.1 + 0.55 + 0.25 = 0.9 秒
        private const float SlideDuration = 0.1f;
        private const float HoldDuration = 0.55f;
        private const float FadeDuration = 0.25f;

        /// <summary>同时最多显示几条（滚动窗口，超出就顶掉最旧的）。</summary>
        private const int MaxNotices = 3;

        /// <summary>提示整体缩放：先缩到 3/5（0.6），再在当前基础上扩大二分之一 → 0.9。</summary>
        private const float NoticeScale = 0.9f;

        private const float NoticeFontSize = 24f;
        private const float IconSize = 38f;

        private static readonly Color NameColor = new Color(0.91f, 0.75f, 0.36f, 1f);
        private static readonly Color TextColor = new Color(0.96f, 0.94f, 0.88f, 1f);

        /// <summary>一条提示的视图（池化复用）。</summary>
        private sealed class NoticeView : MonoBehaviour
        {
            internal CanvasGroup Group;
            internal Image Icon;
            internal TextMeshProUGUI Text;
            internal LayoutElement TextLayout;
            internal Coroutine Routine;
        }

        private RectTransform _list;
        private TMP_FontAsset _font;
        private Sprite _background;

        private readonly List<NoticeView> _active = new List<NoticeView>();
        private readonly List<NoticeView> _pool = new List<NoticeView>();

        private void Awake()
        {
            try
            {
                BuildCanvas();
            }
            catch (Exception e)
            {
                Diag.Report("法宝提示框初始化失败", e);
            }
        }

        internal void Enqueue(int typeId, string detail)
        {
            try
            {
                if (_list == null)
                    BuildCanvas();

                if (_list == null)
                    return;

                // 预热时游戏 UI 可能还没建好，取不到字体；这里每次再试一次
                if (_font == null)
                    _font = ResolveFont();

                if (_font == null)
                    return;

                NoticeView view;

                if (_active.Count >= MaxNotices)
                {
                    // 滚动窗口：直接顶掉最旧的一条，并把它挪到队尾复用
                    view = _active[0];
                    _active.RemoveAt(0);
                    StopViewRoutine(view);
                    view.transform.SetAsLastSibling();
                    view.gameObject.SetActive(true);
                }
                else if (_pool.Count > 0)
                {
                    view = _pool[_pool.Count - 1];
                    _pool.RemoveAt(_pool.Count - 1);
                    view.gameObject.SetActive(true);
                    view.transform.SetAsLastSibling();
                }
                else
                {
                    view = CreateView();
                }

                Populate(view, typeId, detail);
                _active.Add(view);
                view.Routine = StartCoroutine(PlayLifetime(view));
            }
            catch (Exception e)
            {
                Diag.Report("法宝提示排版失败", e);
            }
        }

        private void StopViewRoutine(NoticeView view)
        {
            if (view.Routine != null)
            {
                StopCoroutine(view.Routine);
                view.Routine = null;
            }
        }

        private void BuildCanvas()
        {
            if (_list != null)
                return;

            _font = ResolveFont();
            _background = GetBackgroundSprite();

            GameObject canvasGo = new GameObject("MagicTreasurePower_NoticeCanvas");
            canvasGo.transform.SetParent(transform, false);
            DontDestroyOnLoad(canvasGo);

            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;

            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject listGo = new GameObject("List", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            listGo.transform.SetParent(canvasGo.transform, false);

            _list = listGo.GetComponent<RectTransform>();
            _list.anchorMin = new Vector2(0.5f, 1f);
            _list.anchorMax = new Vector2(0.5f, 1f);
            _list.pivot = new Vector2(0.5f, 1f);
            _list.anchoredPosition = new Vector2(0f, -150f);
            _list.sizeDelta = new Vector2(900f, 0f);

            // 整体缩放（当前 0.9）：pivot 在顶部中央，缩放后仍然贴住原来的位置往下排
            _list.localScale = new Vector3(NoticeScale, NoticeScale, 1f);

            VerticalLayoutGroup layout = listGo.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            ContentSizeFitter fitter = listGo.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private NoticeView CreateView()
        {
            GameObject noticeGo = new GameObject(
                "Notice",
                typeof(RectTransform),
                typeof(Image),
                typeof(HorizontalLayoutGroup),
                typeof(ContentSizeFitter),
                typeof(CanvasGroup),
                typeof(LayoutElement));

            RectTransform rect = noticeGo.GetComponent<RectTransform>();
            rect.SetParent(_list, false);

            Image background = noticeGo.GetComponent<Image>();
            background.sprite = _background;
            background.type = Image.Type.Sliced;
            background.color = Color.white;
            background.raycastTarget = false;

            HorizontalLayoutGroup layout = noticeGo.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(14, 18, 7, 7);
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            ContentSizeFitter fitter = noticeGo.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            NoticeView view = noticeGo.AddComponent<NoticeView>();
            view.Group = noticeGo.GetComponent<CanvasGroup>();
            view.Group.alpha = 0f;
            view.Group.blocksRaycasts = false;
            view.Group.interactable = false;

            GameObject iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            iconGo.transform.SetParent(noticeGo.transform, false);

            Image icon = iconGo.GetComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            LayoutElement iconLayout = iconGo.GetComponent<LayoutElement>();
            iconLayout.preferredWidth = IconSize;
            iconLayout.preferredHeight = IconSize;
            iconLayout.minWidth = IconSize;
            iconLayout.minHeight = IconSize;

            GameObject textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            textGo.transform.SetParent(noticeGo.transform, false);

            TextMeshProUGUI text = textGo.GetComponent<TextMeshProUGUI>();
            text.font = _font;
            text.fontSize = NoticeFontSize;
            text.color = TextColor;
            text.richText = true;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.raycastTarget = false;

            view.Icon = icon;
            view.Text = text;
            view.TextLayout = textGo.GetComponent<LayoutElement>();
            view.TextLayout.preferredHeight = 34f;

            return view;
        }

        /// <summary>更新一条提示的内容（复用时只改文本与图标，不重建节点）。</summary>
        private void Populate(NoticeView view, int typeId, string detail)
        {
            string name = TreasureCatalog.GetDisplayName(typeId);

            Sprite icon = TreasureRegistrar.GetIcon(typeId);
            view.Icon.sprite = icon;
            view.Icon.gameObject.SetActive(icon != null);

            string coloredName = string.Format(
                "<color=#{0}><b>{1}</b></color>",
                ColorUtility.ToHtmlStringRGB(NameColor),
                name);

            view.Text.text = string.IsNullOrEmpty(detail) ? coloredName : coloredName + "  " + detail;
            view.TextLayout.preferredWidth = Mathf.Min(760f, view.Text.preferredWidth + 4f);
        }

        private IEnumerator PlayLifetime(NoticeView view)
        {
            CanvasGroup group = view.Group;
            Transform tr = view.transform;

            float time = 0f;
            while (time < SlideDuration && view != null)
            {
                time += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(time / SlideDuration);
                group.alpha = k;
                tr.localScale = Vector3.one * Mathf.Lerp(0.96f, 1f, k);
                yield return null;
            }

            if (view != null)
            {
                group.alpha = 1f;
                tr.localScale = Vector3.one;
            }

            yield return new WaitForSecondsRealtime(HoldDuration);

            time = 0f;
            while (time < FadeDuration && view != null)
            {
                time += Time.unscaledDeltaTime;
                group.alpha = 1f - Mathf.Clamp01(time / FadeDuration);
                yield return null;
            }

            if (view == null)
                yield break;

            _active.Remove(view);
            view.Routine = null;
            group.alpha = 0f;
            view.gameObject.SetActive(false);

            if (!_pool.Contains(view))
                _pool.Add(view);
        }

        /// <summary>
        /// 从游戏里正在使用的 TextMeshPro 上取字体：优先挑能显示这几个汉字的，
        /// 保证与原版中文字形、字重一致。
        /// </summary>
        private static TMP_FontAsset ResolveFont()
        {
            TMP_FontAsset fallback = null;

            TextMeshProUGUI[] all = Resources.FindObjectsOfTypeAll<TextMeshProUGUI>();
            for (int i = 0; i < all.Length; i++)
            {
                TextMeshProUGUI text = all[i];
                if (text == null || text.font == null)
                    continue;

                if (fallback == null)
                    fallback = text.font;

                if (text.font.HasCharacter('法') && text.font.HasCharacter('宝') && text.font.HasCharacter('伤'))
                    return text.font;
            }

            if (fallback != null)
                return fallback;

            return TMP_Settings.defaultFontAsset;
        }

        /// <summary>
        /// 运行时生成圆角九宫格底图：深色半透明填充 + 1.5px 金色描边。
        /// 原版没有可直接复用的圆角面板资源，这里按原版配色自行绘制。
        /// </summary>
        private static Sprite GetBackgroundSprite()
        {
            const int size = 64;
            const int radius = 18;
            const float borderWidth = 1.5f;

            Color fill = new Color(0.055f, 0.06f, 0.07f, 0.88f);
            Color border = new Color(0.91f, 0.75f, 0.36f, 0.85f);

            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = RoundedRectDistance(x + 0.5f, y + 0.5f, size, radius);

                    // 边缘 1px 抗锯齿
                    float alpha = Mathf.Clamp01(-distance + 0.5f);
                    Color color = distance > -borderWidth ? border : fill;
                    color.a *= alpha;
                    texture.SetPixel(x, y, color);
                }
            }

            texture.Apply();

            GameObject holderGo = new GameObject("MagicTreasurePower_NoticeBg");
            UnityEngine.Object.DontDestroyOnLoad(holderGo);
            holderGo.AddComponent<NoticeBackgroundHolder>().Texture = texture;

            return Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(radius, radius, radius, radius));
        }

        private static float RoundedRectDistance(float x, float y, float size, float radius)
        {
            float half = size * 0.5f;
            float inner = half - radius;
            float dx = Mathf.Max(Mathf.Abs(x - half) - inner, 0f);
            float dy = Mathf.Max(Mathf.Abs(y - half) - inner, 0f);
            return Mathf.Sqrt(dx * dx + dy * dy) - radius;
        }

        /// <summary>贴图必须挂在常驻对象上，否则切图后会被回收。</summary>
        private sealed class NoticeBackgroundHolder : MonoBehaviour
        {
            internal Texture2D Texture;
        }
    }
}
