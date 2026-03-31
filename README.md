# 🐺 郊狼相生 (Slay the Spire 2 - TazeU)

把鸡煲的电球和郊狼连接，电流相生不再只对敌人有效了！

> [!IMPORTANT]
> **完整的使用指南、详细配置项与开发文档现已迁移至我们的官方文档站点！**  
> 🔗 **[官方中文文档 (Usage & Dev Guide)](https://star-whisper9.github.io/sts2-tazeu/zh/)**  
> 🔗 **[Official English Documentation](https://star-whisper9.github.io/sts2-tazeu/en/)**

**⚠️安全警告**

> **使用前必读**：本 Mod 控制物理外设，请注意安全！务必在游玩前通过“测试电击”功能确认设定的强度在您的生理承受范围之内。请务必设置合理的“伤害上限”以防止高额伤害转换为过高电流。若感身体不适请立即切断连接或按下“断开连接”快捷键。本模组作者不对任何因使用本模组造成的身体不适或设备危险负责。

_游玩请适当哦~_

## ✨ 核心特性

- **可视化配置**：基于 ModConfig，支持自由调节强度、修改波形。
- **Combo 递增系统**：连续电球强度递增（*爽飞局专享*）。
- **科学的电流算法**：引入 Stevens 幂律，将伤害平滑映射到您的身体。
- **一对多联机共享**：仅需一个扫码，支持多台郊狼手机连接（1 人游玩，N 人被电）。
- **纯内网直连 / 远程控制**：低延迟无云端，可通过修改 IP 及内网穿透实现异地调教。

详细的使用教程与参数调优指北，请点击上方链接移步至我们的 **官方文档**。

## 📥 获取与安装

1. 确保已安装 [ModConfig](https://www.nexusmods.com/slaythespire2/mods/27) 前置模组。
2. 前往 [Releases](https://github.com/star-whisper9/sts2-tazeu/releases) 下载最新版本的发行包。
3. 将解压出来的文件夹放入游戏根目录的 `mods/` 文件夹中。

> 详情及连接流程见文档的 [快速入门](https://star-whisper9.github.io/sts2-tazeu/zh/usage/) 章节。

## 🛠️ 开发与贡献

本模组基于 Godot 4.5 与 C# 开发，核心特性包括原生 TcpListener 桥接的 WebSocket 以及一套基于反射实现的弱依赖前置挂载。

针对二次开发或详细的代码架构解析，请参见文档站的 [开发文档](https://star-whisper9.github.io/sts2-tazeu/zh/development/)。

欢迎 PR 与提出议题！

## 📄 License

通过 Apache License 2.0 许可协议授权。详情请参见 [LICENSE](LICENSE) 文件。
