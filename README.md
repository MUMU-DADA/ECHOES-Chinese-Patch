# ECHOES 简体中文补丁项目

此目录集中保存 ECHOES 简体中文补丁的发布成品、翻译资料、插件源码、构建暂存文件、辅助工具和外部归档。游戏本体位于本目录的上一级。

## 最终成品

可直接交付或安装的文件位于 `发布成品`：

- `发布成品\ECHOES_简体中文补丁_v1.0.0.zip`
- `发布成品\ECHOES_简体中文补丁_v1.0.0.sha256.txt`
- 补丁 ZIP 内含游戏根目录下的“卸载汉化.cmd”，双击后按提示即可移除汉化插件。

当前 ZIP SHA-256：

`6D41BF74E3B227145BA6B03C1763D9E79C1FEC75DFFBD279409365B6A19451C6`

安装时将 ZIP 内的全部文件解压到上一级游戏目录，即 `ECHOES.exe` 所在位置。

## 总目录分类

| 目录 | 内容 | 是否交付玩家 |
|---|---|---|
| `发布成品` | 最终补丁 ZIP 与 SHA-256 校验文件 | 是 |
| `Translation` | 原文目录、简体中文译文映射、术语与人物语气规范 | 否 |
| `PatchSource` | BepInEx/Harmony 汉化插件 C# 源码 | 否 |
| `Patch` | 发布包构建暂存目录 | 否 |
| `Tools` | 构建、提取、字体生成、验证工具及归档材料 | 否 |

`汉化项目索引.txt` 是便于纯文本查看的简版索引，本 README 是完整说明。

## Tools 分类

| 目录 | 内容 |
|---|---|
| `Tools\Archives` | 外部原始压缩包；字体和加载器的详细哈希见其中的 `README.md` |
| `Tools\Artifacts` | 提取的视频、字体预览等审计材料，不进入补丁 |
| `Tools\Audits` | 历次发布 ZIP 的解压审计副本；`03_final-v1.0.0` 为最终版本 |
| `Tools\BepInEx` | 正式构建使用的 BepInEx 5.4.23.5 x64 解压文件 |
| `Tools\CompileStubs` | 仅用于离线编译验证的桩文件，正式构建禁止使用 |
| `Tools\FontBuilder` | 按最终译文裁剪生成中文 TTF 的工具源码 |
| `Tools\FusionPixel12` | 当前采用的 Fusion Pixel Font 12px 构建输入与许可证 |
| `Tools\MediaExtractor` | 媒体审计工具源码 |
| `Tools\PluginChecks` | 汉化插件逻辑测试程序 |

## 常用脚本

| 脚本 | 用途 |
|---|---|
| `Tools\Build-Patch.ps1` | 生成翻译表、字体、插件、发布清单、ZIP 和 SHA-256 |
| `Tools\Validate-Patch.ps1` | 检查翻译覆盖、占位符、字体、加载器和发布范围 |
| `Tools\Build-Translation.ps1` | 从译文映射生成运行时 `translations.json` |
| `Tools\Compile-Plugin.ps1` | 使用真实 BepInEx/Harmony 编译插件 |
| `Tools\Extract-EchoesText.ps1` | 从游戏资源和程序集提取日文文本目录 |

## 当前汉化范围

- 剧情、三个结局、日记、物品、菜单和游戏内教程已翻译。
- 使用 Fusion Pixel Font 12px 简体中文像素字体。
- 日语音素、假名答案和游戏判定逻辑保持原样。
- 少女的虚构语句只在相关音素全部解读后追加中文释义。
- 教程视频保持原版，没有替换、重编码或打入补丁。
- “卸载汉化.cmd”只删除 `BepInEx\plugins\EchoesChinese`，不会删除其他插件或游戏存档。

## 验证状态

- 405 条运行时翻译通过覆盖和占位符检查。
- 最终发布包包含 30 个有效载荷文件，均与发布清单的大小和 SHA-256 一致。
- 插件使用真实 BepInEx 5.4.23.5 x64 与 Harmony 2.9.0.0 编译。
- 发布包不包含视频、提取媒体、编译桩或重复许可证。
- 按制作要求未启动游戏进行实机测试，实际显示效果由用户后续验证。
