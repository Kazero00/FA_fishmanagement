using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace FantasyAquariumMod
{
    /// <summary>
    /// 各面板共用的 UI 小工具：配色、字体（含中文）、节点/文本/按钮创建。
    ///
    /// 注意：本插件里我们自己的 MonoBehaviour 收不到 Unity 回调，
    /// 所以这些 UI 全部是“纯代码创建 + Button.onClick 交互”，不挂任何自定义脚本。
    /// </summary>
    internal static class UiKit
    {
        public static readonly Color PanelBg = new Color(0.055f, 0.08f, 0.12f, 0.95f);
        public static readonly Color HeaderColor = new Color(0.65f, 0.82f, 0.95f, 1f);
        public static readonly Color TextColor = new Color(0.93f, 0.96f, 1f, 1f);
        public static readonly Color DimColor = new Color(0.62f, 0.68f, 0.75f, 1f);
        public static readonly Color PrimaryBtn = new Color(0.16f, 0.42f, 0.62f, 1f);
        public static readonly Color AccentBtn = new Color(0.20f, 0.55f, 0.42f, 1f);
        public static readonly Color DangerBtn = new Color(0.66f, 0.24f, 0.24f, 1f);
        public static readonly Color ActiveBtn = new Color(0.85f, 0.62f, 0.20f, 1f);
        public static readonly Color DisabledBtn = new Color(0.22f, 0.24f, 0.28f, 0.85f);
        public static readonly Color RowStripe = new Color(1f, 1f, 1f, 0.045f);

        /// <summary>
        /// 稀有度配色，与游戏官方档位一一对应（从游戏 zh-hans 本地化表实证）：
        /// 0 绿色 / 1 蓝色 / 2 金色 / 3 闪彩 / 4 异色。
        /// </summary>
        public static readonly Color[] RarityColor =
        {
            new Color(0.42f, 0.80f, 0.46f, 1f),   // 绿色
            new Color(0.38f, 0.64f, 0.96f, 1f),   // 蓝色
            new Color(0.95f, 0.78f, 0.30f, 1f),   // 金色
            new Color(0.88f, 0.45f, 0.96f, 1f),   // 闪彩 (rarity 3)
            new Color(1.00f, 0.62f, 0.98f, 1f),   // 闪彩★ (rarity 4)：更亮一档，便于和 rarity 3 区分
        };

        private static Font _font;
        private static bool _resolved;

        public static Font Font
        {
            get
            {
                if (!_resolved)
                {
                    _resolved = true;
                    _font = Resolve();
                }
                return _font;
            }
        }

        private static Font Resolve()
        {
            try
            {
                var cjk = Font.CreateDynamicFontFromOSFont(
                    new[] { "Microsoft YaHei", "微软雅黑", "SimHei", "Noto Sans CJK SC", "Arial Unicode MS" }, 16);
                if (cjk != null) return cjk;
            }
            catch { /* 回退内置字体 */ }

            foreach (var name in new[] { "LegacyRuntime.ttf", "Arial.ttf" })
            {
                try
                {
                    var f = Resources.GetBuiltinResource<Font>(name);
                    if (f != null) return f;
                }
                catch { /* 换下一个 */ }
            }

            Plugin.Log.LogWarning("[UI] 未找到可用字体，文字可能不显示。");
            return null;
        }

        public static bool HasCjk => Font != null && Font.dynamic;

        public static string T(string zh, string en) => HasCjk ? zh : en;

        public static Color Rarity(int rarity)
        {
            if (rarity < 0 || rarity >= RarityColor.Length) return TextColor;
            return RarityColor[rarity];
        }

        /// <summary>
        /// 稀有度名称，与游戏官方一致：绿色 / 蓝色 / 金色 / 闪彩 / 闪彩★。
        /// rarity 4 在游戏里与 rarity 3 同属「闪彩」（同一张卡片美术、同一个 UnlockedShiny 计数），
        /// 但获取渠道完全不同（rarity 4 只能靠好感度投资抽奖，普通鱼袋池明确排除），
        /// 所以这里给 rarity 4 追加 ★ 以便区分。
        /// </summary>
        public static string RarityName(int rarity)
        {
            switch (rarity)
            {
                case 0: return T("绿色", "Green");
                case 1: return T("蓝色", "Blue");
                case 2: return T("金色", "Gold");
                case 3: return T("闪彩", "Shiny");
                case 4: return T("闪彩★", "Shiny★");
                default: return "?";
            }
        }

        // ---------------------------------------------------------------- 节点

        public static GameObject NewNode(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        /// <summary>按父节点左上角定位（父节点的 anchor 需为左上或铺满）。</summary>
        public static void Place(GameObject go, float x, float y, float w, float h)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, -y);
        }

        public static void Stretch(GameObject go)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        public static Image Image(string name, Transform parent, float x, float y, float w, float h, Color color)
        {
            var go = NewNode(name, parent);
            Place(go, x, y, w, h);
            var img = go.AddComponent<Image>();
            img.color = color;
            return img;
        }

        public static Text Label(string name, Transform parent, string content, int size, TextAnchor anchor,
            Color color, float x, float y, float w, float h)
        {
            var go = NewNode(name, parent);
            Place(go, x, y, w, h);
            return Configure(go.AddComponent<Text>(), content, size, anchor, color);
        }

        public static Text Label(string name, Transform parent, string content, int size, TextAnchor anchor, Color color)
        {
            var go = NewNode(name, parent);
            return Configure(go.AddComponent<Text>(), content, size, anchor, color);
        }

        public static Text Configure(Text t, string content, int size, TextAnchor anchor, Color color)
        {
            t.font = Font;
            t.fontSize = size;
            t.text = content;
            t.alignment = anchor;
            t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        public static Button MakeButton(string name, Transform parent, string label, float x, float y, float w, float h,
            UnityAction onClick, Color color, int fontSize = 15)
        {
            var go = NewNode(name, parent);
            Place(go, x, y, w, h);

            var img = go.AddComponent<Image>();
            img.color = color;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            if (onClick != null) btn.onClick.AddListener(onClick);

            var text = Label("Label", go.transform, label, fontSize, TextAnchor.MiddleCenter, TextColor);
            Stretch(text.gameObject);

            return btn;
        }

        /// <summary>取按钮上的文字（按钮子节点必须叫 Label）。</summary>
        public static Text LabelOf(Transform button)
        {
            var t = button.Find("Label");
            return t != null ? t.GetComponent<Text>() : null;
        }

        /// <summary>
        /// 创建一个单行输入框。
        /// 注意：调用方不要在 onValueChanged 里重建整个面板，否则输入框会被销毁、光标/焦点丢失。
        /// </summary>
        public static InputField MakeInput(string name, Transform parent, string initial, string placeholder,
            float x, float y, float w, float h, int fontSize, UnityAction<string> onChanged,
            UnityAction<string> onEndEdit = null)
        {
            var go = NewNode(name, parent);
            Place(go, x, y, w, h);

            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.12f, 0.15f, 0.19f, 1f);

            // 注意：必须先 Stretch 成拉伸锚点，再改 offset；否则 sizeDelta 会变成负数，文字完全不渲染
            var text = Label("Text", go.transform, "", fontSize, TextAnchor.MiddleLeft, TextColor);
            Stretch(text.gameObject);
            var trt = (RectTransform)text.transform;
            trt.offsetMin = new Vector2(7f, 2f);
            trt.offsetMax = new Vector2(-7f, -2f);
            text.supportRichText = false;

            var ph = Label("Placeholder", go.transform, placeholder, fontSize, TextAnchor.MiddleLeft,
                new Color(0.48f, 0.54f, 0.60f, 1f));
            Stretch(ph.gameObject);
            var prt = (RectTransform)ph.transform;
            prt.offsetMin = new Vector2(7f, 2f);
            prt.offsetMax = new Vector2(-7f, -2f);
            ph.supportRichText = false;

            var input = go.AddComponent<InputField>();
            input.targetGraphic = bg;
            input.textComponent = text;
            input.placeholder = ph;
            input.lineType = InputField.LineType.SingleLine;
            input.contentType = InputField.ContentType.DecimalNumber;
            input.characterLimit = 10;
            input.caretColor = TextColor;
            input.selectionColor = new Color(0.25f, 0.5f, 0.8f, 0.6f);
            if (onChanged != null) input.onValueChanged.AddListener(onChanged);
            if (onEndEdit != null) input.onEndEdit.AddListener(onEndEdit);
            input.text = initial ?? "";
            return input;
        }

        public static void SetButtonColor(Button btn, Color color)
        {
            if (btn != null && btn.targetGraphic is Image img) img.color = color;
        }

        public static string PathOf(Transform t)
        {
            var sb = new StringBuilder(t.name);
            var p = t.parent;
            int depth = 0;
            while (p != null && depth++ < 8)
            {
                sb.Insert(0, p.name + "/");
                p = p.parent;
            }
            return sb.ToString();
        }
    }
}
