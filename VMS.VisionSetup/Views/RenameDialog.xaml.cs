using System.Windows;

namespace VMS.VisionSetup.Views
{
    public partial class RenameDialog : Window
    {
        public string ToolName
        {
            get => NameTextBox.Text;
            set => NameTextBox.Text = value;
        }

        public RenameDialog()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                NameTextBox.Focus();
                NameTextBox.SelectAll();
            };
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NameTextBox.Text))
            {
                Common.MessageDialog.Show(this, "Tool name cannot be empty.", "Rename Tool",
                    Common.MessageDialogKind.Warning);
                return;
            }

            DialogResult = true;
        }
    }
}
