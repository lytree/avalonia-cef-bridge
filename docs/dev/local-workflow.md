# 本地提交流程

> 从拉取最新到开 PR 的完整本地流程、Conventional Commit、Agent 协作。

## 1. 标准流程

```powershell
# 0. 拉最新 master
git pull --rebase

# 1. 还原 + 构建(0 警告是硬指标)
dotnet restore tarui.net.slnx --configfile NuGet.Config
dotnet build tarui.net.slnx -c Release --no-restore

# 2. 跑自测试 + 架构门禁
./eng/test-all.ps1 -BaselineCount 21
dotnet run --project tests/Tarui.Architecture.Tests -c Release --no-build

# 3. 前端 lint + build
cd web
pnpm install --frozen-lockfile
pnpm lint
pnpm build
cd ..

# 4. commit(Conventional Commit:feat / refactor / fix / chore / docs / test)
git checkout -b codex/<topic>
git add -A
git commit -m "feat: <imperative summary>"

# 5. 推分支
git push -u origin codex/<topic>

# 6. 开 PR,描述行为/架构影响、列出验证命令、关联 issue
```

## 2. 提交规范

历史提交采用 Conventional Commit,例如:

- `feat: implement ...`
- `refactor: compose ...`
- `chore: initialize ...`
- `fix: correct ...`
- `docs: ...`
- `test: ...`

提交应聚焦单一目的,摘要使用祈使语气。PR 应说明行为和架构影响、列出验证命令、关联相关 Issue;可见的 Web 或桌面 UI 变化需附截图。

## 3. 同步规则

触及契约、配置、工作流时同步更新:

- `README.md` —— 用户视角的快速上手与项目状态
- `docs/` —— 受众分类的详细文档
- `schemas/*.json` —— JSON Schema

## 4. Agent 协作

Agent 辅助任务应主动拆分独立子任务,优先使用 Luna,其次使用 Terra,并在集成前审查所有委派结果。

具体协作纪律:

- 子任务定义清晰(目标 / 输入 / 输出 / 验收)。
- 集成时复核所有委派结果(不能直接相信)。
- 涉及架构门禁的改动在子任务完成后立即跑 `Tarui.Architecture.Tests`。

## 5. 跳过 CI 的临时变更

如需临时跳过某项门禁:

- 编译警告:在 `Directory.Build.props` 抑制特定 warning number(说明 PR)。
- 架构门禁:不跳过(`TreatWarningsAsErrors=true` 不可绕过)。
- 版本一致性:`#skip-version-check` 注释不可行;调整版本对齐即可。

## 6. 紧急修复流程

针对发布版本的 hotfix:

1. 从 `main` 拉分支 `hotfix/<topic>`。
2. 修复 + 自测试 + 架构门禁全绿。
3. PR 标题 `[hotfix] <description>`。
4. Reviewer 优先 Review。
5. 合并后自动触发 `release.yml`(手动 dispatch)。
