using HarmonyLib;
using UI_Main.UI_Store;
using UIFramework;
using UnityEngine;

namespace FantasyAquariumMod
{
    /// <summary>
    /// 功能 2 的购买限制改造点。
    ///
    /// 原版逻辑（已核对反编译）：
    ///   FishBag.OnClickYes()                   —— 买鱼面板的“确定”，当前缸装不下就弹“鱼缸已满”
    ///   UIGetFishOptionNumMoreEvent.OnPressBtn / OnClickBtn —— 数量“+”按钮，超出当前缸空位就弹提示
    ///   UIGetFishOptionNumMoreEvent.OnClickMax —— “最大”按钮，按当前缸空位算上限
    /// 这三处只认“当前鱼缸”，所以缸满就完全买不了。
    /// 这里改成按“所有鱼缸的总空位”判断：只要总空位够就放行，
    /// 具体每条鱼落到哪个缸由 PlacementPatches（InitFishToTank）自动分配。
    /// </summary>
    internal static class PurchasePatches
    {
        // ------------------------------------------------------------------
        // 买鱼面板“确定”
        // ------------------------------------------------------------------
        [HarmonyPatch(typeof(FishBag), nameof(FishBag.OnClickYes))]
        private static class FishBag_OnClickYes
        {
            private static bool Prefix(FishBag __instance)
            {
                if (!Plugin.AutoRedirectToOtherTanks.Value) return true;

                var bagInfo = __instance.bagInfo;
                if (bagInfo == null || __instance.optionNumGb == null) return true;

                var numComp = __instance.optionNumGb.GetComponent<GetFishOptionNumNum>();
                if (numComp == null) return true;

                int optionNum = numComp.optionNum;
                double cost = (float)optionNum * bagInfo.price;

                // 钱不够 → 交回原逻辑弹“金额不足”
                if (MoneyController.Instance.GetMoneyNow() < cost) return true;

                // 所有鱼缸的总空位都不够 → 交回原逻辑弹“鱼缸已满”
                int need = optionNum * bagInfo.bagCount;
                if (TankRouting.TotalFreeSlots() < need) return true;

                // 够钱、总空位够 → 走原版的成功分支（开抽卡面板 + 扣钱 + 成就），并跳过原方法
                var panel = UIManager.Instance.PushPanel("FishCardList") as FishCardList;
                if (panel == null) return true;

                panel.InitFishCardList(bagInfo, optionNum);
                MoneyController.Instance.SpendMoney(cost);
                AchievementManager.Instance.AddAchState("STA_FISHBAG_50000", optionNum);

                Plugin.Log.LogInfo($"[Hook] 跨缸购买放行：{optionNum} 条，花费 {cost}，" +
                                   $"当前总空位 {TankRouting.TotalFreeSlots()}。");
                return false;
            }
        }

        // ------------------------------------------------------------------
        // 数量选择器（+ / 最大）
        // ------------------------------------------------------------------
        [HarmonyPatch(typeof(UIGetFishOptionNumMoreEvent))]
        private static class OptionNumMore
        {
            [HarmonyPatch(nameof(UIGetFishOptionNumMoreEvent.OnPressBtn))]
            [HarmonyPrefix]
            private static bool OnPressBtn_Prefix(UIGetFishOptionNumMoreEvent __instance)
                => Increase(__instance);

            [HarmonyPatch(nameof(UIGetFishOptionNumMoreEvent.OnClickBtn))]
            [HarmonyPrefix]
            private static bool OnClickBtn_Prefix(UIGetFishOptionNumMoreEvent __instance)
                => Increase(__instance);

            [HarmonyPatch(nameof(UIGetFishOptionNumMoreEvent.OnClickMax))]
            [HarmonyPrefix]
            private static bool OnClickMax_Prefix(UIGetFishOptionNumMoreEvent __instance)
            {
                if (!Plugin.AutoRedirectToOtherTanks.Value) return true;

                var bagInfo = __instance.fishBag != null ? __instance.fishBag.bagInfo : null;
                var numComp = __instance.optionNumGb != null
                    ? __instance.optionNumGb.GetComponent<GetFishOptionNumNum>()
                    : null;
                if (bagInfo == null || numComp == null) return true;

                SingleMono<AudioController>.Instance.PlayAClip("Store_GetFish_ArrowDown_Press");

                int slotCost = bagInfo.bagCount > 0 ? bagInfo.bagCount : 4;
                int bySpace = TankRouting.TotalFreeSlots() / slotCost;
                int byMoney = bagInfo.price > 0f
                    ? (int)(MoneyController.Instance.GetMoneyNow() / bagInfo.price)
                    : int.MaxValue;

                int best = Mathf.Min(bySpace, byMoney);
                numComp.optionNum = best > 0 ? best : 1;
                return false;
            }

            private static bool Increase(UIGetFishOptionNumMoreEvent inst)
            {
                if (!Plugin.AutoRedirectToOtherTanks.Value) return true;

                var numComp = inst.optionNumGb != null
                    ? inst.optionNumGb.GetComponent<GetFishOptionNumNum>()
                    : null;
                if (numComp == null) return true;

                SingleMono<AudioController>.Instance.PlayAClip("Store_GetFish_ArrowDown_Press");

                int slotCost = inst.fishBag != null && inst.fishBag.bagInfo != null &&
                               inst.fishBag.bagInfo.bagCount > 0
                    ? inst.fishBag.bagInfo.bagCount
                    : 4;

                int next = numComp.optionNum + 1;
                if (TankRouting.TotalFreeSlots() >= next * slotCost)
                {
                    numComp.optionNum = next;
                }
                else if (inst.tankIsFullGb != null)
                {
                    inst.tankIsFullGb.SetActive(true);
                }
                return false;
            }
        }
    }
}
