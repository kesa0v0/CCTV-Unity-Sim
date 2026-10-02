"""unity_runs/<session> 검증: 정합성 점검 + bbox/world 재투영을 이미지에 그려 <session>_check/ 에 저장.

사용: python tools/check_run.py unity_runs/unity-classroom-01 [--frames 1,5,10]
- 빨간 박스 = frames.jsonl 의 정답 bbox
- 초록 십자 = world 좌표를 cameras.json 으로 직접 재투영한 점 (bbox 아랫변 중앙 근처에 와야 정상)
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
    for i in range(1, n):
        if frames[i]["ts"] - frames[i - 1]["ts"] != 100:
            problems.append(f"ts 간격이 100ms가 아님 (frame {frames[i]['frame']})")
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
                    dr.text((bb[0] + 4, bb[1] + 4), f'{ob["object_id"]} ({ob["cls"]})', fill=(255, 255, 0))
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
