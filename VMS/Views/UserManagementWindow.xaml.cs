using System.Windows;
using VMS.ViewModels;

namespace VMS.Views
{
    public partial class UserManagementWindow : Window
    {
        public UserManagementWindow()
        {
            InitializeComponent();
        }

        private void AddUser_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is UserManagementViewModel vm)
            {
                vm.NewPassword = NewPasswordBox.Password;
            }
        }

        private void ChangePassword_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is UserManagementViewModel vm)
            {
                vm.ChangePasswordValue = ChangePasswordBox.Password;
            }
        }
    }
}
