# FA_fishmanagement · Fantasy Aquarium 鱼类管理 Mod

给 Steam 游戏 **《Fantasy Aquarium》**（appid `3183350`）写的功能扩展 Mod：一个**跨鱼缸的鱼类管理面板** + 一系列游戏 bug 修复。

> 纯本地单机 Mod，不需要联机、不修改游戏本体文件；卸载只需删掉几个文件。
> **本仓库不包含任何游戏本体资源**（贴图/音频/数据/可执行文件），使用需自备正版游戏。

---

## 功能

### 鱼类管理面板（游戏内按 `F10`）

把 9 个鱼缸的鱼汇总成一张表，跨鱼缸统一管理：

| 能力 | 说明 |
|---|---|
| **筛选（可任意叠加）** | 鱼缸 / 异色 / 稀有度（多选）/ 锁定状态 / 收益区间 / 售价区间 / **养成值区间** / 成长（成年·幼年） |
| **排序** | 稀有度 / 收益/天 / 售价 / 成长 / 按鱼缸 / 按编号（均带稳定决胜项，顺序不会乱跳） |
| **批量操作** | 对「整个筛选结果」或「勾选的行」：加锁 / 解锁 / 卖出 / 换缸 |
| **单条操作** | 卖出 / 上锁 / 跳到所在缸 |
| **数值区间** | 收益、售价按最小~最大过滤，可只填一边，回车即应用 |
| **二次确认** | 批量卖出需点两次；筛选条件变化会自动取消确认状态 |

### 跨缸落鱼

买鱼时如果当前鱼缸已满，自动把新鱼放进其它有空位的鱼缸（不再被"鱼缸已满"卡住）。

配套：落在非当前鱼缸里的鱼也会按游戏自身的公式定期补算成长，避免长期停在 0%。
（纯数据层计算，不激活/不移动任何 GameObject。）

### 抽奖机保留投资额

好感度投资幸运屋抽完后，**保留当前已选的投资额**（50/200/1000 投资票的次数），
下次打开机器直接沿用，不用每次都重新点一遍；次数会写入配置，**跨游戏重启也保留**。

* 关闭开关后恢复游戏原行为（结算后次数清零）。
* 想清空：在机器里把次数减到 0，或把配置里的 `SavedCount*` 改成 0。

### 游戏 bug 修复（Harmony 补丁）

以下均为游戏本身存在的问题（与是否安装本 Mod 无关）：

| 问题 | 修复 |
|---|---|
| 成长度被夹成预制体默认上限 `100`（表现为"100% 变 0%"） | 改用鱼的**真实上限**封顶/补算 |
| `experienceAll` 未初始化时成长度被写成 0 | 进入前修正上限，拿不到上限则整段跳过 |
| 卖出满成长鱼时 `OnDestroy` 链路抛 `NullReferenceException` | 卖前置 `experienceAdder.firstIn = true`（游戏对新建鱼的同款做法） |

---

## 安装

前置：正版游戏 + [BepInEx 5.4.23.2 **win x64**](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.2)

1. 把 BepInEx 5.4.23.2 x64 压缩包内的**全部文件**解压进游戏根目录（与 `Fantasy Aquarium.exe` 同级）。
2. 把本仓库编译出的 `FantasyAquariumMod.dll` 放进 `BepInEx\plugins\`。
3. 启动游戏，按 `F10` 打开面板。

详细说明：[docs/INSTALL_zh.md](docs/INSTALL_zh.md)　配置项：`BepInEx\config\local.fantasyaquarium.mod.cfg`

**卸载**：删除 `winhttp.dll`、`doorstop_config.ini`、`.doorstop_version`、`changelog.txt`、`BepInEx\` 即可（存档不受影响）。

---

## 编译

插件是 `net472` 类库，引用游戏自带的程序集与 BepInEx：

```bash
# 默认从下面这个路径读游戏程序集，可用 -p:GameDir=... 覆盖
dotnet build plugin/FantasyAquariumMod/FantasyAquariumMod.csproj -c Release \
  -p:GameDir="E:/SteamLibrary/steamapps/common/FantasyAquarium"
```

产物：`plugin/FantasyAquariumMod/bin/Release/FantasyAquariumMod.dll`

---

## 仓库结构

```
plugin/FantasyAquariumMod/   插件源码（C#）
  Plugin.cs                  BepInEx 入口 + 配置项
  FishManagerPanel.cs        管理面板（静态类，由 Harmony 补丁驱动 Tick）
  UiKit.cs / UiRoot.cs       UGUI 工具（CJK 字体、排序层、布局）
  TankRouting.cs             跨缸路由 / 换缸 / 卖鱼前置处理
  GrowthGuard.cs             成长度保护与补算
  Patches.cs / PlacementPatches.cs / PurchasePatches.cs   各 Harmony 补丁
plugin/README.md             完整功能文档 + 游戏机制实证附录
MODDING_GUIDE.md             逆向/可行性笔记（Unity 结构、Hook 点、坑位）
tools/save_edit.py           存档加解密工具（AES-128-CBC）
docs/INSTALL_zh.md           安装说明（中文）
docs/CHANGELOG.md            更新记录
```

---

## 免责声明

* 本项目仅供学习交流与单机 Mod 使用，**不提供、不包含任何游戏本体文件**，也不支持任何形式的盗版。
* 游戏《Fantasy Aquarium》及其素材版权归 MoonscapeStudio 所有；BepInEx / HarmonyX 遵循其各自开源许可（见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)）。
* 使用 Mod 可能有存档风险，重要存档请自行备份。

## License

本仓库自有代码采用 [MIT](LICENSE) 许可。
