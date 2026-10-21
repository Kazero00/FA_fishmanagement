using System.Collections.Generic;
using UIFramework;
using UnityEngine;

namespace FantasyAquariumMod
{
    /// <summary>
    /// 把我们对“鱼”的改动同步给游戏原生的 UI。
    ///
    /// 为什么必须做：游戏的 `TankListController`（鱼缸列表面板）在 `Start()` 里**一次性**用
    /// `allFishList` 建好所有鱼条目（`_allFishTab`），之后游戏自己的代码每次改动鱼都会显式通知它：
    ///   * 卖鱼：`FishTab.OnClickSell` → `SellFish(...)` + `RemoveSoldFish(tab)`
    ///   * 换缸：`TankTab.OnClickBtn`  → `RemoveFish/AddFish` + `RemoveSoldFish(tab)`
    /// 我们的面板直接在数据层改动（可能作用于任意鱼缸），如果不同步，那个面板里的鱼条目就会和
    /// 真实数据错位（显示/操作到已不存在的鱼）。这里按同样的约定做同步。
    /// </summary>
    internal static class GameUiSync
    {
        /// <summary>在任何“卖鱼 / 换缸 / 移除”之后调用。</summary>
        public static void AfterFishChanged()
        {
            var go = GameObject.FindWithTag("TankList");
            if (go == null) return;                       // 面板没开就不用管

            var ctrl = go.GetComponent<TankListController>();
            if (ctrl == null || ctrl._allFishTab == null) return;

            var currentGo = TankController.CurrentTank;
            var currentTank = currentGo != null ? currentGo.GetComponent<Tank>() : null;
            if (currentTank == null) return;

            // 面板缓存的是另一个鱼缸 → 直接关掉，让它下次按当前缸重新建表
            // （currentTank 是私有字段，用公开的 tankFishController 做等价判断）
            if (currentTank.tankFishController == null ||
                ctrl.tankFishController != currentTank.tankFishController)
            {
                CloseIfTopmost(ctrl);
                Plugin.Log.LogInfo("[Fish] 游戏鱼缸列表面板对应的是别的鱼缸，已关闭以便重建。");
                return;
            }

            var list = currentTank.tankFishController != null
                ? currentTank.tankFishController.allFishList
                : null;
            if (list == null) return;

            var expected = new HashSet<GameObject>();
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null) expected.Add(list[i]);
            }

            int removed = 0;
            var kept = new HashSet<GameObject>();
            foreach (var tab in new List<GameObject>(ctrl._allFishTab))
            {
                if (tab == null) continue;
                var fishTab = tab.GetComponent<FishTab>();
                var fishGo = fishTab != null ? fishTab.fishGb : null;

                if (fishGo != null && expected.Contains(fishGo) && kept.Add(fishGo)) continue;

                // 与游戏自己的做法一致：把失效条目从面板里摘掉
                ctrl.RemoveSoldFish(tab);
                removed++;
            }

            if (removed > 0)
            {
                Plugin.Log.LogInfo($"[Fish] 同步游戏原生鱼缸列表：移除 {removed} 个已失效条目。");
            }

            int missing = expected.Count - kept.Count;
            if (missing > 0)
            {
                // 面板只在 Start() 里建表，没法增量插入新条目 → 关掉让它下次重建
                CloseIfTopmost(ctrl);
                Plugin.Log.LogInfo($"[Fish] 游戏鱼缸列表缺少 {missing} 个新条目，已关闭面板以便重建。");
            }
        }

        /// <summary>只有该面板位于面板栈顶时才关闭，避免误关别的界面。</summary>
        private static void CloseIfTopmost(TankListController ctrl)
        {
            var ui = UIManager.Instance;
            if (ui == null || ui.panelStack == null || ui.panelStack.Count == 0) return;
            if (!ReferenceEquals(ui.panelStack.Peek(), ctrl)) return;
            ui.PopPanel();
        }
    }
}
