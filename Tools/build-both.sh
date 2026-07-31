#!/usr/bin/env bash
# 两端出包 / 两端编译回归。
#
# 必须分两次调用 Unity：切换 Build Profile 会改 scripting defines
# （MRBASE_QUEST <-> MRBASE_PICO），触发脚本重编译并打断正在执行的编辑器脚本。
# 所以没有 BuildBoth() 这个 C# 入口。
#
# 用法：
#   tools/build-both.sh                     # 两端都打
#   tools/build-both.sh Quest               # 只打 Quest
#   UNITY=/path/to/Unity tools/build-both.sh
set -euo pipefail

PROJECT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY="${UNITY:-/Applications/Unity/Hub/Editor/6000.4.4f1/Unity.app/Contents/MacOS/Unity}"

if [[ ! -x "$UNITY" ]]; then
  echo "找不到 Unity：$UNITY" >&2
  echo "用 UNITY=/path/to/Unity 指定。" >&2
  exit 1
fi

targets=("${@:-Quest Pico}")
# shellcheck disable=SC2206
targets=(${targets[*]})

for target in "${targets[@]}"; do
  log="$PROJECT/Builds/build-${target}.log"
  mkdir -p "$PROJECT/Builds"
  # 花括号必须留着：紧跟其后的全角字符会被 bash 当成变量名的一部分。
  echo "==> 构建 ${target}（日志：${log}）"

  if "$UNITY" -batchmode -quit -nographics \
      -projectPath "$PROJECT" \
      -logFile "$log" \
      -executeMethod "BuildScript.Build$target"; then
    echo "==> $target 成功"
  else
    code=$?
    echo "==> ${target} 失败（退出码 ${code}），日志尾部：" >&2
    tail -40 "$log" >&2
    exit $code
  fi
done

echo "==> 全部成功"
