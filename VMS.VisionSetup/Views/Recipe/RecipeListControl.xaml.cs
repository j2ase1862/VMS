using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace VMS.VisionSetup.Views.Recipe
{
    /// <summary>
    /// RecipeListControl.xaml 코드 비하인드
    /// </summary>
    public partial class RecipeListControl : UserControl
    {
        private List<RecipeInfo> _allRecipes = new();
        private RecipeInfo? _selectedRecipe;

        public event EventHandler<RecipeInfo>? RecipeSelected;
        public event EventHandler<Models.Recipe>? RecipeLoaded;

        public RecipeListControl()
        {
            InitializeComponent();
            RefreshRecipeList();
        }

        /// <summary>
        /// 레시피 목록 새로고침
        /// </summary>
        public void RefreshRecipeList()
        {
            _allRecipes = RecipeService.Instance.GetRecipeList();
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var searchText = SearchBox?.Text?.ToLowerInvariant() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(searchText))
            {
                RecipeList.ItemsSource = _allRecipes;
            }
            else
            {
                RecipeList.ItemsSource = _allRecipes.Where(r =>
                    r.Name.ToLowerInvariant().Contains(searchText) ||
                    r.Author.ToLowerInvariant().Contains(searchText) ||
                    r.Version.ToLowerInvariant().Contains(searchText)
                ).ToList();
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = string.Empty;
        }

        private void RecipeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedRecipe = RecipeList.SelectedItem as RecipeInfo;

            bool hasSelection = _selectedRecipe != null;
            LoadButton.IsEnabled = hasSelection;
            DeleteButton.IsEnabled = hasSelection;
            ExportButton.IsEnabled = hasSelection;

            if (_selectedRecipe != null)
            {
                RecipeSelected?.Invoke(this, _selectedRecipe);
            }
        }

        private void NewRecipe_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new NewRecipeDialog();
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() == true)
            {
                var recipe = RecipeService.Instance.CreateNewRecipe(dialog.RecipeName);
                recipe.Description = dialog.RecipeDescription;
                recipe.Author = dialog.RecipeAuthor;

                RecipeService.Instance.SaveRecipe(recipe);
                RefreshRecipeList();

                RecipeLoaded?.Invoke(this, recipe);
            }
        }

        private void LoadRecipe_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedRecipe == null) return;

            var recipe = RecipeService.Instance.LoadRecipe(_selectedRecipe.FilePath);
            if (recipe != null)
            {
                RecipeLoaded?.Invoke(this, recipe);
            }
            else
            {
                MessageBox.Show("레시피를 로드할 수 없습니다.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeleteRecipe_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedRecipe == null) return;

            var result = MessageBox.Show(
                $"'{_selectedRecipe.Name}' 레시피를 삭제하시겠습니까?\n이 작업은 되돌릴 수 없습니다.",
                "Delete Recipe",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                if (RecipeService.Instance.DeleteRecipe(_selectedRecipe.FilePath))
                {
                    RefreshRecipeList();
                }
                else
                {
                    MessageBox.Show("레시피 삭제에 실패했습니다.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ImportRecipe_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Import Recipe",
                Filter = "Recipe Files (*.json)|*.json|All Files (*.*)|*.*",
                DefaultExt = ".json"
            };

            if (dialog.ShowDialog() == true)
            {
                var recipe = RecipeService.Instance.ImportRecipe(dialog.FileName);
                if (recipe != null)
                {
                    RefreshRecipeList();
                    MessageBox.Show($"레시피 '{recipe.Name}'를 성공적으로 가져왔습니다.", "Import Successful",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("레시피 가져오기에 실패했습니다.", "Import Failed",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ExportRecipe_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedRecipe == null) return;

            var recipe = RecipeService.Instance.LoadRecipe(_selectedRecipe.FilePath);
            if (recipe == null)
            {
                MessageBox.Show("레시피를 로드할 수 없습니다.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Export Recipe",
                Filter = "Recipe Files (*.json)|*.json",
                DefaultExt = ".json",
                FileName = $"recipe_{recipe.Name.Replace(" ", "_").ToLower()}"
            };

            if (dialog.ShowDialog() == true)
            {
                if (RecipeService.Instance.ExportRecipe(recipe, dialog.FileName))
                {
                    MessageBox.Show($"레시피를 '{dialog.FileName}'에 내보냈습니다.", "Export Successful",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("레시피 내보내기에 실패했습니다.", "Export Failed",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var folderPath = RecipeService.Instance.RecipeFolderPath;
                Process.Start(new ProcessStartInfo
                {
                    FileName = folderPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"폴더를 열 수 없습니다: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    /// <summary>
    /// 새 레시피 생성 다이얼로그
    /// </summary>
    public class NewRecipeDialog : Window
    {
        public string RecipeName { get; private set; } = string.Empty;
        public string RecipeDescription { get; private set; } = string.Empty;
        public string RecipeAuthor { get; private set; } = string.Empty;

        private TextBox _nameBox;
        private TextBox _descriptionBox;
        private TextBox _authorBox;

        public NewRecipeDialog()
        {
            Title = "New Recipe";
            Width = 420;
            Height = 415;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x1E, 0x1E, 0x1E));

            // Main border with subtle glow effect
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
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Name label
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Name box
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Desc label
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Desc box
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Author label
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Author box
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons (moved up)
            //grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons (moved up)

            // Dialog Title
            var titleText = new TextBlock
            {
                Text = "Create New Recipe",
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xFF, 0xD7, 0x00)),
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 15)
            };
            Grid.SetRow(titleText, 0);
            grid.Children.Add(titleText);

            // Name
            var nameLabel = new TextBlock
            {
                Text = "Recipe Name",
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC)),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 5)
            };
            Grid.SetRow(nameLabel, 1);
            grid.Children.Add(nameLabel);

            _nameBox = CreateStyledTextBox();
            Grid.SetRow(_nameBox, 2);
            grid.Children.Add(_nameBox);

            // Description
            var descLabel = new TextBlock
            {
                Text = "Description (optional)",
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC)),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 5)
            };
            Grid.SetRow(descLabel, 3);
            grid.Children.Add(descLabel);

            _descriptionBox = CreateStyledTextBox();
            _descriptionBox.AcceptsReturn = true;
            _descriptionBox.Height = 50;
            _descriptionBox.TextWrapping = TextWrapping.Wrap;
            _descriptionBox.VerticalContentAlignment = VerticalAlignment.Top;
            Grid.SetRow(_descriptionBox, 4);
            grid.Children.Add(_descriptionBox);

            // Author
            var authorLabel = new TextBlock
            {
                Text = "Author",
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC)),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 5)
            };
            Grid.SetRow(authorLabel, 5);
            grid.Children.Add(authorLabel);

            _authorBox = CreateStyledTextBox();
            _authorBox.Text = Environment.UserName;
            Grid.SetRow(_authorBox, 6);
            grid.Children.Add(_authorBox);

            // Buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 20, 0, 0)
            };
            Grid.SetRow(buttonPanel, 7);

            var cancelButton = CreateStyledButton("Cancel", false);
            cancelButton.Click += (s, e) => DialogResult = false;
            buttonPanel.Children.Add(cancelButton);

            var createButton = CreateStyledButton("Create Recipe", true);
            createButton.Click += CreateButton_Click;
            buttonPanel.Children.Add(createButton);

            grid.Children.Add(buttonPanel);
            mainBorder.Child = grid;
            Content = mainBorder;

            // Focus on name box when loaded
            Loaded += (s, e) => _nameBox.Focus();
        }

        private TextBox CreateStyledTextBox()
        {
            return new TextBox
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
                CaretBrush = System.Windows.Media.Brushes.White
            };
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

            // Create style with hover effect
            var style = new Style(typeof(Button));

            if (isPrimary)
            {
                // Primary button (blue)
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
                // Secondary button (gray)
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

        private void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_nameBox.Text))
            {
                MessageBox.Show("Please enter a recipe name.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                _nameBox.Focus();
                return;
            }

            RecipeName = _nameBox.Text.Trim();
            RecipeDescription = _descriptionBox.Text.Trim();
            RecipeAuthor = _authorBox.Text.Trim();
            DialogResult = true;
        }
    }
}
