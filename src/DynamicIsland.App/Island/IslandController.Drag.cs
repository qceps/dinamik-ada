using DynamicIsland.Core.State;
using DynamicIsland.Interop;

namespace DynamicIsland.App.Island;

/// <summary>
/// Sürükleyerek konumlandırma. Yalnızca Ayarlar penceresi açıkken etkin (normal kullanımda ada yanlışlıkla kaymasın).
/// Sürüklerken pencere doğrudan taşınır; bırakınca yatay konum ve üst mesafe ayarlara kaydedilir.
/// Ortaya yaklaşınca ortaya yapışır.
/// </summary>
public sealed partial class IslandController
{
    private const double CenterSnapDip = 12;
    private const double MaxTopMarginDip = 80; // ayarlardaki kaydırıcıyla aynı üst sınır

    private bool _dragEnabled;
    private double _dragStartOffset;
    private double _dragStartTop;
    private double _dragOffset;
    private double _dragTop;

    /// <summary>Ayarlar penceresi açılınca/kapanınca çağrılır.</summary>
    public void SetDragEnabled(bool enabled)
    {
        if (_dragEnabled == enabled)
        {
            return;
        }

        _dragEnabled = enabled;
        _window.DragEnabled = enabled;
        Recompute(); // oyun modunda da tıklanabilir olsun, çerçeve görünsün
    }

    private void InitializeDrag()
    {
        _window.DragStarted += () =>
        {
            var s = _settings.Current;
            _dragStartOffset = _dragOffset = s.HorizontalOffset;
            _dragStartTop = _dragTop = s.TopMargin;
        };

        _window.DragMoved += (dxPx, dyPx) =>
        {
            var s = _settings.Current;
            var monitor = DisplayMonitors.Resolve(s.MonitorDeviceName);
            var dpi = monitor.DpiScale > 0 ? monitor.DpiScale : 1;
            var max = PlacementCalculator.MaxOffsetDip(monitor.Bounds, dpi, IslandWindow.MaxIslandWidth * s.Scale);

            var offset = Math.Clamp(_dragStartOffset + dxPx / dpi, -max, max);
            if (Math.Abs(offset) < CenterSnapDip)
            {
                offset = 0;
            }

            _dragOffset = Math.Round(offset);
            _dragTop = Math.Round(Math.Clamp(_dragStartTop + dyPx / dpi, 0, MaxTopMarginDip));
            Place(_dragOffset, _dragTop);
        };

        _window.DragCompleted += () =>
        {
            if (_dragOffset != _dragStartOffset || _dragTop != _dragStartTop)
            {
                _settings.Update(s =>
                {
                    s.HorizontalOffset = _dragOffset;
                    s.TopMargin = _dragTop;
                });
            }
        };
    }
}
