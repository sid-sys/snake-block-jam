using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using TMPro;
using UnityEngine.UI;

namespace SnakeJam
{
    public static class UIKit
    {
        public static readonly Color Ink = new Color(0.16f, 0.12f, 0.35f, 1f);
        public static readonly Color Card = new Color(1f, 0.98f, 0.94f, 1f);
        public static readonly Color Chip = new Color(0.12f, 0.1f, 0.3f, 0.35f);

        public static Canvas MakeCanvas(string name, int order)
        {
            var go = new GameObject(name);
            var cv = go.AddComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceCamera;
            cv.worldCamera = Camera.main;
            cv.planeDistance = 5f;
            cv.sortingOrder = 20000 + order;   // above every board sprite (bridge/over layers use up to ~3000)
            var sc = go.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            // BASE SCREEN RULE: 720 x 1612 px (6.86 inch phone, 9:20). Layout is 1080 units wide on every phone, height follows the screen.
            sc.referenceResolution = new Vector2(1080, 2418);
            sc.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            sc.matchWidthOrHeight = 0f;
            go.AddComponent<GraphicRaycaster>();
            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<InputSystemUIInputModule>();
            }
            return cv;
        }

        public static RectTransform Stretch(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static RectTransform Anchored(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        static TextAlignmentOptions Align(TextAnchor a)
        {
            switch (a)
            {
                case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
                case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
                case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
                case TextAnchor.MiddleLeft: return TextAlignmentOptions.Left;
                case TextAnchor.MiddleRight: return TextAlignmentOptions.Right;
                case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
                case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
                case TextAnchor.LowerRight: return TextAlignmentOptions.BottomRight;
                default: return TextAlignmentOptions.Center;
            }
        }

        // TextMeshPro label (crisp SDF text, soft drop shadow, no outline)
        public static TMP_Text Label(Transform parent, string s, int size, Color col, TextAnchor a = TextAnchor.MiddleCenter, bool shadow = true)
        {
            var rt = Stretch(parent, "Text");
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = Art.TmpFont;
            t.fontSize = size; t.color = col; t.alignment = Align(a); t.text = s;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.richText = false;
            t.raycastTarget = false;
            if (shadow) t.fontSharedMaterial = Art.TmpShadowMaterial;
            return t;
        }

        // typewriter: shows the text one letter at a time
        public static Tweener Type(TMP_Text t, string s, float duration)
        {
            t.text = s;
            t.maxVisibleCharacters = 0;
            return DOVirtual.Float(0f, s.Length, duration, v => { if (t != null) t.maxVisibleCharacters = Mathf.RoundToInt(v); })
                .OnComplete(() => { if (t != null) t.maxVisibleCharacters = 99999; });
        }

        public static Image Icon(Transform parent, string sprite, Vector2 size, Vector2 anchor, Vector2 pivot, Vector2 pos)
        {
            var rt = Anchored(parent, sprite, anchor, pivot, pos, size);
            var im = rt.gameObject.AddComponent<Image>();
            im.sprite = Art.Get(sprite);
            im.preserveAspect = true;
            im.raycastTarget = false;
            return im;
        }

        public static Image Panel(Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color col, string name = "Panel")
        {
            var rt = Anchored(parent, name, anchor, pivot, pos, size);
            var im = rt.gameObject.AddComponent<Image>();
            im.sprite = Art.Round; im.type = Image.Type.Sliced; im.color = col;
            im.pixelsPerUnitMultiplier = 0.9f;
            im.raycastTarget = false;
            return im;
        }

        // pill shaped, one flat colour, no outline. colour = purple | green | blue | orange
        public static Button Pill(Transform parent, string label, string color, Vector2 size, Vector2 anchor, Vector2 pivot, Vector2 pos, Action click, string icon = null, int fontSize = 46)
        {
            var rt = Anchored(parent, "Pill_" + label, anchor, pivot, pos, size);
            var im = rt.gameObject.AddComponent<Image>();
            im.sprite = Art.Get("pill_" + color);
            im.type = Image.Type.Sliced;
            im.pixelsPerUnitMultiplier = (70f * 100f / 256f) / (size.y * 0.5f);
            var b = rt.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.targetGraphic = im;
            b.onClick.AddListener(() =>
            {
                if (!SilentClicks) Sfx.Play("ui_click");
                rt.DOKill(true);
                rt.DOPunchScale(new Vector3(-0.07f, -0.07f, 0f), 0.18f, 1, 0f);
                click?.Invoke();
            });
            float iconW = 0f;
            if (icon != null)
            {
                float s = size.y * 0.72f;
                iconW = s;
                UIKit.Icon(rt, icon, new Vector2(s, s), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(size.y * 0.28f, 0));
            }
            if (!string.IsNullOrEmpty(label))
            {
                var t = Label(rt, label, fontSize, Color.white);
                var trt = t.rectTransform;
                trt.offsetMin = new Vector2(iconW + size.y * 0.3f, 0);
                trt.offsetMax = new Vector2(-size.y * 0.15f, 0);
                if (icon == null) { trt.offsetMin = Vector2.zero; }
            }
            return b;
        }

        public static void SetPillLabel(Button b, string s)
        {
            var t = b.GetComponentInChildren<TMP_Text>();
            if (t != null) t.text = s;
        }

        public static bool SilentClicks;   // the sound test panel turns the button click sound off, so it does not cover the sound being tested

        public class PopupHandle
        {
            public RectTransform dim, card, content;
            public Action onClosed;
            Image dimImg;
            bool closing;
            public PopupHandle(RectTransform d, RectTransform c, RectTransform content, Image di) { dim = d; card = c; this.content = content; dimImg = di; }
            public void Close()
            {
                if (closing) return;
                closing = true;
                Sfx.Play("popup_close");
                var seq = DOTween.Sequence();
                seq.Append(card.DOScale(0.8f, 0.18f).SetEase(Ease.InBack));
                seq.Join(dimImg.DOFade(0f, 0.18f));
                UnityEngine.Object.Destroy(dim.gameObject, 0.22f);
                onClosed?.Invoke();
            }
        }

        public static PopupHandle Popup(Canvas cv, string title, Vector2 size, bool showClose = true, float dimAlpha = 0.6f, bool noCard = false)
        {
            Sfx.Play("popup_open");
            var dim = Stretch(cv.transform, "Popup");
            var di = dim.gameObject.AddComponent<Image>();
            di.color = new Color(0.05f, 0.03f, 0.15f, 0f);
            di.DOFade(dimAlpha, 0.2f);
            var card = Panel(dim, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size, noCard ? Color.clear : Card, "Card");
            card.transform.localScale = Vector3.one * 0.7f;
            card.transform.DOScale(1f, 0.3f).SetEase(Ease.OutBack);
            var content = Stretch(card.transform, "Content");
            if (!string.IsNullOrEmpty(title))
            {
                var tt = Label(content, title, 64, Ink, TextAnchor.UpperCenter, false);
                tt.rectTransform.offsetMin = new Vector2(0, 0); tt.rectTransform.offsetMax = new Vector2(0, -34);
            }
            var h = new PopupHandle(dim, card.rectTransform, content, di);
            if (showClose)
            {
                Pill(card.transform, "X", "orange", new Vector2(130, 84), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-24, -24), () => h.Close(), null, 48);
            }
            return h;
        }
    }
}
