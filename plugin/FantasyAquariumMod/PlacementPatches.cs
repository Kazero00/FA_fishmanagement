using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace FantasyAquariumMod
{
    /// <summary>
    /// 功能 2 的落缸改造点。
    ///
    /// 全游戏只有 FishCard.InitFishToTank() 一处真正把新鱼放进鱼缸：
    ///     Tank component = TankController.CurrentTank.GetComponent&lt;Tank&gt;();
    ///     Instantiate(..., component.tankFishGb.transform);
    ///     component.tankFishController.allFishList.Add(gameObject);
    /// 这里做两件事：
    ///   1) Transpiler 把 IL 里的 TankController.get_CurrentTank() 换成 TankRouting.PickTargetTank()，
    ///      方法体其余逻辑（鱼数据、命名、事件）原样保留；
    ///   2) Postfix 兜底：万一 Transpiler 因游戏更新失效，只要鱼把所在鱼缸撑爆了，就搬到有空位的缸。
    /// </summary>
    internal static class PlacementPatches
    {
        [HarmonyPatch(typeof(FishCard), "InitFishToTank")]
        private static class FishCard_InitFishToTank_Transpiler
        {
            private static readonly MethodInfo Original =
                AccessTools.Method(typeof(TankController), "get_CurrentTank");

            private static readonly MethodInfo Replacement =
                AccessTools.Method(typeof(TankRouting), nameof(TankRouting.PickTargetTank));

            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                int patched = 0;

                foreach (var ci in instructions)
                {
                    if (IsCurrentTankCall(ci))
                    {
                        ci.opcode = OpCodes.Call;
                        ci.operand = Replacement;
                        patched++;
                    }
                    yield return ci;
                }

                if (patched > 0)
                    Plugin.Log.LogInfo($"[Hook] InitFishToTank 已接管 {patched} 处落缸目标。");
                else
                    Plugin.Log.LogError("[Hook] InitFishToTank 未找到 TankController.CurrentTank 调用，" +
                                        "已改用兜底搬缸逻辑，请反馈游戏版本。");
            }

            private static bool IsCurrentTankCall(CodeInstruction ci)
            {
                if (ci.opcode != OpCodes.Call && ci.opcode != OpCodes.Callvirt) return false;
                if (!(ci.operand is MethodInfo mi)) return false;
                if (Original != null && mi == Original) return true;
                // 兜底比较：Mono 下 MethodInfo 的 == 不保证引用相等
                return mi.Name == "get_CurrentTank" && mi.DeclaringType == typeof(TankController);
            }
        }

        /// <summary>
        /// 兜底：新鱼落进某个鱼缸后，如果该缸已超过容量，就搬到第一个有空位的缸。
        /// Transpiler 正常工作时不会触发（鱼本来就被放进了有空位的缸）。
        /// </summary>
        [HarmonyPatch(typeof(FishCard), "Init")]
        private static class FishCard_Init_Postfix
        {
            private static void Postfix(FishCard __instance)
            {
                if (!Plugin.AutoRedirectToOtherTanks.Value) return;

                var fishGo = __instance.fishInTank;
                if (fishGo == null) return;

                var owner = TankRouting.FindOwner(fishGo);
                if (owner == null) return;

                // 只有真的超容量才搬，恰好装满（now == all）保持原样
                int used = TankRouting.UsedSlots(owner);
                if (used <= owner.GetTankValueAll()) return;

                var target = TankRouting.FindTankWithFreeSlots(owner);
                if (target == null) return;

                TankRouting.MoveFish(fishGo, owner, target);
                Plugin.Log.LogInfo($"[Hook] 兜底搬缸：鱼 {fishGo.name} 从鱼缸 {owner.GetTankNo():00} " +
                                   $"移到 {target.GetTankNo():00}。");
            }
        }
    }
}
