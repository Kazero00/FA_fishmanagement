using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace FantasyAquariumMod
{
    /// <summary>
    /// 跨鱼缸的鱼类管理面板。
    ///
    /// 筛选（全部可叠加）：
    ///   鱼缸 / 普通·彩色 / 稀有度 / 体型 / 锁定状态 / 收益区间 / 售价区间 / 成长区间（后三者手动输入，可只填一边）
    /// 排序：稀有度 / 收益 / 售价 / 成长 / 鱼缸 / 编号
    /// 批量操作：
    ///   - 对**整个筛选结果**（跨页全部）：加锁 / 解锁 / 卖出
    ///   - 对**勾选的鱼**：加锁 / 解锁 / 卖出 / 换缸
    ///   - 单条：卖出 / 上锁 / 跳到所在缸
    ///
    /// 纯静态类 + Harmony 补丁驱动，零自定义 MonoBehaviour 回调（原因见 Plugin.cs 注释）。
    /// </summary>
    internal static class FishManagerPanel
    {
        private const int PageSize = 14;
        private const float RowHeight = 34f;
        private const float PanelWidth = 1500f;
        private const float PanelHeight = 866f;
        private const float ListX = 26f;
        private const float ListTop = 288f;
        private const float RowWidth = 1448f;

        private const int FontTitle = 26;
        private const int FontSummary = 17;
        private const int FontHeader = 15;
        private const int FontCell = 16;
        private const int FontButton = 15;
        private const int FontRowButton = 14;
        private const int FontHint = 14;
        private const int FontGroup = 14;

        private struct Col
        {
            public string Title;
            public float X;
            public float W;
            public TextAnchor Anchor;
        }

        // 表头与数据行共用这一份列定义 —— 保证标签和数值严格对齐
        private static readonly Col[] Columns =
        {
            new Col { Title = "",       X = 0f,     W = 34f,   Anchor = TextAnchor.MiddleCenter },
            new Col { Title = "名称",    X = 46f,    W = 360f,  Anchor = TextAnchor.MiddleLeft },
            new Col { Title = "稀有",    X = 416f,   W = 60f,   Anchor = TextAnchor.MiddleCenter },
            new Col { Title = "体型",    X = 486f,   W = 60f,   Anchor = TextAnchor.MiddleCenter },
            new Col { Title = "异色",    X = 556f,   W = 60f,   Anchor = TextAnchor.MiddleCenter },
            new Col { Title = "鱼缸",    X = 626f,   W = 60f,   Anchor = TextAnchor.MiddleCenter },
            new Col { Title = "收益/天",  X = 696f,   W = 120f,  Anchor = TextAnchor.MiddleRight },
            new Col { Title = "售价",    X = 826f,   W = 110f,  Anchor = TextAnchor.MiddleRight },
            new Col { Title = "成长",    X = 946f,   W = 90f,   Anchor = TextAnchor.MiddleRight },
        };

        private sealed class Entry
        {
            public GameObject go;
            public Fish fish;
            public Tank owner;
            public int tankNo;
            public int fishId;
            public int rarity;
            public bool isBig;
            public bool isColorful;
            public bool isLocked;
            public double perDay;
            public float sell;
            public float progress;
            public string displayName;
        }

        private static readonly List<Entry> _all = new List<Entry>();
        private static readonly List<Entry> _view = new List<Entry>();
        private static readonly HashSet<GameObject> _selected = new HashSet<GameObject>();

        private static GameObject _btnGo;
        private static GameObject _panel;
        private static GameObject _contentRoot;
        private static bool _visible;

        // ---- 筛选条件（全部可叠加）----
        private static int _filterTank = -1;      // -1 全部
        private static int _filterType;           // 0 全部 / 1 普通 / 2 彩色
        private static int _rarityMask;           // 位掩码：0 = 全部；bit 0..4 = 绿色/蓝色/金色/闪彩/闪彩★（可多选）
        private static int _filterSize;           // 0 全部 / 1 小 / 2 大
        private static int _filterLock;           // 0 全部 / 1 已锁 / 2 未锁
        private static int _filterGrow;           // 成长：0 全部 / 1 =100% / 2 <100%

        private static string _incMin = "", _incMax = "";     // 收益/天
        private static string _sellMin = "", _sellMax = "";   // 售价
        private static bool _incOn, _sellOn;
        private static double _incLo, _incHi, _sellLo, _sellHi;
        private static bool _rangeDirty;

        private static int _sortMode;
        private static int _page;
        private static bool _confirmSellFiltered;
        private static bool _confirmSellSelected;
        private static string _lastFilterSig = "";

        private static int _lastToggleFrame = -1;
        private static float _nextBeat;
        private static int _beats;

        // ------------------------------------------------------------------ 驱动

        public static void Tick()
        {
            UiRoot.EnsureBuilt();
            if (!UiRoot.IsReady) return;

            if (_btnGo == null) BuildEntryButton();
            PollToggleKey();
            GrowthCatchUpTick();

            if (_rangeDirty)
            {
                _rangeDirty = false;
                if (_visible && _panel != null) ApplyRanges();
            }

            if (_visible && _panel == null)
            {
                BuildPanel();
                if (_panel != null) _panel.SetActive(true);
                Rebuild();
            }

            if (Time.unscaledTime >= _nextBeat && _beats < 12)
            {
                _nextBeat = Time.unscaledTime + 5f;
                _beats++;
                Plugin.Log.LogInfo($"[Fish] Tick 心跳#{_beats} t={Time.unscaledTime:F0}s visible={_visible} " +
                                   $"entries={_all.Count} shown={_view.Count} selected={_selected.Count}");
            }
        }

        private static void PollToggleKey()
        {
            if (Time.frameCount == _lastToggleFrame) return;
            _lastToggleFrame = Time.frameCount;

            var key = Plugin.ToggleKey.Value;
            if (key.MainKey == KeyCode.None) return;
            if (key.IsDown()) SetVisible(!_visible);
        }

        private static void BuildEntryButton()
        {
            var root = UiRoot.Root;
            if (root == null) return;

            _btnGo = UiKit.NewNode("FishManagerButton", root);
            var rt = (RectTransform)_btnGo.transform;
            int x = Plugin.ButtonX.Value;
            int y = Plugin.ButtonY.Value;
            if (x < 0)
            {
                rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(1f, 1f);
                rt.anchoredPosition = new Vector2(-18f, -y);
            }
            else
            {
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(x, -y);
            }
            rt.sizeDelta = new Vector2(220f, 48f);

            var img = _btnGo.AddComponent<Image>();
            img.color = UiKit.AccentBtn;
            var btn = _btnGo.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => SetVisible(!_visible));

            var label = UiKit.Label("Label", _btnGo.transform,
                UiKit.T("鱼类管理 (F10)", "Fish Manager (F10)"), 19, TextAnchor.MiddleCenter, UiKit.TextColor);
            UiKit.Stretch(label.gameObject);

            _btnGo.SetActive(Plugin.ShowFishManagerButton.Value);
            Plugin.Log.LogInfo("[Fish] 入口按钮已创建。");
        }

        /// <summary>成长度自检：统计 experienceAll 无效 / 成长=0 的鱼，用于确认清零机制是否仍在发生。</summary>
        private static void GrowthAudit()
        {
            int total = 0, noLimit = 0, mismatch = 0, zero = 0, zeroWithTime = 0, tiny = 0, big = 0;
            string zeroSamples = "";
            foreach (var tank in TankRouting.AllTanks())
            {
                var list = tank.tankFishController != null ? tank.tankFishController.allFishList : null;
                if (list == null) continue;
                for (int i = 0; i < list.Count; i++)
                {
                    var go = list[i];
                    if (go == null) continue;
                    var f = go.GetComponent<Fish>();
                    if (f == null) continue;

                    var a = f.experienceAdder;
                    var d = f.fishData;
                    var info = d != null ? d.fishInfo : null;
                    if (info == null && d != null) info = AllFishInfo.Instance.GetFishInfo(d.fishId);
                    total++;
                    if (a == null || a.experienceAll <= 0) noLimit++;
                    int trueLimit = GrowthGuard.TrueLimit(a);
                    if (a != null && trueLimit > 0 && a.experienceAll != trueLimit) mismatch++;
                    if (f.GetIsBig()) big++;

                    int live = LiveExperience(f, d);
                    int cap = info != null ? info.experienceAll : 0;
                    if (live <= 0)
                    {
                        zero++;
                        if (d != null && !string.IsNullOrEmpty(d.falseTime)) zeroWithTime++;
                        if (zero <= 8 && d != null)
                            zeroSamples += $" 缸{tank.GetTankNo():00}#{d.fishId:03d}(上限{cap},falseTime={d.falseTime})";
                    }
                    else if (cap > 0 && live / (float)cap < 0.01f)
                    {
                        tiny++;
                    }
                }
            }
            Plugin.Log.LogInfo(
                $"[Fish] 成长自检：共 {total} 条｜experienceAll 无效 {noLimit} 条｜上限不一致 {mismatch} 条｜" +
                $"真零成长 {zero} 条（falseTime 非空 {zeroWithTime} 条）｜<1%（极小）{tiny} 条｜已成大只 {big} 条｜" +
                $"已补回上限 {GrowthGuard.FixedCount} 次，吞掉初始化异常 {GrowthGuard.SwallowedCount} 次");
            if (zero > 0) Plugin.Log.LogInfo($"[Fish] 真零样本：{zeroSamples}");
        }

        private static void SetVisible(bool visible)
        {
            _visible = visible;
            if (visible && _panel == null) BuildPanel();
            if (_panel != null) _panel.SetActive(visible);

            if (_btnGo != null)
            {
                var label = UiKit.LabelOf(_btnGo.transform);
                if (label != null)
                    label.text = visible
                        ? UiKit.T("隐藏鱼类管理 (F10)", "Hide Fish Manager (F10)")
                        : UiKit.T("鱼类管理 (F10)", "Fish Manager (F10)");
            }

            if (visible)
            {
                _confirmSellFiltered = false;
                _confirmSellSelected = false;
                GrowthAudit();
                Rebuild();
            }
            SingleMono<AudioController>.Instance.PlayAClip("Main_Button_Click");
            Plugin.Log.LogInfo($"[Fish] 面板 {(visible ? "打开" : "关闭")}。");
        }

        // ------------------------------------------------------------------ 骨架

        private static void BuildPanel()
        {
            var root = UiRoot.Root;
            if (root == null) return;

            _panel = UiKit.NewNode("FishManagerPanel", root);
            var rt = (RectTransform)_panel.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            rt.anchoredPosition = Vector2.zero;

            var bg = _panel.AddComponent<Image>();
            bg.color = UiKit.PanelBg;

            _panel.SetActive(false);
            Plugin.Log.LogInfo("[Fish] 面板骨架已创建。");
        }

        private static void Rebuild()
        {
            if (_panel == null) return;

            if (_contentRoot != null)
            {
                _contentRoot.SetActive(false);
                Object.Destroy(_contentRoot);
            }
            _contentRoot = UiKit.NewNode("Content", _panel.transform);
            UiKit.Stretch(_contentRoot);
            var p = _contentRoot.transform;

            // 筛选/分页/排序一变，就取消“二次确认”状态，避免在第一击之后换了条件又误触第二击
            string sig = $"{_filterTank}|{_filterType}|{_rarityMask}|{_filterSize}|{_filterLock}|{_filterGrow}|" +
                         $"{_incMin}|{_incMax}|{_sellMin}|{_sellMax}|{_page}|{_sortMode}";
            if (sig != _lastFilterSig)
            {
                _lastFilterSig = sig;
                _confirmSellFiltered = false;
                _confirmSellSelected = false;
            }

            Collect();
            ApplyFilterAndSort();

            int pages = Mathf.Max(1, Mathf.CeilToInt(_view.Count / (float)PageSize));
            if (_page >= pages) _page = pages - 1;
            if (_page < 0) _page = 0;

            BuildTitle(p);
            BuildTankFilter(p);
            BuildAttrFilter(p);
            BuildRangeRow(p);
            BuildSummary(p, pages);
            BuildSortRow(p);
            BuildHeaderRow(p);
            BuildList(p);
            BuildFooter(p, pages);
        }

        private static void Collect()
        {
            _all.Clear();
            foreach (var tank in TankRouting.AllTanks())
            {
                var list = tank.tankFishController != null ? tank.tankFishController.allFishList : null;
                if (list == null) continue;

                for (int i = 0; i < list.Count; i++)
                {
                    var go = list[i];
                    if (go == null) continue;
                    var fish = go.GetComponent<Fish>();
                    var data = fish != null ? fish.fishData : null;
                    if (data == null || data.fishInfo == null) continue;

                    var info = data.fishInfo;
                    _all.Add(new Entry
                    {
                        go = go,
                        fish = fish,
                        owner = tank,
                        tankNo = tank.GetTankNo(),
                        fishId = data.fishId,
                        rarity = info.rarity,
                        isBig = data.isBig,
                        isColorful = data.type == FishType.Colorful,
                        isLocked = fish.GetLock(),
                        perDay = info.moneyPerDay,
                        sell = fish.GetSellValue(),
                        // 成长度必须读“实时值”：FishData.experienceNow 只是偶尔同步的缓存，
                        // 用缓存会导致面板显示陈旧成长度，并且在卖鱼触发游戏同步后突然跳变（看起来像错位）。
                        progress = info.experienceAll > 0
                            ? Mathf.Clamp01(LiveExperience(fish, data) / (float)info.experienceAll)
                            : 0f,
                        displayName = $"#{data.fishId:D3} " + (UiKit.HasCjk ? info.zhName : info.enName),
                    });
                }
            }

            _selected.RemoveWhere(go => go == null);
        }

        /// <summary>
        /// 鱼的实时成长进度。
        ///
        /// ⚠️ 绝对不能调用 <c>FishExperienceAdder.GetExperienceNow()</c>：它内部有
        /// `if (experienceNow > experienceAll) experienceNow = experienceAll;`
        /// —— 当某条鱼所在鱼缸从未被激活过时，该组件 Start() 没跑、`experienceAll` 仍是 0，
        /// 这个"getter"就会把 experienceNow 直接清零（成长度 100% 变 0%）。
        /// 所以这里只读字段，并在 experienceAll 无效时退回 FishData 里保存的值。
        /// </summary>
        // ------------------------------------------------------------------ 非当前鱼缸的成长补算

        private static float _catchUpTimer;
        private static float _catchUpLogTimer;
        private static int _catchUpAppliedTotal;

        /// <summary>
        /// 每 CatchUpIntervalSec 给"非激活鱼缸"里的鱼补算真实时间成长。
        ///
        /// ⚠️ 这里**只用数据层计算**（直接改 FishExperienceAdder.experienceNow / falseTime），
        /// 绝不 SetActive / 移动鱼缸 GameObject —— v0.8.11 曾用"激活鱼缸一帧"的做法，
        /// 会连带触发鱼缸的水体/渲染组件，导致运行中出现渲染黑屏。本版已彻底移除该做法。
        /// </summary>
        private static void GrowthCatchUpTick()
        {
            if (Plugin.GrowthCatchUp == null || !Plugin.GrowthCatchUp.Value) return;

            float interval = Plugin.GrowthCatchUpIntervalSec != null
                ? Mathf.Max(10f, Plugin.GrowthCatchUpIntervalSec.Value)
                : 30f;

            _catchUpTimer += Time.deltaTime;
            if (_catchUpTimer < interval) return;
            _catchUpTimer = 0f;

            var tanks = TankRouting.AllTanks();
            if (tanks == null) return;

            int applied = 0;
            for (int ti = 0; ti < tanks.Count; ti++)
            {
                var tank = tanks[ti];
                if (tank == null) continue;
                var list = tank.tankFishController != null ? tank.tankFishController.allFishList : null;
                if (list == null) continue;

                // 正在激活的缸由游戏自己推进，跳过（避免和游戏自己的计时重复）
                if (tank.gameObject != null && tank.gameObject.activeInHierarchy) continue;

                for (int i = 0; i < list.Count; i++)
                {
                    var go = list[i];
                    if (go == null) continue;
                    var fish = go.GetComponent<Fish>();
                    if (fish == null || fish.experienceAdder == null) continue;
                    if (GrowthGuard.CatchUp(fish.experienceAdder)) applied++;
                }
            }

            if (applied > 0)
            {
                _catchUpAppliedTotal += applied;
                _catchUpLogTimer += interval;
                if (_catchUpLogTimer >= 300f)   // 最多每 5 分钟打一次，避免刷屏
                {
                    _catchUpLogTimer = 0f;
                    Plugin.Log.LogInfo($"[Growth] 成长补算：本次 {applied} 条鱼（累计 {_catchUpAppliedTotal}），按真实时间补给非激活鱼缸");
                }
            }
        }

        /// <summary>
        /// 成长列格式化：小数值保留足够精度，避免 "0.05%" 被 F0 取整成 "0%" 造成“成长度归零”的错觉。
        /// </summary>
        private static string FormatGrowth(Entry e)
        {
            if (e.progress >= 1f) return "100%";
            if (e.progress <= 0f) return "0%";
            float pct = e.progress * 100f;
            if (pct >= 10f) return $"{pct:F0}%";
            if (pct >= 1f) return $"{pct:F1}%";
            return $"{pct:F2}%";
        }

        private static int LiveExperience(Fish fish, FishData data)
        {
            var adder = fish != null ? fish.experienceAdder : null;
            if (adder != null && adder.experienceAll > 0) return adder.experienceNow;
            return data != null ? data.experienceNow : 0;
        }

        /// <summary>所有筛选条件在这里叠加。</summary>
        private static bool Pass(Entry e)
        {
            if (_filterTank >= 0 && e.tankNo != _filterTank) return false;
            if (_filterType == 1 && e.isColorful) return false;
            if (_filterType == 2 && !e.isColorful) return false;
            if (_rarityMask != 0 && (_rarityMask & (1 << e.rarity)) == 0) return false;
            if (_filterSize == 1 && e.isBig) return false;
            if (_filterSize == 2 && !e.isBig) return false;
            if (_filterLock == 1 && !e.isLocked) return false;
            if (_filterLock == 2 && e.isLocked) return false;
            if (_incOn && (e.perDay < _incLo || e.perDay > _incHi)) return false;
            if (_sellOn && (e.sell < _sellLo || e.sell > _sellHi)) return false;
            if (_filterGrow == 1 && e.progress < 1f) return false;
            if (_filterGrow == 2 && e.progress >= 1f) return false;
            return true;
        }

        private static void ApplyFilterAndSort()
        {
            _view.Clear();
            foreach (var e in _all)
            {
                if (Pass(e)) _view.Add(e);
            }

            // 全部排序都带“稳定决胜项”（鱼缸 → 编号 → 实例ID），
            // 否则 List.Sort（不稳定排序）在同稀有度/同收益的鱼之间每次重建都可能换顺序，
            // 表现就是“刚卖完那一行又变成另一条鱼”的错位感。
            switch (_sortMode)
            {
                case 0: _view.Sort(SortByRarity); break;
                case 1: _view.Sort(SortByIncome); break;
                case 2: _view.Sort(SortBySell); break;
                case 3: _view.Sort(SortByGrowth); break;
                case 4: _view.Sort(SortByTank); break;
                case 5: _view.Sort(SortByFishId); break;
            }
        }

        // ---- 排序比较器（都以保证唯一性的 TieBreak 收尾，保证顺序稳定可复现）----

        private static int TieBreak(Entry a, Entry b)
        {
            int c = a.tankNo.CompareTo(b.tankNo);
            if (c != 0) return c;
            c = a.fishId.CompareTo(b.fishId);
            if (c != 0) return c;

            int ia = a.go != null ? a.go.GetInstanceID() : 0;
            int ib = b.go != null ? b.go.GetInstanceID() : 0;
            return ia.CompareTo(ib);
        }

        private static int SortByRarity(Entry a, Entry b)
        {
            int c = b.rarity.CompareTo(a.rarity);
            if (c != 0) return c;
            c = b.perDay.CompareTo(a.perDay);
            return c != 0 ? c : TieBreak(a, b);
        }

        private static int SortByIncome(Entry a, Entry b)
        {
            int c = b.perDay.CompareTo(a.perDay);
            return c != 0 ? c : TieBreak(a, b);
        }

        private static int SortBySell(Entry a, Entry b)
        {
            int c = b.sell.CompareTo(a.sell);
            return c != 0 ? c : TieBreak(a, b);
        }

        private static int SortByGrowth(Entry a, Entry b)
        {
            int c = b.progress.CompareTo(a.progress);
            return c != 0 ? c : TieBreak(a, b);
        }

        private static int SortByTank(Entry a, Entry b)
        {
            int c = a.tankNo.CompareTo(b.tankNo);
            if (c != 0) return c;
            c = b.rarity.CompareTo(a.rarity);
            return c != 0 ? c : TieBreak(a, b);
        }

        private static int SortByFishId(Entry a, Entry b)
        {
            int c = a.fishId.CompareTo(b.fishId);
            return c != 0 ? c : TieBreak(a, b);
        }

        // ------------------------------------------------------------------ 数值区间（用户输入）

        private static bool TryParse(string text, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var s = text.Trim().Replace(',', '.');
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static bool ParseRange(string minText, string maxText, out double lo, out double hi)
        {
            lo = double.MinValue;
            hi = double.MaxValue;
            bool on = false;

            if (TryParse(minText, out var a)) { lo = a; on = true; }
            if (TryParse(maxText, out var b)) { hi = b; on = true; }
            if (on && lo > hi) { var t = lo; lo = hi; hi = t; }
            return on;
        }

        private static void ApplyRanges()
        {
            _incOn = ParseRange(_incMin, _incMax, out _incLo, out _incHi);
            _sellOn = ParseRange(_sellMin, _sellMax, out _sellLo, out _sellHi);

            _page = 0;
            Rebuild();
            Plugin.Log.LogInfo($"[Fish] 筛选应用：{ActiveFilterText()} → 命中 {_view.Count}/{_all.Count} 条。");
        }

        private static void ClearRanges()
        {
            _incMin = _incMax = _sellMin = _sellMax = "";
            _incOn = _sellOn = false;
            _incLo = _sellLo = double.MinValue;
            _incHi = _sellHi = double.MaxValue;
            _page = 0;
            Rebuild();
            Plugin.Log.LogInfo("[Fish] 数值区间已清除。");
        }

        private static string Fmt(double v) => v.ToString("0.##");

        private static string RangeText(bool on, double lo, double hi)
        {
            if (!on) return "-";
            bool hasLo = lo > double.MinValue;
            bool hasHi = hi < double.MaxValue;
            if (hasLo && hasHi) return $"{Fmt(lo)}~{Fmt(hi)}";
            if (hasLo) return $"\u2265{Fmt(lo)}";
            return $"\u2264{Fmt(hi)}";
        }

        /// <summary>把当前生效的筛选条件拼成一行文本（默认值不显示）。</summary>
        private static string ActiveFilterText()
        {
            var parts = new List<string>();
            if (_filterTank >= 0) parts.Add($"缸{_filterTank + 1:00}");
            if (_filterType == 1) parts.Add(UiKit.T("普通", "common"));
            if (_filterType == 2) parts.Add(UiKit.T("异色", "alt"));
            if (_rarityMask != 0)
            {
                string rs = "";
                for (int r = 0; r < 5; r++)
                {
                    if ((_rarityMask & (1 << r)) != 0) rs += UiKit.RarityName(r);
                }
                parts.Add(UiKit.T("稀有", "rarity") + rs);
            }
            if (_filterSize == 1) parts.Add(UiKit.T("小", "small"));
            if (_filterSize == 2) parts.Add(UiKit.T("大", "big"));
            if (_filterLock == 1) parts.Add(UiKit.T("已锁", "locked"));
            if (_filterLock == 2) parts.Add(UiKit.T("未锁", "unlocked"));
            if (_filterGrow == 1) parts.Add(UiKit.T("成年", "adult"));
            if (_filterGrow == 2) parts.Add(UiKit.T("幼年", "juvenile"));
            if (_incOn) parts.Add(UiKit.T("收益", "income") + RangeText(true, _incLo, _incHi));
            if (_sellOn) parts.Add(UiKit.T("售价", "sell") + RangeText(true, _sellLo, _sellHi));
            return parts.Count == 0 ? UiKit.T("无（全部鱼）", "none (all fish)") : string.Join(" + ", parts);
        }

        // ------------------------------------------------------------------ 各区块

        private static void BuildTitle(Transform p)
        {
            UiKit.Label("Title", p, UiKit.T("鱼类管理 · 跨鱼缸", "Fish Manager - All Tanks"),
                FontTitle, TextAnchor.MiddleLeft, UiKit.TextColor, 26f, 16f, 900f, 36f);

            UiKit.MakeButton("Close", p, UiKit.T("关闭", "Close"), PanelWidth - 116f, 16f, 90f, 36f,
                () => SetVisible(false), UiKit.DisabledBtn, FontButton);
        }

        private static void BuildTankFilter(Transform p)
        {
            var tanks = TankRouting.AllTanks();

            UiKit.MakeButton("TankAll", p, UiKit.T("全部鱼缸", "All tanks"), 26f, 58f, 108f, 32f,
                () => { _filterTank = -1; _page = 0; Rebuild(); },
                _filterTank < 0 ? UiKit.ActiveBtn : UiKit.PrimaryBtn, FontGroup);

            for (int i = 0; i < tanks.Count; i++)
            {
                int tankNo = tanks[i].GetTankNo();
                int free = TankRouting.FreeSlots(tanks[i]);
                string label = $"{tankNo + 1:00}" + (free > 0 ? $"({free})" : "");
                float x = 146f + i * 112f;

                UiKit.MakeButton("Tank" + tankNo, p, label, x, 58f, 106f, 32f,
                    () => { _filterTank = tankNo; _page = 0; Rebuild(); },
                    _filterTank == tankNo ? UiKit.ActiveBtn : UiKit.PrimaryBtn, FontGroup);
            }
        }

        /// <summary>离散属性筛选：类型 / 稀有度 / 体型 / 锁定状态（彼此可叠加）。</summary>
        private static void BuildAttrFilter(Transform p)
        {
            float y = 100f;

            // ---- 异色（FishType.Colorful，与稀有度正交）----
            UiKit.Label("LbType", p, UiKit.T("异色", "Alt"), FontGroup, TextAnchor.MiddleLeft,
                UiKit.DimColor, 26f, y + 2f, 52f, 30f);
            string[] types = { UiKit.T("全部", "All"), UiKit.T("普通", "Common"), UiKit.T("异色", "Alt") };
            for (int i = 0; i < types.Length; i++)
            {
                int captured = i;
                UiKit.MakeButton("Type" + i, p, types[i], 82f + i * 72f, y, 68f, 32f,
                    () => { _filterType = captured; _page = 0; Rebuild(); },
                    _filterType == i ? UiKit.ActiveBtn : UiKit.PrimaryBtn, FontGroup);
            }

            // ---- 稀有度：5 档可选（闪彩 rarity3 / 闪彩★ rarity4 分开）----
            UiKit.Label("LbRarity", p, UiKit.T("稀有度", "Rarity"), FontGroup, TextAnchor.MiddleLeft,
                UiKit.DimColor, 306f, y + 2f, 66f, 30f);
            UiKit.MakeButton("RarAll", p, UiKit.T("全部", "All"), 376f, y, 58f, 32f,
                () => { _rarityMask = 0; _page = 0; Rebuild(); },
                _rarityMask == 0 ? UiKit.ActiveBtn : UiKit.PrimaryBtn, FontGroup);
            for (int r = 0; r < 5; r++)
            {
                int captured = r;
                bool on = (_rarityMask & (1 << r)) != 0;
                UiKit.MakeButton("Rar" + r, p, UiKit.RarityName(r), 440f + r * 70f, y, r == 4 ? 78f : 66f, 32f,
                    () => { ToggleRarity(captured); },
                    on ? UiKit.ActiveBtn : UiKit.PrimaryBtn, FontGroup);
            }

            // ---- 体型 ----
            UiKit.Label("LbSize", p, UiKit.T("体型", "Size"), FontGroup, TextAnchor.MiddleLeft,
                UiKit.DimColor, 812f, y + 2f, 52f, 30f);
            string[] sizes = { UiKit.T("全部", "All"), UiKit.T("小", "Sml"), UiKit.T("大", "Big") };
            for (int i = 0; i < sizes.Length; i++)
            {
                int captured = i;
                UiKit.MakeButton("Size" + i, p, sizes[i], 868f + i * 72f, y, 68f, 32f,
                    () => { _filterSize = captured; _page = 0; Rebuild(); },
                    _filterSize == i ? UiKit.ActiveBtn : UiKit.PrimaryBtn, FontGroup);
            }

            // ---- 锁定状态 ----
            UiKit.Label("LbLock", p, UiKit.T("锁定", "Lock"), FontGroup, TextAnchor.MiddleLeft,
                UiKit.DimColor, 1094f, y + 2f, 52f, 30f);
            string[] locks = { UiKit.T("全部", "All"), UiKit.T("已锁", "Locked"), UiKit.T("未锁", "Free") };
            for (int i = 0; i < locks.Length; i++)
            {
                int captured = i;
                UiKit.MakeButton("Lock" + i, p, locks[i], 1150f + i * 72f, y, 68f, 32f,
                    () => { _filterLock = captured; _page = 0; Rebuild(); },
                    _filterLock == i ? UiKit.ActiveBtn : UiKit.PrimaryBtn, FontGroup);
            }
        }

        /// <summary>数值区间（收益/售价，可只填一边，回车即应用） + 成长三态按钮。</summary>
        private static void BuildRangeRow(Transform p)
        {
            float y = 142f;

            UiKit.Label("LbInc", p, UiKit.T("收益/天", "Income"), FontGroup, TextAnchor.MiddleLeft,
                UiKit.DimColor, 26f, y + 2f, 66f, 30f);
            UiKit.MakeInput("IncMin", p, _incMin, UiKit.T("最小", "min"), 94f, y, 116f, 32f, FontGroup,
                t => _incMin = t, t => _rangeDirty = true);
            UiKit.Label("TildeInc", p, "~", FontGroup, TextAnchor.MiddleCenter, UiKit.DimColor, 212f, y + 2f, 14f, 30f);
            UiKit.MakeInput("IncMax", p, _incMax, UiKit.T("最大", "max"), 228f, y, 116f, 32f, FontGroup,
                t => _incMax = t, t => _rangeDirty = true);

            UiKit.Label("LbSell", p, UiKit.T("售价", "Sell"), FontGroup, TextAnchor.MiddleLeft,
                UiKit.DimColor, 356f, y + 2f, 52f, 30f);
            UiKit.MakeInput("SellMin", p, _sellMin, UiKit.T("最小", "min"), 410f, y, 116f, 32f, FontGroup,
                t => _sellMin = t, t => _rangeDirty = true);
            UiKit.Label("TildeSell", p, "~", FontGroup, TextAnchor.MiddleCenter, UiKit.DimColor, 528f, y + 2f, 14f, 30f);
            UiKit.MakeInput("SellMax", p, _sellMax, UiKit.T("最大", "max"), 544f, y, 116f, 32f, FontGroup,
                t => _sellMax = t, t => _rangeDirty = true);

            // 成长：三态按钮（全部 / 正好 100% / 未满 100%）
            UiKit.Label("LbGrow", p, UiKit.T("成长", "Growth"), FontGroup, TextAnchor.MiddleLeft,
                UiKit.DimColor, 676f, y + 2f, 52f, 30f);

            string[] growLabels = { UiKit.T("全部", "All"), UiKit.T("成年", "Adult"), UiKit.T("幼年", "Juvenile") };
            float[] growX = { 732f, 810f, 888f };
            float[] growW = { 74f, 74f, 74f };
            for (int i = 0; i < growLabels.Length; i++)
            {
                int captured = i;
                UiKit.MakeButton("Grow" + i, p, growLabels[i], growX[i], y, growW[i], 32f,
                    () => { _filterGrow = captured; _page = 0; Rebuild(); },
                    _filterGrow == i ? UiKit.ActiveBtn : UiKit.PrimaryBtn, FontGroup);
            }

            // 延迟一帧执行，避免在输入框事件处理中销毁控件
            UiKit.MakeButton("RangeApply", p, UiKit.T("应用", "Apply"), 990f, y, 100f, 32f,
                () => { _rangeDirty = true; }, UiKit.PrimaryBtn, FontButton);
            UiKit.MakeButton("RangeClear", p, UiKit.T("清除", "Clear"), 1096f, y, 100f, 32f,
                ClearRanges, UiKit.DisabledBtn, FontButton);

            UiKit.Label("RangeNote", p, UiKit.T("留空 = 该方向不限；回车即应用", "empty = no bound; Enter applies"),
                FontHint, TextAnchor.MiddleLeft, UiKit.DimColor, 1206f, y + 2f, 280f, 30f);
        }

        private static void BuildSummary(Transform p, int pages)
        {
            int selected = 0;
            double selectedValue = 0;
            foreach (var e in _all)
            {
                if (e.go == null || !_selected.Contains(e.go)) continue;
                selected++;
                selectedValue += e.sell;
            }

            string text = string.Format(
                UiKit.T("筛选：{0}｜共 {1} 条 → 命中 {2} 条｜第 {3}/{4} 页｜已选 {5} 条（售价合计 {6:N1}）",
                        "Filter: {0} | {1} total -> {2} shown | page {3}/{4} | selected {5} ({6:N1})"),
                ActiveFilterText(), _all.Count, _view.Count, _page + 1, pages, selected, selectedValue);

            UiKit.Label("Summary", p, text, FontSummary, TextAnchor.MiddleLeft,
                UiKit.HeaderColor, 26f, 184f, PanelWidth - 52f, 28f);
        }

        private static void BuildSortRow(Transform p)
        {
            float y = 216f;

            UiKit.Label("LbSort", p, UiKit.T("排序", "Sort"), FontGroup, TextAnchor.MiddleLeft,
                UiKit.DimColor, 26f, y + 2f, 60f, 30f);

            string[] sortLabels =
            {
                UiKit.T("稀有度↓", "Rarity↓"), UiKit.T("收益↓", "Income↓"), UiKit.T("售价↓", "Sell↓"),
                UiKit.T("成长↓", "Growth↓"), UiKit.T("按鱼缸", "By tank"), UiKit.T("按编号", "By id"),
            };
            for (int i = 0; i < sortLabels.Length; i++)
            {
                int captured = i;
                UiKit.MakeButton("Sort" + i, p, sortLabels[i], 90f + i * 140f, y, 132f, 32f,
                    () => { _sortMode = captured; _page = 0; Rebuild(); },
                    _sortMode == i ? UiKit.ActiveBtn : UiKit.PrimaryBtn, FontButton);
            }
        }

        private static void BuildHeaderRow(Transform p)
        {
            for (int i = 0; i < Columns.Length; i++)
            {
                var c = Columns[i];
                UiKit.Label("Head" + i, p, c.Title, FontHeader, c.Anchor, UiKit.DimColor,
                    ListX + c.X, 256f, c.W, 26f);
            }
        }

        private static void BuildList(Transform p)
        {
            if (_view.Count == 0)
            {
                UiKit.Label("Empty", p, UiKit.T("当前筛选下没有鱼。", "No fish match the filter."),
                    18, TextAnchor.MiddleCenter, UiKit.DimColor, ListX, ListTop + 60f, RowWidth, 40f);
                return;
            }

            int start = _page * PageSize;
            for (int i = 0; i < PageSize; i++)
            {
                int index = start + i;
                if (index >= _view.Count) break;
                BuildRow(p, _view[index], i);
            }
        }

        private static void BuildRow(Transform p, Entry e, int rowIndex)
        {
            var row = UiKit.NewNode("Row" + rowIndex, p);
            UiKit.Place(row, ListX, ListTop + rowIndex * RowHeight, RowWidth, RowHeight - 2f);

            if (rowIndex % 2 == 1)
            {
                var stripe = row.AddComponent<Image>();
                stripe.color = UiKit.RowStripe;
                stripe.raycastTarget = false;
            }

            bool selected = _selected.Contains(e.go);

            UiKit.MakeButton("Select", row.transform, selected ? "✓" : "", Columns[0].X + 2f, 5f, 30f, 22f,
                () => ToggleSelect(e), selected ? UiKit.AccentBtn : UiKit.DisabledBtn, FontRowButton);

            DrawCell(row, 1, e.displayName, UiKit.Rarity(e.rarity));
            DrawCell(row, 2, UiKit.RarityName(e.rarity), UiKit.Rarity(e.rarity));
            DrawCell(row, 3, e.isBig ? UiKit.T("大", "Big") : UiKit.T("小", "Sml"), UiKit.TextColor);
            DrawCell(row, 4, e.isColorful ? UiKit.T("异色", "Alt") : "-",
                e.isColorful ? UiKit.ActiveBtn : UiKit.DimColor);
            DrawCell(row, 5, $"{e.tankNo + 1:00}", UiKit.TextColor);
            DrawCell(row, 6, e.perDay.ToString("N1"), UiKit.TextColor);
            DrawCell(row, 7, e.sell.ToString("N1"), UiKit.HeaderColor);
            DrawCell(row, 8, FormatGrowth(e), UiKit.TextColor);

            if (e.isLocked)
            {
                UiKit.MakeButton("Sell", row.transform, UiKit.T("锁定中", "Locked"), 1050f, 5f, 80f, 22f,
                    () => { }, UiKit.DisabledBtn, FontRowButton);
            }
            else
            {
                UiKit.MakeButton("Sell", row.transform, UiKit.T("卖出", "Sell"), 1050f, 5f, 80f, 22f,
                    () => SellOne(e), UiKit.DangerBtn, FontRowButton);
            }

            UiKit.MakeButton("Lock", row.transform, e.isLocked ? UiKit.T("解锁", "Unlock") : UiKit.T("锁定", "Lock"),
                1140f, 5f, 80f, 22f, () => ToggleLock(e), UiKit.PrimaryBtn, FontRowButton);

            UiKit.MakeButton("Focus", row.transform, UiKit.T("所在缸", "Goto"), 1230f, 5f, 90f, 22f,
                () => GotoTank(e), UiKit.PrimaryBtn, FontRowButton);
        }

        private static void DrawCell(GameObject row, int colIndex, string text, Color color)
        {
            var c = Columns[colIndex];
            UiKit.Label("Col" + colIndex, row.transform, text, FontCell, c.Anchor, color, c.X, 0f, c.W, RowHeight - 2f);
        }

        private static void BuildFooter(Transform p, int pages)
        {
            int selected = _selected.Count;
            int shown = _view.Count;

            // ---- 第 1 行：分页 + 对「整个筛选结果」的批量操作 ----
            float y1 = 776f;
            UiKit.MakeButton("Prev", p, UiKit.T("上一页", "Prev"), 26f, y1, 100f, 34f,
                () => { _page--; Rebuild(); }, UiKit.PrimaryBtn, FontButton);
            UiKit.Label("PageInfo", p, $"{_page + 1}/{pages}", FontSummary, TextAnchor.MiddleCenter,
                UiKit.TextColor, 132f, y1, 80f, 34f);
            UiKit.MakeButton("Next", p, UiKit.T("下一页", "Next"), 218f, y1, 100f, 34f,
                () => { _page++; Rebuild(); }, UiKit.PrimaryBtn, FontButton);

            UiKit.MakeButton("SelectPage", p, UiKit.T("全选本页", "Select page"), 334f, y1, 130f, 34f,
                SelectPage, UiKit.DisabledBtn, FontButton);
            UiKit.MakeButton("ClearSel", p, UiKit.T("取消选择", "Clear sel"), 470f, y1, 120f, 34f,
                () => { _selected.Clear(); _confirmSellSelected = false; Rebuild(); }, UiKit.DisabledBtn, FontButton);

            var lockFilter = UiKit.MakeButton("LockFilter", p,
                UiKit.T($"加锁筛选 ({shown})", $"Lock shown ({shown})"),
                606f, y1, 200f, 34f, () => SetLockForFiltered(true), UiKit.PrimaryBtn, FontButton);

            var unlockFilter = UiKit.MakeButton("UnlockFilter", p,
                UiKit.T($"解锁筛选 ({shown})", $"Unlock shown ({shown})"),
                812f, y1, 200f, 34f, () => SetLockForFiltered(false), UiKit.AccentBtn, FontButton);

            var sellFilter = UiKit.MakeButton("SellFilter", p,
                _confirmSellFiltered
                    ? UiKit.T($"再点一次卖出 {shown} 条", $"Confirm sell {shown}")
                    : UiKit.T($"卖出筛选 ({shown})", $"Sell shown ({shown})"),
                1018f, y1, 200f, 34f, SellFiltered,
                shown > 0 ? UiKit.DangerBtn : UiKit.DisabledBtn, FontButton);

            if (shown == 0)
            {
                lockFilter.interactable = false;
                unlockFilter.interactable = false;
                sellFilter.interactable = false;
            }

            bool auto = Plugin.AutoRedirectToOtherTanks.Value;
            UiKit.MakeButton("AutoToggle", p,
                auto ? UiKit.T("跨缸分流：开", "Auto-route: ON") : UiKit.T("跨缸分流：关", "Auto-route: OFF"),
                1224f, y1, 180f, 34f, ToggleAutoRedirect,
                auto ? UiKit.AccentBtn : UiKit.DisabledBtn, FontButton);

            UiKit.MakeButton("Refresh", p, UiKit.T("刷新", "Refresh"), 1410f, y1, 84f, 34f,
                Rebuild, UiKit.DisabledBtn, FontButton);

            // ---- 第 2 行：对「勾选的鱼」的批量操作 ----
            float y2 = 820f;
            UiKit.Label("SelHint", p, UiKit.T("选中操作：", "Selected:"), FontCell, TextAnchor.MiddleLeft,
                UiKit.TextColor, 26f, y2, 100f, 32f);

            var selLock = UiKit.MakeButton("LockSel", p, UiKit.T($"加锁选中 ({selected})", $"Lock sel ({selected})"),
                128f, y2, 160f, 32f, () => SetLockForSelection(true), UiKit.PrimaryBtn, FontButton);

            var selUnlock = UiKit.MakeButton("UnlockSel", p, UiKit.T($"解锁选中 ({selected})", $"Unlock sel ({selected})"),
                294f, y2, 160f, 32f, () => SetLockForSelection(false), UiKit.AccentBtn, FontButton);

            var selSell = UiKit.MakeButton("SellSel", p,
                _confirmSellSelected
                    ? UiKit.T($"再点一次卖出 {selected} 条", $"Confirm sell {selected}")
                    : UiKit.T($"卖出选中 ({selected})", $"Sell sel ({selected})"),
                460f, y2, 160f, 32f, SellSelected,
                selected > 0 ? UiKit.DangerBtn : UiKit.DisabledBtn, FontButton);

            if (selected == 0)
            {
                selLock.interactable = false;
                selUnlock.interactable = false;
                selSell.interactable = false;
            }

            UiKit.Label("MoveHint", p, UiKit.T("换缸到：", "Move to:"), FontCell, TextAnchor.MiddleLeft,
                UiKit.TextColor, 632f, y2, 80f, 32f);

            var tanks = TankRouting.AllTanks();
            for (int i = 0; i < tanks.Count; i++)
            {
                int tankNo = tanks[i].GetTankNo();
                var btn = UiKit.MakeButton("MoveTo" + tankNo, p, $"{tankNo + 1:00}",
                    716f + i * 72f, y2, 66f, 32f, () => MoveSelectedTo(tankNo), UiKit.AccentBtn, FontButton);
                if (selected == 0) btn.interactable = false;
            }
        }

        // ------------------------------------------------------------------ 操作

        private static void ToggleAutoRedirect()
        {
            Plugin.AutoRedirectToOtherTanks.Value = !Plugin.AutoRedirectToOtherTanks.Value;
            SingleMono<AudioController>.Instance.PlayAClip("Main_Button_Click");
            Plugin.Log.LogInfo($"[Fish] 跨缸分流 = {Plugin.AutoRedirectToOtherTanks.Value}");
            Rebuild();
        }

        /// <summary>稀有度多选：点一下切换选中状态。</summary>
        private static void ToggleRarity(int rarity)
        {
            _rarityMask ^= 1 << rarity;
            _page = 0;
            Rebuild();
        }

        private static void ToggleSelect(Entry e)
        {
            if (e.go == null) return;
            if (!_selected.Remove(e.go)) _selected.Add(e.go);
            _confirmSellSelected = false;
            Rebuild();
        }

        private static void SelectPage()
        {
            int start = _page * PageSize;
            for (int i = 0; i < PageSize && start + i < _view.Count; i++)
            {
                var e = _view[start + i];
                if (e.go != null) _selected.Add(e.go);
            }
            Rebuild();
        }

        private static void ToggleLock(Entry e)
        {
            if (e.fish == null) return;
            e.fish.SetLock(!e.fish.GetLock());
            SingleMono<AudioController>.Instance.PlayAClip("Main_Button_Click");
            Rebuild();
        }

        /// <summary>对“整个筛选结果”（跨页全部命中）批量加锁/解锁。</summary>
        private static void SetLockForFiltered(bool locked)
        {
            int changed = 0, already = 0;
            foreach (var e in _view)
            {
                if (e.fish == null || e.go == null) continue;
                if (e.fish.GetLock() == locked) { already++; continue; }
                e.fish.SetLock(locked);
                changed++;
            }

            SingleMono<AudioController>.Instance.PlayAClip("Main_Button_Click");
            Plugin.Log.LogInfo($"[Fish] 筛选{(locked ? "加锁" : "解锁")}：{ActiveFilterText()}，" +
                               $"改动 {changed} 条（已是目标状态 {already} 条）。");
            Rebuild();
        }

        /// <summary>对“勾选的鱼”批量加锁/解锁。</summary>
        private static void SetLockForSelection(bool locked)
        {
            if (_selected.Count == 0) return;

            int changed = 0, already = 0;
            foreach (var e in _all)
            {
                if (e.go == null || !_selected.Contains(e.go)) continue;
                if (e.fish == null) continue;
                if (e.fish.GetLock() == locked) { already++; continue; }
                e.fish.SetLock(locked);
                changed++;
            }

            SingleMono<AudioController>.Instance.PlayAClip("Main_Button_Click");
            Plugin.Log.LogInfo($"[Fish] 选中{(locked ? "加锁" : "解锁")}：改动 {changed} 条" +
                               $"（已是目标状态 {already} 条，选中 {_selected.Count} 条）。");
            Rebuild();
        }

        private static void SellOne(Entry e)
        {
            if (e.fish == null || e.go == null || e.owner == null) return;
            if (e.fish.GetLock()) return;

            float value = e.fish.GetSellValue();
            TankRouting.PrepareForDestroy(e.fish);
            e.owner.tankFishController.SellFish(e.go);
            _selected.Remove(e.go);
            _confirmSellSelected = false;

            SingleMono<AudioController>.Instance.PlayAClip("Main_Button_Click");
            Plugin.Log.LogInfo($"[Fish] 卖出 1 条 {e.displayName}（缸{e.tankNo + 1:00}），+{value:N1}");
            GameUiSync.AfterFishChanged();
            Rebuild();
        }

        /// <summary>卖出“整个筛选结果”（跨页），两次确认。</summary>
        private static void SellFiltered()
        {
            if (_view.Count == 0) return;

            if (!_confirmSellFiltered)
            {
                _confirmSellFiltered = true;
                Rebuild();
                return;
            }
            _confirmSellFiltered = false;

            int sold = 0, locked = 0;
            double earned = 0;
            foreach (var e in new List<Entry>(_view))
            {
                if (e.go == null || e.fish == null || e.owner == null) continue;
                if (e.fish.GetLock()) { locked++; continue; }

                earned += e.fish.GetSellValue();
                TankRouting.PrepareForDestroy(e.fish);
                e.owner.tankFishController.SellFish(e.go);
                _selected.Remove(e.go);
                sold++;
            }

            SingleMono<AudioController>.Instance.PlayAClip("Main_Button_Click");
            Plugin.Log.LogInfo($"[Fish] 卖出筛选（{ActiveFilterText()}）：{sold} 条，共 +{earned:N1}" +
                               $"（跳过锁定 {locked} 条）。");
            GameUiSync.AfterFishChanged();
            Rebuild();
        }

        /// <summary>卖出“勾选的鱼”，两次确认。</summary>
        private static void SellSelected()
        {
            if (_selected.Count == 0) return;

            if (!_confirmSellSelected)
            {
                _confirmSellSelected = true;
                Rebuild();
                return;
            }
            _confirmSellSelected = false;

            int sold = 0, locked = 0;
            double earned = 0;
            foreach (var e in _all)
            {
                if (e.go == null || !_selected.Contains(e.go)) continue;
                if (e.fish == null || e.owner == null) continue;
                if (e.fish.GetLock()) { locked++; continue; }

                earned += e.fish.GetSellValue();
                TankRouting.PrepareForDestroy(e.fish);
                e.owner.tankFishController.SellFish(e.go);
                sold++;
            }

            _selected.Clear();
            SingleMono<AudioController>.Instance.PlayAClip("Main_Button_Click");
            Plugin.Log.LogInfo($"[Fish] 卖出选中 {sold} 条，共 +{earned:N1}（跳过锁定 {locked} 条）。");
            GameUiSync.AfterFishChanged();
            Rebuild();
        }

        private static void MoveSelectedTo(int tankNo)
        {
            if (_selected.Count == 0) return;

            Tank target = null;
            foreach (var t in TankRouting.AllTanks())
            {
                if (t.GetTankNo() == tankNo) { target = t; break; }
            }
            if (target == null) return;

            int free = TankRouting.FreeSlots(target);
            int moved = 0, noSpace = 0, same = 0;

            foreach (var e in new List<Entry>(_all))
            {
                if (e.go == null || !_selected.Contains(e.go)) continue;
                if (e.owner == null) continue;
                if (e.owner == target) { same++; continue; }
                if (free <= 0) { noSpace++; continue; }

                TankRouting.MoveFish(e.go, e.owner, target);
                free--;
                moved++;
            }

            _selected.Clear();
            SingleMono<AudioController>.Instance.PlayAClip("Main_Button_Click");
            Plugin.Log.LogInfo(
                $"[Fish] 移动到鱼缸 {tankNo + 1:00}：成功 {moved} 条" +
                (same > 0 ? $"，{same} 条本来就在该缸" : "") +
                (noSpace > 0 ? $"，{noSpace} 条因目标缸没空位跳过" : "") + "。");
            GameUiSync.AfterFishChanged();
            Rebuild();
        }

        private static void GotoTank(Entry e)
        {
            if (e.owner == null) return;
            SingleMono<AudioController>.Instance.PlayAClip("Main_Button_Click");
            TankController.ChangeCurrentTank(e.owner.gameObject);
            Plugin.Log.LogInfo($"[Fish] 切换到鱼缸 {e.tankNo + 1:00} {e.owner.GetTankName()}");
        }
    }
}
