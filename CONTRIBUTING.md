# 参与与日常迭代

欢迎提交小范围、可验证的改动。先阅读 AGENTS.md，保持现有简洁界面与输入行为。

## 修改与提交

1. 从 main 创建工作分支：

   ```powershell
   git switch main
   git pull --ff-only
   git switch -c fix/简短名称
   ```

2. 修改后执行 ./build.ps1 -CI。涉及输入、剪贴板或 UI 的改动，再在本机执行 --unit / --ui 验证，查看实际截图；CI 不替代这些检查。
3. 在 CHANGELOG.md 的 Unreleased 下记录影响；不要提交真实 Key、用户配置、日志和构建产物。
4. 检查待提交内容并扫描：

   ```powershell
   git add <本次修改的文件>
   ./scripts/verify-secrets.ps1 -Staged
   git diff --cached
   git commit -m "fix: 简述改动"
   git push -u origin HEAD
   ```

在 GitHub 提交 Pull Request，写明问题、变化和实际验证。

## 发布新版本

1. 确认 main 的自动检查通过，git pull --ff-only。
2. 只修改 Directory.Build.props 中的 Version，例如从 0.2.0 改为 0.2.1；将 Unreleased 内容移到 CHANGELOG.md 对应版本，更新说明和本机验证记录。
3. 执行 ./build.ps1 -Package，验证安装、启动、升级、卸载及凭据扫描。已发布的包和标签不覆盖。
4. 提交并推送版本改动，然后推送同版本标签：

   ```powershell
   git add Directory.Build.props CHANGELOG.md 使用说明.md 验证记录.md
   ./scripts/verify-secrets.ps1 -Staged
   git commit -m "chore: release 0.2.1"
   git push origin main
   git tag -a v0.2.1 -m "Translate 0.2.1"
   git push origin v0.2.1
   ```

Package and release 工作流会构建并上传安装包、便携 ZIP 和 SHA256SUMS.txt。上传先创建草稿，成功后公开。若失败留下草稿，先检查失败日志与资产，再决定处理方式；脚本不会覆盖已有 Release。

手动触发 Package and release 只生成 Actions 构建产物，不公开 Release。Build and check 也支持手动触发。翻译 Key 不需要配置到 GitHub Secrets，CI 不调用真实模型服务。

## 本机安装验证

~~~powershell
$env:ISCC_PATH = & ./scripts/install-inno.ps1
./scripts/test-installation.ps1
~~~

脚本安装到 artifacts/qa-open-source 下的独立目录，验证安装、快捷方式、启动、托盘、单实例、模拟补丁升级、路径限定退出和卸载，检查已有配置哈希不变。模拟补丁包复用当前程序 payload，只测试安装器升级逻辑，不作为发布版本。

脚本检测到已有正式安装、Translate 快捷方式或运行实例时会停止，防止覆盖它们。需先由使用者退出托盘；不在 CI 中执行。失败时保留诊断与测试安装，不会自动清除可能正在使用的文件。

## 问题与安全反馈

普通问题请使用 Issues 模板，提供复现步骤与版本，不要附带 Key 或配置文件。截图请裁剪到应用本身并遮盖敏感信息。如涉及凭据泄露，不要在公开 Issue 中粘贴秘密；先在服务商处撤销泄露 Key，再使用 GitHub 私密漏洞报告入口（若仓库已启用）。
