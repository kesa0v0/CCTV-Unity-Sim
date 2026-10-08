"""unity_runs/<session> 검증: 정합성 점검 + bbox/world 재투영을 이미지에 그려 <session>_check/ 에 저장.

사용: python tools/check_run.py unity_runs/unity-classroom-01 [--frames 1,5,10]
- 빨간 박스 = frames.jsonl 의 정답 bbox
- 초록 십자 = world 좌표를 cameras.json 으로 직접 재투영한 점 (bbox 아랫변 중앙 근처에 와야 정상)
- 파란 점 = 기록된 발목 화면 좌표 (ankles), 박스 라벨 옆 v=가려짐 비율 (visible_ratio)
"""
import argparse
import json
import math
import sys
from pathlib import Path

from PIL import Image, ImageDraw

W, H = 1920, 1080


def rotate_inv(q, v):
    """쿼터니언 q의 역회전을 v에 적용 (월드 → 카메라)."""
    x, y, z, w = q
    x, y, z = -x, -y, -z  # 켤레(단위 쿼터니언의 역)
    vx, vy, vz = v
    tx = 2 * (y * vz - z * vy)
    ty = 2 * (z * vx - x * vz)
    tz = 2 * (x * vy - y * vx)
    return (vx + w * tx + (y * tz - z * ty),
            vy + w * ty + (z * tx - x * tz),
            vz + w * tz + (x * ty - y * tx))


def project(cam, p):
    d = [p[i] - cam["position"][i] for i in range(3)]
    cx, cy, cz = rotate_inv(cam["rotation_quat"], d)
    if cz <= 0:
        return None
    f = (H / 2) / math.tan(math.radians(cam["vertical_fov_deg"]) / 2)
    return W / 2 + f * cx / cz, H / 2 - f * cy / cz


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("run")
    ap.add_argument("--frames", default="", help="그릴 프레임 번호 (쉼표). 기본: 처음/중간/마지막")
    a = ap.parse_args()

    run = Path(a.run)
    cams = {c["camera_id"]: c for c in json.loads((run / "cameras.json").read_text(encoding="utf-8"))["cameras"]}
    frames = [json.loads(l) for l in (run / "frames.jsonl").read_text(encoding="utf-8").splitlines() if l.strip()]

    problems = []
    n = len(frames)
    print(f"frames.jsonl: {n}줄")
    for cid in cams:
        files = sorted((run / cid).glob("*.jpg"))
        print(f"{cid}: 이미지 {len(files)}장")
        if len(files) != n:
            problems.append(f"{cid} 이미지 수({len(files)}) != frames.jsonl 줄 수({n})")
        if files:
            size = Image.open(files[0]).size
            if size != (W, H):
                problems.append(f"{cid} 해상도 {size} != {(W, H)}")
    for i, fr in enumerate(frames):
        if fr["frame"] != i + 1:
            problems.append(f"frame 번호 불연속: index {i} -> {fr['frame']}")
            break
    # ts = 시작 + round((frame-1) * 1000 / fps). fps는 events.json(없으면 예전 기본값 10)
    events_path = run / "events.json"
    fps = json.loads(events_path.read_text(encoding="utf-8"))["fps"] if events_path.exists() else 10
    for i in range(1, n):
        if frames[i]["ts"] - frames[0]["ts"] != round(i * 1000 / fps):
            problems.append(f"ts가 {fps}fps 간격이 아님 (frame {frames[i]['frame']})")
            break

    # 재투영 오차: world 의 재투영점이 bbox 안(가로) / 아랫변 근처에 있는지
    errs = []
    for fr in frames:
        for ob in fr["objects"]:
            for cid, bb in ob["bbox"].items():
                pt = project(cams[cid], ob["world"])
                if pt is None:
                    continue
                cx = (bb[0] + bb[2]) / 2
                errs.append((abs(pt[0] - cx), abs(pt[1] - bb[3]), fr["frame"], ob["object_id"], cid))
    if errs:
        print(f"world 재투영 vs bbox 아랫변 중앙: 평균 dx={sum(e[0] for e in errs)/len(errs):.1f}px, "
              f"dy={sum(e[1] for e in errs)/len(errs):.1f}px (큰 값이면 좌표/카메라 설정 확인)")
    else:
        problems.append("기록된 오브젝트/bbox가 없음 (Tracked 없음, 또는 카메라에 안 보임)")

    # 사람 world(캐릭터 피벗) ↔ 두 발목 가운데 바닥 거리, 발목 화면 좌표 재투영 일치
    offs = {True: [], False: []}
    ank_err = []
    for fr in frames:
        for ob in fr["objects"]:
            ank = ob.get("ankles")
            if not ank:
                continue
            l, r = ank["world"]["left"], ank["world"]["right"]
            mid = ((l[0] + r[0]) / 2, (l[2] + r[2]) / 2)
            offs[ob.get("moving", False)].append(math.dist(mid, (ob["world"][0], ob["world"][2])))
            for cid in cams:
                for side, w in (("left", l), ("right", r)):
                    rec, pt = ank[cid][side], project(cams[cid], w)
                    if rec and pt:
                        ank_err.append(math.dist(rec, pt))
    for mv, v in offs.items():
        if v:
            v.sort()
            print(f"world ↔ 두 발목 가운데 (바닥 거리, {'걷는 중' if mv else '정지'}): "
                  f"평균 {100 * sum(v) / len(v):.1f}cm, 중앙값 {100 * v[len(v) // 2]:.1f}cm, 최대 {100 * v[-1]:.1f}cm ({len(v)}개)")
    if ank_err and max(ank_err) > 2:
        problems.append(f"발목 화면 좌표가 cameras.json 재투영과 다름 (최대 {max(ank_err):.1f}px)")

    # 가려짐 비율 요약
    vis = {}
    for fr in frames:
        for ob in fr["objects"]:
            for cid, v in (ob.get("visible_ratio") or {}).items():
                if v is not None:
                    vis.setdefault((ob["object_id"], cid), []).append(v)
    for (oid, cid), v in sorted(vis.items()):
        print(f"visible_ratio {oid} {cid}: 평균 {sum(v) / len(v):.2f}, 최소 {min(v):.2f}, 0.5 미만 {sum(x < 0.5 for x in v)}프레임")

    ids = sorted({o["object_id"] for fr in frames for o in fr["objects"]})
    print("오브젝트:", ", ".join(ids) or "(없음)")

    want = [int(x) for x in a.frames.split(",") if x] or sorted({1, max(1, n // 2), n})
    out = run.parent / (run.name + "_check")
    for cid in cams:
        (out / cid).mkdir(parents=True, exist_ok=True)
    for fn in want:
        if not 1 <= fn <= n:
            continue
        fr = frames[fn - 1]
        for cid, cam in cams.items():
            img = Image.open(run / cid / f"{fn:06d}.jpg").convert("RGB")
            dr = ImageDraw.Draw(img)
            for ob in fr["objects"]:
                bb = ob["bbox"].get(cid)
                if bb:
                    dr.rectangle(bb, outline=(255, 40, 40), width=3)
                    v = (ob.get("visible_ratio") or {}).get(cid)
                    label = f'{ob["object_id"]} ({ob["cls"]})' + (f" v={v:.2f}" if v is not None else "")
                    dr.text((bb[0] + 4, bb[1] + 4), label, fill=(255, 255, 0))
                for side in ("left", "right"):
                    ap = ((ob.get("ankles") or {}).get(cid) or {}).get(side)
                    if ap:
                        dr.ellipse([ap[0] - 6, ap[1] - 6, ap[0] + 6, ap[1] + 6], fill=(40, 120, 255))
                pt = project(cam, ob["world"])
                if pt:
                    dr.line([pt[0] - 12, pt[1], pt[0] + 12, pt[1]], fill=(0, 255, 0), width=3)
                    dr.line([pt[0], pt[1] - 12, pt[0], pt[1] + 12], fill=(0, 255, 0), width=3)
            img.save(out / cid / f"{fn:06d}.jpg", quality=90)
    print("그린 이미지:", out)

    if problems:
        print("\n문제:")
        for p in problems:
            print(" -", p)
        sys.exit(1)
    print("\n구조 검사 통과")


if __name__ == "__main__":
    main()
