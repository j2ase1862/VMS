#!/usr/bin/env python3
"""
VMS PaddleOCR Fine-tuning 학습 스크립트.
VMS TrainingService가 이 스크립트를 외부 프로세스로 실행합니다.

stdout 프로토콜:
    [EPOCH] 5/100
    [LOSS] 0.0234
    [ACC] 0.9512
    [PROGRESS] 45.5
    [ONNX] C:\\output\\model.onnx
    [DONE]
    [ERROR] message

사전 준비:
    pip install paddlepaddle paddleocr paddle2onnx onnx

사용 예:
    python train_ppocr.py --target recognition --dataset ./data --output ./output --epochs 100

VMS Labeling 탭에서 Export PaddleOCR로 내보낸 폴더를 --dataset에 지정하세요.
"""

import argparse
import os
import sys
import shutil
import subprocess


def parse_args():
    parser = argparse.ArgumentParser(description="VMS PaddleOCR Fine-tuning")
    parser.add_argument("--target", choices=["detection", "recognition"], default="recognition",
                        help="학습 대상: detection(텍스트 검출) 또는 recognition(텍스트 인식)")
    parser.add_argument("--dataset", required=True, help="학습 데이터셋 경로 (VMS Export 폴더)")
    parser.add_argument("--output", required=True, help="학습 결과 출력 디렉토리")
    parser.add_argument("--pretrained", default="", help="사전학습 모델 경로 (Fine-tuning 기반)")
    parser.add_argument("--epochs", type=int, default=100, help="학습 에폭 수")
    parser.add_argument("--lr", type=float, default=0.001, help="학습률")
    parser.add_argument("--batch_size", type=int, default=8, help="배치 크기")
    parser.add_argument("--export_onnx", action="store_true", help="학습 완료 후 ONNX 변환")
    return parser.parse_args()


def check_dependencies():
    """필수 패키지 확인"""
    missing = []
    for pkg in ["paddle", "paddleocr"]:
        try:
            __import__(pkg)
        except ImportError:
            missing.append(pkg)

    if missing:
        print(f"[ERROR] 필수 패키지가 설치되지 않았습니다: {', '.join(missing)}")
        print(f"[ERROR] 설치: pip install paddlepaddle paddleocr")
        sys.exit(1)


def create_rec_config(args):
    """PaddleOCR Recognition 학습 설정 YAML 생성"""
    config = f"""
Global:
  use_gpu: true
  epoch_num: {args.epochs}
  log_smooth_window: 20
  print_batch_step: 10
  save_model_dir: {args.output}/rec_model
  save_epoch_step: 10
  eval_batch_step: [0, 100]
  cal_metric_during_train: true
  pretrained_model: {args.pretrained if args.pretrained else ""}
  checkpoints:
  save_inference_dir: {args.output}/rec_inference
  use_visualdl: false
  infer_img:
  character_dict_path: {args.dataset}/ppocr_keys_v1.txt
  max_text_length: 25
  infer_mode: false
  use_space_char: true

Optimizer:
  name: Adam
  beta1: 0.9
  beta2: 0.999
  lr:
    name: Cosine
    learning_rate: {args.lr}
    warmup_epoch: 5
  regularizer:
    name: L2
    factor: 3.0e-05

Architecture:
  model_type: rec
  algorithm: SVTR_LCNet
  Transform:
  Backbone:
    name: PPLCNetV3
    scale: 0.95
  Head:
    name: MultiHead
    head_list:
      - CTCHead:
          Neck:
            name: svtr
            dims: 120
            depth: 2
            hidden_dims: 120
            kernel_size: [1, 3]
            use_guide: true
          Head:
            fc_decay: 0.00001
      - NRTRHead:
          nrtr_dim: 384
          max_text_length: 25

Loss:
  name: MultiLoss
  loss_config_list:
    - CTCLoss:
    - NRTRLoss:

PostProcess:
  name: CTCLabelDecode

Metric:
  name: RecMetric
  main_indicator: acc

Train:
  dataset:
    name: SimpleDataSet
    data_dir: {args.dataset}/train_crops
    label_file_list: ["{args.dataset}/train_rec.txt"]
    transforms:
      - DecodeImage:
          img_mode: BGR
          channel_first: false
      - RecAug:
      - MultiLabelEncode:
      - RecResizeImg:
          image_shape: [3, 48, 320]
      - KeepKeys:
          keep_keys: ["image", "label_ctc", "label_sar", "length", "valid_ratio"]
  loader:
    shuffle: true
    batch_size_per_card: {args.batch_size}
    drop_last: true
    num_workers: 2

Eval:
  dataset:
    name: SimpleDataSet
    data_dir: {args.dataset}/val_crops
    label_file_list: ["{args.dataset}/val_rec.txt"]
    transforms:
      - DecodeImage:
          img_mode: BGR
          channel_first: false
      - MultiLabelEncode:
      - RecResizeImg:
          image_shape: [3, 48, 320]
      - KeepKeys:
          keep_keys: ["image", "label_ctc", "label_sar", "length", "valid_ratio"]
  loader:
    shuffle: false
    drop_last: false
    batch_size_per_card: {args.batch_size}
    num_workers: 2
"""
    config_path = os.path.join(args.output, "rec_config.yml")
    os.makedirs(args.output, exist_ok=True)
    with open(config_path, "w", encoding="utf-8") as f:
        f.write(config)
    return config_path


def create_det_config(args):
    """PaddleOCR Detection 학습 설정 YAML 생성"""
    config = f"""
Global:
  use_gpu: true
  epoch_num: {args.epochs}
  log_smooth_window: 20
  print_batch_step: 10
  save_model_dir: {args.output}/det_model
  save_epoch_step: 10
  eval_batch_step: [0, 500]
  cal_metric_during_train: false
  pretrained_model: {args.pretrained if args.pretrained else ""}
  checkpoints:
  save_inference_dir: {args.output}/det_inference
  use_visualdl: false

Optimizer:
  name: Adam
  beta1: 0.9
  beta2: 0.999
  lr:
    name: Cosine
    learning_rate: {args.lr}
    warmup_epoch: 2
  regularizer:
    name: L2
    factor: 5.0e-05

Architecture:
  model_type: det
  algorithm: DB
  Transform:
  Backbone:
    name: PPLCNetV3
    scale: 0.75
    det: true
  Neck:
    name: RSEFPN
    out_channels: 96
    shortcut: true
  Head:
    name: DBHead
    k: 50

Loss:
  name: DBLoss
  balance_loss: true
  main_loss_type: DiceLoss
  alpha: 5
  beta: 10
  ohem_ratio: 3

PostProcess:
  name: DBPostProcess
  thresh: 0.3
  box_thresh: 0.6
  max_candidates: 1000
  unclip_ratio: 1.5

Metric:
  name: DetMetric
  main_indicator: hmean

Train:
  dataset:
    name: SimpleDataSet
    data_dir: {args.dataset}/train_images
    label_file_list: ["{args.dataset}/train_det.txt"]
    ratio_list: [1.0]
    transforms:
      - DecodeImage:
          img_mode: BGR
          channel_first: false
      - DetLabelEncode:
      - IaaAugment:
      - EastRandomCropData:
          size: [960, 960]
          max_tries: 50
          keep_ratio: true
      - MakeBorderMap:
          shrink_ratio: 0.4
          thresh_min: 0.3
          thresh_max: 0.7
      - MakeShrinkMap:
          shrink_ratio: 0.4
          min_text_size: 8
      - NormalizeImage:
          scale: 1./255.
          mean: [0.485, 0.456, 0.406]
          std: [0.229, 0.224, 0.225]
          order: hwc
      - ToCHWImage:
      - KeepKeys:
          keep_keys: ["image", "threshold_map", "threshold_mask", "shrink_map", "shrink_mask"]
  loader:
    shuffle: true
    drop_last: false
    batch_size_per_card: {args.batch_size}
    num_workers: 2

Eval:
  dataset:
    name: SimpleDataSet
    data_dir: {args.dataset}/val_images
    label_file_list: ["{args.dataset}/val_det.txt"]
    transforms:
      - DecodeImage:
          img_mode: BGR
          channel_first: false
      - DetLabelEncode:
      - DetResizeForTest:
          limit_side_len: 960
          limit_type: max
      - NormalizeImage:
          scale: 1./255.
          mean: [0.485, 0.456, 0.406]
          std: [0.229, 0.224, 0.225]
          order: hwc
      - ToCHWImage:
      - KeepKeys:
          keep_keys: ["image", "shape", "polys", "ignore_tags"]
  loader:
    shuffle: false
    drop_last: false
    batch_size_per_card: 1
    num_workers: 2
"""
    config_path = os.path.join(args.output, "det_config.yml")
    os.makedirs(args.output, exist_ok=True)
    with open(config_path, "w", encoding="utf-8") as f:
        f.write(config)
    return config_path


def train_with_paddleocr(config_path, args):
    """
    PaddleOCR 학습 실행.
    PaddleOCR의 tools/train.py를 직접 호출하거나,
    paddleocr 라이브러리를 활용합니다.
    """
    import paddle
    from paddleocr import PaddleOCR

    # PaddleOCR tools 경로 찾기
    paddleocr_dir = os.path.dirname(os.path.abspath(PaddleOCR.__module__
                                                      .replace(".", os.sep) + ".py"))
    # paddleocr 패키지 위치에서 tools/train.py 탐색
    possible_paths = [
        os.path.join(os.path.dirname(paddle.__file__), "..", "paddleocr", "tools", "train.py"),
        shutil.which("paddleocr"),
    ]

    # 직접 paddle 학습 루프 실행 (tools/train.py 못 찾을 경우)
    print(f"[EPOCH] 0/{args.epochs}", flush=True)
    print(f"[PROGRESS] 0", flush=True)

    # PaddleOCR CLI로 학습 시도
    cmd = [
        sys.executable, "-m", "paddle.distributed.launch",
        "--gpus", "0",
        "-m", "paddleocr.tools.train",
        "-c", config_path,
    ]

    # 간단한 방법: ppocr train CLI
    cmd_simple = [
        sys.executable, "-m", "tools.train",
        "-c", config_path,
    ]

    # paddleocr 설치 경로에서 train 모듈 찾기
    try:
        import paddleocr as poc
        poc_dir = os.path.dirname(poc.__file__)
        train_script = os.path.join(poc_dir, "tools", "train.py")

        if not os.path.exists(train_script):
            # ppocr 소스에서 찾기
            train_script = os.path.join(poc_dir, "..", "tools", "train.py")

        if os.path.exists(train_script):
            cmd = [sys.executable, train_script, "-c", config_path]
        else:
            print(f"[ERROR] PaddleOCR train.py를 찾을 수 없습니다.", flush=True)
            print(f"[ERROR] PaddleOCR 소스를 설치하세요: pip install paddleocr", flush=True)
            print(f"[ERROR] 또는 https://github.com/PaddlePaddle/PaddleOCR 를 클론하세요.", flush=True)
            sys.exit(1)
    except Exception as e:
        print(f"[ERROR] PaddleOCR 모듈 탐색 실패: {e}", flush=True)
        sys.exit(1)

    print(f"실행 명령: {' '.join(cmd)}", flush=True)

    proc = subprocess.Popen(
        cmd,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        encoding="utf-8",
        errors="replace",
        bufsize=1,
    )

    for line in proc.stdout:
        line = line.strip()
        if not line:
            continue

        # PaddleOCR 로그를 VMS 프로토콜로 변환
        print(line, flush=True)  # 원본 로그도 전달

        # epoch 파싱: "epoch: [5/100]"
        if "epoch:" in line.lower():
            try:
                parts = line.split("epoch:")[1].strip().strip("[]").split("/")
                if len(parts) == 2:
                    cur = int(parts[0].strip())
                    total = int(parts[1].strip())
                    print(f"[EPOCH] {cur}/{total}", flush=True)
                    print(f"[PROGRESS] {cur / total * 100:.1f}", flush=True)
            except (ValueError, IndexError):
                pass

        # loss 파싱: "loss: 0.0234"
        if "loss:" in line.lower():
            try:
                loss_val = float(line.lower().split("loss:")[1].strip().split()[0].rstrip(","))
                print(f"[LOSS] {loss_val}", flush=True)
            except (ValueError, IndexError):
                pass

        # accuracy 파싱: "acc: 0.95"
        if "acc:" in line.lower():
            try:
                acc_val = float(line.lower().split("acc:")[1].strip().split()[0].rstrip(","))
                print(f"[ACC] {acc_val}", flush=True)
            except (ValueError, IndexError):
                pass

    proc.wait()
    return proc.returncode


def export_to_onnx(args):
    """학습된 모델을 ONNX로 변환"""
    try:
        import paddle
        from paddle.static import InputSpec

        if args.target == "recognition":
            inference_dir = os.path.join(args.output, "rec_inference")
            model_dir = os.path.join(args.output, "rec_model", "latest")
            onnx_path = os.path.join(args.output, "custom_rec.onnx")
        else:
            inference_dir = os.path.join(args.output, "det_inference")
            model_dir = os.path.join(args.output, "det_model", "latest")
            onnx_path = os.path.join(args.output, "custom_det.onnx")

        # paddle2onnx로 변환
        cmd = [
            sys.executable, "-m", "paddle2onnx",
            "--model_dir", inference_dir,
            "--model_filename", "inference.pdmodel",
            "--params_filename", "inference.pdiparams",
            "--save_file", onnx_path,
            "--opset_version", "14",
            "--enable_onnx_checker", "true",
        ]

        print(f"ONNX 변환 중: {' '.join(cmd)}", flush=True)
        result = subprocess.run(cmd, capture_output=True, text=True)

        if result.returncode == 0 and os.path.exists(onnx_path):
            print(f"[ONNX] {os.path.abspath(onnx_path)}", flush=True)
            return True
        else:
            print(f"[ERROR] ONNX 변환 실패: {result.stderr}", flush=True)
            return False

    except Exception as e:
        print(f"[ERROR] ONNX 변환 오류: {e}", flush=True)
        return False


def main():
    args = parse_args()

    print(f"=" * 60, flush=True)
    print(f"VMS PaddleOCR Fine-tuning", flush=True)
    print(f"  Target:    {args.target}", flush=True)
    print(f"  Dataset:   {args.dataset}", flush=True)
    print(f"  Output:    {args.output}", flush=True)
    print(f"  Epochs:    {args.epochs}", flush=True)
    print(f"  LR:        {args.lr}", flush=True)
    print(f"  Batch:     {args.batch_size}", flush=True)
    print(f"  ONNX:      {args.export_onnx}", flush=True)
    print(f"=" * 60, flush=True)

    # 의존성 확인
    check_dependencies()

    # 설정 YAML 생성
    if args.target == "recognition":
        config_path = create_rec_config(args)
    else:
        config_path = create_det_config(args)

    print(f"설정 파일 생성: {config_path}", flush=True)

    # 학습 실행
    exit_code = train_with_paddleocr(config_path, args)

    if exit_code != 0:
        print(f"[ERROR] 학습 실패 (exit code: {exit_code})", flush=True)
        sys.exit(exit_code)

    # ONNX 변환
    if args.export_onnx:
        export_to_onnx(args)

    print("[DONE]", flush=True)


if __name__ == "__main__":
    main()
