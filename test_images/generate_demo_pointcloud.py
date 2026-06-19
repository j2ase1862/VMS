#!/usr/bin/env python3
"""
VMS 전시회 데모용 합성 포인트클라우드(.vpc) 생성기.

VMS.Camera.Models.PointCloudData 의 SaveToFile() 바이너리 포맷을 그대로 재현한다.
포맷:
    [4 bytes]  "VPC1" 시그니처
    [int32 LE] PointCount
    [int32 LE] GridWidth
    [int32 LE] GridHeight
    [str]      Name  (.NET BinaryWriter 의 7bit-encoded length-prefixed UTF-8)
    [float32 LE x N x 3]  Positions (X, Y, Z)
    [byte x N x 3]        Colors (R, G, B)

산업 부품을 흉내 낸 조직화(organized) 그리드 클라우드를 만든다.
- 베이스 평면(Z=0)
- 중앙의 사각 단차(boss) : 단차 높이 측정 / 평면-평면 거리·각도 데모용
- 구면 함몰(dent) 결함     : 3D 결함 시각화 / 클러스터 데모용
- 약한 가우시안 노이즈      : 보크셀/SOR 필터 데모용

OK 품과 NG 품 두 가지를 생성한다 (영상 before/after 연출용).
"""
import struct
import math

# ----- 그리드/스케일 파라미터 -----------------------------------------------
W, H = 320, 240          # 그리드 해상도 (76,800 points)
PITCH = 0.30             # mm/픽셀  -> 96mm x 72mm 부품
STEP_H = 8.0             # 중앙 단차 높이 (mm)

# 단차(boss) 영역 (그리드 좌표 비율)
BOSS_X0, BOSS_X1 = 0.34, 0.66
BOSS_Y0, BOSS_Y1 = 0.30, 0.70


def smoothstep(edge0, edge1, x):
    if edge0 == edge1:
        return 0.0 if x < edge0 else 1.0
    t = (x - edge0) / (edge1 - edge0)
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


# 결정론적 의사난수 (numpy 비의존, 재현성 보장)
_seed = 0x9E3779B9
def rnd():
    global _seed
    _seed = (1103515245 * _seed + 12345) & 0x7FFFFFFF
    return _seed / 0x7FFFFFFF  # [0,1)


def jet(t):
    """t in [0,1] -> (r,g,b) bytes, matplotlib jet 근사."""
    t = max(0.0, min(1.0, t))
    def clamp(v):
        return int(max(0, min(255, round(v * 255))))
    r = clamp(min(max(1.5 - abs(4 * t - 3), 0), 1))
    g = clamp(min(max(1.5 - abs(4 * t - 2), 0), 1))
    b = clamp(min(max(1.5 - abs(4 * t - 1), 0), 1))
    return r, g, b


def height(ix, iy, ng=False):
    """그리드 좌표 -> Z 높이(mm)."""
    fx = ix / (W - 1)
    fy = iy / (H - 1)
    z = 0.0

    # 중앙 사각 단차 (가장자리 부드럽게)
    m = 0.012
    sx = smoothstep(BOSS_X0 - m, BOSS_X0 + m, fx) * (1 - smoothstep(BOSS_X1 - m, BOSS_X1 + m, fx))
    sy = smoothstep(BOSS_Y0 - m, BOSS_Y0 + m, fy) * (1 - smoothstep(BOSS_Y1 - m, BOSS_Y1 + m, fy))
    boss = sx * sy
    z += boss * STEP_H

    # NG 품: boss 윗면을 살짝 기울임(2.2도) -> 평면-평면 각도 측정에서 불량으로 보임
    if ng:
        tilt = math.radians(2.2)
        z += boss * (fx - 0.5) * (W * PITCH) * math.tan(tilt)

    # 구면 함몰 결함 (베이스 평면 위, 좌하단)
    cx, cy = 0.22 * W, 0.74 * H
    dent_r_px = (5.0 if not ng else 7.5) / PITCH   # mm -> px
    dent_depth = 1.0 if not ng else 2.4
    dx = ix - cx
    dy = iy - cy
    rr = math.hypot(dx, dy)
    if rr < dent_r_px:
        # 구면 캡 형태의 함몰
        z -= dent_depth * (math.cos(rr / dent_r_px * (math.pi / 2)))

    # 미세 표면 노이즈
    z += (rnd() - 0.5) * 0.06
    return z


def build(ng=False):
    n = W * H
    positions = bytearray(n * 12)
    colors = bytearray(n * 3)

    # Z 범위 선계산(컬러 정규화용)
    zmin, zmax = 1e9, -1e9
    zbuf = [0.0] * n
    for iy in range(H):
        for ix in range(W):
            z = height(ix, iy, ng)
            zbuf[iy * W + ix] = z
            if z < zmin: zmin = z
            if z > zmax: zmax = z
    zrange = (zmax - zmin) or 1.0

    for iy in range(H):
        for ix in range(W):
            i = iy * W + ix
            x = (ix - W / 2) * PITCH
            y = (iy - H / 2) * PITCH
            z = zbuf[i]
            struct.pack_into('<fff', positions, i * 12, x, y, z)
            r, g, b = jet((z - zmin) / zrange)
            o = i * 3
            colors[o] = r
            colors[o + 1] = g
            colors[o + 2] = b
    return n, positions, colors


def write_7bit_len(buf, length):
    while length >= 0x80:
        buf.append((length & 0x7F) | 0x80)
        length >>= 7
    buf.append(length)


def save_vpc(path, name, n, positions, colors):
    out = bytearray()
    out += b'VPC1'
    out += struct.pack('<i', n)
    out += struct.pack('<i', W)
    out += struct.pack('<i', H)
    name_bytes = name.encode('utf-8')
    write_7bit_len(out, len(name_bytes))
    out += name_bytes
    out += positions
    out += colors
    with open(path, 'wb') as f:
        f.write(out)
    print(f"  wrote {path}  ({n:,} pts, {len(out):,} bytes)")


if __name__ == '__main__':
    import os
    here = os.path.dirname(os.path.abspath(__file__))
    out_dir = os.path.join(here, 'pc_demo')
    os.makedirs(out_dir, exist_ok=True)

    for ng, fname, label in [
        (False, 'demo_part_ok.vpc', 'Demo_Part_OK'),
        (True,  'demo_part_ng.vpc', 'Demo_Part_NG'),
    ]:
        print(f"building {label} ...")
        n, pos, col = build(ng)
        save_vpc(os.path.join(out_dir, fname), label, n, pos, col)
    print("done.")
