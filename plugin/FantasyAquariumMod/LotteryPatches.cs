using System;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace FantasyAquariumMod
{
    /// <summary>
    /// 抽奖机（好感度投资幸运屋）"保留投资额"。
    ///
    /// 游戏原行为：`LikabilityButton.Settlement()` 在每次结算时把 `_count` 清零并隐藏计数图标，
    /// 所以每抽一次都要重新点 10 次 1000 票之类的操作。本功能：
    ///   1) 结算后把三个按钮的次数（和图标）恢复回去 → 下次打开机器直接沿用；
    ///   2) 把次数写入 BepInEx 配置 → 跨游戏重启也保留。
    ///
    /// 只改 UI 选择的"待投资额"，不做任何自动结算/自动抽奖；扣钱仍由玩家点"结算"触发。
    /// </summary>
    internal static class LotteryState
    {
        private const int MaxCount = 10;
        private static AccessTools.FieldRef<LikabilityButton, int> _countRef;
        private static bool _refFailed;
        private static readonly System.Collections.Generic.HashSet<int> _loggedRestore = new System.Collections.Generic.HashSet<int>();
        private static readonly System.Collections.Generic.HashSet<int> _loggedKeep = new System.Collections.Generic.HashSet<int>();
        private static int _errLogged;

        private static AccessTools.FieldRef<LikabilityButton, int> CountRef
        {
            get
            {
                if (_countRef != null || _refFailed) return _countRef;
                try
                {
                    _countRef = AccessTools.FieldRefAccess<LikabilityButton, int>("_count");
                }
                catch (Exception e)
                {
                    _refFailed = true;
                    if (Plugin.Log != null) Plugin.Log.LogWarning("[Lottery] 无法访问 LikabilityButton._count：" + e.Message);
                }
                return _countRef;
            }
        }

        public static int GetCount(LikabilityButton btn)
        {
            var r = CountRef;
            if (r == null || btn == null) return 0;
            try { return Mathf.Clamp(r(btn), 0, MaxCount); } catch { return 0; }
        }

        public static void SetCount(LikabilityButton btn, int n)
        {
            var r = CountRef;
            if (r == null || btn == null) return;
            try { r(btn) = Mathf.Clamp(n, 0, MaxCount); } catch { }
        }

        /// <summary>按次数显示/隐藏计数图标（游戏自己是用 DOTween 弹出，这里直接置为最终状态）。</summary>
        public static void ApplyVisual(LikabilityButton btn, int count)
        {
            try
            {
                var list = btn != null ? btn.countFishList : null;
                if (list == null) return;
                for (int i = 0; i < list.Count; i++)
                {
                    var go = list[i];
                    if (go == null) continue;
                    bool on = i < count;
                    if (go.activeSelf != on) go.SetActive(on);
                    if (on)
                    {
                        var rt = go.GetComponent<RectTransform>();
                        if (rt != null) rt.localScale = Vector3.one;
                    }
                }
            }
            catch { }
        }

        public static ConfigEntry<int> EntryFor(int buttonType)
        {
            switch (buttonType)
            {
                case 0: return Plugin.LotteryCount50;
                case 1: return Plugin.LotteryCount200;
                case 2: return Plugin.LotteryCount1000;
                default: return null;   // 未知按钮类型：不处理
            }
        }

        public static int Load(int buttonType)
        {
            var e = EntryFor(buttonType);
            return e != null ? Mathf.Clamp(e.Value, 0, MaxCount) : 0;
        }

        /// <summary>把按钮当前次数写入配置（只在值变化时写盘）。</summary>
        public static void Persist(LikabilityButton btn)
        {
            try
            {
                if (btn == null) return;
                var e = EntryFor(btn.buttonType);
                if (e == null) return;
                int c = GetCount(btn);
                if (e.Value == c) return;
                e.Value = c;
                if (Plugin.Cfg != null) Plugin.Cfg.Save();
                if (Plugin.Log != null) Plugin.Log.LogInfo($"[Lottery] 已保存投资额：按钮{btn.buttonType} ×{c}（合计 {TotalValue()}）");
            }
            catch (Exception e)
            {
                LogError(e);
            }
        }

        /// <summary>三个按钮的次数换算成金币总额（50/200/1000）。</summary>
        public static int TotalValue()
        {
            int v = 0;
            int c0 = Load(0), c1 = Load(1), c2 = Load(2);
            v += c0 * 50 + c1 * 200 + c2 * 1000;
            return v;
        }

        /// <summary>面板/机器首次构建时，把配置里的次数恢复到按钮上。</summary>
        public static void RestoreOnStart(LikabilityButton btn)
        {
            try
            {
                if (btn == null) return;
                int saved = Load(btn.buttonType);
                if (saved <= 0) return;
                SetCount(btn, saved);
                ApplyVisual(btn, saved);
                if (_loggedRestore.Add(btn.buttonType) && Plugin.Log != null)
                {
                    Plugin.Log.LogInfo($"[Lottery] 已恢复投资额：按钮{btn.buttonType} ×{saved}（合计 {TotalValue()} 金币）");
                }
            }
            catch (Exception e)
            {
                LogError(e);
            }
        }

        /// <summary>结算后把所有按钮的次数恢复（游戏已在 Settlement 里清零）。</summary>
        public static void KeepAfterSettlement(LikabilityButton btn, int count)
        {
            try
            {
                if (btn == null || count <= 0) return;
                SetCount(btn, count);
                ApplyVisual(btn, count);
                Persist(btn);
                if (_loggedKeep.Add(btn.buttonType) && Plugin.Log != null)
                {
                    Plugin.Log.LogInfo($"[Lottery] 已保留投资额：按钮{btn.buttonType} ×{count}（下次进入沿用）");
                }
            }
            catch (Exception e)
            {
                LogError(e);
            }
        }

        private static void LogError(Exception e)
        {
            _errLogged++;
            if (Plugin.Log != null && _errLogged <= 3)
                Plugin.Log.LogWarning($"[Lottery] 处理投资额时出错（已忽略）：{e.GetType().Name}: {e.Message}");
        }
    }

    /// <summary>结算时捕获次数并在结算后恢复。</summary>
    [HarmonyPatch(typeof(LikabilityButton), nameof(LikabilityButton.Settlement))]
    internal static class LotteryPatches_Settlement
    {
        private static void Prefix(LikabilityButton __instance, out int __state)
        {
            __state = LotteryState.GetCount(__instance);
        }

        private static void Postfix(LikabilityButton __instance, int __state)
        {
            try
            {
                if (Plugin.KeepLotteryInvestment == null || !Plugin.KeepLotteryInvestment.Value) return;
                LotteryState.KeepAfterSettlement(__instance, __state);
            }
            catch (Exception e)
            {
                if (Plugin.Log != null) Plugin.Log.LogWarning("[Lottery] Settlement 后处理失败：" + e.Message);
            }
        }
    }

    /// <summary>机器/面板重新打开（按钮 Start）时恢复上次的投资额。</summary>
    [HarmonyPatch(typeof(LikabilityButton), "Start")]
    internal static class LotteryPatches_Start
    {
        private static void Postfix(LikabilityButton __instance)
        {
            try
            {
                if (Plugin.KeepLotteryInvestment == null || !Plugin.KeepLotteryInvestment.Value) return;
                LotteryState.RestoreOnStart(__instance);
            }
            catch (Exception e)
            {
                if (Plugin.Log != null) Plugin.Log.LogWarning("[Lottery] Start 恢复失败：" + e.Message);
            }
        }
    }

    /// <summary>玩家点 +（加次数）后保存。</summary>
    [HarmonyPatch(typeof(LikabilityButton), nameof(LikabilityButton.OnClick))]
    internal static class LotteryPatches_OnClick
    {
        private static void Postfix(LikabilityButton __instance)
        {
            try
            {
                if (Plugin.KeepLotteryInvestment != null && Plugin.KeepLotteryInvestment.Value)
                    LotteryState.Persist(__instance);
            }
            catch { }
        }
    }

    /// <summary>玩家点 -（减次数）后保存。</summary>
    [HarmonyPatch(typeof(LikabilityButton), nameof(LikabilityButton.ReduceCount))]
    internal static class LotteryPatches_ReduceCount
    {
        private static void Postfix(LikabilityButton __instance)
        {
            try
            {
                if (Plugin.KeepLotteryInvestment != null && Plugin.KeepLotteryInvestment.Value)
                    LotteryState.Persist(__instance);
            }
            catch { }
        }
    }

    /// <summary>右键减少次数后保存。</summary>
    [HarmonyPatch(typeof(LikabilityButton), nameof(LikabilityButton.OnPointerClick))]
    internal static class LotteryPatches_OnPointerClick
    {
        private static void Postfix(LikabilityButton __instance)
        {
            try
            {
                if (Plugin.KeepLotteryInvestment != null && Plugin.KeepLotteryInvestment.Value)
                    LotteryState.Persist(__instance);
            }
            catch { }
        }
    }
}
