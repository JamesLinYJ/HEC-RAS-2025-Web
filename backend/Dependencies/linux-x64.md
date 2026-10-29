# Linux x64 原生运行库

`Packages` 中的 Linux 包构建于 Ubuntu 26.04 x86-64（glibc 2.43）。当前 HDF5 核心库要求至少 GLIBC_2.38，DSS 库要求 GLIBC_2.43；这些版本需求不能推广到旧版发行版。运行还需要 `libgfortran.so.5`、`libz.so.1`、`libsz.so.2` 及其系统依赖。GDAL 使用 MaxRev 3.13.3.557 的 Linux x64 包。

HDF5 2.1.1 来源为 HDF Group commit `4fde4fd787744b6a19b12032f4dad0383fbe6fbd`，源码归档 SHA-256 `5849ed7a81be6bc84ff8aa65dd966430adf0daf71e6bcb734b7a37474f92c859`。启用线程安全核心、HL、zlib 和 SZIP/AEC；关闭测试、工具、示例、C++、Fortran。上游将线程安全核心与 HL 同时启用标记为不受支持，该组合不能推断 HL 普遍线程安全。发布时必须成对替换 GDAL 附带的同 SONAME 的核心及 HL 库，运行时仍检查核心线程安全能力。

DSS 使用 USACE HEC 的 hec-dss v7-IT-2，commit `34fcb129c7a7c48b02591eb012a44ea3b0823cd6`，归档 SHA-256 `eceeb5de681fef415b136b94c7524fa697cded1b443780499cae3b7ee3d7ef25`。Linux 编译保留 `-Werror`，对独立构建目录作三处修正：使用 glibc `strcasestr`；将 `ztsIrregMergeBlocks.c` 和 `zprintFileInfo.c` 中用于日志的 `messageString` 扩为 256 字节。NuGet 包含各源许可证及库哈希；产品目录不包含第三方源码。
