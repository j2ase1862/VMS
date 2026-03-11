using System.Collections.Generic;
using VMS.Core.Models.Annotation;

namespace VMS.Core.Interfaces
{
    public interface IAnnotationService
    {
        string DatasetFolderPath { get; }

        AnnotationDataset CreateDataset(string name, LabelType taskType);
        AnnotationDataset? LoadDataset(string datasetPath);
        bool SaveDataset(AnnotationDataset dataset);
        bool DeleteDataset(string datasetId);
        List<AnnotationDataset> GetDatasetList();

        AnnotationImage? AddImage(AnnotationDataset dataset, string sourceImagePath);
        List<AnnotationImage> AddImages(AnnotationDataset dataset, IEnumerable<string> sourceImagePaths);
        bool RemoveImage(AnnotationDataset dataset, string imageId);

        LabelInfo AddLabel(AnnotationImage image, LabelInfo label);
        bool UpdateLabel(AnnotationImage image, LabelInfo label);
        bool RemoveLabel(AnnotationImage image, string labelId);

        bool AddClass(AnnotationDataset dataset, string className);
        bool RenameClass(AnnotationDataset dataset, string oldName, string newName);
        bool RemoveClass(AnnotationDataset dataset, string className);

        void AutoSplitDataset(AnnotationDataset dataset);

        bool ExportYolo(AnnotationDataset dataset, string outputPath);
        bool ExportPaddleOcrDet(AnnotationDataset dataset, string outputPath);
        bool ExportPaddleOcrRec(AnnotationDataset dataset, string outputPath);
    }
}
