#!/usr/bin/env python3
"""
VMS 포토메트릭 스테레오 데모용 — 프레스 가공 자동차 도어 패널(흠집/덴트) 다중조명 이미지 생성.

PsVerify(tools/PsVerify/Program.cs)와 동일한 렌더링/조명 규약을 따른다:
  - Lambertian: I = albedo * max(0, n·L), 8bit grayscale
  - 법선 n = (-zx, -zy, 1)/|·|  (높이장 중심차분)
  - 조명 5개: 고도 50°, 방위 0/90/180/270° + 정수리(top)
이렇게 해야 PhotometricStereoTool 의 동일 조명방향 설정으로 정확히 복원된다.

도어 패널 형상:
  - 완만한 곡률(프레스 패널) + 캐릭터 라인(feature line) 1개
  - 결함: 얕은 덴트(dent) 3개 + 가는 스크래치(scratch) 4개
  - 균일 알베도(도장면) — 결함은 형상(곡률)에서 드러나도록

출력: test_images/ps_door/light_0..4.png  (+ preview_lit.png / preview_curv.png)
"""
import os
import numpy as np

N = 512
ALBEDO = 0.85


def point_seg_dist(X, Y, p0, p1):
    """각 픽셀에서 선분 p0-p1 까지의 최단거리(벡터화)."""
    x0, y0 = p0
    x1, y1 = p1
    dx, dy = x1 - x0, y1 - y0
    L2 = dx * dx + dy * dy + 1e-9
    t = ((X - x0) * dx + (Y - y0) * dy) / L2
    t = np.clip(t, 0.0, 1.0)
    px = x0 + t * dx
    py = y0 + t * dy
    return np.hypot(X - px, Y - py)


def build_height():
    ax = np.arange(N, dtype=np.float64)
    X, Y = np.meshgrid(ax, ax)
    cx = cy = N / 2.0

    z = np.zeros((N, N), dtype=np.float64)

    # 1) 완만한 곡률 (프레스 패널: x 방향 원통형 + 약한 구면)
    z += -5.0 * ((X - cx) / (N * 0.7)) ** 2          # x 원통형
    z += -2.5 * ((Y - cy) / (N * 0.9)) ** 2          # 약한 세로 곡률

    # 2) 캐릭터 라인(feature line): 좌상→우하 완만한 능선
    d_line = point_seg_dist(X, Y, (40, 150), (N - 40, N - 110))
    z += 3.2 * np.exp(-(d_line ** 2) / (2 * 16.0 ** 2))

    # 3) 결함 — 덴트(얕은 함몰). 슈퍼가우시안(p>2)으로 가장자리를 또렷이 → 곡률 검출 ↑
    dents = [  # (cx, cy, depth, sigma, p)
        (150, 360, 6.0, 7.0, 2.4),
        (330, 200, 5.0, 5.5, 2.4),
        (380, 400, 6.5, 8.0, 2.4),
    ]
    for dx, dy, depth, sig, p in dents:
        rr = np.hypot(X - dx, Y - dy)
        z -= depth * np.exp(-0.5 * (rr / sig) ** p)

    # 4) 결함 — 스크래치(가는 고랑)
    scratches = [  # (p0, p1, depth, halfwidth)
        ((110, 110), (210, 175), 2.6, 1.3),
        ((300, 330), (430, 300), 2.2, 1.1),
        ((200, 430), (260, 330), 2.4, 1.2),
        ((360, 120), (360, 230), 2.0, 1.0),
    ]
    for p0, p1, depth, hw in scratches:
        d = point_seg_dist(X, Y, p0, p1)
        z -= depth * np.exp(-(d ** 2) / (2 * hw ** 2))

    # 5) 미세 표면 노이즈
    rng = np.random.default_rng(7)
    z += rng.normal(0, 0.05, size=z.shape)
    return z


def normals(z):
    zx = np.zeros_like(z)
    zy = np.zeros_like(z)
    zx[:, 1:-1] = (z[:, 2:] - z[:, :-2]) * 0.5
    zy[1:-1, :] = (z[2:, :] - z[:-2, :]) * 0.5
    nx, ny, nz = -zx, -zy, np.ones_like(z)
    m = np.sqrt(nx * nx + ny * ny + nz * nz)
    return nx / m, ny / m, nz / m


def lights():
    el = np.deg2rad(50.0)
    c, s = np.cos(el), np.sin(el)
    L = [(c * np.cos(a), c * np.sin(a), s) for a in np.deg2rad([0, 90, 180, 270])]
    L.append((0.0, 0.0, 1.0))  # top
    return L


def main():
    from PIL import Image
    here = os.path.dirname(os.path.abspath(__file__))
    out = os.path.join(here, 'ps_door')
    os.makedirs(out, exist_ok=True)

    z = build_height()
    nx, ny, nz = normals(z)

    for i, (lx, ly, lz) in enumerate(lights()):
        dot = np.clip(nx * lx + ny * ly + nz * lz, 0, None)
        img = np.clip(ALBEDO * dot * 255.0, 0, 255).astype(np.uint8)
        Image.fromarray(img).save(os.path.join(out, f'light_{i}.png'))
        print(f'  light_{i}.png  L=({lx:6.3f},{ly:6.3f},{lz:6.3f})')

    # 미리보기: 측면광(light_0) + 곡률맵(라플라시안 근사 → 결함 강조 예상)
    dot0 = np.clip(nx * 0.643 + nz * 0.766, 0, None)
    Image.fromarray((ALBEDO * dot0 * 255).astype(np.uint8)).save(os.path.join(out, 'preview_lit.png'))
    lap = np.zeros_like(z)
    lap[1:-1, 1:-1] = (z[2:, 1:-1] + z[:-2, 1:-1] + z[1:-1, 2:] + z[1:-1, :-2] - 4 * z[1:-1, 1:-1])
    v = np.clip(0.5 + lap * 1.2, 0, 1)
    Image.fromarray((v * 255).astype(np.uint8)).save(os.path.join(out, 'preview_curv.png'))
    print('done. ps_door/light_0..4.png + preview_lit.png + preview_curv.png')


if __name__ == '__main__':
    main()
