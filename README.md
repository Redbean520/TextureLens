# Texture Lens（贴图透镜）

RimWorld **1.6 / 1.7** 的贴图查看器：在信息卡按 **T** 打开高清查看窗——物品按游戏真实材质渲染，角色立绘实时渲染（含待机动作与手持武器），支持 Ctrl+滚轮缩放、朝向旋转、128~2048 多档尺寸和 PNG 导出。需要 Harmony。

装了 **IsoRim Waylith**（1.7 的 3D 渲染模组）时，物品和建筑的查看窗会直接显示它渲染的 **3D 模型**（页脚可以切回原版贴图）。这一项是完全可选的：没装 IsoRim 时行为和以前一模一样。

## 目录

    About\            模组元数据（About.xml / ModIcon.png / Preview.png）
    1.6\Assemblies\   1.6（net472）编译产物 TextureLens.dll + 0Harmony.dll
    1.7\Assemblies\   1.7（net10.0）编译产物 TextureLens.dll（Harmony 由 Harmony 模组提供）
    Defs\             预留
    Languages\        中英文文本（Keyed）
    Source\TextureLens\   C# 工程（测试版才有，发布包不含）
    LoadFolders.xml
    tools\            打包脚本
    docs\             设计/验收/工坊文案

## 安装

- 需要 **Harmony**：
  - RimWorld 1.6 需要 Harmony 2（brrainz.harmony 的 1.6 版本）
  - RimWorld 1.7 需要 Harmony 3（brrainz.harmony 的 1.7 版本）
- 发布包解压后整个文件夹放进 Mods/TextureLens/
- 游戏内模组列表勾选，重启

## 与 IsoRim Waylith 的配合

- 只在 **1.7** 有效（IsoRim 仅支持 1.7），并且需要游戏里装了 IsoRim（packageId `Peren.IsoRim17`）。
- 通过反射调用 IsoRim，**不硬引用它的程序集**，所以发布包不需要带 IsoRim、也不会因为它缺失而报错。
- 查看窗显示 3D 模型时，页脚多一个「3D 模型」按钮：亮着 = 显示 3D 模型，点一下切回原版贴图（选择会记进 Mod 设置 `viewerShow3D`）。
- PNG 导出同样会导出 3D 模型那张图。
- 角色（pawn）目前仍然走 2D 立绘；只有物品 / 建筑用 3D 模型。
- 3D 模型是静态的，只在切换朝向 / 打开新窗口时重渲染一次。

## 构建

    dotnet build Source\TextureLens\TextureLens.csproj -c Release

工程是多目标（net472;net10.0），一次编译两个版本，产物自动复制到：

- 1.6\Assemblies\TextureLens.dll（net472，并附带 Harmony 2 的 0Harmony.dll）
- 1.7\Assemblies\TextureLens.dll（net10.0，不附带 0Harmony，运行时由 Harmony 模组提供）

LoadFolders.xml 会让 1.6 只加载 1.6 目录、1.7 只加载 1.7 目录（根目录的 Defs/Languages 等两边共用）。

参考程序集：RimWorld 1.6 用 Krafs.Rimworld.Ref 1.6.4871 + Lib.Harmony 2.4.2；1.7 用 Krafs.Rimworld.Ref 1.7.4985-unstable + Lib.Harmony 3.0.0-rc.3。

## 测试版 / 发布版

- **测试版** = 本仓库（含 Source\），游戏直接读这个目录
- **发布版** = 运行 tools\make-release.ps1，产出到工作区的 release\TextureLens\（无源码），压缩包挂 GitHub Release
- 分支：main = 发布版，beta = 测试版；发版打 tag v<版本>

## 版本号（三处一致）

1. About\About.xml 的 <modVersion>
2. Source\TextureLens\TextureLensMod.cs 的 const string Version
3. Source\TextureLens\TextureLens.csproj 的 <Version>

## 发版

    powershell -NoProfile -ExecutionPolicy Bypass -File tools\make-release.ps1

## 许可

- 作者：Redbean
- 需要 RimWorld 1.6 / 1.7 与 Harmony（brrainz.harmony）
