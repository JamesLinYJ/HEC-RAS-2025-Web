# 依赖

`packages.json` 固定本项目随附 NuGet 包的身份、版本、SHA-256 和必需文件。构建前由 `Tools/verify_packages.py` 校验；普通外部依赖继续由 NuGet 恢复。

该目录不保存第三方实现源码、源修复工具或本地开发日志。NuGet 包中的许可证和来源信息随包保留。
