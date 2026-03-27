using System;
using System.IO;
using System.Windows;
using VMS.Core.Interfaces;
using VMS.Core.Services;
using VMS.DeepLearning.Services;
using VMS.DeepLearning.ViewModels;
using VMS.DeepLearning.Views;

namespace VMS.DeepLearning
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            string datasetFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VMS", "Datasets");

            IAnnotationService annotationService = new AnnotationService(datasetFolder);
            ITrainingService trainingService = new TrainingService();
            ILabelingDialogService dialogService = new LabelingDialogService();

            var viewModel = new LabelingMainViewModel(annotationService, trainingService, dialogService);

            var mainWindow = new MainWindow
            {
                DataContext = viewModel
            };
            mainWindow.Show();
        }
    }
}
