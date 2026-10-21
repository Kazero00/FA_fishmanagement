# 安装说明（中文）

## 前置条件

| 项目 | 要求 |
|---|---|
| 游戏 | 《Fantasy Aquarium》（Steam appid `3183350`），**正版** |
| 游戏运行时 | Unity 2022.3.54f1 / **Mono x64**（Steam Windows 版） |
| Mod 加载器 | [BepInEx 5.4.23.2 **win x64**](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.2) |
| 编译插件（可选） | .NET SDK 6+（目标框架 `net472`，需要游戏自带的程序集做引用） |

---

## 安装步骤

1. **完全退出游戏**（任务管理器确认 `Fantasy Aquarium.exe` 不在运行）。
2. 安装 BepInEx：把 BepInEx 5.4.23.2 x64 压缩包里的**全部内容**（`winhttp.dll`、`doorstop_config.ini`、`.doorstop_version`、`changelog.txt`、`BepInEx\`）解压到**游戏根目录**
   （与 `Fantasy Aquarium.exe` 同一层，例如 `E:\SteamLibrary\steamapps\common\FantasyAquarium\`）。
3. 编译或获取 `FantasyAquariumMod.dll`，放进 `BepInEx\plugins\`。
4. 直接启动游戏（**不要**给 Steam 加启动参数）。
5. 首次启动会生成配置：`BepInEx\config\local.fantasyaquarium.mod.cfg`。

游戏内按 **`F10`** 打开鱼类管理面板（也可以点右上角入口按钮）。

---

## 配置项

`BepInEx\config\local.fantasyaquarium.mod.cfg`：

```ini
[FishManager]
ShowFishManagerButton = true     # 是否显示右上角入口按钮
ToggleKey = F10                  # 开关面板的快捷键
ButtonX = -1                     # 按钮横坐标，-1 = 贴右边缘
ButtonY = 120                    # 按钮纵坐标

[MultiTank]
AutoRedirectToOtherTanks = true  # 买鱼时当前缸满则自动放进其它有空位的缸

[UI]
UseGameCanvas = false            # 高级：UI 是否挂进游戏画布

[Growth]
CatchUpInactiveTanks = true      # 非激活鱼缸的鱼也按真实时间补算成长
CatchUpIntervalSec = 30          # 补算间隔（秒，最小 10）

[Lottery]
KeepInvestment = true            # 抽奖机抽完后保留投资额
SavedCount50 = 0                 # 持久化的 50 金币票次数（0-10，插件自动写入）
SavedCount200 = 0                # 持久化的 200 金币票次数
SavedCount1000 = 0               # 持久化的 1000 金币票次数
```

改完重启游戏生效。

---

## 怎么确认装好了

打开 `BepInEx\LogOutput.log`，应能看到：

```
[Info :Fantasy Aquarium Mod] Fantasy Aquarium Mod vX.Y.Z loaded. ...
[Info :Fantasy Aquarium Mod] [Harmony] 已打补丁 13 处：...
[Info :Fantasy Aquarium Mod] [Fish] 入口按钮已创建。
[Info :Fantasy Aquarium Mod] [Fish] Tick 心跳#1 ...
```

游戏内按 `F10` 能弹出面板即安装成功。

面板每次打开会打印一行**成长自检**，便于判断状态：

```
[Fish] 成长自检：共 N 条｜experienceAll 无效 0 条｜上限不一致 0 条｜真零成长 X 条（falseTime 非空 X 条）｜<1%（极小）Y 条｜已成大只 Z 条｜...
```

* `上限不一致` 必须为 `0`（非 0 说明成长上限曾被写坏）。
* `真零成长` 通常只有少量新鱼；若数量很大且 `falseTime` 很旧，请反馈日志。

---

## 卸载

删除游戏根目录下这 5 项即可完全恢复原版：

```
winhttp.dll
doorstop_config.ini
.doorstop_version
changelog.txt
BepInEx\
```

（配置与日志都在 `BepInEx\` 里，一并删除即干净；**游戏存档不受影响**。）

---

## 常见问题

**Q：面板不出现 / 按 F10 没反应？**
A：看 `BepInEx\LogOutput.log` 是否有 `Fantasy Aquarium Mod vX.Y.Z loaded`。没有则是 BepInEx 没装好（游戏根目录层级不对，或用了 x86 版）。

**Q：字显示成方块？**
A：面板用 `Microsoft YaHei` 动态字体。系统缺该字体会回退到其它 CJK 字体；Windows 中文系统一般都有。

**Q：卖出后游戏原生鱼缸列表错位？**
A：v0.8.1 起已修（补齐了游戏自己的 `TankListController.RemoveSoldFish` 同步）。若还有问题请附日志。

**Q：某些鱼成长度显示 0%？**
A：先看面板自检行的 `真零成长`。数量少 = 刚买的新鱼（正常，上限小的鱼几十分钟就满）；数量大且长期不涨请反馈。

**Q：会不会影响 Steam 云存档？**
A：不改存档格式，正常不影响。但**重要存档请自行备份**。
