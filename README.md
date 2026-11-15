# RPGDemo · Unity 工程

Unity 6000.6.4f1。此工程是 Demo 的迁移目标，默认 Play 与 Windows 构建从 `Assets/Scenes/Demo_Launcher.unity` 启动，随后加载 `Base_Stage2`。原 `SLG/` 工程已于 2026-11-03 整体移入项目父目录的 `归档/SLG/`，仅作回顾、不再使用；旧检查、开发工具与迁移前内容仍在其中。

游戏保留新营地与巴图袭击当天两个阶段预设档，各有男女主变体，共四份；完整序章通过主菜单开始新游戏，旧游戏存档不迁移。数值工具在 Unity「SLG → 其他工具 → 导入」中：`Demo数值表（校验与预览）`读取 `../策划/DEMO/DEMO数值配置表.xlsx` 做全量导入；`Demo核心六页表（局部覆盖）`读取 `../策划/DEMO/DEMO核心数值表.xlsx`，只覆盖六页已接入字段，保留当前JSON中的其他规则。两者都先预览，确认后更新快照及四份阶段档，不改玩家已有存档。六页来源另记在 `DemoBalance.core-source.json`，组合标识同时包含工作簿与运行基底摘要。

迁移状态、验证与未完成事项见 [STATUS.md](../STATUS.md)。`Assets/Resources` 包含按名称动态加载的运行资源，删除前须核对实际引用。旧地图工作台、旧场景检查和坡道实验未迁入游戏入口；共享运行模块仍有待进一步筛选。

## 在场景里调整暖木界面

在 Unity 编辑模式打开 [Base_Stage2](Assets/Scenes/Base_Stage2.unity)。在 Hierarchy 展开 `Base_Stage2 / UI 布局源`，临时勾选该组及要看的界面节点。在 Scene 视图用 Rect Tool 拖动或缩放，或在 Inspector 调整颜色，保存场景即可。`PartyMemberCard` 控制背包和交易复用的角色卡，`NpcDialogue` 控制对白，`TownTrade` 控制交易。列表会在运行时从游戏数据填入，重复行的外观在同组模板上调整。
