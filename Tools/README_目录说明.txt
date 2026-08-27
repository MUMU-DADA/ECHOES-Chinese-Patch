Tools 目录说明

Archives   外部原始压缩包，按 Fonts 和 Loaders 分类。
Artifacts  文本/媒体/字体审计过程中生成的辅助材料，不进入补丁。
Audits     发布 ZIP 的解压审计副本；03_final-v1.0.0 是最终版本副本。
BepInEx    从 x64 原包解压的真实构建依赖。
CompileStubs 仅用于离线编译验证，发布脚本会拒绝将其打包。
FontBuilder、MediaExtractor、PluginChecks 为自制辅助工具源码。
FusionPixel12 是最终采用的 12px 字体构建输入与许可证。

最终交付文件位于游戏根目录下的“发布成品”文件夹。

补丁内附带“卸载汉化.cmd”。它只删除 BepInEx\plugins\EchoesChinese，保留其他 BepInEx 插件和游戏文件。
