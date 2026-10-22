using System.ComponentModel;
using System.Windows.Media;

namespace Clothesline;

/// <summary>绳子上的一张"晾着"的截图。</summary>
public sealed class PegItem : INotifyPropertyChanged
{
    public string FilePath { get; }
    public ImageSource Bitmap { get; }
    public double Angle { get; }

    public RotateTransform Rot { get; }
    public ScaleTransform Scale { get; }
    public TranslateTransform Hang { get; }
    public TransformGroup Xf { get; }

    private double _zoom = PegZoom.Default;

    /// <summary>相对基准尺寸的缩放。悬停的轻微放大仍走 Scale，不改这里。</summary>
    public double Zoom
    {
        get => _zoom;
        set
        {
            double next = PegZoom.Next(value, 0);
            if (Math.Abs(_zoom - next) < 1e-9) return;
            _zoom = next;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThumbWidth)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThumbHeight)));
        }
    }

    public double ThumbWidth => PegZoom.BaseWidth * _zoom;
    public double ThumbHeight => PegZoom.BaseHeight * _zoom;

    private bool _copyFeedback;
    public bool CopyFeedback
    {
        get => _copyFeedback;
        set
        {
            if (_copyFeedback == value) return;
            _copyFeedback = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CopyFeedback)));
        }
    }

    public PegItem(string filePath, ImageSource bitmap, double angle)
    {
        FilePath = filePath;
        Bitmap = bitmap;
        Angle = angle;
        Rot = new RotateTransform(angle);
        Scale = new ScaleTransform(1, 1);
        Hang = new TranslateTransform();
        Xf = new TransformGroup();
        Xf.Children.Add(Rot);
        Xf.Children.Add(Scale);
        Xf.Children.Add(Hang);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
