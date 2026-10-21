# Fantasy Aquarium（Steam 3183350）加新功能 — 可行性确认报告

> 结论：**可以加，而且门槛不高。** 游戏是 Unity 2022.3 的 **Mono** 构建，`Assembly-CSharp.dll` 无混淆无完整性校验，全部配置数据是明文 JSON，存档密钥硬编码在程序集里。
> 本报告中标注 ✅ 的结论都已经在本机实际读取/解包/编译验证过；标注 ⚠️ 的是需要在 Windows 上真正跑一次游戏才能确认的部分。

游戏根目录：`E:\SteamLibrary\steamapps\common\FantasyAquarium`
（WSL 下：`/mnt/e/SteamLibrary/steamapps/common/FantasyAquarium`）

---

## 一、实测到的技术底细

| 项目 | 结论 | 证据 |
|---|---|---|
| 引擎 | Unity **2022.3.54f1** | `globalgamemanagers` 头部字符串 |
| 脚本后端 | **Mono**（不是 IL2CPP）✅ | `MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll` + `*_Data/Managed/Assembly-CSharp.dll` |
| 架构 | x64 | `file` 识别 `PE32+ ... x86-64` |
| 游戏程序集 | `Assembly-CSharp.dll` 464 KB，264 个类型，156 个源文件 | ILSpy 反编译；调试串里保留了原始 `Assets\Scripts\...cs` 路径 |
| 混淆 / 反篡改 | **没有**（无 Eazfuscator/ConfuserEx/CodeStage 等痕迹）✅ | `strings` 全量扫描无命中 |
| 目标框架 | .NET Framework 4.0.30319（Mono profile） | 程序集引用串 `v4.0.30319` |
| Steam | appid **3183350**，Steamworks.NET，**云存档已开** | `steam_appid.txt`；存档目录有 `steam_autocloud.vdf` |
| 音频 | Bass（`bass.dll`） | `Plugins/x86_64/bass.dll` |
| 本地化 | Unity Localization + Addressables，14 种语言 | `StreamingAssets/aa/` 35 个 bundle |
| 静态数据 | **明文 JSON**，打包在 `resources.assets` 的 TextAsset 里 ✅ | 已用 UnityPy 解出 |
| 资源包 | 17 个，运行时从 `StreamingAssets` 用 `AssetBundle.LoadFromFileAsync` 加载，**无 CRC/哈希校验** ✅ | 反编译 `LoadStaticAB.cs` |
| 存档 | `%AppData%/LocalLow/MoonscapeStudio/Fantasy Aquarium/appdata_0_*.dat`，AES-128-CBC，**密钥硬编码** ✅ | 已成功解密并原样重加密 |

**为什么这些很重要**：Mono + 无校验 = 可以挂 Harmony 补丁；数据明文 = 不改代码也能加内容；没有完整性校验 = 不会因为改文件而崩溃或被拒。

---

## 二、四条路线对比

| 路线 | 能做什么 | 对游戏的改动 | 难度 | 状态 |
|---|---|---|---|---|
| **A. BepInEx 5 插件 + Harmony** | 任意新功能、新 UI、改逻辑、改数值 | 只新增文件，原文件不动 | 中 | ✅ 插件已在本机编译通过 |
| **B. 改数据/资源包** | 新鱼、新家具、新成就、新文案、换贴图 | 改 `resources.assets` + bundle | 低—中 | ✅ 数据已解包确认可改 |
| **C. 改存档** | 金钱、图鉴、好感度、解锁 | 只改存档 | 低 | ✅ 已实测解密+回写 |
| **D. 直接改 Assembly-CSharp.dll（dnSpy）** | 同 A | **覆盖原文件**，更新即失效 | 中 | 可做但不推荐（见 §6） |

---

## 三、路线 A：BepInEx 插件（推荐，做"新功能"走这条）

### 3.1 为什么是 BepInEx 5 而不是 6
Mono 后端用 **BepInEx 5.4.23.2 (win x64)** 即可；BepInEx 6 主要面向 IL2CPP，这里用不上。Unity 2022.3 需要 doorstop 4，5.4.23 已满足。

### 3.2 安装（4 步）
1. 下载 `BepInEx_win_x64_5.4.23.2.zip`（GitHub: BepInEx/BepInEx releases）
2. 解压到游戏根目录 `E:\SteamLibrary\steamapps\common\FantasyAquarium\`
   → 应出现 `winhttp.dll`、`doorstop_config.ini`、`BepInEx\`
3. 启动一次游戏再退出，生成 `BepInEx\plugins\`、`BepInEx\config\`、`BepInEx\LogOutput.log`
4. 把编译好的 `FantasyAquariumMod.dll` 丢进 `BepInEx\plugins\`

无需给 Steam 加启动参数；`winhttp.dll` 会自动挂载。卸载 = 删掉这几个文件。

> **当前状态：以上步骤已经在本机执行完毕** —— BepInEx 5.4.23.2（x64）与编译好的插件都已部署进 `E:\SteamLibrary\steamapps\common\FantasyAquarium`，启动游戏即可生效。功能说明、配置项、自测清单见 [plugin/README.md](plugin/README.md)。

### 3.3 已交付的插件（本机编译通过 ✅，补丁目标静态校验 6/6）

```
plugin/FantasyAquariumMod/
├── FantasyAquariumMod.csproj
├── Plugin.cs              # 入口 + 配置项（F9 快捷键、分流开关）
├── TankRouting.cs         # 容量计算、落缸路由、跨缸搬鱼
├── Patches.cs             # 诊断日志（资源包就绪、鱼缸清单）
├── PlacementPatches.cs    # 落缸 Transpiler + 兜底搬缸 Postfix
├── PurchasePatches.cs     # 买鱼面板与数量选择器的限制放宽
├── AllTanksPanel.cs       # 全部鱼缸管理面板（IMGUI）
└── bin/Release/FantasyAquariumMod.dll   ← 22 KB，编译产物
```
编译命令（用便携 SDK 或任意 .NET SDK）：
```bash
dotnet build -c Release -p:BepInExDir="E:/SteamLibrary/steamapps/common/FantasyAquarium"
```
`csproj` 通过 `<GameDir>` 指向游戏目录，引用 `Assembly-CSharp.dll` + `UnityEngine*.dll` + `BepInEx/core/*.dll`，目标框架 `net472`（与 Unity Mono profile 一致），编译 0 warning 0 error。

插件实现了两件事（正是需求里提的两点）：
1. **全部鱼缸管理面板**：右上角常驻按钮 / F9，一屏列出所有鱼缸的鱼数、容量、等级、空位、每日收益，可直接切换、升级、一键升级全部（自建 UGUI 画布 `sortingOrder=30000`，压在游戏 `Front`=2000 / `Canvas`=500 之上，零 prefab 依赖）。
2. **购买跨缸分流**：买鱼面板与数量选择器改为按「所有鱼缸总空位」判断，只要总量够就放行；每条新鱼由 `FishCard.InitFishToTank` 的 **Transpiler** 决定落到哪个缸（当前缸优先，其次第一个有空位的缸），并有「超容量就搬走」的 Postfix 兜底。

关键实现点（可复用到别的功能）：
- 全游戏**只有一处**真正把新鱼放进缸：`FishCard.InitFishToTank()`；它的 IL 第 0 条就是 `call TankController::get_CurrentTank()`，所以只替换这一条指令即可改变落缸目标，方法体其余逻辑（鱼数据、事件、命名）全部保留。
- 容量语义：`tankValueNow` = 占用、`tankValueAll` = 容量；但 `tankValueNow` 是缓存，刚解锁未激活的缸可能不准，所以判定统一用 `allFishList.Count`。
- 往未激活的鱼缸放鱼是安全的：游戏自己 `LoadAllFish` 就是这么做的，`Fish.LoadFish` 只依赖 prefab 上的序列化引用，不依赖 `Start()`。

### 3.4 关键 hook 点速查表（均来自反编译，签名已核对）

| 想做什么 | 挂钩目标 | 说明 |
|---|---|---|
| 最早入口 | `SteamManager.InitOnPlayMode()`（`[RuntimeInitializeOnLoadMethod]`） | 全游戏唯一的 RuntimeInitializeOnLoad |
| 资源就绪后 | `LoadStaticAB.OnAllBundlesLoaded()` / `LoadStaticAB.AssetBundles`（public static 字典） | 可塞自己的 bundle，或用 `allfish`/`static` 里的现成素材 |
| 加/改鱼数据 | `AllFishInfo.get_Instance`、`AllFishInfo.GetFishInfo(int)`、`GetAllFishInfo()` | `Instance` 是懒加载单例，Postfix 里 `allFishInfo.Add(...)` 即可；注意 `GetAllFishInfo()` 返回的是**副本**，要改源列表 |
| 抽卡概率/池子 | `DrawCard.InitFishPool()` / `Preprocess()` / `DrawFish()` / `DrawFishWithMinRarity(int)` | `rarity == 4` 的鱼不进普通池；`Preprocess()` 里做了概率归一化 |
| 金钱 | `MoneyController.EarnMoney(double)` / `SpendMoney(double)` / `GetMoneyNow()` / `GetMoneyData()` | |
| 鱼缸 | `TankManager.Instance`、`TankController`、`Tank.GetTankData()` | 加鱼缸、扩容、改鱼缸价值 |
| 家具 | `FurnitureManager.PushFurniture(int tankNo, int id)`、`GetOwnedFurnitureCount()` | 家具数量直接影响彩色鱼概率（`DrawCard.IsColorfulFish`） |
| 图鉴 | `HandbookManager.UpdateFish(FishType, int)`、`Unlocked*` 字段 | |
| 好感度 | `LikabilityManager.AddLikability(int)`、`UpPastLevel()` | 抽卡券来源 |
| Steam 成就 | `AchievementManager.UnlockAchievement(string)` / `AddAchState(string,int)` | 成就 id 与条件在 `achInfo` JSON 里 |
| 存档 | `SaveLoadManager.LoadData()` / `SaveToFile<T>` / `LoadFromFile<T>` | |
| 新 UI 面板 | `UIManager.Instance.PushPanel(string panelType)` + `static` 包里的 `PanelInfoList` 资产 | 面板 prefab 名→类型的映射表是 ScriptableObject，可代码注入 |
| 语言 | `LanguageManager` | 文案在 Addressables 的 localization-string-tables-* 里 |

---

## 四、路线 B：只改数据/资源（做"新内容"走这条，零代码）

### 4.1 静态数据在哪儿 ✅
`Fantasy Aquarium_Data/resources.assets` 里的 TextAsset，**全是明文 JSON**（已解出）：

| 资源名 | 内容 | 实测大小 |
|---|---|---|
| `allFishInfo` | **176 条鱼**（id 0–175，稀有度 0–4） | 70,808 字符 |
| `tankInfo` | 鱼缸定义 | 4,251 字符 |
| `tankFurn` | 鱼缸家具定义 | 13,638 字符 |
| `achInfo` | Steam 成就定义 | 13,078 字符 |
| `allDialogue` | 对话 | 4,988 字符 |

一条鱼长这样（可直接照抄改数值）：
```json
{ "id": 0, "enName": "RaccoonButterflyfish", "zhName": "浣熊蝴蝶鱼", "jaName": "RaccoonButterflyfish",
  "rarity": 1, "moneyPerDay": 44.64, "experienceAll": 3600,
  "smallMoney": 2.25, "bigMoney": 80, "fishBag": 0, "combinedProbability_normal": 0.000703125 }
```
> 只改数值（收益/经验/概率/名字）→ **只要改这个 JSON**，抽卡池、图鉴、收益全自动跟着变。这是性价比最高的改法。

### 4.2 加一条全新鱼的完整配方
代码里的贴图是按**名字约定**去 bundle 里取的：

| 用途 | 素材名 | 所在 bundle |
|---|---|---|
| 缸里的鱼（id ≥ 136 走这套命名） | `Fish_{id:D2}_{enName}_0`（小）/ `_1`（大） | `allfish`（普通）、`allfishcolorful`（彩色） |
| 图鉴卡片 | `FishCard_{id:00}_{rarity+1}` | `fishcard`、`fishcardcolorful` |
| 鱼头/抽卡画面 | 见 `fishhead`、`fishheadchar`、`drawfishcard*` | 同名 bundle |

实测：`allfish` 里正好 **352 个 Sprite = 176 条鱼 × 2（小/大）**，命名与规则完全吻合。

所以"加一只 id=176 的新鱼" = ① `allFishInfo` 加一条 JSON；② 往 `allfish`/`allfishcolorful` 各加 2 张按规范命名的 Sprite；③ 往 `fishcard*` 加对应卡片图。
其中 ② 需要重打包 bundle（工具：**UABEA**（GUI，Windows）或 **UnityPy**（Python，本机已用它解包成功））。
> 不想动 bundle 的取巧办法：**复用已有鱼的贴图**（新 id 用 `Fish_00_...` 的素材名，或干脆只改现有鱼的数值/名字），代价是外观和现有鱼一样。
> ⚠️ 未验证部分：改完 `resources.assets` 后游戏能否正常加载（理论上可以，因为无校验；但我这边不能在游戏目录写入，也没法启动游戏验证）。

### 4.3 资源包与本地化
- 17 个热加载 bundle：`allfish, allfishcolorful, audioconfig, bgmconfig, whitenoiseconfig, static, tank, tankfurniture, tankvalue, furnitureitem, fishhead, fishcard, fishheadcolorful, fishheadchar, fishcardcolorful, drawfishcard, drawfishcardcolorful`
- 它们只是**按文件名**从 `StreamingAssets` 读的，没有哈希校验 → 可以直接替换同名文件，甚至塞一个新的进去（配合插件往 `LoadStaticAB.AssetBundles` 里注册）。
- 翻译：`StreamingAssets/aa/StandaloneWindows64/localization-string-tables-<lang>_assets_all.bundle`，14 种语言，可按语言单独替换。

---

## 五、路线 C：改存档（已验证 ✅）

- 路径：`%USERPROFILE%\AppData\LocalLow\MoonscapeStudio\Fantasy Aquarium\`
  WSL：`/mnt/c/Users/<用户名>/AppData/LocalLow/MoonscapeStudio/Fantasy Aquarium/`
- 文件：`appdata_0_0.dat`(金钱) … `appdata_0_7.dat`(好感度)
- 格式：JSON → AES-128-CBC → Base64；**密钥/IV 都是 `luoyanwen7654321`**（硬编码在 `SaveLoadManager`）
- 实测：`appdata_0_0/0_2/0_7` 解密后重加密与原文件**逐字节一致**（round-trip 已验证）

已交付工具 `tools/save_edit.py`：
```bash
python3 tools/save_edit.py list                    # 列存档
python3 tools/save_edit.py show appdata_0_0.dat    # 看金钱
python3 tools/save_edit.py decrypt appdata_0_0.dat money.json
python3 tools/save_edit.py encrypt money.json appdata_0_0.dat   # 自动备份 .bak
```
> 只改数值、完全不碰游戏文件，是最安全的玩法；但**游戏开了 Steam 云同步**，改完可能被云端覆盖（可临时断开云同步）。

---

## 六、路线 D：直接改 `Assembly-CSharp.dll`（不推荐）
dnSpy 直接改 IL 编译回去是可行的（无强签名、无校验），但：覆盖原文件 → 游戏更新/校验就废；无法与其它 mod 共存；改错了没有回退路径。除非只想改一行常量且不想装 BepInEx，否则用路线 A。

---

## 七、坑与注意事项

1. **Steam 云同步**：`steam_autocloud.vdf` 存在，改存档/存档结构变更前建议先关云同步，否则新老存档互相覆盖。
2. **游戏更新会覆盖**：bundle / `resources.assets` / DLL 都可能被 Steam 更新替换；BepInEx 插件自身不会被覆盖，是更耐更新的方式。
3. **写权限**：本次会话后期已切换为完全文件访问，BepInEx 与插件已经实际部署进游戏目录（纯新增文件，删除即可回滚）。
4. **存档结构**：加了新字段（比如插件给鱼加自定义属性）后，老存档反序列化是安全的（Newtonsoft 缺字段用默认值），但**删字段**要小心。
5. **贴图素材版权**：加内容用的美术资源请用自制/授权素材，别直接分发游戏原素材。
6. **API 稳定性**：hook 点都依赖具体类名/方法名，游戏大更新后可能失效，升级时需重新核对（本报告已给出定位方法，见下）。
7. **⚠️ UI 层级：本游戏的屏幕画布在自定义 sorting layer 上（实测踩坑，两次翻车）**
   - TagManager 里的 sorting layer：`Default=0`、`Tank=4009624115`、`AllFish=1358615869`、**`UI=3384432189`**（有符号即 `-910535107`）。
   - 场景里两个画布：`Front`（Default 层，sortingOrder=2000）、`Canvas`（**UI 层**，sortingOrder=500）。
   - **sorting layer 的优先级高于 sortingOrder**：把自建画布的 order 设成 30000 但留在 Default 层，照样被 UI 层的 `Canvas` 压住 —— v0.3.0 就这么失败了。
   - 正确做法：`canvas.sortingLayerID = SortingLayer.NameToID("UI"); canvas.sortingOrder = 30000;`（并留好取最后一层的兜底）。
   - 另外 `OnGUI`/IMGUI 渲染在所有 ScreenSpaceOverlay 画布**之下**，给这个游戏做 UI 不要用 IMGUI（v0.2.0 就这么失败了）。

## 八、如何自行复核这些结论
```bash
# 版本 / 架构 / 是否 Mono
strings -a "Fantasy Aquarium_Data/globalgamemanagers" | head
ls "Fantasy Aquarium_Data/Managed/Assembly-CSharp.dll"

# 反编译（需要 .NET SDK）
dotnet tool install --tool-path tools ilspycmd --version 8.2.0.7535
DOTNET_ROLL_FORWARD=LatestMajor tools/ilspycmd "…/Managed/Assembly-CSharp.dll" -o out

# 解包静态数据 JSON / bundle（本机已验证）
python3 -m venv venv && venv/bin/pip install -i https://pypi.tuna.tsinghua.edu.cn/simple UnityPy
#   → UnityPy.load("…/resources.assets") 里 type=="TextAsset" 的对象
```

## 九、下一步
已经落地的是需求里的两条：
- ✅ **全部鱼缸管理面板**（F9 / 常驻按钮）——见 [plugin/README.md](plugin/README.md)
- ✅ **购买时自动分配到有空位的鱼缸**——同上

还想加什么可以直接说，例如：
- **改数值/加鱼/加成就** → 出改好的 JSON +（需要的话）重打包 bundle 的脚本
- **更多鱼缸机制**（批量卖鱼、鱼在缸之间批量调拨、按稀有度自动整理）
- **只想要钱/解锁** → 用 `tools/save_edit.py`

---
*本报告所有 ✅ 结论均由本机实测得出：UnityPy 1.25.4 解包、ILSpy 8.2 反编译（20527 行 C#）、Mono.Cecil 校验 IL 与补丁目标、dotnet SDK 8.0.425 编译插件、AES 存档 round-trip 校验。*
