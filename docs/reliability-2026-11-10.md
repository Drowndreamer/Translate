# Translate 0.2.2 稳定性与性能验证

2026-11-25，使用者反馈这段时间持续使用未再遇到问题，准备将已在本机验证的 0.2.1、0.2.2 修复统一作为 0.2.2 公开发布。下文保留各轮检查的实际日期、结果和待确认事项。

验证日期：2026-11-10，本机 Windows x64 / .NET 10；交互界面使用本机 175% 缩放。所有 API 测试使用模拟响应及测试凭据，没有调用真实翻译服务或读取真实 Key。既有运行实例和用户加密配置没有被替换。

## 闪退原因与复现

本机 Windows Application 日志在 2026-11-09、2026-11-10 记录了 Translate.exe 未处理的 System.Reflection.TargetParameterCountException，调用栈经过 Delegate.DynamicInvokeImpl 和 WPF DispatcherOperation。

ThemeService 的系统偏好变化处理把 Apply(bool? forceDark = null) 方法组直接交给 Dispatcher.BeginInvoke。可选参数仍是委托参数，调度器以零个参数调用时发生参数数量不匹配。新增回归检查在修改前实际捕获相同异常并失败；修复后通过。现在使用明确的无参数 Action，合并连续通知，主题不变时保留已有画刷。

## 修复覆盖

- UI Automation 使用独立 MTA 工作线程，只允许一个提供程序调用执行；卡住的外部提供程序不会排队占用更多工作线程。
- 剪贴板事务使用独立 STA dispatcher，包含快照、复制、读取及恢复；主界面能够持续响应。
- 模拟目标程序先清空剪贴板、120 毫秒后发布文字，验证中间空状态不会导致取词过早结束。复制开始后的取消仍等待恢复事务完成；恢复遇到瞬时占用时有限重试。
- 释放剪贴板快照中的流和图像，限制快照总大小；超长选区明确提示，避免只翻译截断的前缀。
- 主窗口、浮窗与连接测试替换请求后，旧任务无法覆盖最新结果和状态；每次操作在结束时释放取消源。
- 网络响应有总字节、单事件及译文长度限制，异常 JSON 结构给出可处理错误；收到 finish_reason 后及时完成，服务端不关闭连接也不会继续空等。
- 鼠标钩子移除空闲权限轮询和进程缓存，按需使用原生定时器；保留短按菜单、右键拖动、Esc 和外部点击关闭行为。
- 单实例服务关闭后不再访问已释放的 CancellationTokenSource；后台和界面异常提供有限大小、排除内容与凭据的本地诊断。

## 自动检查与桌面检查

| 检查 | 本机结果 |
| --- | --- |
| Release 编译 | 0 警告、0 错误 |
| 逻辑、DPAPI、API/SSE、取消、异常响应、线程隔离、诊断与单实例检查 | CI 子集 79 项通过 |
| 上述检查及本机剪贴板检查 | 最终打包检查 85 项通过，包含上述 79 项 |
| 实际 WPF、输入法组合、原生缩放、右键与软件兼容性 | 51 项通过 |
| 新增稳定性、显示基准及资源压力检查 | 27 项通过 |
| 凭据扫描器自身检查 | 5 项通过 |

实际桌面检查覆盖 Edge、记事本和隔离配置的 VS Code 选中文字翻译，以及 UIA / 剪贴板两条路径、短按菜单、长按抑制菜单、Esc、外部点击、取消期间恢复剪贴板。右键拖动状态逻辑通过自动检查。

压力检查包含 2000 次系统偏好通知、100 次取消后重试、750 次浮窗显示与隐藏、复制中取消并关闭取词线程、超长 UIA 选区、请求中退出，以及后台异常收集。未出现未处理的界面异常或未观察的任务异常。

实际截图位于 artifacts/qa-reliability 和 artifacts/qa-reliability-ui，已查看主界面与浮窗的实际深浅主题截图，布局保持现有风格。

## 流式文本更新基准

同一个测试窗口，向只读 WPF TextBox 追加 10000 个单字符片段。旧方式逐次 AppendText；新方式合并小片段并关闭只读结果撤销历史。计时覆盖追加调用，不包含后续完整布局绘制；累计分配量不是峰值内存。该基准仅衡量本机文本更新，网络与模型耗时需另行观察。

| 指标 | 逐片段追加 | 合并追加 |
| --- | ---: | ---: |
| TextChanged 次数 | 10000 | 5 |
| 追加调用耗时 | 13513 ms | 2 ms |
| 当前线程累计分配 | 740307248 bytes | 486208 bytes |

文本内容完全相同，文本变更次数减少 99.95%。日常流式输出在有待显示内容时每 40 毫秒刷新，达到缓冲阈值或翻译结束时立即提交；空闲时不运行该定时器。

每 250 次浮窗循环后等待空闲并执行测试用 GC，采样如下：

| 累计循环 | 句柄 | 线程 | 托管堆 bytes | 进程 private bytes |
| --- | ---: | ---: | ---: | ---: |
| 250 | 1036 | 36 | 3233600 | 159715328 |
| 500 | 1036 | 36 | 3200840 | 156213248 |
| 750 | 1020 | 32 | 3196640 | 161652736 |

这些样本没有呈现句柄或托管堆持续增长。原始记录在 artifacts/qa-reliability/metrics.json，可通过以下命令重现：

~~~powershell
dotnet run --project tests/Translate.Tests/Translate.Tests.csproj -c Release -- --reliability artifacts/qa-reliability
dotnet run --project tests/Translate.Tests/Translate.Tests.csproj -c Release -- --ui artifacts/qa-reliability-ui
~~~

交互检查会打开独立测试窗口、移动鼠标并使用剪贴板。模拟长按和缩放期间暂时限定鼠标到测试轨迹，每次手势后恢复原范围；检查结束后恢复剪贴板和鼠标位置。不要在无人值守 CI 中执行。

## 分发与边界

### 后台闪退跟进

当天 17:38 的 Windows Application 日志仍记录 0.2.0.0 的 TargetParameterCountException，故障路径为 artifacts/Translate/Translate.exe。桌面 Translate.lnk 也指向该旧目录，而首次生成的 0.2.1 修复包位于 artifacts/releases/0.2.1。用户报告后台闲置时退出，与旧版接收系统偏好变化后调用错误委托的机制一致。

补充两个独立检查入口。--background-stability 运行主题回调回归，避免执行完整交互检查中的剪贴板和鼠标操作。--background-process 接受一个正在运行的 Translate 进程 ID，向该进程的隐藏系统事件窗口定向投递 2000 次设置通知，并检查进程身份、存活与响应状态。后者覆盖实际发布程序的入口，投递通知不会更改 Windows 设置。

旧程序目录仍被系统占用，未能直接替换。修复版部署至 artifacts/Translate-current，桌面和旧目录内的两个快捷方式已改为该运行目录。部署的 475 个文件与发布目录逐一通过 SHA256 校验，正在运行的实际进程版本确认为 0.2.1.0。旧目录中的 Translate.exe 仍为 0.2.0，应使用更新后的快捷方式。

跟进检查结果为 Release 编译零警告零错误、79 项 CI 检查通过、2 项独立主题回归通过。实际发布进程接受两轮各 2000 次定向通知后仍存活并响应，其中一轮窗口处于后台最小化状态。检查时未新增 Translate 的 Windows Application 崩溃事件，已查看新版实际窗口截图。托盘关闭再唤起的界面检查遇到桌面工具的用户输入检测与最小化状态提示，未完成该项。

真实进程检查采用带超时的同步 SendMessageTimeout 调用，避免异步消息对指针参数的限制。实现依据为 [Microsoft PostMessage 文档](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-postmessagew) 和 [SendMessageTimeout 文档](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendmessagetimeoutw)。

~~~powershell
dotnet run --project tests/Translate.Tests/Translate.Tests.csproj -c Release -- --background-stability artifacts/qa-background
# 将数字替换为需要检查的 Translate 进程 ID
dotnet run --project tests/Translate.Tests/Translate.Tests.csproj -c Release -- --background-process 12345
~~~

使用 build.ps1 -Package 构建 0.2.1 安装包、便携 ZIP 及 SHA256SUMS.txt，用户设置不进入分发包。首次打包未替换运行入口，随后完成上述便携版部署。安装器升级/卸载尚未复测，安装器逻辑沿用 0.2.0。

尚未完成连续数日的真实使用观察、第二台设备或混合缩放双屏复测。上述复现和压力检查覆盖了已定位故障及本次改动范围；长期稳定性继续通过实际运行观察和本地异常诊断验证。

异常记录位于 %LOCALAPPDATA%/Translate/diagnostics，仅保存异常类型与方法栈，不保存异常消息、Key、接口地址、原文或译文。最多保留当前与上一份两个 256 KiB 日志文件。

## 0.2.2 剪贴板截图拦截修复

用户反馈无论选中什么文字，都提示“当前剪贴板内容过大”。该提示来自复制选区前对原剪贴板的备份。旧总数据量上限为 16 MiB，单张 3840 × 2160 BGRA 图片约 31.6 MiB，截图工具同时提供 Bitmap、DIB、PNG 等格式时总量更大。因此旧剪贴板中的截图可以持续阻止后续短文本取词。

快照总数据量上限调整为 128 MiB，并抽取可注入 IDataObject 的快照入口。图片、原始流、字节数组和伴随文本均独立复制用于恢复，超限、未知格式和格式数量限制有单独回归检查。128 MiB 是快照内容预算，实际峰值内存还包含提供程序物化、克隆及恢复开销。

检查使用合成图片、内存数据与模拟 API。新增独立检查先在旧 16 MiB 默认值下复现相同报错，调整后 CI 共 103 项通过，本机单元及剪贴板检查共 109 项通过。真实输入检查共 22 项通过，包含剪贴板放置 4K 图片后长按右键取词、模拟翻译、图片尺寸与像素恢复、Esc、外部点击和复制中取消。已查看实际成功浮窗截图 artifacts/qa-clipboard-0.2.2/popup-large-clipboard.png。

首次打包时旧有 later user copy preserved 断言发生一次失败。随后独立重跑和最终打包的 109 项均通过。该复合断言同时检查恢复返回值与系统全局剪贴板文本，单次失败原因尚未确定。

使用 build.ps1 -Package 生成 0.2.2。实际运行目录 artifacts/Translate-current 已更新 4 个变化文件，全部 475 个部署文件与发布目录逐一通过 SHA256 校验，桌面快捷方式指向此运行目录。实际进程确认为 0.2.2.0，再次接收 2000 次定向系统通知后存活并正常响应。

~~~powershell
dotnet run --project tests/Translate.Tests/Translate.Tests.csproj -c Release -- --clipboard-snapshot
dotnet run --project tests/Translate.Tests/Translate.Tests.csproj -c Release -- --ui-input artifacts/qa-clipboard-0.2.2
~~~
