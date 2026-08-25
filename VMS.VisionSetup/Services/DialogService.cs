using System.Windows;
using Microsoft.Win32;
using VMS.Core.Interfaces;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Models;

using VMS.VisionSetup.Views;
using VMS.VisionSetup.Views.Calibration;
using VMS.VisionSetup.Views.Camera;
using VMS.VisionSetup.Views.Recipe;
using VMS.VisionSetup.Views.Sequence;

namespace VMS.VisionSetup.Services
{
    public class DialogService : IDialogService
    {
        private readonly ICameraService _cameraService;
        private readonly IRecipeService _recipeService;
        private readonly IParameterSyncService? _parameterSyncService;

        public DialogService(ICameraService cameraService, IRecipeService recipeService,
            IParameterSyncService? parameterSyncService = null)
        {
            _cameraService = cameraService;
            _recipeService = recipeService;
            _parameterSyncService = parameterSyncService;
        }

        // 모든 메시지는 공용 다크 MessageDialog 로 통일 (WPF 기본 흰 MessageBox 대체, 2026-08-25)

        public void ShowInformation(string message, string title)
        {
            Views.Common.MessageDialog.Show(Application.Current.MainWindow, message, title,
                Views.Common.MessageDialogKind.Info);
        }

        public void ShowWarning(string message, string title)
        {
            Views.Common.MessageDialog.Show(Application.Current.MainWindow, message, title,
                Views.Common.MessageDialogKind.Warning);
        }

        public void ShowError(string message, string title)
        {
            Views.Common.MessageDialog.Show(Application.Current.MainWindow, message, title,
                Views.Common.MessageDialogKind.Error);
        }

        public bool ShowConfirmation(string message, string title)
        {
            return Views.Common.MessageDialog.Show(Application.Current.MainWindow, message, title,
                Views.Common.MessageDialogKind.Question, isConfirmation: true);
        }

        public string? ShowOpenFileDialog(string title, string filter)
        {
            var dialog = new OpenFileDialog
            {
                Title = title,
                Filter = filter
            };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        public string? ShowFolderBrowserDialog(string description)
        {
            var dialog = new OpenFolderDialog
            {
                Title = description
            };
            return dialog.ShowDialog() == true ? dialog.FolderName : null;
        }

        public string? ShowSaveFileDialog(string filter, string defaultExt, string? fileName = null)
        {
            var dialog = new SaveFileDialog
            {
                Filter = filter,
                DefaultExt = defaultExt
            };
            if (fileName != null)
                dialog.FileName = fileName;
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        public System.Collections.Generic.Dictionary<string, string>? ShowCameraRemapDialog(
            System.Collections.Generic.IReadOnlyList<(string oldId, int stepCount)> unregistered,
            System.Collections.Generic.IReadOnlyList<VMS.Camera.Models.CameraInfo> cameras)
        {
            var dialog = new CameraRemapDialog(unregistered, cameras)
            {
                Owner = Application.Current.MainWindow,
            };
            return dialog.ShowDialog() == true ? dialog.Mapping : null;
        }

        public string? ShowRenameDialog(string currentName)
        {
            var dialog = new RenameDialog
            {
                Owner = Application.Current.MainWindow,
                ToolName = currentName
            };
            return dialog.ShowDialog() == true ? dialog.ToolName : null;
        }

        public void ShowCameraManagerDialog()
        {
            var window = new CameraManagerWindow(_cameraService, this);
            window.Owner = Application.Current.MainWindow;
            window.ShowDialog();
        }

        public void ShowCalibrationManagerDialog()
        {
            var window = new CalibrationManagerWindow(_recipeService, this);
            window.Owner = Application.Current.MainWindow;
            window.ShowDialog();
        }

        public Recipe? ShowRecipeManagerDialog()
        {
            Recipe? loadedRecipe = null;
            var window = new RecipeManagerWindow(_recipeService, _cameraService, this, _parameterSyncService);
            window.Owner = Application.Current.MainWindow;
            window.RecipeLoaded += (s, recipe) =>
            {
                loadedRecipe = recipe;
            };
            window.ShowDialog();
            return loadedRecipe;
        }

        public void ShowSequenceEditorDialog(System.Collections.Generic.IEnumerable<SequenceDeviceEntry>? extraDevices = null)
        {
            var window = new SequenceEditorWindow(_recipeService, _cameraService, this, extraDevices);
            window.Owner = Application.Current.MainWindow;
            window.ShowDialog();
        }

        public RecipeTemplate? ShowTemplateGalleryDialog()
        {
            var window = new Views.Templates.TemplateGalleryWindow
            {
                Owner = Application.Current.MainWindow
            };
            return window.ShowDialog() == true ? window.SelectedTemplate : null;
        }
    }
}

