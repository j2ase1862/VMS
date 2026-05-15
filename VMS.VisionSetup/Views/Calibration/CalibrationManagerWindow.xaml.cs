using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.ViewModels;

namespace VMS.VisionSetup.Views.Calibration
{
    public partial class CalibrationManagerWindow : Window
    {
        private readonly CalibrationManagerViewModel _viewModel;

        public CalibrationManagerWindow(IRecipeService recipeService, IDialogService dialogService)
        {
            InitializeComponent();
            _viewModel = new CalibrationManagerViewModel(recipeService, dialogService);
            DataContext = _viewModel;
            Closed += (_, _) => _viewModel.Dispose();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        /// <summary>
        /// 미리보기 Image의 클릭 좌표를 원본 이미지 좌표로 변환 후 ViewModel에 전달.
        /// Stretch="Uniform"이므로 종횡비 보존 — letterbox 오프셋을 빼고 스케일로 나눔.
        /// </summary>
        private void PreviewImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Image img || img.Source is not BitmapSource bmp) return;
            if (img.ActualWidth <= 0 || img.ActualHeight <= 0) return;

            var pos = e.GetPosition(img);
            double sx = img.ActualWidth / bmp.PixelWidth;
            double sy = img.ActualHeight / bmp.PixelHeight;
            double scale = Math.Min(sx, sy);
            double offsetX = (img.ActualWidth - bmp.PixelWidth * scale) / 2.0;
            double offsetY = (img.ActualHeight - bmp.PixelHeight * scale) / 2.0;

            double srcX = (pos.X - offsetX) / scale;
            double srcY = (pos.Y - offsetY) / scale;

            if (srcX < 0 || srcY < 0 || srcX >= bmp.PixelWidth || srcY >= bmp.PixelHeight) return;

            _viewModel.AddPickedPoint(srcX, srcY);
        }
    }
}
