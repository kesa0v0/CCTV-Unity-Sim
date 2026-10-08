#!/usr/bin/env bash
# 시나리오 0(1~3회) ~ 9를 차례로 녹화한다 (12컷 → unity_runs/unity-grid-sc*). Unity 에디터에서 프로젝트를 닫고 실행.
# 사용: bash tools/run_all.sh [시나리오 번호...]   예) bash tools/run_all.sh 1 5
set -euo pipefail
UNITY="${UNITY:-C:/Program Files/Unity/Hub/Editor/6000.3.8f1/Editor/Unity.exe}"
PROJECT="$(cd "$(dirname "$0")/.." && pwd)"

cuts=()
for n in "${@:-0 1 2 3 4 5 6 7 8 9}"; do
  for s in $n; do
    if [ "$s" = 0 ]; then cuts+=("0 1" "0 2" "0 3"); else cuts+=("$s 1"); fi
  done
done

for c in "${cuts[@]}"; do
  set -- $c
  echo "== 시나리오 $1 회차 $2"
  "$UNITY" -batchmode -projectPath "$PROJECT" -executeMethod SimBatch.Run \
    -simScenario "$1" -simRound "$2" -logFile "$PROJECT/Logs/sim-sc$1-r$2.log"
done
