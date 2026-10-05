"""S4 검증: 구간(100프레임)별 사람-사람 최소 거리, 두 카메라 동시 관측률, 백팩 기록 여부.

사용: python tools/check_s4.py unity_runs/unity-classroom-03-s4 [--seg 100]
기대값: 4-1 0.5m · 4-2 0.3m · 4-3 0.5m · 4-4 1.0m · 4-5 0.5m
"""
import argparse
import json
import math
from pathlib import Path

NAMES = ["4-1 정면 접근", "4-2 직각 교차", "4-3 정지+접근", "4-4 나란히 걷기", "4-5 먼 곳 접근"]
EXPECT = [0.5, 0.3, 0.5, 1.0, 0.5]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("run")
    ap.add_argument("--seg", type=int, default=100, help="구간당 프레임 수")
    a = ap.parse_args()

    frames = [json.loads(l) for l in (Path(a.run) / "frames.jsonl").read_text(encoding="utf-8").splitlines() if l.strip()]
    print(f"frames: {len(frames)}")

    for s in range(math.ceil(len(frames) / a.seg)):
        chunk = frames[s * a.seg:(s + 1) * a.seg]
        min_d, min_f = 1e9, None
        both = {"person_1": 0, "person_2": 0}
        for fr in chunk:
            obs = {o["object_id"]: o for o in fr["objects"]}
            for pid in both:
                if pid in obs and {"cam1", "cam2"} <= obs[pid]["bbox"].keys():
                    both[pid] += 1
            if "person_1" in obs and "person_2" in obs:
                p, q = obs["person_1"]["world"], obs["person_2"]["world"]
                d = math.hypot(p[0] - q[0], p[2] - q[2])
                if d < min_d:
                    min_d, min_f = d, fr["frame"]
        name = NAMES[s] if s < len(NAMES) else f"구간 {s + 1}"
        exp = f" (목표 {EXPECT[s]}m)" if s < len(EXPECT) else ""
        n = len(chunk)
        print(f"{name}: 최소 거리 {min_d:.2f}m @frame {min_f}{exp} | 두 카메라 동시 관측 p1 {both['person_1']}/{n}, p2 {both['person_2']}/{n}")

    bps = {}
    for fr in frames:
        for o in fr["objects"]:
            if o["cls"] == "backpack":
                bps.setdefault(o["object_id"], []).append(o)
    for oid, lst in sorted(bps.items()):
        both = sum(1 for o in lst if {"cam1", "cam2"} <= o["bbox"].keys())
        w = lst[0]["world"]
        print(f"{oid}: world=({w[0]:.2f}, {w[1]:.2f}, {w[2]:.2f}) 기록 {len(lst)}/{len(frames)}프레임, 두 카메라 {both}프레임")
    if not bps:
        print("백팩 기록 없음!")


if __name__ == "__main__":
    main()
