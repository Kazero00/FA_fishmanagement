# 更新记录

## v0.8.14

* 新增**按最大养成值筛选**：数值区间（最小/最大，可只填一边）对应 `fishInfo.experienceAll`
  —— 即"长成大只所需的总经验"，用来快速找出"好养/难养"的鱼。
* 面板表格新增**养成值**列（显示 `experienceAll`）。
* **移除体型筛选**（体型与「成长」语义重复），属性行重排；三个区间 + 成长 + 按钮重排到同一行，避免 UI 拥挤。

## v0.8.13

* 新增**抽奖机保留投资额**：好感度投资幸运屋结算后不再把 50/200/1000 投资票的次数清零，
  下次打开机器直接沿用；次数写入 BepInEx 配置，跨游戏重启也保留。
  配置项：`[Lottery] KeepInvestment`（开关）、`SavedCount50/200/1000`（持久化值）。

## v0.8.12

* 成长补算实现调整为**纯数据层**计算：直接按游戏公式更新鱼的成长字段，不再操作任何 GameObject（不 `SetActive`、不移动），对渲染零影响。

## v0.8.11

* 新增**非当前鱼缸的成长补算**：游戏加载时 `Instantiate`（触发 `OnEnable`）早于 `LoadFish`，非当前缸的鱼拿不到真实 `falseTime`，离线成长补算被跳过；之后该缸保持未激活状态，那些鱼会长期停在存档值（大量 0%）。
* 新增配置 `[Growth] CatchUpInactiveTanks` / `CatchUpIntervalSec`。

## v0.8.10

* 成长列改为自适应精度：`<1%` 显示两位小数（如 `0.05%`），不再把极小进度显示成 `0%`。
* 自检日志区分「真零成长」与「<1%（极小）」，并打印真零样本（缸/编号/上限/falseTime）。

## v0.8.9

* **修复成长度被夹成 `100` 的根因**：鱼的预制体里序列化了一份默认 `FishData`（`fishInfo.experienceAll = 100`），组件在 `LoadFish`/`Start` 之前 `experienceAll` 为 0/100，此时 `GetExperienceNow()` / `RecalculateExperienceNow()` 会把成长度夹成上限值。
* 修法：`EnsureLimit()` 只要与真实上限不一致就修正；`Start`/`OnEnable` 增加 Prefix 先修正；`GetExperienceNow()` 用真实上限封顶。

## v0.8.8

* 堵住 `RecalculateExperienceNow()` / `Start()` / `OnEnable()` 的清零与初始化异常路径（吞掉初始化 NRE 并补上限）。

## v0.8.7

* 稀有度区分 `rarity 3`「闪彩」与 `rarity 4`「闪彩★」（列内标记 + 独立筛选按钮）。
* 属性筛选行重新排版容纳 5 个稀有度按钮。

## v0.8.6

* 修正概念：异色（`FishType.Colorful`）不再当作第 5 档稀有度，而是与稀有度**正交**的独立维度。
* 稀有度收敛为官方 4 档（绿色/蓝色/金色/闪彩）；`rarity 4` 与 `3` 同属闪彩。

## v0.8.5

* **修复稀有度映射整体错位一档**（原先 `0=白/1=绿/2=蓝/3=金/4=闪`，导致 `rarity 3` 的鱼在图鉴是闪彩、面板显示金色）。
* 名称与配色对齐游戏 zh-hans 本地化表：绿色 / 蓝色 / 金色 / 闪彩 / 异色。

## v0.8.4

* 成长筛选按钮文案改为「成年 / 幼年」（判定不变：成年 = 100%）。

## v0.8.3

* 面板成长度改读组件字段（无副作用）；`GetExperienceNow()` 用 Harmony 改成**纯读取**（原实现带写入副作用，会在 `experienceAll` 未初始化时清零）。

## v0.8.2

* 成长列改读实时值 `FishExperienceAdder.experienceNow`（原先读 `FishData` 缓存，卖鱼触发同步后会突然跳变）。
* 批量卖满成长鱼时游戏 `OnDestroy` 会抛 `NullReferenceException`（一次批卖 174 条刷 94 条）→ 卖前置 `experienceAdder.firstIn = true` 跳过重算。
* 所有排序加入稳定决胜项（鱼缸 → 编号 → 实例ID），消除同分鱼每次重建换位置的错位感。

## v0.8.1

* 修复卖出/换缸后游戏原生「鱼缸列表」面板条目错位：补齐游戏自己会做的 `TankListController.RemoveSoldFish(...)` 同步。

## v0.8.0

* 稀有度筛选改为可多选（位掩码）。
* 成长筛选改为三态按钮。
* 筛选与排序覆盖全部属性、可叠加。
* 批量操作支持作用于「整个筛选结果」（跨页）或「勾选的行」。
* 移除底部说明小字。

## v0.7.x 及更早

* 面板从 IMGUI 迁移到 UGUI（IMGUI 会渲染在所有 ScreenSpaceOverlay 画布之下，游戏内不可见）。
* 解决排序层问题：游戏主 Canvas 在自定义 Sorting Layer `UI` 上，自建画布必须同层 + 高 `sortingOrder`。
* 解决生命周期问题：Chainloader 阶段创建的 `MonoBehaviour` 会在场景加载时被销毁，改为**静态类 + Harmony 补丁驱动 Tick**。
* 跨缸落鱼：`FishCard.InitFishToTank` 的 Transpiler 把 `TankController.get_CurrentTank()` 换成按空位选择目标缸；补上购买上限与数量选择的判定。
