using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;

namespace VMS.Core.Models.Annotation
{
    public enum DataSplit
    {
        Train,
        Validation,
        Test,
        Unassigned
    }

    public class AnnotationImage : ObservableObject
    {
        private string _id = Guid.NewGuid().ToString();
        public string Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        private string _imagePath = string.Empty;
        public string ImagePath
        {
            get => _imagePath;
            set => SetProperty(ref _imagePath, value);
        }

        private int _imageWidth;
        public int ImageWidth
        {
            get => _imageWidth;
            set => SetProperty(ref _imageWidth, value);
        }

        private int _imageHeight;
        public int ImageHeight
        {
            get => _imageHeight;
            set => SetProperty(ref _imageHeight, value);
        }

        private DataSplit _split = DataSplit.Unassigned;
        public DataSplit Split
        {
            get => _split;
            set => SetProperty(ref _split, value);
        }

        private bool _isLabeled;
        public bool IsLabeled
        {
            get => _isLabeled;
            set => SetProperty(ref _isLabeled, value);
        }

        private ObservableCollection<LabelInfo> _labels = new();
        public ObservableCollection<LabelInfo> Labels
        {
            get => _labels;
            set => SetProperty(ref _labels, value);
        }

        private DateTime _createdAt = DateTime.Now;
        public DateTime CreatedAt
        {
            get => _createdAt;
            set => SetProperty(ref _createdAt, value);
        }

        private DateTime _modifiedAt = DateTime.Now;
        public DateTime ModifiedAt
        {
            get => _modifiedAt;
            set => SetProperty(ref _modifiedAt, value);
        }
    }
}
