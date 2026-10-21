using System.Collections.Generic;
using UnityEngine;

namespace FantasyAquariumMod
{
    /// <summary>
    /// 鱼缸容量计算与“新鱼落缸”路由。
    /// 游戏内语义（已从 Assembly-CSharp 反编译核对）：
    ///   TankData.tankValueNow = 当前鱼数量（占用），TankData.tankValueAll = 当前等级容量
    ///   Tank.CheckValue()     = 容量 - 占用
    ///
    /// 注意：tankValueNow 是缓存值，靠 FishList.ItemAdded 事件刷新；对于“刚解锁但从未激活过”
    /// 的鱼缸可能不准，所以这里统一用 allFishList.Count（实时）作为占用数。
    /// </summary>
    internal static class TankRouting
    {
        /// <summary>当前场景里所有已加载的鱼缸（顺序与存档 allTank 一致）。</summary>
        public static List<Tank> AllTanks()
        {
            var result = new List<Tank>();
            var ctrl = TankController.Instance;
            if (ctrl == null || ctrl.allTankList == null) return result;

            for (int i = 0; i < ctrl.allTankList.Count; i++)
            {
                var go = ctrl.allTankList[i];
                if (go == null) continue;
                var tank = go.GetComponent<Tank>();
                if (tank != null) result.Add(tank);
            }
            return result;
        }

        /// <summary>实时占用数（鱼的实际数量）。</summary>
        public static int UsedSlots(Tank tank)
        {
            if (tank == null) return 0;
            if (tank.tankFishController != null) return tank.tankFishController.GetFishCount();
            return tank.GetTankValueNow();
        }

        public static int FreeSlots(Tank tank)
        {
            if (tank == null) return 0;
            int free = tank.GetTankValueAll() - UsedSlots(tank);
            return free > 0 ? free : 0;
        }

        public static int TotalFreeSlots()
        {
            int sum = 0;
            foreach (var t in AllTanks()) sum += FreeSlots(t);
            return sum;
        }

        public static Tank FindOwner(GameObject fishGo)
        {
            if (fishGo == null) return null;
            foreach (var t in AllTanks())
            {
                var list = t.tankFishController != null ? t.tankFishController.allFishList : null;
                if (list != null && list.Contains(fishGo)) return t;
            }
            return null;
        }

        public static Tank FindTankWithFreeSlots(Tank exclude)
        {
            foreach (var t in AllTanks())
            {
                if (t == exclude) continue;
                if (FreeSlots(t) > 0) return t;
            }
            return null;
        }

        /// <summary>
        /// 新鱼落缸：优先当前鱼缸；当前缸满了就找第一个有空位的鱼缸。
        /// 由 FishCard.InitFishToTank 的 Transpiler 调用（替换其中的 TankController.CurrentTank）。
        /// </summary>
        public static GameObject PickTargetTank()
        {
            var current = TankController.CurrentTank;
            if (!Plugin.AutoRedirectToOtherTanks.Value) return current;

            var currentTank = current != null ? current.GetComponent<Tank>() : null;
            if (currentTank != null && FreeSlots(currentTank) > 0) return current;

            var target = FindTankWithFreeSlots(currentTank);
            if (target != null) return target.gameObject;

            // 所有缸都没有空位（购买前已检查总空位，正常不应发生）：保持原行为
            Plugin.Log.LogWarning("所有鱼缸都没有空位，新鱼仍放入当前鱼缸。");
            return current;
        }

        /// <summary>
        /// 卖鱼（销毁）之前的准备：把 <see cref="FishExperienceAdder.firstIn"/> 置为 true。
        ///
        /// 游戏在 GameObject 被销毁时会执行 `FishExperienceAdder.OnDestroy()` →
        /// `RecalculateExperienceNow()`，满成长时再调 `Fish.FishToBig()` → `SetSprite()`；
        /// 此时对象已在销毁中，`AssetBundle.LoadAssetWithSubAssets` 会抛 NullReferenceException
        /// （批量卖满成长鱼时刷屏），并且会把 experienceNow/isBig 写回 FishData。
        /// 置 firstIn = true 可让 OnDestroy 跳过这段重算 —— 游戏自己对新建鱼就是这么做的。
        /// </summary>
        public static void PrepareForDestroy(Fish fish)
        {
            if (fish == null) return;
            var adder = fish.experienceAdder;
            if (adder != null) adder.firstIn = true;
        }

        /// <summary>把一条已存在的鱼从一个鱼缸搬到另一个（等价于游戏内 TankTab 的换缸操作）。</summary>
        public static void MoveFish(GameObject fishGo, Tank from, Tank to)
        {
            if (fishGo == null || from == null || to == null || from == to) return;
            if (from.tankFishController == null || to.tankFishController == null) return;

            from.tankFishController.RemoveFish(fishGo);
            fishGo.transform.SetParent(to.tankFishController.transform);
            to.tankFishController.AddFish(fishGo);
        }

        /// <summary>某个鱼缸每天产出（用于总览面板）。</summary>
        public static double DailyIncome(Tank tank)
        {
            if (tank == null || tank.tankFishController == null) return 0.0;
            double sum = 0.0;
            var list = tank.tankFishController.allFishList;
            for (int i = 0; i < list.Count; i++)
            {
                var fish = list[i] != null ? list[i].GetComponent<Fish>() : null;
                var data = fish != null ? fish.fishData : null;
                if (data == null) continue;
                if (data.fishInfo != null) sum += data.fishInfo.moneyPerDay;
                else sum += AllFishInfo.Instance.GetFishMoneyPerDay(data.fishId);
            }
            return sum;
        }
    }
}
