#!/usr/bin/env bash
# =============================================================================
# verify-macos.sh — macOS 真机验收脚本（本轮 Tauri 对齐缺口包）
#
# 覆盖验收项（对应 docs/tauri-desktop-alignment-plan.md §15「窗口/IPC 对齐缺口包」行）：
#   A1  plugin:webview|eval / eval-with-callback（真实 CEF ExecuteScript 路径）
#   A2  webview://render-process-gone 鲁棒性（渲染进程崩溃注入，应用存活）
#   A3  core:window|deny-close 关闭拦截 + 超时兜底强制关闭
#   B1  WindowOptions.Transparent（AcrylicBlur/透明视效）
#   B2  WindowOptions.Parent + Modal（模态禁用父窗口 / 非模态 Owner 关联）
#   C1  plugin:websocket|connect/send/close（真实 wss 回环 echo）
#   C2  plugin:positioner|set-position（屏幕锚位 + Tray* 回退）
#
# 自动段：构建 0 警告断言、全部 tests/Tarui.*.Tests、pnpm lint/build（可跳过）。
# 人工段：启动 demo 后按清单逐项执行，在 DevTools console 粘贴脚本内提供的片段。
#
# 用法：
#   ./eng/verify-macos.sh                # 全量（构建 + 测试 + 交互验收）
#   ./eng/verify-macos.sh --skip-build   # 跳过构建/测试/前端，直接进入交互验收
#
# 前置：macOS + .NET 10 + pnpm + python3；已执行 eng/cef/install-runtime.ps1
#       （或设置 TARUI_CEF_ROOT 指向 CEF 运行时目录）。
# 说明：脚本会临时给 examples/demo/capabilities/main.json 的 core:window|create
#       scope 追加 {"path":"palette"}（验收窗口 label），退出时自动还原备份。
# =============================================================================
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEMO_PROJ="examples/demo/Demo.Desktop/Demo.Desktop.csproj"
CAP_FILE="examples/demo/capabilities/main.json"
CAP_BAK="${CAP_FILE}.verify-macos.bak"
DEMO_LOG="${TMPDIR:-/tmp}/tarui-demo-macos.log"
CLOSE_FALLBACK_SECONDS=3   # 与 WindowLifecycleOptions.DefaultCloseRequestTimeout 一致

C_OK=$'\033[32m'; C_ERR=$'\033[31m'; C_HI=$'\033[36m'; C_DIM=$'\033[2m'; C_OFF=$'\033[0m'
say()  { printf '%s\n' "$*"; }
ok()   { printf '%s%s%s\n' "$C_OK" "$*" "$C_OFF"; }
err()  { printf '%s%s%s\n' "$C_ERR" "$*" "$C_OFF"; }
head() { printf '\n%s%s%s\n' "$C_HI" "$*" "$C_OFF"; }
dim()  { printf '%s%s%s\n' "$C_DIM" "$*" "$C_OFF"; }

SKIP_BUILD=false
[[ "${1:-}" == "--skip-build" ]] && SKIP_BUILD=true

AUTOMATED_FAIL=0
MANUAL_PASS=0; MANUAL_FAIL=0; MANUAL_SKIP=0
declare -a MANUAL_RESULTS=()

cleanup() {
  if [[ -n "${DEMO_PID:-}" ]] && kill -0 "$DEMO_PID" 2>/dev/null; then
    kill "$DEMO_PID" 2>/dev/null || true
    dim "demo 进程已终止（PID $DEMO_PID）"
  fi
  if [[ -f "$CAP_BAK" ]]; then
    mv "$CAP_BAK" "$REPO_ROOT/$CAP_FILE"
    dim "capabilities/$CAP_FILE 已还原"
  fi
}
trap cleanup EXIT

record() { # record <pass|fail|skip> <说明>
  MANUAL_RESULTS+=("$1: $2")
  case "$1" in
    pass) MANUAL_PASS=$((MANUAL_PASS+1)); ok "  ✓ $2" ;;
    fail) MANUAL_FAIL=$((MANUAL_FAIL+1)); err "  ✗ $2" ;;
    *)    MANUAL_SKIP=$((MANUAL_SKIP+1)); dim "  ⊘ $2（跳过）" ;;
  esac
}

ask() { # ask <说明> — 交互收集 pass/fail/skip
  local verdict
  while true; do
    read -r -p "  结果 [pass/fail/skip]: " verdict </dev/tty || return 1
    case "$verdict" in
      pass) record pass "$1"; return 0 ;;
      fail) record fail "$1"; return 0 ;;
      skip) record skip "$1"; return 0 ;;
      *) echo "  请输入 pass / fail / skip" ;;
    esac
  done
}

console() { # console — 打印一段可直接粘贴到 DevTools console 的 JS
  dim "  ── 粘贴到 DevTools Console ──"
  while IFS= read -r line; do printf '    %s\n' "$line"; done
  dim "  ─────────────────────────────"
}

# ── 前置检查 ─────────────────────────────────────────────────────────────────
head "〔0/4〕前置检查"
if [[ "$(uname -s)" != "Darwin" ]]; then
  err "本脚本必须在 macOS 真机上运行（当前：$(uname -s)）"
  exit 1
fi
for tool in dotnet pnpm python3; do
  command -v "$tool" >/dev/null 2>&1 || { err "缺少 $tool，请先安装"; exit 1; }
done
ok "macOS $(sw_vers -productVersion 2>/dev/null || echo '?') / dotnet $(dotnet --version 2>/dev/null | head -1)"
if [[ -z "${TARUI_CEF_ROOT:-}" ]]; then
  dim "提示：未设置 TARUI_CEF_ROOT，运行时将回退到构建输出目录的 CEF/osx-* 或 TARUI_APP 本地缓存"
fi

cd "$REPO_ROOT"

# ── 自动段：构建 + 全部自测试 + 前端 ────────────────────────────────────────
if [[ "$SKIP_BUILD" == false ]]; then
  head "〔1/4〕构建（断言 0 警告 0 错误）"
  BUILD_LOG="${TMPDIR:-/tmp}/tarui-build-macos.log"
  if ! dotnet build tarui.net.slnx >"$BUILD_LOG" 2>&1; then
    err "dotnet build 失败，输出末尾："; tail -20 "$BUILD_LOG"; exit 1
  fi
  WARN=$(grep -Eo '[0-9]+ Warning\(s\)' "$BUILD_LOG" | grep -Eo '[0-9]+' | tail -1)
  ERRN=$(grep -Eo '[0-9]+ Error\(s\)' "$BUILD_LOG" | grep -Eo '[0-9]+' | tail -1)
  if [[ "${WARN:-1}" == 0 && "${ERRN:-1}" == 0 ]]; then
    ok "构建成功：0 警告 / 0 错误"
  else
    err "警告/错误计数异常（W=$WARN E=$ERRN），人工检查 $BUILD_LOG"; AUTOMATED_FAIL=1
  fi

  head "〔2/4〕全部 tests/Tarui.*.Tests 自测试"
  FAIL_COUNT=0; TOTAL=0
  for proj in tests/Tarui.*.Tests; do
    TOTAL=$((TOTAL+1))
    if dotnet run --project "$proj" --no-build >/dev/null 2>&1; then
      ok "  PASS  $proj"
    else
      err "  FAIL  $proj"; FAIL_COUNT=$((FAIL_COUNT+1))
    fi
  done
  [[ $FAIL_COUNT -eq 0 ]] && ok "自测试全部通过（$TOTAL 套）" || { err "$FAIL_COUNT/$TOTAL 套失败"; AUTOMATED_FAIL=1; }

  head "〔3/4〕前端 lint + build"
  if (cd web && pnpm lint >/dev/null 2>&1 && pnpm build >/dev/null 2>&1); then
    ok "pnpm lint + build 通过"
  else
    err "pnpm lint/build 失败"; AUTOMATED_FAIL=1
  fi
else
  dim "（--skip-build：跳过自动段）"
fi

# ── 临时放开 create scope（palette 验收窗口）────────────────────────────────
head "〔4/4〕人工真机验收（交互式）"
cp "$CAP_FILE" "$CAP_BAK"
if sed -i '' 's|"allow": \[{ "path": "editor" }\]|"allow": [{ "path": "editor" }, { "path": "palette" }]|' "$CAP_FILE"; then
  ok "已临时放行 core:window|create scope → palette（退出自动还原）"
else
  err "capabilities 临时放行失败，palette 相关验收项请标记 skip"
fi

# 重新构建 capabilities 是否生效取决于宿主是否重新载入清单；保险起见跳过增量构建，
# 直接以当前二进制启动（capabilities 由 demo 宿主启动时读取磁盘文件）。

say ""
dim "启动 demo（日志：$DEMO_LOG）…"
dotnet run --project "$DEMO_PROJ" --no-build >"$DEMO_LOG" 2>&1 &
DEMO_PID=$!
sleep 8
if ! kill -0 "$DEMO_PID" 2>/dev/null; then
  err "demo 启动失败，日志末尾："; tail -20 "$DEMO_LOG"; exit 1
fi
ok "demo 已启动（PID $DEMO_PID）。保持窗口前台，逐步执行以下清单。"

# ── M0：打开 DevTools + 安装桥接观察器 ──────────────────────────────────────
say ""
say "━━ M0. 准备：打开 DevTools 并安装桥接观察器 ━━"
say "  1) 在 demo 主窗口页面点击 DevTools 按钮（webview.openDevtools）。"
say "  2) 在 DevTools 的 Console 标签页（执行上下文为页面 top，非 DevTools 自身）。"
say "  3) 先粘贴「观察器」片段（后续所有回执帧都会以 [tap] 前缀打印）："
console <<'JS'
(() => {
  if (window.__tapInstalled) return '[tap] already installed';
  const raw = window.__tarui_dispatchBase64;
  window.__tarui_dispatchBase64 = (b64) => {
    try {
      const bytes = Uint8Array.from(atob(b64), c => c.charCodeAt(0));
      const json = JSON.parse(new TextDecoder().decode(bytes));
      console.log('[tap]', json.type ?? 'invoke-response', JSON.stringify(json));
    } catch (e) { console.log('[tap] <undecodable>', String(e)); }
    return raw(b64);
  };
  window.__invoke = (command, payload, id) =>
    window.invokeCSharpAction(JSON.stringify({
      protocol: 1, id: id ?? ('m' + Math.random().toString(36).slice(2, 8)),
      command, payload: payload ?? {}, windowLabel: 'main', webViewLabel: 'main',
    })) || '[invoke sent]';
  window.__tapInstalled = true;
  return '[tap] installed + __invoke() ready';
})()
JS
ask "M0 桥接观察器安装成功（console 返回 [tap] installed）"

# ── M1：eval 基础路径 ────────────────────────────────────────────────────────
say ""
say "━━ M1. eval：真实 CEF ExecuteScript 路径（A1 基础）━━"
console <<'JS'
__invoke('plugin:webview|eval', { script: "document.body.style.background = 'lightgreen'" })
JS
say "  预期：demo 页面背景立即变浅绿；[tap] 打印 success InvokeResponse。"
ask "M1 eval 副作用生效且回执 success"

# ── M2：eval-with-callback 完成值回执 ───────────────────────────────────────
say ""
say "━━ M2. eval-with-callback：完成值经 Channel 回执（A1 核心）━━"
console <<'JS'
__invoke('plugin:webview|eval-with-callback', { script: '2 + 3', onEvent: 'manual-eval-1' })
JS
say "  预期：[tap] 打印一条 channel 帧，payload 为 {\"ok\":true,\"value\":\"5\",\"error\":null}"
say "  （value 是 JSON 编码的完成值；脚本抛异常时应回 ok:false——可改 script: 'throw new Error(42)' 复测）。"
ask "M2 收到 ok:true value:\"5\" 的完成帧"

# ── M3：deny-close 拦截 + 兜底（在 editor 窗口上验收，主窗口留待收尾）────────
say ""
say "━━ M3. deny-close：关闭拦截与超时兜底（A3，P1-16）━━"
say "  3a) 确保 editor 窗口存在（不存在则先创建）："
console <<'JS'
__invoke('core:window|list', {})
JS
say "      若列表无 editor，再粘贴："
console <<'JS'
__invoke('core:window|create', { label: 'editor', title: 'Editor' })
JS
say "  3b) 点击 editor 窗口的关闭按钮，然后立刻（${CLOSE_FALLBACK_SECONDS}s 回退窗口内）粘贴："
console <<'JS'
__invoke('core:window|deny-close', { label: 'editor' })
JS
say "      预期：窗口保持打开；[tap] 可见 window://close-requested 事件与 deny-close success。"
say "  3c) 再次点击 editor 关闭按钮，本次不粘贴任何内容。"
say "      预期：约 ${CLOSE_FALLBACK_SECONDS}s 后 editor 被兜底强制关闭，应用（主窗口）继续运行。"
ask "M3 deny 拦截成功且无操作时按超时兜底关闭"

# ── M4：render-process-gone 鲁棒性 ──────────────────────────────────────────
say ""
say "━━ M4. 渲染进程崩溃鲁棒性（A2）━━"
say "  注意：事件投递与 capability 门控已由 Tarui.Shell.Tests 覆盖；本项验证真机行为——"
say "  渲染进程死亡后宿主应用必须存活。粘贴以下 OOM 注入片段："
console <<'JS'
(() => { const chunks = []; try { for (;;) chunks.push(new Array(64e6).fill('x')); } catch (e) { return 'alloc stopped: ' + e; } })()
JS
say "  预期（数秒内）："
say "    · 页面白屏/冻结，活动监视器中 demo 的 Renderer 子进程退出；"
say "    · 宿主应用本身不退出（主窗口仍在，活动监视器中主进程存活）。"
say "  验收后重启 demo 前请先跳到 M5-M8 之前：若页面已死，本项之后的 M5/M6/M8 请改在"
say "  editor 窗口或重启 demo 后补验（脚本允许部分 skip）。"
ask "M4 渲染进程死亡但宿主应用存活"

# ── M5：透明 + 模态子窗口 ────────────────────────────────────────────────────
say ""
say "━━ M5. 透明窗口 + 模态对话框（B1 + B2）━━"
say "  5a) 若 M4 已破坏主窗口页面并重启了 demo，请先在新的 DevTools console 重新粘贴 M0 观察器片段。"
say "      创建透明模态窗口："
console <<'JS'
__invoke('core:window|create', { label: 'palette', title: 'Palette', width: 420, height: 300, transparent: true, parent: 'main', modal: true })
JS
say "      预期：出现 420x300 半透明（亚克力模糊视效）小窗；"
say "            主窗口被模态禁用（点击无效/置灰）；[tap] 见 create success。"
say "  5b) 关闭 palette："
console <<'JS'
__invoke('core:window|close', { label: 'palette', force: true })
JS
say "      预期：palette 关闭后主窗口恢复可交互。"
say "  5c) 非模态 Owner 关联："
console <<'JS'
__invoke('core:window|create', { label: 'palette', title: 'Palette', width: 420, height: 300, transparent: true, parent: 'main', modal: false })
JS
say "      预期：palette 再次出现（半透明），主窗口与 palette 均可交互（非模态）。"
ask "M5 透明视效 + 模态禁用/恢复 + 非模态共立均符合"

# ── M6：WebSocket 回环 ───────────────────────────────────────────────────────
say ""
say "━━ M6. WebSocket 插件（C1，真实 wss echo）━━"
say "  需要外网可达 wss://echo.websocket.events（离线请标记 skip）。连接："
console <<'JS'
__invoke('plugin:websocket|connect', { url: 'wss://echo.websocket.events', onMessage: 'manual-ws-1' })
JS
say "  预期：[tap] 打印 success InvokeResponse，payload.id 形如 \"ws-xxxxxx\"（记下该 id）。"
say "  用该 id 发送（替换 <id>）："
console <<'JS'
__invoke('plugin:websocket|send', { id: '<id>', text: 'hello from macos' })
JS
say "  预期：[tap] 打印 channel 帧 {\"kind\":\"text\",\"text\":\"hello from macos\"}（echo 回显；"
say "        另有该服务自发的欢迎消息帧属正常）。随后关闭："
console <<'JS'
__invoke('plugin:websocket|close', { id: '<id>' })
JS
say "  预期：[tap] 打印终止帧 {\"kind\":\"closed\",\"code\":1000,...}。"
say "  附加：连接一个 scope 白名单外的地址（如 ws://example.invalid）应收到 PERMISSION/SCOPE 拒绝回执。"
ask "M6 connect/echo 回显/关闭帧/scope 拒绝均符合"

# ── M7：Positioner ───────────────────────────────────────────────────────────
say ""
say "━━ M7. Positioner 锚位（C2）━━"
console <<'JS'
__invoke('plugin:positioner|set-position', { anchor: 'TopRight' })
JS
say "  预期：palette 窗口跳到主屏工作区右上角。再粘贴："
console <<'JS'
__invoke('plugin:positioner|set-position', { anchor: 'TrayCenter' })
JS
say "  预期：窗口移到工作区底部居中（macOS 无任务栏矩形，Tray* 回退语义——与文档一致）。"
say "  再试带边距："
console <<'JS'
__invoke('plugin:positioner|set-position', { anchor: 'TopLeft', x: 40, y: 40 })
JS
say "  预期：窗口贴近左上角并留出 40px 边距。"
ask "M7 三个锚位/回退/边距位置符合"

# ── 收尾 ─────────────────────────────────────────────────────────────────────
say ""
say "━━ 收尾：验收完成后关闭 demo 主窗口退出应用（或按 Ctrl-C 由脚本清理）。━━"
say ""
head "═══ 验收汇总 ═══"
if [[ $AUTOMATED_FAIL -eq 0 ]]; then ok "自动段：构建/自测试/前端 全部通过"; else err "自动段存在失败，请回看上方日志"; fi
for line in "${MANUAL_RESULTS[@]}"; do
  case "$line" in pass*) ok "  $line" ;; fail*) err "  $line" ;; *) dim "  $line" ;; esac
done
say ""
if [[ $MANUAL_FAIL -eq 0 && $AUTOMATED_FAIL -eq 0 ]]; then
  ok "验收结论：通过（pass=$MANUAL_PASS fail=0 skip=$MANUAL_SKIP）"
  exit 0
else
  err "验收结论：未通过（pass=$MANUAL_PASS fail=$MANUAL_FAIL skip=$MANUAL_SKIP）"
  exit 1
fi
