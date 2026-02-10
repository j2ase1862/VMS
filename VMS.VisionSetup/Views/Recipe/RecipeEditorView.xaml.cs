using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace VMS.VisionSetup.Views.Recipe
{
    /// <summary>
    /// RecipeEditorView.xaml 코드 비하인드
    /// </summary>
    public partial class RecipeEditorView : UserControl
    {
        private Models.Recipe? _currentRecipe;
        private bool _hasUnsavedChanges;
        private bool _isLoadingProperties;

        // Tree node tags to identify item types
        private const string RecipeTag = "Recipe";
        private const string StepTag = "Step";
        private const string ToolTag = "Tool";

        public event EventHandler? RecipeSaved;
        public event EventHandler? RecipeDiscarded;

        public RecipeEditorView()
        {
            InitializeComponent();
            UpdateUI();
        }

        /// <summary>
        /// 레시피 로드
        /// </summary>
        public void LoadRecipe(Models.Recipe recipe)
        {
            _currentRecipe = recipe;
            _hasUnsavedChanges = false;
            RefreshTree();
            UpdateUI();
            UpdateStatus($"Loaded recipe: {recipe.Name}");
        }

        /// <summary>
        /// 현재 레시피 가져오기
        /// </summary>
        public Models.Recipe? GetCurrentRecipe() => _currentRecipe;

        private void RefreshTree()
        {
            RecipeTree.Items.Clear();

            if (_currentRecipe == null) return;

            // Root node for recipe
            var recipeNode = new TreeViewItem
            {
                Header = CreateTreeHeader(_currentRecipe.Name, "Recipe"),
                Tag = new TreeNodeData(RecipeTag, _currentRecipe.Id),
                IsExpanded = true
            };

            // Add steps
            foreach (var step in _currentRecipe.Steps.OrderBy(s => s.Sequence))
            {
                var stepNode = new TreeViewItem
                {
                    Header = CreateTreeHeader(step.Name, $"Step {step.Sequence}"),
                    Tag = new TreeNodeData(StepTag, step.Id),
                    IsExpanded = true
                };

                // Add tools for this step
                foreach (var tool in step.Tools.OrderBy(t => t.Sequence))
                {
                    var toolNode = new TreeViewItem
                    {
                        Header = CreateTreeHeader(tool.Name, tool.ToolType),
                        Tag = new TreeNodeData(ToolTag, tool.Id, step.Id)
                    };
                    stepNode.Items.Add(toolNode);
                }

                recipeNode.Items.Add(stepNode);
            }

            RecipeTree.Items.Add(recipeNode);
        }

        private StackPanel CreateTreeHeader(string name, string subtitle)
        {
            var panel = new StackPanel { Orientation = Orientation.Vertical };
            panel.Children.Add(new TextBlock
            {
                Text = name,
                Foreground = System.Windows.Media.Brushes.White,
                FontWeight = FontWeights.SemiBold
            });
            panel.Children.Add(new TextBlock
            {
                Text = subtitle,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88)),
                FontSize = 10
            });
            return panel;
        }

        private void UpdateUI()
        {
            bool hasRecipe = _currentRecipe != null;

            SaveButton.IsEnabled = hasRecipe && _hasUnsavedChanges;
            DiscardButton.IsEnabled = hasRecipe && _hasUnsavedChanges;
            AddStepButton.IsEnabled = hasRecipe;

            if (_currentRecipe != null)
            {
                RecipeNameText.Text = _currentRecipe.Name;
                RecipeInfoText.Text = $"v{_currentRecipe.Version} | {_currentRecipe.Steps.Count} steps | " +
                    $"{_currentRecipe.Steps.Sum(s => s.Tools.Count)} tools";
            }
            else
            {
                RecipeNameText.Text = "No Recipe Loaded";
                RecipeInfoText.Text = "";
            }
        }

        private void UpdateStatus(string message)
        {
            StatusText.Text = message;
        }

        private void MarkUnsavedChanges()
        {
            if (!_isLoadingProperties)
            {
                _hasUnsavedChanges = true;
                UpdateUI();
            }
        }

        private void RecipeTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var selectedItem = RecipeTree.SelectedItem as TreeViewItem;
            if (selectedItem?.Tag is TreeNodeData nodeData)
            {
                ShowProperties(nodeData);
            }
            else
            {
                HideAllProperties();
            }

            UpdateButtonStates();
        }

        private void UpdateButtonStates()
        {
            var selectedItem = RecipeTree.SelectedItem as TreeViewItem;
            var nodeData = selectedItem?.Tag as TreeNodeData;

            AddToolButton.IsEnabled = nodeData?.Type == StepTag;
            RemoveButton.IsEnabled = nodeData?.Type == StepTag || nodeData?.Type == ToolTag;
        }

        private void ShowProperties(TreeNodeData nodeData)
        {
            _isLoadingProperties = true;

            HideAllProperties();

            switch (nodeData.Type)
            {
                case RecipeTag:
                    ShowRecipeProperties();
                    break;
                case StepTag:
                    ShowStepProperties(nodeData.Id);
                    break;
                case ToolTag:
                    ShowToolProperties(nodeData.Id, nodeData.ParentId);
                    break;
            }

            _isLoadingProperties = false;
        }

        private void HideAllProperties()
        {
            RecipePropertiesPanel.Visibility = Visibility.Collapsed;
            StepPropertiesPanel.Visibility = Visibility.Collapsed;
            ToolPropertiesPanel.Visibility = Visibility.Collapsed;
            NoSelectionText.Visibility = Visibility.Visible;
        }

        private void ShowRecipeProperties()
        {
            if (_currentRecipe == null) return;

            NoSelectionText.Visibility = Visibility.Collapsed;
            RecipePropertiesPanel.Visibility = Visibility.Visible;
            PropertiesTitle.Text = "Recipe Properties";

            RecipeNameBox.Text = _currentRecipe.Name;
            RecipeDescriptionBox.Text = _currentRecipe.Description;
            RecipeVersionBox.Text = _currentRecipe.Version;
            RecipeAuthorBox.Text = _currentRecipe.Author;

            // Load cameras
            UsedCamerasList.Items.Clear();
            var allCameras = CameraService.Instance.GetAllCameras();
            foreach (var camera in allCameras)
            {
                var item = new CheckBox
                {
                    Content = $"{camera.Name} ({camera.Manufacturer} {camera.Model})",
                    Tag = camera.Id,
                    Foreground = System.Windows.Media.Brushes.White,
                    IsChecked = _currentRecipe.UsedCameraIds.Contains(camera.Id)
                };
                item.Checked += CameraCheckBox_Changed;
                item.Unchecked += CameraCheckBox_Changed;
                UsedCamerasList.Items.Add(item);
            }
        }

        private void ShowStepProperties(string stepId)
        {
            if (_currentRecipe == null) return;

            var step = _currentRecipe.Steps.FirstOrDefault(s => s.Id == stepId);
            if (step == null) return;

            NoSelectionText.Visibility = Visibility.Collapsed;
            StepPropertiesPanel.Visibility = Visibility.Visible;
            PropertiesTitle.Text = "Step Properties";

            StepNameBox.Text = step.Name;
            StepExposureBox.Text = step.Exposure.ToString();
            StepGainBox.Text = step.Gain.ToString();
            StepLightChannelBox.Text = step.LightingChannel.ToString();
            StepLightIntensityBox.Text = step.LightingIntensity.ToString();

            // Load cameras into combo box
            StepCameraBox.Items.Clear();
            var cameras = CameraService.Instance.GetAllCameras();
            foreach (var camera in cameras)
            {
                StepCameraBox.Items.Add(new ComboBoxItem
                {
                    Content = camera.Name,
                    Tag = camera.Id
                });
            }

            // Select current camera
            foreach (ComboBoxItem item in StepCameraBox.Items)
            {
                if (item.Tag?.ToString() == step.CameraId)
                {
                    StepCameraBox.SelectedItem = item;
                    break;
                }
            }
        }

        private void ShowToolProperties(string toolId, string? stepId)
        {
            if (_currentRecipe == null || stepId == null) return;

            var step = _currentRecipe.Steps.FirstOrDefault(s => s.Id == stepId);
            var tool = step?.Tools.FirstOrDefault(t => t.Id == toolId);
            if (tool == null) return;

            NoSelectionText.Visibility = Visibility.Collapsed;
            ToolPropertiesPanel.Visibility = Visibility.Visible;
            PropertiesTitle.Text = "Tool Properties";

            ToolNameBox.Text = tool.Name;
            ToolTypeText.Text = tool.ToolType;
            ToolEnabledCheckBox.IsChecked = tool.IsEnabled;
            ToolUseROICheckBox.IsChecked = tool.UseROI;

            ToolROIXBox.Text = tool.ROIX.ToString();
            ToolROIYBox.Text = tool.ROIY.ToString();
            ToolROIWidthBox.Text = tool.ROIWidth.ToString();
            ToolROIHeightBox.Text = tool.ROIHeight.ToString();

            // Show parameters summary
            if (tool.Parameters != null && tool.Parameters.Count > 0)
            {
                var paramList = tool.Parameters.Select(p => $"{p.Key}: {p.Value}");
                ToolParametersInfo.Text = string.Join("\n", paramList);
            }
            else
            {
                ToolParametersInfo.Text = "No parameters configured";
            }
        }

        private void CameraCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_currentRecipe == null || _isLoadingProperties) return;

            _currentRecipe.UsedCameraIds.Clear();
            foreach (CheckBox item in UsedCamerasList.Items)
            {
                if (item.IsChecked == true && item.Tag is string cameraId)
                {
                    _currentRecipe.UsedCameraIds.Add(cameraId);
                }
            }
            MarkUnsavedChanges();
        }

        private void RecipeProperty_Changed(object sender, RoutedEventArgs e)
        {
            if (_currentRecipe == null || _isLoadingProperties) return;

            _currentRecipe.Name = RecipeNameBox.Text;
            _currentRecipe.Description = RecipeDescriptionBox.Text;
            _currentRecipe.Version = RecipeVersionBox.Text;
            _currentRecipe.Author = RecipeAuthorBox.Text;

            MarkUnsavedChanges();
            RefreshTree();
        }

        private void RecipeProperty_Changed(object sender, TextChangedEventArgs e)
        {
            RecipeProperty_Changed(sender, (RoutedEventArgs)e);
        }

        private void StepProperty_Changed(object sender, RoutedEventArgs e)
        {
            if (_currentRecipe == null || _isLoadingProperties) return;

            var selectedItem = RecipeTree.SelectedItem as TreeViewItem;
            var nodeData = selectedItem?.Tag as TreeNodeData;
            if (nodeData?.Type != StepTag) return;

            var step = _currentRecipe.Steps.FirstOrDefault(s => s.Id == nodeData.Id);
            if (step == null) return;

            step.Name = StepNameBox.Text;

            if (double.TryParse(StepExposureBox.Text, out double exposure))
                step.Exposure = exposure;
            if (double.TryParse(StepGainBox.Text, out double gain))
                step.Gain = gain;
            if (int.TryParse(StepLightChannelBox.Text, out int channel))
                step.LightingChannel = channel;
            if (int.TryParse(StepLightIntensityBox.Text, out int intensity))
                step.LightingIntensity = intensity;

            var selectedCamera = StepCameraBox.SelectedItem as ComboBoxItem;
            if (selectedCamera?.Tag is string cameraId)
            {
                step.CameraId = cameraId;
            }

            MarkUnsavedChanges();
        }

        private void StepProperty_Changed(object sender, TextChangedEventArgs e)
        {
            StepProperty_Changed(sender, (RoutedEventArgs)e);
        }

        private void StepProperty_Changed(object sender, SelectionChangedEventArgs e)
        {
            StepProperty_Changed(sender, (RoutedEventArgs)e);
        }

        private void ToolProperty_Changed(object sender, RoutedEventArgs e)
        {
            if (_currentRecipe == null || _isLoadingProperties) return;

            var selectedItem = RecipeTree.SelectedItem as TreeViewItem;
            var nodeData = selectedItem?.Tag as TreeNodeData;
            if (nodeData?.Type != ToolTag || nodeData.ParentId == null) return;

            var step = _currentRecipe.Steps.FirstOrDefault(s => s.Id == nodeData.ParentId);
            var tool = step?.Tools.FirstOrDefault(t => t.Id == nodeData.Id);
            if (tool == null) return;

            tool.Name = ToolNameBox.Text;
            tool.IsEnabled = ToolEnabledCheckBox.IsChecked ?? true;
            tool.UseROI = ToolUseROICheckBox.IsChecked ?? false;

            if (int.TryParse(ToolROIXBox.Text, out int roiX))
                tool.ROIX = roiX;
            if (int.TryParse(ToolROIYBox.Text, out int roiY))
                tool.ROIY = roiY;
            if (int.TryParse(ToolROIWidthBox.Text, out int roiWidth))
                tool.ROIWidth = roiWidth;
            if (int.TryParse(ToolROIHeightBox.Text, out int roiHeight))
                tool.ROIHeight = roiHeight;

            MarkUnsavedChanges();
        }

        private void ToolProperty_Changed(object sender, TextChangedEventArgs e)
        {
            ToolProperty_Changed(sender, (RoutedEventArgs)e);
        }

        private void AddStep_Click(object sender, RoutedEventArgs e)
        {
            if (_currentRecipe == null) return;

            var step = new InspectionStep
            {
                Name = $"Step {_currentRecipe.Steps.Count(s => string.IsNullOrEmpty(s.CameraId)) + 1}",
            };

            RecipeService.Instance.AddStep(_currentRecipe, step);
            MarkUnsavedChanges();
            RefreshTree();
            UpdateStatus($"Added step: {step.Name}");
        }

        private void AddTool_Click(object sender, RoutedEventArgs e)
        {
            if (_currentRecipe == null) return;

            var selectedItem = RecipeTree.SelectedItem as TreeViewItem;
            var nodeData = selectedItem?.Tag as TreeNodeData;
            if (nodeData?.Type != StepTag) return;

            var step = _currentRecipe.Steps.FirstOrDefault(s => s.Id == nodeData.Id);
            if (step == null) return;

            // Show tool selection dialog
            var dialog = new AddToolDialog();
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() == true)
            {
                var toolConfig = new ToolConfig
                {
                    Name = dialog.ToolName,
                    ToolType = dialog.SelectedToolType,
                    Sequence = step.Tools.Count + 1,
                    IsEnabled = true
                };

                RecipeService.Instance.AddToolToStep(_currentRecipe, step.Id, toolConfig);
                MarkUnsavedChanges();
                RefreshTree();
                UpdateStatus($"Added tool: {toolConfig.Name}");
            }
        }

        private void RemoveSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_currentRecipe == null) return;

            var selectedItem = RecipeTree.SelectedItem as TreeViewItem;
            var nodeData = selectedItem?.Tag as TreeNodeData;
            if (nodeData == null) return;

            if (nodeData.Type == StepTag)
            {
                var step = _currentRecipe.Steps.FirstOrDefault(s => s.Id == nodeData.Id);
                if (step == null) return;

                var result = MessageBox.Show(
                    $"'{step.Name}' 스텝과 포함된 모든 툴을 삭제하시겠습니까?",
                    "Delete Step",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    RecipeService.Instance.RemoveStep(_currentRecipe, nodeData.Id);
                    MarkUnsavedChanges();
                    RefreshTree();
                    UpdateStatus($"Removed step: {step.Name}");
                }
            }
            else if (nodeData.Type == ToolTag && nodeData.ParentId != null)
            {
                var step = _currentRecipe.Steps.FirstOrDefault(s => s.Id == nodeData.ParentId);
                var tool = step?.Tools.FirstOrDefault(t => t.Id == nodeData.Id);
                if (tool == null) return;

                var result = MessageBox.Show(
                    $"'{tool.Name}' 툴을 삭제하시겠습니까?",
                    "Delete Tool",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    RecipeService.Instance.RemoveToolFromStep(_currentRecipe, nodeData.ParentId, nodeData.Id);
                    MarkUnsavedChanges();
                    RefreshTree();
                    UpdateStatus($"Removed tool: {tool.Name}");
                }
            }
        }

        private void MoveStepUp_Click(object sender, RoutedEventArgs e)
        {
            if (_currentRecipe == null) return;

            var selectedItem = RecipeTree.SelectedItem as TreeViewItem;
            var nodeData = selectedItem?.Tag as TreeNodeData;
            if (nodeData?.Type != StepTag) return;

            var step = _currentRecipe.Steps.FirstOrDefault(s => s.Id == nodeData.Id);
            if (step == null || step.Sequence <= 1) return;

            RecipeService.Instance.MoveStep(_currentRecipe, step.Id, step.Sequence - 1);
            MarkUnsavedChanges();
            RefreshTree();
        }

        private void MoveStepDown_Click(object sender, RoutedEventArgs e)
        {
            if (_currentRecipe == null) return;

            var selectedItem = RecipeTree.SelectedItem as TreeViewItem;
            var nodeData = selectedItem?.Tag as TreeNodeData;
            if (nodeData?.Type != StepTag) return;

            var step = _currentRecipe.Steps.FirstOrDefault(s => s.Id == nodeData.Id);
            if (step == null) return;
            int cameraStepCount = _currentRecipe.Steps.Count(s => s.CameraId == step.CameraId);
            if (step.Sequence >= cameraStepCount) return;

            RecipeService.Instance.MoveStep(_currentRecipe, step.Id, step.Sequence + 1);
            MarkUnsavedChanges();
            RefreshTree();
        }

        private void SaveRecipe_Click(object sender, RoutedEventArgs e)
        {
            if (_currentRecipe == null) return;

            _currentRecipe.ModifiedAt = DateTime.Now;
            RecipeService.Instance.SaveRecipe(_currentRecipe);
            _hasUnsavedChanges = false;
            UpdateUI();
            UpdateStatus($"Saved recipe: {_currentRecipe.Name}");
            RecipeSaved?.Invoke(this, EventArgs.Empty);
        }

        private void DiscardChanges_Click(object sender, RoutedEventArgs e)
        {
            if (_currentRecipe == null) return;

            var result = MessageBox.Show(
                "저장되지 않은 변경 사항을 모두 취소하시겠습니까?",
                "Discard Changes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                // Reload from file
                var reloaded = RecipeService.Instance.LoadRecipe(
                    System.IO.Path.Combine(RecipeService.Instance.RecipeFolderPath,
                        $"recipe_{_currentRecipe.Name.ToLower().Replace(" ", "_")}.json"));

                if (reloaded != null)
                {
                    LoadRecipe(reloaded);
                }

                UpdateStatus("Discarded changes");
                RecipeDiscarded?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// 저장되지 않은 변경 사항 확인
        /// </summary>
        public bool HasUnsavedChanges => _hasUnsavedChanges;
    }

    /// <summary>
    /// 트리 노드 데이터
    /// </summary>
    internal class TreeNodeData
    {
        public string Type { get; }
        public string Id { get; }
        public string? ParentId { get; }

        public TreeNodeData(string type, string id, string? parentId = null)
        {
            Type = type;
            Id = id;
            ParentId = parentId;
        }
    }

    /// <summary>
    /// 툴 추가 다이얼로그
    /// </summary>
    public class AddToolDialog : Window
    {
        public string ToolName { get; private set; } = string.Empty;
        public string SelectedToolType { get; private set; } = string.Empty;

        private TextBox _nameBox;
        private ComboBox _typeBox;

        private static readonly string[] ToolTypes = new[]
        {
            "GrayscaleTool",
            "BlurTool",
            "ThresholdTool",
            "EdgeDetectionTool",
            "MorphologyTool",
            "HistogramTool",
            "FeatureMatchTool",
            "BlobTool",
            "CaliperTool",
            "LineFitTool",
            "CircleFitTool"
        };

        public AddToolDialog()
        {
            Title = "Add Tool";
            Width = 400;
            Height = 280;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x1E, 0x1E, 0x1E));

            // Main border with styling
            var mainBorder = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x25, 0x25, 0x26)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x3E, 0x3E, 0x42)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(10),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(20)
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Title
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Type label
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Type box
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Name label
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Name box
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Spacer
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons

            // Dialog Title
            var titleText = new TextBlock
            {
                Text = "Add Vision Tool",
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xFF, 0xD7, 0x00)),
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 15)
            };
            Grid.SetRow(titleText, 0);
            grid.Children.Add(titleText);

            // Tool Type
            var typeLabel = new TextBlock
            {
                Text = "Tool Type",
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC)),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 5)
            };
            Grid.SetRow(typeLabel, 1);
            grid.Children.Add(typeLabel);

            _typeBox = new ComboBox
            {
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x3E, 0x3E, 0x42)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x55, 0x55, 0x55)),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 12),
                FontSize = 13
            };
            foreach (var type in ToolTypes)
            {
                _typeBox.Items.Add(type);
            }
            _typeBox.SelectedIndex = 0;
            _typeBox.SelectionChanged += TypeBox_SelectionChanged;
            Grid.SetRow(_typeBox, 2);
            grid.Children.Add(_typeBox);

            // Name
            var nameLabel = new TextBlock
            {
                Text = "Tool Name",
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC)),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 5)
            };
            Grid.SetRow(nameLabel, 3);
            grid.Children.Add(nameLabel);

            _nameBox = new TextBox
            {
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x3E, 0x3E, 0x42)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x55, 0x55, 0x55)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 12),
                FontSize = 13,
                CaretBrush = System.Windows.Media.Brushes.White,
                Text = "GrayscaleTool 1"
            };
            Grid.SetRow(_nameBox, 4);
            grid.Children.Add(_nameBox);

            // Buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            Grid.SetRow(buttonPanel, 6);

            var cancelButton = CreateStyledButton("Cancel", false);
            cancelButton.Click += (s, e) => DialogResult = false;
            buttonPanel.Children.Add(cancelButton);

            var addButton = CreateStyledButton("Add Tool", true);
            addButton.Click += AddButton_Click;
            buttonPanel.Children.Add(addButton);

            grid.Children.Add(buttonPanel);
            mainBorder.Child = grid;
            Content = mainBorder;
        }

        private Button CreateStyledButton(string text, bool isPrimary)
        {
            var button = new Button
            {
                Content = text,
                MinWidth = 100,
                Height = 36,
                Margin = new Thickness(isPrimary ? 10 : 0, 0, 0, 0),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Cursor = System.Windows.Input.Cursors.Hand,
                BorderThickness = new Thickness(0)
            };

            var style = new Style(typeof(Button));

            if (isPrimary)
            {
                style.Setters.Add(new Setter(Button.BackgroundProperty,
                    new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD4))));
                style.Setters.Add(new Setter(Button.ForegroundProperty,
                    System.Windows.Media.Brushes.White));

                var hoverTrigger = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
                hoverTrigger.Setters.Add(new Setter(Button.BackgroundProperty,
                    new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x10, 0x84, 0xD8))));
                style.Triggers.Add(hoverTrigger);

                var pressedTrigger = new Trigger { Property = Button.IsPressedProperty, Value = true };
                pressedTrigger.Setters.Add(new Setter(Button.BackgroundProperty,
                    new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x00, 0x5A, 0x9E))));
                style.Triggers.Add(pressedTrigger);
            }
            else
            {
                style.Setters.Add(new Setter(Button.BackgroundProperty,
                    new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x3E, 0x3E, 0x42))));
                style.Setters.Add(new Setter(Button.ForegroundProperty,
                    System.Windows.Media.Brushes.White));

                var hoverTrigger = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
                hoverTrigger.Setters.Add(new Setter(Button.BackgroundProperty,
                    new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x50, 0x50, 0x54))));
                style.Triggers.Add(hoverTrigger);

                var pressedTrigger = new Trigger { Property = Button.IsPressedProperty, Value = true };
                pressedTrigger.Setters.Add(new Setter(Button.BackgroundProperty,
                    new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x2D, 0x2D, 0x30))));
                style.Triggers.Add(pressedTrigger);
            }

            // Add template for rounded corners
            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            borderFactory.SetValue(Border.PaddingProperty, new Thickness(16, 8, 16, 8));

            var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(contentPresenter);

            template.VisualTree = borderFactory;
            style.Setters.Add(new Setter(Button.TemplateProperty, template));

            button.Style = style;
            return button;
        }

        private void TypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_typeBox.SelectedItem is string toolType)
            {
                _nameBox.Text = $"{toolType} 1";
            }
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_nameBox.Text))
            {
                MessageBox.Show("Please enter a tool name.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ToolName = _nameBox.Text.Trim();
            SelectedToolType = _typeBox.SelectedItem?.ToString() ?? "GrayscaleTool";
            DialogResult = true;
        }
    }
}
