#!/usr/bin/env python3
"""
VMS 전시회 데모용 — 자동차 변속기 '클러스터 기어' 합성 포인트클라우드(.vpc) 생성기.

실제 구조광(Mech-Mind 류) 단일뷰 스캔을 흉내 낸 2.5D organized 그리드 클라우드.
- 대형 스퍼기어(하단) 위에 소형 스퍼기어(상단)가 적층된 카운터샤프트/클러스터 기어 형상
- 중앙 보어, 경량화 홀, 치형(involute 근사 trapezoid), 단차 모따기(fillet)
- 머시닝(선삭) 텍스처 + 스캔 노이즈 + 벽/에지 결손(dropout)
- 무데이터 영역은 Z=0  (PointCloudViewer 가 Z==0/NaN/Inf 점을 자동 스킵)

NG 품: 대형기어 이빨 1개 치손(chipped tooth) + 표면 스크래치 + 소형기어 미세 딩(ding).

출력: test_images/pc_demo/transmission_gear_ok.vpc , transmission_gear_ng.vpc
"""
import os
import struct
import numpy as np

# ── 그리드 / 스케일 ──────────────────────────────────────────────
N = 480                  # 그리드 해상도 (480x480 = 230,400 pts)
PITCH = 0.22             # mm/px  → ~105.6mm 시야

# ── 기어 형상 파라미터 (mm) ──────────────────────────────────────
R_BORE   = 7.0
R1_ROOT, R1_TIP, NT1, Z1 = 42.0, 47.0, 40, 8.0    # 대형 기어(하단)
R2_ROOT, R2_TIP, NT2, Z2 = 22.0, 27.0, 22, 20.0   # 소형 기어(상단)
FILLET   = 1.4           # 단차 모따기 반경(mm)
LIGHTEN_HOLES = 6        # 경량화 홀 개수
LIGHTEN_R = 4.0
LIGHTEN_RADIUS = 34.0
TILT_DEG = 0.30          # 스테이지 미세 기울임


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0 + 1e-9), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def tooth_boundary(theta, nteeth, r_root, r_tip):
    """치형 외곽 반경 r_bound(theta). 사다리꼴 치형(land+flank)."""
    phase = (theta * nteeth / (2 * np.pi)) % 1.0
    w, f = 0.34, 0.13      # land 폭, flank 폭(피치 비율)
    land = (smoothstep(0.5 - w / 2 - f, 0.5 - w / 2, phase)
            - smoothstep(0.5 + w / 2, 0.5 + w / 2 + f, phase))
    return r_root + (r_tip - r_root) * land


def build(ng=False, seed=12345):
    rng = np.random.default_rng(seed)

    ax = (np.arange(N) - N / 2) * PITCH
    X, Y = np.meshgrid(ax, ax)
    R = np.hypot(X, Y)
    TH = np.arctan2(Y, X)

    rb1 = tooth_boundary(TH, NT1, R1_ROOT, R1_TIP)
    rb2 = tooth_boundary(TH, NT2, R2_ROOT, R2_TIP)

    small = (R <= rb2) & (R >= R_BORE)              # 소형 기어 윗면
    large = (~small) & (R <= rb1) & (R >= R_BORE)   # 대형 기어 윗면

    Z = np.zeros((N, N), dtype=np.float64)          # 0 = 무데이터
    Z[large] = Z1
    Z[small] = Z2

    # 단차 모따기: 소형기어 외곽(rb2) 바깥 FILLET 밴드를 Z1→Z2 코사인 블렌딩
    band = large & (R > rb2) & (R < rb2 + FILLET)
    if band.any():
        t = (R[band] - rb2[band]) / FILLET          # 0(벽쪽)~1(바깥)
        Z[band] = Z2 + (Z1 - Z2) * (0.5 - 0.5 * np.cos(np.clip(t, 0, 1) * np.pi)) ** 0.6

    valid = Z > 0

    # 경량화 홀(관통) → 무데이터
    for k in range(LIGHTEN_HOLES):
        a = 2 * np.pi * k / LIGHTEN_HOLES + 0.18
        hx, hy = LIGHTEN_RADIUS * np.cos(a), LIGHTEN_RADIUS * np.sin(a)
        hole = (np.hypot(X - hx, Y - hy) < LIGHTEN_R)
        Z[hole] = 0.0
    valid = Z > 0

    # 머시닝(선삭) 동심 텍스처 + 방사 밀링 마크
    machine = 0.045 * np.sin(R * 2.4) + 0.025 * np.sin(TH * NT1 * 0.5) * (R > R2_TIP)
    Z[valid] += machine[valid]

    # 스테이지 미세 기울임
    slope = np.tan(np.radians(TILT_DEG))
    Z[valid] += slope * X[valid]

    # ── 결함 (NG) ─────────────────────────────────────────────
    if ng:
        # 1) 대형기어 이빨 1개 치손: 특정 각도 섹터의 tip 부분 제거(무데이터)
        a0 = np.deg2rad(34.0)
        dth = np.angle(np.exp(1j * (TH - a0)))
        chip = (np.abs(dth) < np.deg2rad(4.0)) & (R > (R1_ROOT + R1_TIP) / 2) & large
        Z[chip] = 0.0
        # 2) 표면 스크래치: 대형기어 면을 가로지르는 가는 고랑(-0.7mm)
        sd = np.abs((X - Y) / np.sqrt(2) - 6.0)        # 대각선 라인까지 거리
        scratch = (sd < 0.45) & large & (R > R2_TIP + 2)
        Z[scratch & (Z > 0)] -= 0.7
        # 3) 소형기어 윗면 미세 딩(눌림)
        dx, dy = 8.0, -6.0
        ding = np.hypot(X - dx, Y - dy) < 2.2
        m = ding & small
        Z[m] -= 0.9 * np.cos(np.hypot(X - dx, Y - dy)[m] / 2.2 * (np.pi / 2))

    valid = Z > 0

    # ── 실제 스캔 특성: 노이즈 + 에지/벽 결손 ─────────────────
    Z[valid] += rng.normal(0, 0.045, size=valid.sum())

    # Z 경사가 큰(벽/에지) 영역일수록 결손 확률↑
    gy, gx = np.gradient(np.where(valid, Z, np.nan))
    grad = np.nan_to_num(np.hypot(gx, gy), nan=0.0)
    drop_p = np.clip(0.012 + grad * 0.6, 0, 0.85)
    drop = (rng.random((N, N)) < drop_p) & valid
    Z[drop] = 0.0
    valid = Z > 0

    # 색상(RGB) — 뷰어는 Z로 재컬러링하지만 포맷상 저장 필요. jet-by-Z 저장.
    z = Z.copy()
    zmin = z[valid].min() if valid.any() else 0.0
    zmax = z[valid].max() if valid.any() else 1.0
    t = np.zeros_like(z)
    t[valid] = (z[valid] - zmin) / (zmax - zmin + 1e-9)
    r = np.clip(1.5 - np.abs(4 * t - 3), 0, 1)
    g = np.clip(1.5 - np.abs(4 * t - 2), 0, 1)
    b = np.clip(1.5 - np.abs(4 * t - 1), 0, 1)
    rgb = np.stack([r, g, b], axis=-1)
    rgb[~valid] = 0
    rgb = (rgb * 255).astype(np.uint8)

    positions = np.stack([X, Y, Z], axis=-1).astype(np.float32)
    return positions.reshape(-1, 3), rgb.reshape(-1, 3), int(valid.sum())


def write_7bit_len(buf, length):
    while length >= 0x80:
        buf.append((length & 0x7F) | 0x80)
        length >>= 7
    buf.append(length)


def save_vpc(path, name, positions, colors):
    n = positions.shape[0]
    out = bytearray()
    out += b'VPC1'
    out += struct.pack('<iii', n, N, N)             # count, gridW, gridH
    nb = name.encode('utf-8')
    write_7bit_len(out, len(nb))
    out += nb
    out += positions.tobytes()                       # float32 XYZ, little-endian
    out += colors.tobytes()                          # uint8 RGB
    with open(path, 'wb') as f:
        f.write(out)
    return n


if __name__ == '__main__':
    here = os.path.dirname(os.path.abspath(__file__))
    out_dir = os.path.join(here, 'pc_demo')
    os.makedirs(out_dir, exist_ok=True)

    for ng, fname, label in [
        (False, 'transmission_gear_ok.vpc', 'Transmission_ClusterGear_OK'),
        (True,  'transmission_gear_ng.vpc', 'Transmission_ClusterGear_NG'),
    ]:
        pos, col, nvalid = build(ng)
        n = save_vpc(os.path.join(out_dir, fname), label, pos, col)
        zs = pos[:, 2]
        zv = zs[zs > 0]
        print(f"{label}: total={n:,} valid(visible)={nvalid:,} "
              f"Z[{zv.min():.2f},{zv.max():.2f}]mm -> {fname}")
    print("done.")
