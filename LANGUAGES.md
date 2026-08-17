# LANGUAGES.md — 游戏内显示文本清单（已同步当前项目结构与汉化进度）

> 本文件已同步「当前项目结构」与「已实际写入游戏」的译文状态。你只需改每条 `译文` 一行，改完后告诉我，我再把改动应用到游戏。
>
> 标记说明：
> - `译文: （待翻译）` → 尚未翻译，仍是英文原文
> - `译文: （不翻译）` → 你已决定保留英文原文
> - `译文: （置空）` → 你已决定清空该文字
> - `译文: （部分翻译）…` → 只翻译了一部分
> - `译文: （已是中文）…` → 开发者/引擎侧已内置中文，无需处理
>
> `{...}`、`${x}` 为动态数值占位符；`{{input:...}}` 为按键提示占位符；`\n` 为换行符。翻译时请原样保留。
>
> **纯数值/动态数值显示（生命值、倒计时、进度、音量、金额、槽位序号、FPS、冷却秒数 `N.Ns` 等）不在本清单中，无需翻译。**

### 本轮结构变化要点（相对上一版）

1. 主菜单不再是独立 `MainMenu` 场景，而是整合进 **Home 场景**（`mainMenuSceneName: Home`），名为「Home Main Menu」，并新增「开始游戏 / 新手教程 / 设置 / 退出游戏」四个按钮与集成式镜头转场。
2. 新增「新手教程」入口，对应场景 `SpawnShelterStoneTest`（`tutorialSceneName`）。
3. 快捷栏/装备栏物品缩减：`Gun`、`SMG`、`Cart` 已移除，仅剩 `Pickaxe / Flashlight / SolidGun / Bomb / PortalGun`。
4. 商店商品缩减为 3 个：`SolidGun`、`FlashLight`、`PortalGun`（displayName 回退为英文）。
5. 新增快捷栏技能冷却显示（数值 `N.Ns`，无需翻译）。

---

## A. 主菜单（Home Main Menu）

> 来源：`Assets/Game/Runtime/UI/MainMenuView.cs`（`PrepareHomePresentation` 运行时设文本）、`Assets/Game/Runtime/UI/MainMenuController.cs`、`Assets/UI/UI/MainMenuCanvas.prefab`（设置面板烘焙文本）。

### A1. 眉题（Overline）
- 出处: `MainMenuView.cs`（PrepareHomePresentation）
- 原文: `MAIN MENU  //  HOME BASE`
- 译文: （待翻译）

### A2. 开始游戏按钮
- 出处: `MainMenuView.cs`（PrepareHomePresentation）
- 原文: `BEGIN DESCENT`
- 译文: （已是中文）`开始游戏`

### A3. 新手教程按钮
- 出处: `MainMenuView.cs`（PrepareHomePresentation）
- 原文: `TUTORIAL`
- 译文: （已是中文）`新手教程`

### A4. 设置按钮
- 出处: `MainMenuView.cs`（PrepareHomePresentation）
- 原文: `SYSTEM SETTINGS`
- 译文: （已是中文）`设置`

### A5. 退出游戏按钮
- 出处: `MainMenuView.cs`（PrepareHomePresentation）
- 原文: `LEAVE EXPEDITION`
- 译文: （已是中文）`退出游戏`

### A6. 状态（就绪）
- 出处: `MainMenuController.cs`（ShowMainMenu）
- 原文: `SYSTEMS READY`
- 译文: （待翻译）

### A7. 状态：进入家园基地
- 出处: `MainMenuController.cs`（BeginIntegratedTransition）
- 原文: `ENTERING HOME BASE...`
- 译文: （待翻译）

### A8. 状态：场景未打包
- 出处: `MainMenuController.cs`（StartGame）
- 原文: `GAMEPLAY SCENE NOT IN BUILD`
- 译文: （待翻译）

### A9. 状态：加载中
- 出处: `MainMenuController.cs`（StartGame，legacy 分支）
- 原文: `LOADING CAVES...`
- 译文: `加载中……`

### A10. 状态：下降开始
- 出处: `MainMenuController.cs`（StartGame）
- 原文: `DESCENT SEQUENCE STARTED`
- 译文: `准备启动探险任务……`

### A11. 状态：教程场景未打包
- 出处: `MainMenuController.cs`（StartTutorial）
- 原文: `TUTORIAL SCENE NOT IN BUILD`
- 译文: （已是中文）`新手教程场景未加入构建`

### A12. 状态：载入教程
- 出处: `MainMenuController.cs`（StartTutorial）
- 原文: `LOADING TUTORIAL...`
- 译文: （已是中文）`正在载入新手教程…`

### A13. 设置面板——全屏开关
- 出处: `MainMenuCanvas.prefab`
- 原文: `FULLSCREEN`
- 译文: `全屏`

### A14. 设置面板——音频分类
- 出处: `MainMenuCanvas.prefab`
- 原文: `AUDIO`
- 译文: `声音`

### A15. 设置面板——音量标签
- 出处: `MainMenuCanvas.prefab`
- 原文: `MASTER VOLUME`
- 译文: `主音量`

### A16. 设置面板——返回按钮
- 出处: `MainMenuCanvas.prefab`
- 原文: `RETURN`
- 译文: `返回`

---

## B. HUD（游戏内界面）

> 来源：`Assets/Game/Runtime/UI/GameHudController.cs`、`HeadingCompass.cs`、`CrosshairInfoDisplay.cs`。

### B1. 生命值标题
- 出处: `GameHudController.cs`
- 原文: `HEALTH`
- 译文: `HEALTH`（不翻译）

### B2. 快捷栏/装备物品名
- 出处: `GameHudController.cs`（HotbarPresenter.GetItemLabel）
- 原文: `PICKAXE` / `FLASHLIGHT` / `SOLIDGUN` / `BOMB` / `PORTALGUN`
- 译文: `探险镐` / `照明灯` / `地形发生器` / `炸弹` / `传送门发生器`

### B3. 罗盘方位缩写
- 出处: `HeadingCompass.cs`（GetHeadingLabel）
- 原文: `N` / `NE` / `E` / `SE` / `S` / `SW` / `W` / `NW`
- 译文: （不翻译）

### B4. 准星信息：宝藏/矿块统计
- 出处: `CrosshairInfoDisplay.cs`（ShowInfo）
- 原文: `FRAGILITY {0}   /   WEIGHT {1:F1} kg`
- 译文: `易碎程度：{0} / 重量：{1:F1} kg`

### B5. 准星信息：耐久度分级
- 出处: `CrosshairInfoDisplay.cs`（FormatDurabilityLabel）
- 原文: `INDESTRUCTIBLE` / `DURABILITY STURDY` / `DURABILITY EXTREMELY HIGH` / `DURABILITY HIGH` / `DURABILITY MEDIUM` / `DURABILITY LOW` / `DURABILITY VERY LOW`
- 译文: `无法摧毁` / `硬度：极高` / `硬度：很高` / `硬度：高` / `硬度：中` / `硬度：低` / `硬度：很低`

### B6. 准星信息：易碎度分级（新）
- 出处: `CrosshairInfoDisplay.cs`（ResolveFragilityTier）
- 原文: `EXTREME` / `HIGH` / `MEDIUM` / `LOW` / `VERY LOW`（按 0-6% / 7-15% / 16-29% / 30-49% / 50%+ 分档）
- 译文: （已是中文）`极高` / `高` / `中` / `低` / `极低`

### B7. 默认矿块名（矿石掉落，类型未识别时）
- 出处: `CrosshairInfoDisplay.cs`（DetectTarget）
- 原文: `Ore`
- 译文: `已开采的矿物`

> **矿石显示规则**：未开采的矿石体素显示原名（如 `黄铁矿`）；被破坏掉落后显示「已开采的」前缀（如 `已开采的黄铁矿`）。

### B8. 加载界面：品牌
- 出处: `GameHudController.cs`（BuildLoadingView，对象 "Brand"）
- 原文: `SUPERNOVA  /  DESCENT`
- 译文: 删除字段

### B9. 加载界面：标题
- 出处: `GameHudController.cs`（BuildLoadingView，对象 "Title"）
- 原文: `LOADING`
- 译文: `加载中`

### B10. 加载阶段文案
- 出处: `GameHudController.cs`（GetLoadingStageLabel）
- 原文: `PREPARING WORLD` / `GENERATING TERRAIN` / `PLACING STRUCTURES` / `BUILDING CAVE MESHES` / `READY`
- 译文: `正在整备……` / `正在补充燃料……` / `正在穿梭……` / `正在星际跃迁……` / `就绪！`

### B11. 加载界面提示
- 出处: `GameHudController.cs`（BuildLoadingView，对象 "Hint"）
- 原文: `PREPARING A SAFE LANDING...`
- 译文: `准备安全降落……`

### B12. 任务倒计时标题
- 出处: `GameHudController.cs`（BuildMissionView，对象 "Caption"）
- 原文: `TIME REMAINING`
- 译文: `剩余时间`

### B13. 性能/FPS 调试窗标题
- 出处: `GameHudController.cs`（BuildFpsDebugView）
- 原文: `PERFORMANCE / {{input:Debug/Hud}}`
- 译文: `{{input:Debug/Hud}}`

---

## C. 暂停菜单（Pause Menu）

> 来源：`GameHudController.cs`、`PauseMenuPresentation.cs`、`InputBindingSettingsView.cs`。

### C1. 眉题
- 出处: `GameHudController.cs`（CreatePauseHeader）
- 原文: `SUPERNOVA  //  SYSTEM`
- 译文: （置空）

### C2. 主面板标题
- 出处: `GameHudController.cs`（BuildPauseMainOptions）
- 原文: `PAUSED`
- 译文: `游戏暂停`

### C3. 设置面板标题
- 出处: `GameHudController.cs`（BuildPauseSettingsPanel）
- 原文: `SETTINGS`
- 译文: `设置`

### C4. 继续按钮
- 出处: `GameHudController.cs`（BuildPauseMainOptions）
- 原文: `RESUME`
- 译文: `返回`

### C5. 设置按钮
- 出处: `GameHudController.cs`（BuildPauseMainOptions）
- 原文: `SETTINGS`
- 译文: `设置`

### C6. 退出到主菜单按钮
- 出处: `GameHudController.cs`（BuildPauseMainOptions）
- 原文: `QUIT TO MENU`
- 译文: `返回主菜单`

### C7. 退出到桌面按钮
- 出处: `GameHudController.cs`（BuildPauseMainOptions）
- 原文: `QUIT TO DESKTOP`
- 译文: `退出游戏`

### C8. 操作设置按钮
- 出处: `GameHudController.cs`（BuildPauseSettingsPanel）
- 原文: `CONTROLS`
- 译文: `控制`

### C9. 全屏开关
- 出处: `GameHudController.cs`（BuildPauseSettingsPanel）
- 原文: `FULLSCREEN`
- 译文: `全屏`

### C10. 主音量标签
- 出处: `GameHudController.cs`（BuildPauseSettingsPanel）
- 原文: `MASTER VOLUME`
- 译文: `主音量`

### C11. 返回按钮
- 出处: `GameHudController.cs`（BuildPauseSettingsPanel）
- 原文: `BACK`
- 译文: `返回`

### C12. 设置即时生效提示
- 出处: `GameHudController.cs`（BuildPauseSettingsPanel，对象 "Settings Hint"）
- 原文: `CHANGES ARE APPLIED IMMEDIATELY`
- 译文: （待翻译）

### C13. 暂停角标（Kicker）
- 出处: `PauseMenuPresentation.cs`（对象 "Pause Kicker"）
- 原文: `FIELD PAUSE  //  00:00`
- 译文: 删除字段

### C14. 按键设置眉题
- 出处: `InputBindingSettingsView.cs`（Build）
- 原文: `INPUT / KEYBOARD & MOUSE`
- 译文: `输入设置`

### C15. 按键设置标题
- 出处: `InputBindingSettingsView.cs`（Build）
- 原文: `CONTROLS`
- 译文: `控制`

### C16. 恢复默认按钮
- 出处: `InputBindingSettingsView.cs`（Build）
- 原文: `RESET DEFAULTS`
- 译文: `恢复默认按键`

### C17. 返回按钮
- 出处: `InputBindingSettingsView.cs`（Build）
- 原文: `BACK`
- 译文: `返回`

### C18. 状态：选择绑定
- 出处: `InputBindingSettingsView.cs`（Build）
- 原文: `SELECT A BINDING TO CHANGE IT`
- 译文: `按下键盘以绑定`

### C19. 观察分组标题
- 出处: `InputBindingSettingsView.cs`（RebuildRows）
- 原文: `LOOK`
- 译文: `移动视角`

### C20. 鼠标灵敏度标签
- 出处: `InputBindingSettingsView.cs`（CreateSensitivityRow）
- 原文: `MOUSE SENSITIVITY`
- 译文: `鼠标灵敏度`

### C21. 未绑定
- 出处: `InputBindingSettingsView.cs`（CreateBindingRow）
- 原文: `UNBOUND`
- 译文: `未绑定`

### C22. 状态：等待按键
- 出处: `InputBindingSettingsView.cs`（BeginRebind）
- 原文: `PRESS A KEY OR MOUSE BUTTON  /  ESC TO CANCEL`
- 译文: 删除字段

### C23. 状态：绑定已保存
- 出处: `InputBindingSettingsView.cs`（BeginRebind）
- 原文: `BINDING SAVED`
- 译文: `已保存`

### C24. 状态：已取消重绑
- 出处: `InputBindingSettingsView.cs`（BeginRebind）
- 原文: `REBIND CANCELLED`
- 译文: `取消绑定`

### C25. 状态：已恢复默认
- 出处: `InputBindingSettingsView.cs`（ResetBindings）
- 原文: `DEFAULT BINDINGS RESTORED`
- 译文: `恢复默认`

---

## D. 装备菜单（Equipment / Loadout Menu）

> 来源：`Assets/Game/Runtime/UI/EquipmentLoadoutMenu.cs`。

### D1. 眉题
- 出处: `EquipmentLoadoutMenu.cs`（BuildPortraitRegion）
- 原文: `TAB  //  EQUIPMENT CONFIGURATION`
- 译文: 删除字段

### D2. 角色状态（可拖动旋转）
- 出处: `EquipmentLoadoutMenu.cs`（BuildPortraitRegion）
- 原文: `CURRENT CHARACTER  //  DRAG TO ROTATE`
- 译文: 删除字段

### D3. 角色状态（预览不可用）
- 出处: `EquipmentLoadoutMenu.cs`（BuildPortraitFromCurrentPlayer）
- 原文: `CURRENT CHARACTER  //  PREVIEW UNAVAILABLE`
- 译文: 删除字段

### D4. 标题
- 出处: `EquipmentLoadoutMenu.cs`（BuildConfigurationRegion）
- 原文: `LOADOUT`
- 译文: `背包`

### D5. 关闭提示
- 出处: `EquipmentLoadoutMenu.cs`（BuildConfigurationRegion）
- 原文: `[ TAB ]  CLOSE`
- 译文: `TAB 关闭`

### D6. 已装备标题
- 出处: `EquipmentLoadoutMenu.cs`（BuildEquippedSlots）
- 原文: `EQUIPPED  //  5 SLOTS`
- 译文: 删除字段

### D7. 空槽位
- 出处: `EquipmentLoadoutMenu.cs`
- 原文: `EMPTY`
- 译文: `空`

### D8. 选择提示
- 出处: `EquipmentLoadoutMenu.cs`（BuildEquippedSlots）
- 原文: `SELECT A SLOT, THEN CHOOSE OWNED EQUIPMENT`
- 译文: 删除字段

### D9. 已拥有装备标题
- 出处: `EquipmentLoadoutMenu.cs`（BuildOwnedGrid）
- 原文: `OWNED EQUIPMENT  //  12 CACHE CELLS`
- 译文: 删除字段

### D10. 状态：未分配
- 出处: `EquipmentLoadoutMenu.cs`
- 原文: `UNASSIGNED`
- 译文: `未装备`

### D11. 状态：已装备到槽位
- 出处: `EquipmentLoadoutMenu.cs`（RefreshView）
- 原文: `EQUIPPED  //  SLOT {n}`
- 译文: `已装备在 {n} 槽`

### D12. 状态：已拥有可用
- 出处: `EquipmentLoadoutMenu.cs`（RefreshView）
- 原文: `OWNED  //  AVAILABLE`
- 译文: 删除字段

### D13. 状态：无装备数据
- 出处: `EquipmentLoadoutMenu.cs`（RefreshView）
- 原文: `NO EQUIPMENT DATA`
- 译文: 空

### D14. 已选槽位提示
- 出处: `EquipmentLoadoutMenu.cs`（RefreshView）
- 原文: `SLOT {n} SELECTED  //  DRAG OWNED EQUIPMENT HERE`
- 译文: 删除字段

### D15. 装备物品名
- 出处: `EquipmentLoadoutMenu.cs`（复用 HotbarPresenter.GetItemLabel）
- 原文: `PICKAXE` / `FLASHLIGHT` / `SOLIDGUN` / `BOMB` / `PORTALGUN`
- 译文: `探险镐` / `照明灯` / `地形发生器` / `炸弹` / `传送门发生器`

---

## E. 任务流程（Mission）

> 来源：`Assets/Game/Runtime/Missions/MissionGameLoop.cs`。

### E1. 洞内目标（Objective）
- 出处: `MissionGameLoop.cs`（RefreshObjective）
- 原文: `LEVEL {nn} · {missionName}\nCOLLECTED  ${stored} / ${required}`
- 译文: `第 {nn} 关 · {missionName}\n已收集  ${stored} / ${required}`

### E2. 家园目标（进行中）
- 出处: `MissionGameLoop.cs`（SetupHome）
- 原文: `SHIP BASE\nNEXT: LEVEL {nn} · {missionName}`
- 译文: `基地`

### E3. 家园目标（全部完成）
- 出处: `MissionGameLoop.cs`（SetupHome）
- 原文: `CAMPAIGN COMPLETE\nALL DESCENTS CLEARED`
- 译文: 删除字段

### E4. 家园提示（全部完成）
- 出处: `MissionGameLoop.cs`（SetupHome / HideCellActionPrompt）
- 原文: `CAMPAIGN COMPLETE    BALANCE: ${credits}`
- 译文: 删除字段

### E5. 家园提示（商店在线）
- 出处: `MissionGameLoop.cs`（SetupHome / HideCellActionPrompt）
- 原文: `SHOP ONLINE    BALANCE: ${credits}`
- 译文: 删除字段

### E6. 舱体提示（全部完成）
- 出处: `MissionGameLoop.cs`（ShowCellActionPrompt）
- 原文: `ALL MISSIONS COMPLETE`
- 译文: 删除字段

### E7. 舱体提示（开始任务）
- 出处: `MissionGameLoop.cs`（ShowCellActionPrompt）
- 原文: `PRESS {{input:Gameplay/Interact}} AT CELL CONSOLE TO START {missionName}`
- 译文: `按 {{input:Gameplay/Interact}} 开始任务`

### E8. 舱体提示（自动撤离倒计时）
- 出处: `MissionGameLoop.cs`（ShowCellActionPrompt）
- 原文: `AUTOMATIC EVACUATION IN {mm:ss}    STORED ${stored} / ${required}`
- 译文: 删除字段

### E9. 收集提示
- 出处: `MissionGameLoop.cs`（DeliverOre）
- 原文: `COLLECTED  +{value}`
- 译文: 删除字段

### E10. 撤离倒计时提示
- 出处: `MissionGameLoop.cs`（RequestEvacuation）
- 原文: `AUTOMATIC EVACUATION IN {mm:ss}`
- 译文: `将在 {mm:ss} 后结束任务`

### E11. 总储存价值提示
- 出处: `MissionGameLoop.cs`（NotifyStoredValueChanged）
- 原文: `TOTAL STORED VALUE: ${value}`
- 译文: `已收集：${value}`

### E12. 结算：成功（晋级）
- 出处: `MissionGameLoop.cs`（ShowResult）
- 原文: `MISSION COMPLETE\n\nCOLLECTED ${delivered}\nBALANCE INCREASED ${reward}\nNEXT: LEVEL {nn} · {missionName}\n\nPRESS ENTER TO RETURN`
- 译文: （部分翻译）`任务完成\n\nCOLLECTED ${delivered}\nBALANCE INCREASED ${reward}\nNEXT: LEVEL {nn} · {missionName}\n\nPRESS ENTER TO RETURN`

### E13. 结算：成功（全部通关）
- 出处: `MissionGameLoop.cs`（ShowResult）
- 原文: `MISSION COMPLETE\n\nCOLLECTED ${delivered}\nBALANCE INCREASED ${reward}\nALL DESCENTS CLEARED\n\nPRESS ENTER TO RETURN`
- 译文: （部分翻译）`任务完成\n\nCOLLECTED ${delivered}\nBALANCE INCREASED ${reward}\nALL DESCENTS CLEARED\n\nPRESS ENTER TO RETURN`

### E14. 结算：迷失洞中
- 出处: `MissionGameLoop.cs`（ShowResult）
- 原文: `MISSION FAILED\n\nEVACUATION WINDOW CLOSED.\nYOU ARE LOST IN THE CAVES.\n\nPRESS ENTER TO RETURN`
- 译文: （部分翻译）`任务结束\n\nEVACUATION WINDOW CLOSED.\nYOU ARE LOST IN THE CAVES.\n\nPRESS ENTER TO RETURN`

### E15. 结算：资源不足
- 出处: `MissionGameLoop.cs`（ShowResult）
- 原文: `MISSION FAILED\n\nINSUFFICIENT RESOURCES COLLECTED\nCOLLECTED ${delivered} / ${required}\n\nPRESS {{input:UI/Submit}} TO RETURN`
- 译文: （部分翻译）`任务结束\n\nINSUFFICIENT RESOURCES COLLECTED\nCOLLECTED ${delivered} / ${required}\n\nPRESS {{input:UI/Submit}} TO RETURN`

### E16. 调试加钱提示（仅编辑器/开发版）
- 出处: `MissionGameLoop.cs`（Update）
- 原文: `DEBUG +$100    BALANCE: ${credits}`
- 译文: （待翻译）

---

## F. 世界空间 UI（World-space UI）

> 来源：`Assets/Game/Runtime/UI/SpawnPointIndicator.cs`。（贵重物品价值、价值损失飘字为纯数值，已排除。）

### F1. 出生点指示距离
- 出处: `SpawnPointIndicator.cs`
- 原文: `Arrival\n{0:0}m`
- 译文: `传送门\n{0:0}m`

---

## G. 商店（Shop）

> 来源：`Assets/Game/Runtime/Shop/ShopProductDisplay.cs`、`HomeShopController.cs`、`Assets/Game/Config/Shop/*.asset`。

### G1. 已拥有
- 出处: `ShopProductDisplay.cs`（RefreshView）
- 原文: `OWNED`
- 译文: `已拥有`

### G2. 商品价格与购买提示
- 出处: `ShopProductDisplay.cs`（RefreshView）
- 原文: `${price}\nPRESS {{input:Gameplay/Interact}} TO BUY`
- 译文: `${price} 按 {{input:Gameplay/Interact}} 购买`

### G3. 购买成功
- 出处: `HomeShopController.cs`（RefreshMissionPrompt）
- 原文: `PURCHASE COMPLETE    BALANCE  ${credits}`
- 译文: `购买成功 -${credits}`

### G4. 余额不足
- 出处: `HomeShopController.cs`（RefreshMissionPrompt）
- 原文: `INSUFFICIENT FUNDS    BALANCE  ${credits}`
- 译文: `余额不足`

### G5. 商店在线
- 出处: `HomeShopController.cs`（RefreshMissionPrompt）
- 原文: `SHOP ONLINE    BALANCE  ${credits}`
- 译文: 删除字段

### G6. 商店商品显示名（本轮缩减为 3 个，displayName 已回退英文）
- 出处: `Assets/Game/Config/Shop/*.asset`
- 原文 → 译文:
  - `SolidGun`（SolidGunProduct.asset）→ （待翻译）
  - `FlashLight`（FlashlightProduct.asset）→ （待翻译）
  - `PortalGun`（PortalGunProduct.asset）→ （待翻译）

---

## H. 场景内烘焙文本（Scene-baked text）

> 直接序列化在 `.scene` 文件中。

### H1. InfiniteCaves.scene / CombatTest.scene 内置 HUD 文字
- 出处: `Assets/Scenes/InfiniteCaves.scene`、`Assets/Scenes/CombatTest.scene`
- 原文: `HEALTH` / `PAUSED` / `RESUME`
- 译文: `HEALTH`（不翻译） / `暂停` / `继续`

### H2. VoxelStructureEditor.scene 内置 HUD 文字
- 出处: `Assets/Scenes/VoxelStructureEditor.scene`
- 原文: `HEALTH`
- 译文: `HEALTH`（不翻译）

### H3. SpawnShelterStoneTest.scene 教学提示（现为新手教程场景）
- 出处: `Assets/Scenes/SpawnShelterStoneTest.scene`
- 原文 → 译文:
  - `{{input:Gameplay/PrimaryAction}} to Mine\n←` → `{{input:Gameplay/PrimaryAction}} 破坏石头\n←`
  - `{{input:Gameplay/Move}} to Move` → `{{input:Gameplay/Move}} 移动`
  - `{{input:Gameplay/ThrowPickaxe}} to throw/summon pickaxe` → `{{input:Gameplay/ThrowPickaxe}} 扔出/召回探险镐`
  - `{{input:Gameplay/Jump}} to Jump` → `{{input:Gameplay/Jump}} 跳跃`
  - `{{input:Gameplay/Interact}} to Interact` → `{{input:Gameplay/Interact}} 交互`
  - `{{input:Gameplay/PrimaryAction}} to hit monsters` → `{{input:Gameplay/PrimaryAction}} 攻击怪物`
  - `Hold {{input:Gameplay/Crouch}} to Crouch` → `Hold {{input:Gameplay/Crouch}} 蹲下`

### H4. SpawnShelterStoneTest.scene 武器拾取提示
- 出处: `Assets/Scenes/SpawnShelterStoneTest.scene`
- 原文: `E  PICK UP\nRIFLE` / `E  PICK UP\nSMG` / `E  PICK UP\nSOLID GUN` / `E  PICK UP\nPORTAL GUN`
- 译文: 删除字段

### H5. SpawnShelterStoneTest.scene 提示牌
- 出处: `Assets/Scenes/SpawnShelterStoneTest.scene`
- 原文: `Try more props!`
- 译文: `更多道具`

---

## I. 数据配置显示名（Data display names）

> 显示在准星信息、任务目标等处（读取 `DisplayName`）。

### I1. 方块类型名（准星信息）
- 出处: `Assets/Game/Config/VoxelTypes/Terrain/*.asset`
- 原文 → 译文:
  - `Stone` → `石头`
  - `Solid Stone` → `坚固的石头`
  - `Bedrock` → `基岩`
  - `Packed Dirt` → `泥土`
  - `Default` → `默认`

### I2. 矿物类型名（准星信息）
- 出处: `Assets/Game/Config/VoxelTypes/Mineral/*.asset`
- 原文 → 译文:
  - `Amethyst` → `紫水晶原石`
  - `Copper` → `铜矿`
  - `YellowIron` → `黄铁矿`
  - `Diamond` → `钻石`
  - `Obsidian` → `黑曜石`

### I3. 结构方块类型名（准星信息）
- 出处: `Assets/Game/Config/VoxelTypes/Structural/*.asset`
- 原文 → 当前值:
  - `Structure Brick` → `硬质岩石`
  - `Fortress Brick` → `堡垒砖块`
  - `Rusted Iron` → `铁块`
  - `Tiger Rock` → `花岗岩`
  - `Plank` → `木板`
  - `Worn Brick` → `砖块`

### I4. 宝藏名（准星信息）
- 出处: `Assets/Game/Config/Treasures/*.asset`
- 原文 → 译文:
  - `Bones` → `化石`
  - `CauldronTreasure` → `炼药锅`
  - `HolyBookTreasure` → `圣书`

### I5. 关卡名（任务目标）
- 出处: `Assets/Game/Config/Levels/*.asset`
- 原文: `COMBAT TEST`（CombatTestLevel.asset）
- 译文: （不翻译）

### I6. 装备名（喷气背包）
- 出处: `Assets/Game/Config/Equipment/Jetpack.asset`
- 原文: `Jetpack`
- 译文: `喷气背包`

---

## J. 输入绑定动作名（派生文本）

> 来源：`GameInputDefinitions.cs`（动作名）+ `GameInput.cs`（中文映射）。

### J1. 操作分组标题（由 ActionMap 名派生）
- 出处: `InputBindingSettingsView.cs`（CreateMapHeader）
- 原文: `GAMEPLAY` / `UI` / `DEBUG` / `SPECTATOR` / `STRUCTUREEDITOR` / `EXAMPLES`
- 译文: （待翻译）

### J2. 动作行标签（由 Action 名派生）
- 出处: `GameInputDefinitions.cs` + `GameInput.cs`
- 原文 → 译文:
  - `Move` → `移动角色`
  - `Look` → `移动视角`
  - `Jump` → `跳跃`
  - `Crouch` → `蹲下`
  - `Sprint` → `冲刺`
  - `PrimaryAction` → `主交互`
  - `SecondaryAction` → `副交互`
  - `Interact` → `交互`
  - `ThrowPickaxe` → `投掷探险镐`
  - `ToggleEquipment` → `切换喷气背包`
  - `Hotbar1`–`Hotbar5` → `装备栏1`–`装备栏5`
  - `HotbarScroll` → `滚动切换装备栏`
  - `TogglePerspective` → `切换视角`
  - `Pause` → `暂停`
  - `ToggleLoadout` → `切换负载`
  - `Navigate` → `导航`
  - `Submit` → `提交`
  - `Cancel` → `取消`
  - `Point` → `点数`
  - `Click` → `点击`
  - `RightClick` → `鼠标右键`
  - `MiddleClick` → `鼠标中键`
  - `ScrollWheel` → `滚轮`
  - `Mission` → `任务`
  - `Hud` → `HUD`
  - `FlyToggle` → `切换飞行模式`
  - `Smile` → `微笑`
  - `Hit` → `击中`
  - `Die` → `死亡`
  - `Recover` → `重生`
  - `FlyUp` → （不翻译，保留 `FLY UP`）
  - `FlyDown` → （不翻译，保留 `FLY DOWN`）
  - `FlyFast` → （不翻译，保留 `FLY FAST`）
  - `LookHold` → （不翻译，保留 `LOOK HOLD`）
  - `OrbitHold` → （不翻译，保留 `ORBIT HOLD`）
  - `Up` → （不翻译，保留 `UP`）
  - `Down` → （不翻译，保留 `DOWN`）
  - `Fast` → （不翻译，保留 `FAST`）
  - `Save` → （不翻译，保留 `SAVE`）
  - `Paint` → （不翻译，保留 `PAINT`）
  - `Erase` → （不翻译，保留 `ERASE`）
  - `ToggleFillMode` → （不翻译，保留 `TOGGLE FILL MODE`）
  - `Fill` → （不翻译，保留 `FILL`）
  - `ClearFillBox` → （不翻译，保留 `CLEAR FILL BOX`）
  - `PortalReset` → （不翻译，保留 `PORTAL RESET`）
  - `PrototypeReset` → （不翻译，保留 `PROTOTYPE RESET`）

---

## 附注

1. `{{input:...}}` 为按键提示占位符，运行时替换成实际按键，翻译时原样保留。
2. `{0}`、`{1}`、`{1:F1}`、`{nn}`、`{mm:ss}`、`{n}` 为格式化占位符，请保留。
3. `${...}` 为金额数值占位符，`\n` 为换行符，请保留。
4. J 节为数据派生文本，汉化需在 `GameInputDefinitions`/`GameInput` 处建中文映射。
5. G6、I3 已在本轮结构变化中被回退/改动，请按当前「原文 → 译文」重新确认。
