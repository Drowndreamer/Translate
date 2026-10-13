# Translate 项目约定

- 查询框架、SDK、API 或 CLI 用法时，先通过 Context7 resolve-library-id 和 query-docs 获取当前文档。一般业务逻辑或代码审查无需查询。
- 本项目采用 C# / WPF / .NET 10，Windows x64。保持主界面简洁。
- 输入钩子禁止执行 UI Automation、剪贴板事务、等待或网络请求。保留短按右键和右键拖动行为。
- 不把真实 API Key 写进源码、测试数据、脚本、日志、截图或发布包。真实联调只能在运行时通过内存 / 标准输入注入凭据。
- 设置使用 Windows DPAPI 当前用户加密，配置在 LocalAppData，发布包不包含用户设置。
- 修改核心输入、取词或请求逻辑后运行测试；修改 UI 后查看实际截图。发布使用 build.ps1 -Package。
- 在 Windows 上递归清理前验证绝对目标路径，并使用同一 shell 的原生命令。
