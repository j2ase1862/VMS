using VMS.Camera.Models;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace VMS.VisionSetup.ViewModels
{
    public partial class RecipeEditorViewModel : ObservableObject
    {
        private readonly IRecipeService _recipeService;
        private readonly ICameraService _cameraService;
        private readonly IDialogService _dialogService;

        [ObservableProperty]
        private Recipe? _currentRecipe;

        [ObservableProperty]
        private bool _hasUnsavedChanges;

        [ObservableProperty]
        private string _recipeName = "No Recipe Loaded";

        [ObservableProperty]
        private string _recipeInfo = string.Empty;

        [ObservableProperty]
        private string _statusText = string.Empty;

        [ObservableProperty]
        private ObservableCollection<RecipeTreeNode> _treeNodes = new();

        [ObservableProperty]
        private RecipeTreeNode? _selectedNode;

        // Properties panel visibility
        [ObservableProperty]
        private bool _showRecipeProperties;

        [ObservableProperty]
        private bool _showStepProperties;

        [ObservableProperty]
        private bool _showToolProperties;

        [ObservableProperty]
        private bool _showNoSelection = true;

        // Button states
        [ObservableProperty]
        private bool _canAddStep;

        [ObservableProperty]
        private bool _canAddTool;

        [ObservableProperty]
        private bool _canRemove;

        public event EventHandler? RecipeSaved;

        // Accessors for code-behind that needs service access (property panels)
        public IRecipeService RecipeServiceAccessor => _recipeService;
        public ICameraService CameraServiceAccessor => _cameraService;
        public IDialogService DialogServiceAccessor => _dialogService;

        public RecipeEditorViewModel(IRecipeService recipeService, ICameraService cameraService, IDialogService dialogService)
        {
            _recipeService = recipeService;
            _cameraService = cameraService;
            _dialogService = dialogService;
        }

        public void LoadRecipe(Recipe recipe)
        {
            CurrentRecipe = recipe;
            HasUnsavedChanges = false;
            RefreshTree();
            UpdateHeader();
            StatusText = $"Loaded recipe: {recipe.Name}";
        }

        partial void OnSelectedNodeChanged(RecipeTreeNode? value)
        {
            HideAllProperties();

            if (value != null)
            {
                CanAddTool = value.NodeType == "Step";
                CanRemove = value.NodeType == "Step" || value.NodeType == "Tool";
            }
            else
            {
                CanAddTool = false;
                CanRemove = false;
            }
        }

        private void HideAllProperties()
        {
            ShowRecipeProperties = false;
            ShowStepProperties = false;
            ShowToolProperties = false;
            ShowNoSelection = true;
        }

        private void RefreshTree()
        {
            TreeNodes.Clear();

            if (CurrentRecipe == null) return;

            var recipeNode = new RecipeTreeNode
            {
                Name = CurrentRecipe.Name,
                Subtitle = "Recipe",
                NodeType = "Recipe",
                NodeId = CurrentRecipe.Id,
                IsExpanded = true
            };

            foreach (var step in CurrentRecipe.Steps.OrderBy(s => s.Sequence))
            {
                var stepNode = new RecipeTreeNode
                {
                    Name = step.Name,
                    Subtitle = $"Step {step.Sequence}",
                    NodeType = "Step",
                    NodeId = step.Id,
                    IsExpanded = true
                };

                foreach (var tool in step.Tools.OrderBy(t => t.Sequence))
                {
                    stepNode.Children.Add(new RecipeTreeNode
                    {
                        Name = tool.Name,
                        Subtitle = tool.ToolType,
                        NodeType = "Tool",
                        NodeId = tool.Id,
                        ParentId = step.Id
                    });
                }

                recipeNode.Children.Add(stepNode);
            }

            TreeNodes.Add(recipeNode);
        }

        private void UpdateHeader()
        {
            if (CurrentRecipe != null)
            {
                RecipeName = CurrentRecipe.Name;
                RecipeInfo = $"v{CurrentRecipe.Version} | {CurrentRecipe.Steps.Count} steps | " +
                    $"{CurrentRecipe.Steps.Sum(s => s.Tools.Count)} tools";
                CanAddStep = true;
            }
            else
            {
                RecipeName = "No Recipe Loaded";
                RecipeInfo = string.Empty;
                CanAddStep = false;
            }
        }

        private void MarkUnsavedChanges()
        {
            HasUnsavedChanges = true;
            UpdateHeader();
        }

        [RelayCommand]
        private void AddStep()
        {
            if (CurrentRecipe == null) return;

            var step = new InspectionStep
            {
                Name = $"Step {CurrentRecipe.Steps.Count(s => string.IsNullOrEmpty(s.CameraId)) + 1}",
            };

            _recipeService.AddStep(CurrentRecipe, step);
            MarkUnsavedChanges();
            RefreshTree();
            StatusText = $"Added step: {step.Name}";
        }

        [RelayCommand]
        private void AddTool()
        {
            if (CurrentRecipe == null || SelectedNode?.NodeType != "Step") return;

            var step = CurrentRecipe.Steps.FirstOrDefault(s => s.Id == SelectedNode.NodeId);
            if (step == null) return;

            var dialog = new Views.Recipe.AddToolDialog();
            dialog.Owner = System.Windows.Application.Current.MainWindow;
            if (dialog.ShowDialog() == true)
            {
                var toolConfig = new ToolConfig
                {
                    Name = dialog.ToolName,
                    ToolType = dialog.SelectedToolType,
                    Sequence = step.Tools.Count + 1,
                    IsEnabled = true
                };

                _recipeService.AddToolToStep(CurrentRecipe, step.Id, toolConfig);
                MarkUnsavedChanges();
                RefreshTree();
                StatusText = $"Added tool: {toolConfig.Name}";
            }
        }

        [RelayCommand]
        private void RemoveSelected()
        {
            if (CurrentRecipe == null || SelectedNode == null) return;

            if (SelectedNode.NodeType == "Step")
            {
                var step = CurrentRecipe.Steps.FirstOrDefault(s => s.Id == SelectedNode.NodeId);
                if (step == null) return;

                if (_dialogService.ShowConfirmation(
                    $"'{step.Name}' 스텝과 포함된 모든 툴을 삭제하시겠습니까?",
                    "Delete Step"))
                {
                    _recipeService.RemoveStep(CurrentRecipe, SelectedNode.NodeId);
                    MarkUnsavedChanges();
                    RefreshTree();
                    StatusText = $"Removed step: {step.Name}";
                }
            }
            else if (SelectedNode.NodeType == "Tool" && SelectedNode.ParentId != null)
            {
                var step = CurrentRecipe.Steps.FirstOrDefault(s => s.Id == SelectedNode.ParentId);
                var tool = step?.Tools.FirstOrDefault(t => t.Id == SelectedNode.NodeId);
                if (tool == null) return;

                if (_dialogService.ShowConfirmation(
                    $"'{tool.Name}' 툴을 삭제하시겠습니까?",
                    "Delete Tool"))
                {
                    _recipeService.RemoveToolFromStep(CurrentRecipe, SelectedNode.ParentId, SelectedNode.NodeId);
                    MarkUnsavedChanges();
                    RefreshTree();
                    StatusText = $"Removed tool: {tool.Name}";
                }
            }
        }

        [RelayCommand]
        private void MoveStepUp()
        {
            if (CurrentRecipe == null || SelectedNode?.NodeType != "Step") return;

            var step = CurrentRecipe.Steps.FirstOrDefault(s => s.Id == SelectedNode.NodeId);
            if (step == null || step.Sequence <= 1) return;

            _recipeService.MoveStep(CurrentRecipe, step.Id, step.Sequence - 1);
            MarkUnsavedChanges();
            RefreshTree();
        }

        [RelayCommand]
        private void MoveStepDown()
        {
            if (CurrentRecipe == null || SelectedNode?.NodeType != "Step") return;

            var step = CurrentRecipe.Steps.FirstOrDefault(s => s.Id == SelectedNode.NodeId);
            if (step == null) return;
            int cameraStepCount = CurrentRecipe.Steps.Count(s => s.CameraId == step.CameraId);
            if (step.Sequence >= cameraStepCount) return;

            _recipeService.MoveStep(CurrentRecipe, step.Id, step.Sequence + 1);
            MarkUnsavedChanges();
            RefreshTree();
        }

        [RelayCommand]
        private void SaveRecipe()
        {
            if (CurrentRecipe == null) return;

            CurrentRecipe.ModifiedAt = DateTime.Now;
            _recipeService.SaveRecipe(CurrentRecipe);
            HasUnsavedChanges = false;
            UpdateHeader();
            StatusText = $"Saved recipe: {CurrentRecipe.Name}";
            RecipeSaved?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        private void DiscardChanges()
        {
            if (CurrentRecipe == null) return;

            if (_dialogService.ShowConfirmation(
                "저장되지 않은 변경 사항을 모두 취소하시겠습니까?",
                "Discard Changes"))
            {
                var reloaded = _recipeService.LoadRecipe(
                    System.IO.Path.Combine(_recipeService.RecipeFolderPath,
                        $"recipe_{CurrentRecipe.Name.ToLower().Replace(" ", "_")}.json"));

                if (reloaded != null)
                {
                    LoadRecipe(reloaded);
                }

                StatusText = "Discarded changes";
            }
        }

        public System.Collections.Generic.List<CameraInfo> GetAllCameras() => _cameraService.GetAllCameras();
    }

    public partial class RecipeTreeNode : ObservableObject
    {
        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private string _subtitle = string.Empty;

        [ObservableProperty]
        private bool _isExpanded;

        public string NodeType { get; set; } = string.Empty;
        public string NodeId { get; set; } = string.Empty;
        public string? ParentId { get; set; }

        public ObservableCollection<RecipeTreeNode> Children { get; } = new();
    }
}
