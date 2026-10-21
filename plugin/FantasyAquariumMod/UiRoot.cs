using UnityEngine;
using UnityEngine.UI;

namespace FantasyAquariumMod
{
    /// <summary>
    /// 共用 UI 画布/根节点。
    ///
    /// 本插件里我们自己的 MonoBehaviour 收不到 Unity 回调（日志实证），
    /// 所以 UI 完全由代码创建、由 Harmony 补丁驱动；
    /// 这里只负责“建一次画布和根节点”，供各面板挂载。
    /// </summary>
    internal static class UiRoot
    {
        private const int OwnSortingOrder = 30000;
        private const string OwnCanvasName = "FantasyAquariumMod_Canvas";

        private static RectTransform _root;
        private static bool _built;

        public static RectTransform Root => _root;

        public static bool IsReady => _built && _root != null;

        /// <summary>游戏主界面就绪后建一次根节点；已经建好就直接返回。</summary>
        public static void EnsureBuilt()
        {
            if (_root != null) return;
            if (TankController.Instance == null || TankController.CurrentTank == null) return;

            bool ownCanvas = !Plugin.UseGameCanvas.Value;
            var host = ownCanvas ? CreateOwnCanvas() : FindGameCanvas();
            if (host == null)
            {
                host = CreateOwnCanvas();
                ownCanvas = true;
            }

            var rootGo = new GameObject("FantasyAquariumMod_UIRoot", typeof(RectTransform));
            _root = (RectTransform)rootGo.transform;
            _root.SetParent(host.transform, false);

            if (ownCanvas)
            {
                _root.anchorMin = Vector2.zero;
                _root.anchorMax = Vector2.one;
                _root.offsetMin = Vector2.zero;
                _root.offsetMax = Vector2.zero;
            }
            else
            {
                // 游戏 UI 的设计空间只有 640x360 级别，直接挂进去会尺寸失配：
                // 根节点固定 1920x1080 并反向抵消宿主缩放，保证按 1:1 设计
                var hostCanvas = host.GetComponent<Canvas>();
                float sf = hostCanvas != null && hostCanvas.scaleFactor > 0.0001f ? hostCanvas.scaleFactor : 1f;
                _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);
                _root.pivot = new Vector2(0.5f, 0.5f);
                _root.sizeDelta = new Vector2(1920f, 1080f);
                _root.anchoredPosition = Vector2.zero;
                _root.localScale = Vector3.one / sf;

                if (host.GetComponent<GraphicRaycaster>() == null &&
                    host.GetComponentInParent<GraphicRaycaster>() == null)
                {
                    host.AddComponent<GraphicRaycaster>();
                }
            }
            _root.SetAsLastSibling();

            _built = true;
            var canvas = host.GetComponent<Canvas>();
            Plugin.Log.LogInfo(
                $"[UI] 根节点已创建：宿主='{host.name}' 自建画布={ownCanvas} " +
                $"layer={(canvas != null ? LayerName(canvas.sortingLayerID) : "?")} " +
                $"order={(canvas != null ? canvas.sortingOrder : 0)} " +
                $"Screen={Screen.width}x{Screen.height} rootRect={_root.rect.size} " +
                $"font={(UiKit.Font != null ? UiKit.Font.name + (UiKit.HasCjk ? "(CJK)" : "(no-cjk)") : "null")}");

            DumpGameButtons();
        }

        private static GameObject CreateOwnCanvas()
        {
            var go = new GameObject(OwnCanvasName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            int layerId = SortingLayer.NameToID("UI");
            if (layerId == 0)
            {
                var layers = SortingLayer.layers;
                if (layers != null && layers.Length > 0) layerId = layers[layers.Length - 1].id;
            }
            canvas.sortingLayerID = layerId;
            canvas.sortingOrder = OwnSortingOrder;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            return go;
        }

        private static GameObject FindGameCanvas()
        {
            Canvas best = null;
            int bestLayer = -1;
            int bestOrder = int.MinValue;

            foreach (var c in Object.FindObjectsOfType<Canvas>())
            {
                if (c == null || !c.isRootCanvas) continue;
                if (c.gameObject.name == OwnCanvasName) continue;
                int li = LayerIndex(c.sortingLayerID);
                if (li > bestLayer || (li == bestLayer && c.sortingOrder > bestOrder))
                {
                    best = c;
                    bestLayer = li;
                    bestOrder = c.sortingOrder;
                }
            }
            if (best != null) return best.gameObject;

            foreach (var name in new[] { "UI_Main", "Canvas", "Front" })
            {
                var go = GameObject.Find(name);
                if (go != null) return go;
            }
            return null;
        }

        /// <summary>把游戏原有按钮的屏幕坐标打一次日志，便于对齐参照。</summary>
        private static void DumpGameButtons()
        {
            var gameCanvas = FindGameCanvas();
            if (gameCanvas == null) return;

            var buttons = gameCanvas.GetComponentsInChildren<Button>(true);
            Plugin.Log.LogInfo($"[UI] 游戏画布 '{gameCanvas.name}' 下共 {buttons.Length} 个 Button，前 4 个：");

            int n = 0;
            foreach (var b in buttons)
            {
                if (b == null || !(b.transform is RectTransform rt)) continue;
                var sp = RectTransformUtility.WorldToScreenPoint(null, rt.position);
                Plugin.Log.LogInfo($"   #{n} {UiKit.PathOf(b.transform)} screen=({sp.x:F0},{sp.y:F0}) size={rt.rect.size}");
                if (++n >= 4) break;
            }
        }

        private static string LayerName(int id)
        {
            foreach (var l in SortingLayer.layers) if (l.id == id) return l.name;
            return "?";
        }

        private static int LayerIndex(int id)
        {
            var layers = SortingLayer.layers;
            for (int i = 0; i < layers.Length; i++) if (layers[i].id == id) return i;
            return -1;
        }
    }
}
