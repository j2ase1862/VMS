using VMS.VisionSetup.Models;
using System;
using System.Windows;
using System.Windows.Controls;

namespace VMS.VisionSetup.Views.Recipe
{
    /// <summary>
    /// RecipeListControl.xaml 코드 비하인드
    /// DataContext is set to RecipeListViewModel by parent window.
    /// </summary>
    public partial class RecipeListControl : UserControl
    {
        public RecipeListControl()
        {
            InitializeComponent();
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
