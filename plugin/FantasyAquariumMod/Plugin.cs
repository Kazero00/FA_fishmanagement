using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace FantasyAquariumMod
{
    /// <summary>
    /// Fantasy Aquarium (Steam appid 3183350, Unity 2022.3.54f1 / Mono x64) 功能插件。
    ///
    /// 功能 1：全部鱼缸管理面板（右上角按钮 / F9）
    /// 功能 2：购买时若当前鱼缸装不下，自动把新鱼放入其它有空位的鱼缸
    ///
    /// ⚠️ 重要实测结论：本插件里**自己的 MonoBehaviour 回调（Update/OnGUI/LateUpdate）不会被调用**
    ///    —— 连 BepInEx 插件组件本身也一样（日志证明心跳从未出现），但 Harmony 补丁照常生效。
    ///    因此面板逻辑全部放在静态类 <see cref="FishManagerPanel"/> 里，由
    ///    `SaveLoadCntroller.Update` 的 Postfix 每帧驱动，不依赖任何自己的 MonoBehaviour 回调。
    /// </summary>
    [BepInPlugin(Guid, "Fantasy Aquarium Mod", "0.8.12")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "local.fantasyaquarium.mod";

        internal static ManualLogSource Log;

        // ---- 功能 2：跨缸分流 ----
        internal static ConfigEntry<bool> AutoRedirectToOtherTanks;

        // ---- 鱼类管理面板 ----
        internal static ConfigEntry<bool> ShowFishManagerButton;
        internal static ConfigEntry<KeyboardShortcut> ToggleKey;
        internal static ConfigEntry<int> ButtonX;
        internal static ConfigEntry<int> ButtonY;

        // ---- 共用 UI ----
        internal static ConfigEntry<bool> UseGameCanvas;

        internal static ConfigEntry<bool> GrowthCatchUp;
        internal static ConfigEntry<int> GrowthCatchUpIntervalSec;

        private float _nextBeat;
        private int _beats;

        private void Awake()
        {
            Log = Logger;

            GrowthCatchUp = Config.Bind(
                "Growth", "CatchUpInactiveTanks", true,
                "让非当前鱼缸里的鱼也按真实时间成长。\n" +
                "游戏在加载鱼缸时先 Instantiate(触发 OnEnable) 再 LoadFish，导致非当前缸的鱼\n" +
                "拿不到真实 falseTime、离线成长补算被跳过；之后该缸一直处于未激活状态，\n" +
                "除非玩家进去，否则那些鱼会永远停在存档里的成长值（常见现象：一堆 0%）。\n" +
                "开启后本 mod 会定期按游戏自己的公式做纯数据层补算：\n" +
                "    经验 += (现在 - falseTime) 的真实秒数；falseTime 前移；满则 FishToBig()。\n" +
                "全程不激活/不移动任何 GameObject，对渲染无影响。");
            GrowthCatchUpIntervalSec = Config.Bind(
                "Growth", "CatchUpIntervalSec", 45,
                "补算间隔（秒）。每次对所有非激活鱼缸里的鱼做一次数据层补算。");

            AutoRedirectToOtherTanks = Config.Bind(
                "MultiTank", "AutoRedirectToOtherTanks", true,
                "购买时如果当前鱼缸放不下，自动把新鱼放进其它有空位的鱼缸（同时解除购买限制）。");

            ShowFishManagerButton = Config.Bind(
                "FishManager", "ShowFishManagerButton", true,
                "是否显示“鱼类管理”入口按钮。");

            ToggleKey = Config.Bind(
                "FishManager", "ToggleKey", new KeyboardShortcut(KeyCode.F10),
                "打开/关闭鱼类管理面板的快捷键。");

            ButtonX = Config.Bind(
                "FishManager", "ButtonX", -1,
                "入口按钮横坐标，-1 表示贴右边缘。");

            ButtonY = Config.Bind(
                "FishManager", "ButtonY", 120,
                "入口按钮纵坐标（左上角为 0）。");

            UseGameCanvas = Config.Bind(
                "UI", "UseGameCanvas", false,
                "false = 插件自建画布（1920x1080 坐标空间，默认）；" +
                "true = 把 UI 挂进游戏自己的画布下。");

            var harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(Plugin).Assembly);

            var patched = harmony.GetPatchedMethods().ToList();
            Log.LogInfo($"[Harmony] 已打补丁 {patched.Count} 处：" +
                        string.Join(", ", patched.Select(m => (m.DeclaringType?.Name ?? "?") + "." + m.Name)));

            Log.LogInfo($"Fantasy Aquarium Mod v0.8.12 loaded. Unity {Application.unityVersion}, " +
                        $"unity={Application.persistentDataPath}");
        }

        // ------------------------------------------------------------------
        // 生命周期诊断：用来确认这个组件到底能不能收到 Unity 回调。
        // 若日志里只有 OnEnable/Start 而没有任何心跳，就说明 MonoBehaviour 链路不可用，
        // 一切逻辑都必须走 Harmony（当前架构就是这么做的）。
        // ------------------------------------------------------------------

        private void OnEnable()
        {
            if (Log != null) Log.LogInfo("[Plugin] OnEnable");
        }

        private void Start()
        {
            if (Log != null) Log.LogInfo("[Plugin] Start");
        }

        private void OnDisable()
        {
            if (Log != null) Log.LogWarning("[Plugin] OnDisable（组件被禁用了）");
        }

        private void OnDestroy()
        {
            if (Log != null) Log.LogWarning("[Plugin] OnDestroy（组件被销毁了）");
        }

        private void Update()
        {
            if (Time.unscaledTime >= _nextBeat && _beats < 6)
            {
                _nextBeat = Time.unscaledTime + 10f;
                _beats++;
                Log.LogInfo($"[Plugin] 看门狗心跳#{_beats} t={Time.unscaledTime:F0}s");
            }

            // 双保险：万一本组件其实有回调，也能驱动面板（Tick 内部按帧去重，不会重复触发）
            FishManagerPanel.Tick();
        }
    }
}
