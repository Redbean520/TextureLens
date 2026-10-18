# Texture Lens（贴图透镜）

RimWorld 1.6 的贴图查看器：在信息卡按 **T** 打开高清查看窗——物品按游戏真实材质渲染，角色立绘实时渲染（含待机动作与手持武器），支持 Ctrl+滚轮缩放、朝向旋转、128~2048 多档尺寸和 PNG 导出。需要 Harmony。

## 目录

    About\            模组元数据（About.xml / ModIcon.png / Preview.png）
    Assemblies\       编译产物 TextureLens.dll + 0Harmony.dll
    Defs\             预留
    Languages\        中英文文本（Keyed）
    Source\TextureLens\   C# 工程（测试版才有，发布包不含）
    LoadFolders.xml
    tools\            打包脚本
    docs\             设计/验收/工坊文案

## 安装

- 需要 RimWorld **1.6** + **Harmony**
- 发布包解压后整个文件夹放进 `Mods/TextureLens/`
- 游戏内模组列表勾选，重启

## 构建

    dotnet build Source\TextureLens\TextureLens.csproj -c Release

编译产物会自动复制到 `Assemblies\`。

## 测试版 / 发布版

- **测试版** = 本仓库（含 `Source\`），游戏直接读这个目录
- **发布版** = 运行 `tools\make-release.ps1`，产出到工作区的 `release\TextureLens\`（无源码），压缩包挂 GitHub Release
- 分支：`main` = 发布版，`beta` = 测试版；发版打 tag `v<版本>`

## 版本号（三处一致）

1. `About\About.xml` 的 `<modVersion>`
2. `Source\TextureLens\TextureLensMod.cs` 的 `const string Version`
3. `Source\TextureLens\TextureLens.csproj` 的 `<Version>`

## 发版

    powershell -NoProfile -ExecutionPolicy Bypass -File tools\make-release.ps1

## 许可

- 作者：Redbean
- 需要 RimWorld 1.6 与 Harmony（`brrainz.harmony`）
