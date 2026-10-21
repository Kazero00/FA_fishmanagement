using System;
using UnityEngine;

namespace FantasyAquariumMod
{
    /// <summary>
    /// 成长度保护。
    ///
    /// 背景（实测）：游戏里 `FishExperienceAdder.experienceAll` 只在组件 `Start()` / `OnEnable()` 里赋值，
    /// 而游戏在鱼刚 `Instantiate`、`fishData` 还是 null 时就会触发 `OnEnable` → 抛 NRE →
    /// 部分鱼的 `experienceAll` 会长期停留在 0。此时：
    ///   * `GetExperienceNow()`            会 `experienceNow = experienceAll`（=0）→ 清零
    ///   * `RecalculateExperienceNow()`    同样在 `num2 >= experienceAll(0)` 时把 `experienceNow` 写成 0
    /// 这两个方法分别在存档/激活鱼缸/对象销毁时被调用，于是成长度反复被清零并写进存档。
    ///
    /// 这里做的是"补回上限"：从 `fishData.fishInfo.experienceAll` 把值填回去，
    /// 使上面两个判断恢复正常语义，成长度就不会再被清零。
    /// </summary>
    internal static class GrowthGuard
    {
        private static int _fixed;
        private static int _swallowed;

        public static int FixedCount => _fixed;
        public static int SwallowedCount => _swallowed;

        /// <summary>取这条鱼的真实成长上限（来自 AllFishInfo 的 experienceAll）。</summary>
        public static int TrueLimit(FishExperienceAdder adder, out int fishId)
        {
            fishId = -1;
            if (adder == null) return 0;

            var fish = adder.GetComponent<Fish>();
            var data = fish != null ? fish.fishData : null;
            if (data == null) return 0;
            fishId = data.fishId;

            var info = data.fishInfo;
            if (info == null)
            {
                info = AllFishInfo.Instance.GetFishInfo(data.fishId);
                if (info != null) data.fishInfo = info;
            }
            return info != null ? info.experienceAll : 0;
        }

        public static int TrueLimit(FishExperienceAdder adder) => TrueLimit(adder, out _);

        /// <summary>
        /// 让 adder.experienceAll 与真实上限一致。
        ///
        /// 关键点：鱼的预制体里序列化了一份默认 FishData（fishInfo.experienceAll = 100），
        /// 所以组件在 LoadFish/Start 之前 experienceAll 是 100 而不是 0。一旦此时调用
        /// GetExperienceNow() 或 RecalculateExperienceNow()，就会把已经积累的成长度
        /// **夹成 100**（对上限几万的鱼来说约等于 0）。因此这里不只修正 0/负数，
        /// 而是"只要和真实上限不一致就修正"。
        /// </summary>
        public static void EnsureLimit(FishExperienceAdder adder)
        {
            if (adder == null) return;

            int trueLimit = TrueLimit(adder, out int fishId);
            if (trueLimit <= 0) return;
            if (adder.experienceAll == trueLimit) return;

            int wrong = adder.experienceAll;
            adder.experienceAll = trueLimit;

            _fixed++;
            if (_fixed <= 5)
            {
                Plugin.Log.LogInfo($"[Growth] 修正 experienceAll：{wrong} → {trueLimit}（鱼 #{fishId}），" +
                                   $"当前成长度 {adder.experienceNow} 保持不变");
            }
        }

        /// <summary>
        /// 纯数据层成长补算（**不激活/不移动任何 GameObject**，因此不影响渲染）。
        ///
        /// 游戏里鱼的成长是"真实时间驱动"的，但只在鱼所在鱼缸被激活时补算；
        /// 非当前鱼缸的鱼在加载时又因为 `Instantiate` 早于 `LoadFish` 而错过首次补算，
        /// 于是会长期停在存档里的值（大量 0%）。这里按游戏自己的公式
        ///     经验 += (现在 - falseTime) 的真实秒数，满则 FishToBig()
        /// 直接补给非激活鱼缸里的鱼，并把锚点 falseTime 前移，避免重复计算。
        ///
        /// 等价于游戏在"玩家进入该鱼缸"时做的一次性补算，只是提前且持续进行。
        /// </summary>
        /// <returns>是否真的补了经验</returns>
        public static bool CatchUp(FishExperienceAdder adder)
        {
            if (adder == null) return false;

            int cap = adder.experienceAll;
            if (cap <= 0) return false;
            if (adder.experienceNow >= cap) return false;

            string ft = adder.falseTime;
            if (string.IsNullOrEmpty(ft))
            {
                // 从未开始计时（新鱼）：把锚点设成现在，之后按真实时间累计
                adder.falseTime = DateTime.Now.ToString();
                return false;
            }

            DateTime anchor;
            if (!DateTime.TryParse(ft, out anchor)) { adder.falseTime = DateTime.Now.ToString(); return false; }

            double sec = (DateTime.Now - anchor).TotalSeconds;
            if (sec < 1.0) return false;

            long add = (long)sec;
            long target = adder.experienceNow + add;
            adder.experienceNow = target >= cap ? cap : (int)target;
            adder.falseTime = DateTime.Now.ToString();   // 锚点前移，绝不重复计入

            if (adder.experienceNow >= cap)
            {
                var fish = adder.GetComponent<Fish>();
                if (fish != null && !fish.GetIsBig())
                {
                    try { fish.FishToBig(); }
                    catch (Exception e) { NoteCatchUpError(e); }
                }
            }
            return true;
        }

        private static int _catchUpErr;

        private static void NoteCatchUpError(Exception e)
        {
            _catchUpErr++;
            if (_catchUpErr <= 3) Plugin.Log.LogWarning($"[Growth] 补算 FishToBig 时出错（已忽略）：{e.GetType().Name}: {e.Message}");
        }

        /// <summary>记录一次被吞掉的初始化 NRE（只在前几次打日志，避免刷屏）。</summary>
        public static void NoteSwallowed()
        {
            _swallowed++;
            if (_swallowed <= 3)
            {
                Plugin.Log.LogInfo("[Growth] 已保护 FishExperienceAdder 的初始化 NRE（游戏自身 bug），后续同类静默处理。");
            }
        }
    }
}
