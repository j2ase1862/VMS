using System;
using VMS.Core.Models.Annotation;

namespace VMS.Core.Services
{
    /// <summary>
    /// 학습 도구의 <see cref="DatasetTaskType"/> ↔ 레지스트리가 쓰는 작업 유형 이름.
    ///
    /// <para>
    /// 이름이 서로 다르다 — 학습 도구는 <c>AnomalyDetection</c>·<c>YoloSegmentation</c> 처럼 부르고,
    /// 레지스트리는 소문자 <c>anomaly</c>·<c>segmentation</c> 을 쓴다.
    /// 대응을 한 곳에 모아 두지 않으면 업로드가 "작업 유형이 맞지 않습니다" 로 막히는데,
    /// 왜 막히는지는 두 이름을 나란히 놓기 전에는 알기 어렵다.
    /// </para>
    /// </summary>
    public static class RegistryTaskType
    {
        /// <summary>레지스트리에 보낼 이름. 모르는 값이면 예외 — 조용히 detection 으로 밀면 안 된다.</summary>
        public static string From(DatasetTaskType taskType) => taskType switch
        {
            DatasetTaskType.Detection => "detection",
            DatasetTaskType.Classification => "classification",
            DatasetTaskType.OCR => "ocr",
            DatasetTaskType.AnomalyDetection => "anomaly",
            DatasetTaskType.Segmentation => "segmentation",
            _ => throw new ArgumentOutOfRangeException(nameof(taskType),
                $"레지스트리에 대응하는 작업 유형이 없습니다: {taskType}"),
        };

        /// <summary>레지스트리가 돌려준 이름을 학습 도구 쪽으로. 모르는 값이면 null.</summary>
        public static DatasetTaskType? To(string? registryName) =>
            (registryName ?? "").Trim().ToLowerInvariant() switch
            {
                "detection" => DatasetTaskType.Detection,
                "classification" => DatasetTaskType.Classification,
                "ocr" => DatasetTaskType.OCR,
                "anomaly" => DatasetTaskType.AnomalyDetection,
                "segmentation" => DatasetTaskType.Segmentation,
                _ => null,
            };

        /// <summary>사람에게 보여 줄 이름</summary>
        public static string Describe(DatasetTaskType taskType) => taskType switch
        {
            DatasetTaskType.Detection => "검출",
            DatasetTaskType.Classification => "분류",
            DatasetTaskType.OCR => "문자 인식",
            DatasetTaskType.AnomalyDetection => "이상 탐지",
            DatasetTaskType.Segmentation => "세그멘테이션",
            _ => taskType.ToString(),
        };
    }
}
