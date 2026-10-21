using HarmonyLib;
using UnityEngine;

namespace FantasyAquariumMod
{
    /// <summary>诊断类补丁 + 面板的每帧驱动源。</summary>
    internal static class Patches
    {
        /// <summary>
        /// 面板的每帧驱动源。
        ///
        /// 为什么挂这里：实测本插件自己的 MonoBehaviour **收不到 Unity 回调**（Update/OnGUI 都不执行），
        /// 但 Harmony 补丁一定生效。`SaveLoadCntroller.Update()` 是场景里一直存在的对象、
        /// 每帧都会执行（它自己用来计时自动存档），所以最合适做“心跳源”。
        /// </summary>
        [HarmonyPatch(typeof(SaveLoadCntroller), "Update")]
        private static class SaveLoadCntroller_Update
        {
            private static void Postfix()
            {
                FishManagerPanel.Tick();
            }
        }

        /// <summary>
        /// 修复游戏自身的 bug（第二条、也是持续归零的那条）：
        /// `FishExperienceAdder.RecalculateExperienceNow()` 里有
        ///     if (num2 >= experienceAll) { experienceNow = experienceAll; ... }
        /// 当 `experienceAll == 0`（组件 Start()/OnEnable() 没成功初始化 —— 游戏在鱼刚
        /// Instantiate、fishData 还是 null 时 OnEnable 会抛 NRE）时，这个判断恒成立，
        /// 于是**每次调用都把 experienceNow 写成 0**，即成长度清零。
        /// 而该方法会在每次鱼缸被激活（游戏每 10 分钟会激活全部鱼缸一次）和对象销毁时调用，
        /// 加上游戏退出时存档，0 就被写进了存档。
        ///
        /// 处理：
        ///   1) 进入前先把 experienceAll 从 fishData.fishInfo 补回来；
        ///   2) 若仍然无效，直接跳过（绝不写 0）；
        ///   3) 顺带把 Start/OnEnable 的 NRE 吞掉并补初始化，避免 experienceAll 永远为 0。
        /// </summary>
        [HarmonyPatch(typeof(FishExperienceAdder), nameof(FishExperienceAdder.RecalculateExperienceNow))]
        private static class FishExperienceAdder_RecalculateExperienceNow
        {
            private static bool Prefix(FishExperienceAdder __instance)
            {
                GrowthGuard.EnsureLimit(__instance);
                return __instance.experienceAll > 0;
            }

            private static System.Exception Finalizer(System.Exception __exception)
            {
                if (__exception is System.NullReferenceException)
                {
                    GrowthGuard.NoteSwallowed();
                    return null;
                }
                return __exception;
            }
        }

        /// <summary>吞掉该组件 Start()/OnEnable() 的初始化 NRE（游戏自身 bug），并补上 experienceAll。</summary>
        [HarmonyPatch(typeof(FishExperienceAdder), "Start")]
        private static class FishExperienceAdder_Start
        {
            // Prefix 在方法体之前跑：先把 experienceAll 修正成真实上限，
            // 这样 OnEnable 里随后的 RecalculateExperienceNow() 才不会用默认值 100 去夹成长度
            private static void Prefix(FishExperienceAdder __instance)
            {
                GrowthGuard.EnsureLimit(__instance);
            }

            private static System.Exception Finalizer(FishExperienceAdder __instance, System.Exception __exception)
            {
                GrowthGuard.EnsureLimit(__instance);
                if (__exception is System.NullReferenceException)
                {
                    GrowthGuard.NoteSwallowed();
                    return null;
                }
                return __exception;
            }
        }

        [HarmonyPatch(typeof(FishExperienceAdder), "OnEnable")]
        private static class FishExperienceAdder_OnEnable
        {
            // Prefix 在方法体之前跑：先把 experienceAll 修正成真实上限，
            // 这样 OnEnable 里随后的 RecalculateExperienceNow() 才不会用默认值 100 去夹成长度
            private static void Prefix(FishExperienceAdder __instance)
            {
                GrowthGuard.EnsureLimit(__instance);
            }

            private static System.Exception Finalizer(FishExperienceAdder __instance, System.Exception __exception)
            {
                GrowthGuard.EnsureLimit(__instance);
                if (__exception is System.NullReferenceException)
                {
                    GrowthGuard.NoteSwallowed();
                    return null;
                }
                return __exception;
            }
        }

        /// <summary>
        /// 修复游戏自身的 bug：`FishExperienceAdder.GetExperienceNow()` 名义上是 getter，
        /// 内部却有 `if (experienceNow > experienceAll) experienceNow = experienceAll;` 的写入。
        /// 对“所在鱼缸从未被激活过”的鱼（组件 Start() 未跑 → experienceAll == 0）来说，
        /// 这会把它的 experienceNow 直接清零（成长度 100% → 0%）。
        /// 而游戏的存档流程 TankManager.Save → Tank.UpdateTankData → TankFishController.UpdateAllFishData
        /// → Fish.GetFishData → GetExperienceNow 会遍历所有鱼缸，因此连游戏自己的自动存档
        /// 都会悄悄吃掉这些鱼的成长度。
        /// 这里改成纯读取：仅在 experienceAll 有效时做正常的封顶，绝不写入。
        /// </summary>
        [HarmonyPatch(typeof(FishExperienceAdder), nameof(FishExperienceAdder.GetExperienceNow))]
        private static class FishExperienceAdder_GetExperienceNow
        {
            private static bool Prefix(FishExperienceAdder __instance, ref int __result)
            {
                int now = __instance.experienceNow;

                // 用“真实上限”封顶：直接用 __instance.experienceAll 会把值夹到预制体默认的 100
                int cap = GrowthGuard.TrueLimit(__instance);
                if (cap <= 0) cap = __instance.experienceAll;

                __result = cap > 0 && now > cap ? cap : now;
                return false;   // 完全接管，杜绝副作用
            }
        }

        /// <summary>17 个静态资源包（allfish / static / fishcard ...）全部加载完成后触发。</summary>
        [HarmonyPatch(typeof(LoadStaticAB), "OnAllBundlesLoaded")]
        private static class LoadStaticAB_OnAllBundlesLoaded
        {
            private static void Postfix()
            {
                Plugin.Log.LogInfo($"[Hook] 资源包全部就绪，共 {LoadStaticAB.AssetBundles.Count} 个 bundle。");
            }
        }

        /// <summary>所有鱼缸加载完成后打印一次，便于核对分缸结果。</summary>
        [HarmonyPatch(typeof(TankController), "LoadAllTank")]
        private static class TankController_LoadAllTank
        {
            private static void Postfix()
            {
                var tanks = TankRouting.AllTanks();
                for (int i = 0; i < tanks.Count; i++)
                {
                    var t = tanks[i];
                    Plugin.Log.LogInfo($"[Hook] 鱼缸 {t.GetTankNo():00} {t.GetTankName()} " +
                                       $"占用 {t.GetTankValueNow()}/{t.GetTankValueAll()} " +
                                       $"Lv{t.GetTankLevel()}");
                }
                Plugin.Log.LogInfo($"[Hook] 共 {tanks.Count} 个鱼缸，总空位 {TankRouting.TotalFreeSlots()}。");
            }
        }
    }
}
