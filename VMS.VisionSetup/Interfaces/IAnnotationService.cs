using System.Collections.Generic;
using VMS.VisionSetup.Models.Annotation;

namespace VMS.VisionSetup.Interfaces
{
    /// <summary>
    /// 딥러닝 라벨링 데이터셋 관리 서비스 인터페이스.
    /// 데이터셋 CRUD, 이미지/라벨 관리, 학습 포맷 내보내기를 담당합니다.
    /// </summary>
    public interface IAnnotationService
    {
        // ── Dataset CRUD ──

        /// <summary>데이터셋 폴더 루트 경로</summary>
        string DatasetFolderPath { get; }

        /// <summary>새 데이터셋 생성</summary>
        AnnotationDataset CreateDataset(string name, LabelType taskType);

        /// <summary>데이터셋 로드</summary>
        AnnotationDataset? LoadDataset(string datasetPath);

        /// <summary>데이터셋 저장</summary>
        bool SaveDataset(AnnotationDataset dataset);

        /// <summary>데이터셋 삭제</summary>
        bool DeleteDataset(string datasetId);

        /// <summary>모든 데이터셋 목록 조회</summary>
        List<AnnotationDataset> GetDatasetList();

        // ── Image Management ──

        /// <summary>이미지 추가 (파일을 데이터셋 폴더로 복사)</summary>
        AnnotationImage? AddImage(AnnotationDataset dataset, string sourceImagePath);

        /// <summary>이미지 다건 추가</summary>
        List<AnnotationImage> AddImages(AnnotationDataset dataset, IEnumerable<string> sourceImagePaths);

        /// <summary>이미지 제거</summary>
        bool RemoveImage(AnnotationDataset dataset, string imageId);

        // ── Label Management ──

        /// <summary>라벨 추가</summary>
        LabelInfo AddLabel(AnnotationImage image, LabelInfo label);

        /// <summary>라벨 수정</summary>
        bool UpdateLabel(AnnotationImage image, LabelInfo label);

        /// <summary>라벨 삭제</summary>
        bool RemoveLabel(AnnotationImage image, string labelId);

        // ── Class Management ──

        /// <summary>클래스 추가</summary>
        bool AddClass(AnnotationDataset dataset, string className);

        /// <summary>클래스 이름 변경 (모든 라벨에 반영)</summary>
        bool RenameClass(AnnotationDataset dataset, string oldName, string newName);

        /// <summary>클래스 삭제 (해당 클래스의 라벨도 삭제)</summary>
        bool RemoveClass(AnnotationDataset dataset, string className);

        // ── Data Split ──

        /// <summary>Train/Validation 자동 분할</summary>
        void AutoSplitDataset(AnnotationDataset dataset);

        // ── Export ──

        /// <summary>YOLO 포맷으로 내보내기</summary>
        bool ExportYolo(AnnotationDataset dataset, string outputPath);

        /// <summary>PaddleOCR Detection 학습 포맷으로 내보내기</summary>
        bool ExportPaddleOcrDet(AnnotationDataset dataset, string outputPath);

        /// <summary>PaddleOCR Recognition 학습 포맷으로 내보내기</summary>
        bool ExportPaddleOcrRec(AnnotationDataset dataset, string outputPath);
    }
}
