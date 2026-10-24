# 2.12.0 工坊更新准备（Redbean 手记）

> 游戏内「上传到创意工坊」的实际行为（Verse.Steam.Workshop.SetWorkshopItemDataFrom）：
> - 已有条目（PublishedFileId.txt 存在）走 StartItemUpdate → **标题、预览图、标签、文件内容会被覆盖**；
> - **描述（Description）只在首次创建条目时写入**，更新时不动 → 工坊页上的旧简介不会自动变！
> - 更新说明是自动生成的 "[Auto-generated text]: Update on <时间>."，游戏内不能自定义。
> - 内容 = 整个 Mods\TextureLens 目录（当前 10 个文件 / 3.03 MB，无源码、无残留文件）。
> - 预览图 = About\Preview.png（1280x720）；标签 = About 里的 supportedVersions 自动加 "1.6"/"1.7"。

## 上传前检查清单

- [x] Mods\TextureLens 是 2.12.0 发布版（9 个包内文件 SHA256 与 release\TextureLens\TextureLens-2.12.0 全部一致）
- [x] About\PublishedFileId.txt = 3822226073（10 字节，无换行）→ 更新现有工坊条目，不会新建重复条目
- [x] About\PublishedFileId.txt 不在发布包 / 发布 zip 里（对外分发的包保持干净）
- [x] Prefs.xml <devMode>True</devMode>（否则 Mod 配置页没有上传按钮）
- [x] Steam 已启动（SteamManager.Initialized）
- [x] Mod 在 ModsConfig.xml 的 activeMods 里
- [x] 无 *.locked 残留、无 Source\、无 bin\obj
- [ ] 想换工坊封面图就先替换 About\Preview.png（1280x720 PNG）再上传

## 上传步骤

1. 启动 RimWorld（当前进程已关闭，下次启动会加载 2.12.0）
2. 主菜单 → **Mod 配置** → 选中 **Texture Lens**
3. 右下角 **上传到创意工坊**（dev mode 才显示）→ 确认
4. 成功后会自动打开工坊页；条目 id 不变：https://steamcommunity.com/sharedfiles/filedetails/?id=3822226073

## 工坊描述建议（英文，直接贴到工坊页 Description）

Texture Lens
A lightweight tool mod that lets you "zoom in" on item and character textures: press T or a hotkey to open a high-definition viewer. Items are rendered with their real materials, characters are previewed in real time with idle animations and drafted weapons, plus a transparent floating window and PNG export.

Select an item or character and press T (or the hotkey) to open a zoomable, rotatable viewer; item textures go through the game's own material / mask / dye pipeline, so colors, masks and dyeing match the map exactly. It also includes a transparent real-time character preview overlay that supports idle animations and shows the weapon in hand when drafted. Supports **RimWorld 1.6 and 1.7** and requires Harmony.

✨ Features
• One-click viewing: a T button in the top-right corner of the info card, or a global hotkey (rebindable in settings).
• Real material rendering: item textures go through the game's own material / mask / dye pipeline—not upscaled icons—so colors match the map.
• Real-time character rendering: the portrait refreshes in real time, supports idle animations, and shows the weapon in hand when drafted.
• Transparent floating preview window: appears when a pawn is selected; Ctrl+scroll zooms the content, Ctrl+drag pans, double-click resets.
• Multiple sizes and rotation: 128 / 256 / 512 / 1024 / 2048 render sizes, four-way rotation, scroll to zoom.
• PNG export: export the current view as a PNG with the transparent border trimmed automatically.

🧊 IsoRim Waylith 3D (1.7 only, optional)
If you have **IsoRim Waylith** (packageId Peren.IsoRim17) installed, the viewer can show the actual 3D model IsoRim renders instead of the 2D texture:
• Items, buildings and pawns (humans, animals, mechs) are all supported; the footer gets a "3D model" toggle.
• The floating pawn preview in the corner shows the 3D model too (hover and click the 3D button).
• Apparel, hair, held weapons and shields are included; pawns are re-captured on the preview refresh interval, so idle animation stays animated.
• Exported PNGs include the 3D view.
• Completely optional and reflection-based: TextureLens does not ship or hard-reference IsoRim, and without it everything behaves exactly as before.

Requirements
• RimWorld 1.6 (Harmony 2) or 1.7 (Harmony 3) — the mod ships both builds, one Workshop item covers both versions.
• Harmony by brrainz.

Source / bugs: https://github.com/Redbean520/TextureLens

## 工坊描述（中文版，可选贴在英文下面）

Texture Lens —— 贴图查看器
物品与角色贴图放大查看：信息卡右上角按 T（或全局快捷键）打开高清查看窗。物品按游戏真实材质渲染，颜色 / 遮罩 / 染色与地图一致；角色立绘实时渲染，支持待机动作与征召时显示手中武器；另有透明悬浮预览窗（Ctrl+滚轮缩放内容、Ctrl+拖拽平移、双击复位）、四向旋转、128~2048 多档尺寸、PNG 导出（自动裁透明边）。

装 **IsoRim Waylith**（1.7）时，物品 / 建筑 / 角色（人、动物、机械体）的查看窗和左下角角色预览浮窗都能显示 3D 模型，页脚 / 顶部 3D 按钮随时切回原版贴图。完全可选：不装 IsoRim 时行为和以前一模一样。

支持 RimWorld 1.6 / 1.7（一个工坊条目同时包含两个版本），需要 Harmony。

## 更新说明（发给玩家 / 群里的版本，游戏内 changelog 只能自动生成）

2.12.0
- 左下角的角色预览浮窗现在也能显示 IsoRim 的 3D 模型（顶部 3D 按钮切换，设置项 previewShow3D）
- 人 / 动物 / 机械体等所有 Pawn 类型通用，没模型或渲染失败自动回退 2D 立绘
- 浮窗的缩放 / 平移 / 刷新间隔对 3D 同样生效

2.11.0
- 查看窗支持角色（人 / 动物 / 机械体）的 IsoRim 3D 预览

2.10.0
- 查看窗支持 IsoRim 3D 模型（物品 / 建筑），支持 PNG 导出

## 上传后

- [ ] 刷新工坊页，确认标题、标签（1.6 / 1.7）、文件大小、更新日期
- [ ] 若描述还是旧的：把上面「工坊描述建议」贴进 Description（更新不会覆盖描述）
- [ ] 想发 GitHub Release：git push origin main + tags（本地 tag v2.12.0 已打，尚未 push）
- [ ] 订阅用户更新后需要重启游戏才会加载新 DLL
