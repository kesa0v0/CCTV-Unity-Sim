"""시나리오 컷 검증: events.json의 이벤트 시각마다 객체 위치(격자 좌표)와 쌍 거리, 경고 구간을 출력.

사용: python tools/check_scenario.py unity_runs/unity-grid-sc1
- 위치: rel 좌표를 격자 좌표(A1 원점)로 바꿔 출력. 사람은 발목 가운데(높이 0으로)도 함께
- 경고 구간: 정답 바닥 거리 1m 미만인 시간. expected_alerts에 없는 쌍에서 나오면 '예상 밖'
"""
import argparse
import json
import math
from itertools import combinations
from pathlib import Path

WARN = 1.0


def load(run):
    run = Path(run)
    frames = [json.loads(l) for l in (run / "frames.jsonl").read_text(encoding="utf-8").splitlines() if l.strip()]
    events = json.loads((run / "events.json").read_text(encoding="utf-8"))
    cams = json.loads((run / "cameras.json").read_text(encoding="utf-8"))
    return frames, events, cams


def floor_dist(p, q):
    return math.hypot(p[0] - q[0], p[1] - q[1])


def intervals(flags, fps):
    """[(시작 초, 끝 초)] — flags[i]는 프레임 i+1."""
    out, start = [], None
    for i, f in enumerate(flags + [False]):
        if f and start is None:
            start = i
        elif not f and start is not None:
            out.append((start / fps, (i - 1) / fps))
            start = None
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("run")
    a = ap.parse_args()
    frames, ev, cams = load(a.run)
    fps = ev["fps"]
    gx0, gy0 = cams["rel_frame"]["grid_A1_rel"]
    print(f"시나리오 {ev['scenario']}{'-' + str(ev['round']) if ev['scenario'] == 0 else ''} {ev['title']}: "
          f"{len(frames)}/{ev['frame_count']}프레임, {ev['duration_s']}초 @ {fps}fps")

    def grid(rel):
        return (rel[0] - gx0, rel[1] - gy0)

    for e in ev["events"]:
        fr = frames[min(e["frame"], len(frames)) - 1]
        obs = {o["object_id"]: o for o in fr["objects"]}
        parts = []
        for oid, o in sorted(obs.items()):
            g = grid(o["rel"])
            s = f"{oid}({g[0]:.2f},{g[1]:.2f})"
            if "ankles" in o:
                m = grid(o["ankles"]["rel"]["mid"])
                s += f"/발목({m[0]:.2f},{m[1]:.2f})"
            parts.append(s)
        dists = [f"{p}-{q} {floor_dist(obs[p]['rel'], obs[q]['rel']):.2f}" for p, q in combinations(sorted(obs), 2)]
        print(f"\n[{e['name']}] t={e['t']}s frame {e['frame']}\n  " + "  ".join(parts) + "\n  거리: " + ", ".join(dists))

    # 쌍마다 정답 경고 구간
    ids = sorted({o["object_id"] for fr in frames for o in fr["objects"]})
    expected = {tuple(sorted(x["pair"])): x for x in ev["expected_alerts"]}
    print("\n경고 구간 (정답 바닥 거리 < 1m):")
    for p, q in combinations(ids, 2):
        flags = []
        for fr in frames:
            obs = {o["object_id"]: o for o in fr["objects"]}
            flags.append(p in obs and q in obs and floor_dist(obs[p]["rel"], obs[q]["rel"]) < WARN)
        iv = intervals(flags, fps)
        exp = expected.get((p, q))
        if not iv and not exp:
            continue
        tag = f"기대 {exp['expect']} ({exp['from']}~{exp['to']})" if exp else "예상 밖"
        span = ", ".join(f"{s:.2f}~{t:.2f}s" for s, t in iv) or "없음"
        print(f"  {p}-{q}: {span}  [{tag}]")

    # 두 카메라 동시 관측률
    print("\n두 카메라 동시 관측:")
    for oid in ids:
        both = sum(1 for fr in frames for o in fr["objects"]
                   if o["object_id"] == oid and {"cam1", "cam2"} <= o["bbox"].keys())
        print(f"  {oid}: {both}/{len(frames)}")


if __name__ == "__main__":
    main()
