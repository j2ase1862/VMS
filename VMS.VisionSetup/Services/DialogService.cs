using System.Windows;
using Microsoft.Win32;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Models;

using VMS.VisionSetup.Views;
using VMS.VisionSetup.Views.Camera;
using VMS.VisionSetup.Views.Recipe;
using VMS.VisionSetup.Views.Sequence;

namespace VMS.VisionSetup.Services
{
    public class DialogService : IDialogService
    {
        private readonly ICameraService _cameraService;
        private readonly IRecipeService _recipeService;

        public DialogService(ICameraService cameraService, IRecipeService recipeService)
        {
            _cameraService = cameraService;
            _recipeService = recipeService;
        }

        public void ShowInformation(string message, string title)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public void ShowWarning(string message, string title)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        public void ShowError(string message, string title)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        public bool ShowConfirmation(string message, string title)
        {
            return MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
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

        public Recipe? ShowRecipeManagerDialog()
        {
            Recipe? loadedRecipe = null;
            var window = new RecipeManagerWindow(_recipeService, _cameraService, this);
            window.Owner = Application.Current.MainWindow;
            window.RecipeLoaded += (s, recipe) =>
            {
                loadedRecipe = recipe;
            };
            window.ShowDialog();
            return loadedRecipe;
        }

        public void ShowSequenceEditorDialog()
        {
            var window = new SequenceEditorWindow(_recipeService, _cameraService, this);
            window.Owner = Application.Current.MainWindow;
            window.ShowDialog();
        }
    }
}

