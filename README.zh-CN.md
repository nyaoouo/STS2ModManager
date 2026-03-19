# STS2 Mod 管理器

一个用于 Slay the Spire 2 的简易 Windows 模组管理器。

## 程序功能

这个工具可以帮你管理模组，不需要再手动移动文件夹。

- 显示当前在 `mods` 文件夹中已启用的模组。
- 显示当前放在单独“已禁用模组”文件夹中的模组。
- 通过在两个目录之间移动模组文件夹来启用或禁用模组。
- 支持通过拖放或启动时传入路径的方式导入模组 `.zip` 压缩包。
- 检测重复的模组 ID，并让你选择保留现有模组还是替换为新模组。
- 可以通过 Steam 重启 Slay the Spire 2。
- 内置存档管理器，可在原版存档槽和模组存档槽之间转移存档。
- 支持英文和简体中文界面。

程序会自动尝试查找 Slay the Spire 2 的安装目录，检查内容包括上级目录、Steam 库目录以及常见安装路径。

## 发布版本

当前会构建两个可执行文件版本：

- `ModManager.FrameworkDependent.exe`
- `ModManager.NativeAot.exe`

### Framework-dependent 版本

如果你的电脑已经安装了所需的 .NET Desktop Runtime，可以使用这个版本。

- 下载体积更小。
- 依赖电脑上已安装匹配版本的 .NET 运行时。
- 对开发阶段的重新构建和调试更友好。

### Native AOT 版本

如果你想提供给普通玩家一个更省事的独立可执行文件，可以使用这个版本。

- 是自带运行环境的 Windows 可执行文件。
- 不需要额外安装 .NET 运行时。
- 通常启动更快。
- 文件体积通常更大。

## 我该用哪个版本？

- 如果你要分发给大多数玩家：使用 `ModManager.NativeAot.exe`。
- 如果你自己已经装好了现代 .NET 开发环境：使用 `ModManager.FrameworkDependent.exe` 也完全可以。

## 构建

在当前目录运行构建脚本：

```bat
STS2ModManager.build.cmd
```

可选构建模式：

```bat
STS2ModManager.build.cmd framework
STS2ModManager.build.cmd aot
STS2ModManager.build.cmd all
```