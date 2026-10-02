using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace YabiDesktopPet
{
    internal sealed class ContinuousGazeView : Viewport3D
    {
        private MeshGeometry3D _mesh;
        private Point[] _rest;
        private Vector[] _eyeX, _eyeY, _headX, _headY, _absHeadX;
        private BitmapSource _texture;
        private bool _standing;
        private double _oldEyeX = double.NaN, _oldEyeY, _oldHeadX, _oldHeadY;
        public ContinuousGazeRig Rig { get; private set; }
        public int VertexCount { get { return _rest == null ? 0 : _rest.Length; } }

        public ContinuousGazeView()
        {
            IsHitTestVisible = false;
            ClipToBounds = true;
            RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
            Camera = new OrthographicCamera(new Point3D(160, -240, 1), new Vector3D(0, 0, -1), new Vector3D(0, 1, 0), 320)
            { NearPlaneDistance = .1, FarPlaneDistance = 10 };
        }

        public void SetTexture(BitmapSource texture, bool standing)
        {
            if (ReferenceEquals(_texture, texture) && _standing == standing) return;
            if (texture.PixelWidth != 320 || texture.PixelHeight != 480) throw new InvalidOperationException("Continuous gaze requires calibrated 320x480 idle frames.");
            _texture = texture; _standing = standing;
            Rig = new ContinuousGazeRig(standing);
            List<double> xs = Axis(320, Rig.EyeLeftX - 10, Rig.EyeLeftX + 10, Rig.EyeRightX - 10, Rig.EyeRightX + 10);
            List<double> ys = Axis(480, Rig.EyeY - 11, Rig.EyeY + 10, -20, -10);
            List<Point> rest = new List<Point>();
            PointCollection uv = new PointCollection();
            Point3DCollection positions = new Point3DCollection();
            foreach (double y in ys) foreach (double x in xs)
            { rest.Add(new Point(x, y)); positions.Add(new Point3D(x, -y, 0)); uv.Add(new Point(x / 320, y / 480)); }
            Int32Collection indices = new Int32Collection();
            for (int y = 0; y < ys.Count - 1; y++) for (int x = 0; x < xs.Count - 1; x++)
            {
                int a = y * xs.Count + x, b = a + 1, c = a + xs.Count, d = c + 1;
                indices.Add(a); indices.Add(c); indices.Add(b); indices.Add(b); indices.Add(c); indices.Add(d);
            }
            uv.Freeze(); indices.Freeze();
            _rest = rest.ToArray();
            _eyeX = new Vector[_rest.Length]; _eyeY = new Vector[_rest.Length];
            _headX = new Vector[_rest.Length]; _headY = new Vector[_rest.Length]; _absHeadX = new Vector[_rest.Length];
            for (int i = 0; i < _rest.Length; i++)
            {
                Point p = _rest[i];
                _eyeX[i] = Rig.Deform(p.X, p.Y, 1, 0, 0, 0) - p;
                _eyeY[i] = Rig.Deform(p.X, p.Y, 0, 1, 0, 0) - p;
                _headY[i] = Rig.Deform(p.X, p.Y, 0, 0, 0, 1) - p;
                Vector plus = Rig.Deform(p.X, p.Y, 0, 0, 1, 0) - p;
                Vector minus = Rig.Deform(p.X, p.Y, 0, 0, -1, 0) - p;
                _headX[i] = (plus - minus) * .5;
                _absHeadX[i] = (plus + minus) * .5;
            }
            _mesh = new MeshGeometry3D { Positions = positions, TextureCoordinates = uv, TriangleIndices = indices };
            // Freezing an ImageBrush also freezes its image. Never freeze the
            // writable surface shared by the video player.
            BitmapSource snapshot = texture;
            if (!texture.IsFrozen)
            {
                snapshot = new WriteableBitmap(texture);
                snapshot.Freeze();
            }
            ImageBrush brush = new ImageBrush(snapshot) { Stretch = Stretch.Fill, ViewportUnits = BrushMappingMode.RelativeToBoundingBox };
            brush.Freeze();
            // An emissive-only pass writes additive RGB without a coverage alpha.
            // It looks colored in some PNG viewers but vanishes in a layered
            // transparent window. Diffuse + white ambient preserves RGBA.
            DiffuseMaterial material = new DiffuseMaterial(brush) { Color = Colors.White, AmbientColor = Colors.White };
            material.Freeze();
            GeometryModel3D model = new GeometryModel3D(_mesh, material) { BackMaterial = material };
            Model3DGroup scene = new Model3DGroup();
            scene.Children.Add(new AmbientLight(Colors.White));
            scene.Children.Add(model);
            Children.Clear(); Children.Add(new ModelVisual3D { Content = scene });
            _oldEyeX = double.NaN;
        }

        public void UpdatePose(ContinuousGazeController state)
        {
            if (_mesh == null) return;
            if (Math.Abs(state.EyeX - _oldEyeX) + Math.Abs(state.EyeY - _oldEyeY) + Math.Abs(state.HeadX - _oldHeadX) + Math.Abs(state.HeadY - _oldHeadY) < .0008) return;
            Point3DCollection points = new Point3DCollection(_rest.Length);
            double absoluteHead = Math.Abs(state.HeadX);
            for (int i = 0; i < _rest.Length; i++)
            {
                // Deformation is affine in these five channels. Weights are built once.
                Point p = _rest[i] + _eyeX[i] * state.EyeX + _eyeY[i] * state.EyeY
                    + _headX[i] * state.HeadX + _headY[i] * state.HeadY + _absHeadX[i] * absoluteHead;
                points.Add(new Point3D(p.X, -p.Y, 0));
            }
            // Swap one collection per rendered update; do not issue thousands of WPF notifications.
            points.Freeze(); _mesh.Positions = points;
            _oldEyeX = state.EyeX; _oldEyeY = state.EyeY; _oldHeadX = state.HeadX; _oldHeadY = state.HeadY;
        }

        private static List<double> Axis(int max, double a, double b, double c, double d)
        {
            List<double> values = new List<double>();
            for (double p = 0; p < max; p += ((p >= a && p <= b) || (p >= c && p <= d)) ? 1 : 6) values.Add(p);
            values.Add(max); return values;
        }
    }
}
