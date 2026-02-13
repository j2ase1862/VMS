using CommunityToolkit.Mvvm.ComponentModel;
using System.Numerics;

namespace VMS.VisionSetup.Models
{
    public class PointCloudData : ObservableObject
    {
        private string _name = "PointCloud";
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private Vector3[] _positions = Array.Empty<Vector3>();
        public Vector3[] Positions
        {
            get => _positions;
            set => SetProperty(ref _positions, value);
        }

        private System.Windows.Media.Color[] _colors = Array.Empty<System.Windows.Media.Color>();
        public System.Windows.Media.Color[] Colors
        {
            get => _colors;
            set => SetProperty(ref _colors, value);
        }

        public int PointCount => Positions.Length;

        public static PointCloudData FromArrays(float[] xyz, byte[]? rgb = null, string name = "PointCloud")
        {
            int count = xyz.Length / 3;
            var positions = new Vector3[count];
            var colors = new System.Windows.Media.Color[count];

            for (int i = 0; i < count; i++)
            {
                positions[i] = new Vector3(xyz[i * 3], xyz[i * 3 + 1], xyz[i * 3 + 2]);

                if (rgb != null && rgb.Length >= (i + 1) * 3)
                {
                    colors[i] = System.Windows.Media.Color.FromRgb(rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2]);
                }
                else
                {
                    colors[i] = System.Windows.Media.Color.FromRgb(255, 255, 255);
                }
            }

            return new PointCloudData
            {
                Name = name,
                Positions = positions,
                Colors = colors
            };
        }
    }
}
