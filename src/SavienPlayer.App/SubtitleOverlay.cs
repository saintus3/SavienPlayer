using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace SavienPlayer.App;

public sealed class SubtitleOverlay : FrameworkElement
{
    private string _text = "";
    private string _fontFamily = "Malgun Gothic";
    private double _fontSize = 32;
    private double _outline = 2;
    private string _position = "Bottom";

    public void Update(string text, string fontFamily, double fontSize, double outline, string position)
    {
        if (_text == text && _fontFamily == fontFamily && _fontSize == fontSize &&
            _outline == outline && _position == position) return;
        _text = text;
        _fontFamily = fontFamily;
        _fontSize = fontSize;
        _outline = outline;
        _position = position;
        InvalidateVisual();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (string.IsNullOrEmpty(_text) || ActualWidth < 40 || ActualHeight < 40) return;
        var formatted = new FormattedText(_text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily(_fontFamily), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            _fontSize, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(1, ActualWidth - 40),
            MaxTextHeight = Math.Max(_fontSize, ActualHeight - 40),
            TextAlignment = TextAlignment.Center,
            Trimming = TextTrimming.CharacterEllipsis
        };
        var y = _position switch
        {
            "Top" => 20,
            "Center" => Math.Max(20, (ActualHeight - formatted.Height) / 2),
            _ => Math.Max(20, ActualHeight - formatted.Height - 30)
        };
        var geometry = formatted.BuildGeometry(new Point(20, y));
        drawingContext.DrawGeometry(Brushes.White,
            _outline > 0 ? new Pen(Brushes.Black, _outline * 2) { LineJoin = PenLineJoin.Round } : null,
            geometry);
        drawingContext.DrawGeometry(Brushes.White, null, geometry);
    }
}
