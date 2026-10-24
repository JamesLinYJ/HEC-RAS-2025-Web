# 来源与依赖

本项目包含来自 [JamesLinYJ/HEC-RAS-2025-Renamed](https://github.com/JamesLinYJ/HEC-RAS-2025-Renamed) 的 C# 代码及相关资源，保留源码原有署名和权利信息，不对上游内容另行授予统一替代许可。

外部依赖由项目文件中的 `PackageReference` 引入。项目随附的四个固定 NuGet 包及校验值列于 `backend/Dependencies/packages.json`。

- `H5.Bindings`：保留绑定层的兼容性修订包，通过 NuGet 参加构建。
- `Hec.Dss.Runtime.osx-arm64`：由 HEC 官方 hec-dss `v7-IT-2` 源码构建的 macOS arm64 运行库。许可证及来源在包内。
- `Hdf5.ThreadSafe.Runtime.osx-arm64`：本项目自行打包，**不是 HDF Group 官方 NuGet 发行包**。底层库使用官方 HDF5 2.1.1 源码并启用线程安全。上游将线程安全核心库与 HL 高层库组合标为不受支持，不能据此宣称全部 HL 接口均线程安全。包内保留许可证、来源和构建信息。
- `Ras.VectorTiles.Schema`：保留矢量瓦片协议的消息定义包。

其他依赖（包括 GDAL 和 Google.Protobuf）的版本及许可由各 NuGet 包声明。随附依赖的许可与来源声明保留在对应包内。
