using System.Windows.Input;
using DynamicIsland.Interop;

namespace DynamicIsland.App.Island;

/// <summary>
/// Sürükleme girdisi (yalnızca <see cref="DragEnabled"/> açıkken). Pencere imlecin altında taşındığı için
/// konum farkı ekran koordinatlarından (fiziksel piksel) hesaplanır. Düğmeler, ilerleme ve ses çubuğu
/// kendi tıklamalarını işler; sürükleme yalnızca adanın boş yerlerinden başlar.
/// </summary>
public partial class IslandWindow
{
    private const int DragThresholdPx = 4;

    private bool _dragEnabled;
    private bool _dragPending;
    private bool _dragging;
    private int _dragStartX;
    private int _dragStartY;

    /// <summary>Fare basılı, sürükleme olabilir (bu sırada fareyle genişleme bekletilir).</summary>
    public bool IsDragPending => _dragPending;

    public event Action? DragStarted;

    /// <summary>Başlangıçtan bu yana toplam fark (fiziksel piksel).</summary>
    public event Action<int, int>? DragMoved;

    public event Action? DragCompleted;

    public bool DragEnabled
    {
        get => _dragEnabled;
        set
        {
            _dragEnabled = value;
            Island.Cursor = value ? Cursors.SizeAll : null;
            Island.ToolTip = value ? "Sürükleyerek taşı (ayarlar açıkken)" : null;
            if (!value)
            {
                EndDrag();
            }
        }
    }

    private void InitializeDrag()
    {
        Island.MouseLeftButtonDown += (_, e) =>
        {
            if (!_dragEnabled || !OverlayWindow.TryGetCursorPos(out var x, out var y))
            {
                return;
            }

            _dragPending = true;
            _dragStartX = x;
            _dragStartY = y;
            Island.CaptureMouse();
            e.Handled = true;
        };

        Island.MouseMove += (_, _) =>
        {
            if (!_dragPending || !OverlayWindow.TryGetCursorPos(out var x, out var y))
            {
                return;
            }

            var dx = x - _dragStartX;
            var dy = y - _dragStartY;
            if (!_dragging)
            {
                if (Math.Abs(dx) + Math.Abs(dy) < DragThresholdPx)
                {
                    return; // küçük titremeler tıklama sayılır
                }

                _dragging = true;
                DragStarted?.Invoke();
            }

            DragMoved?.Invoke(dx, dy);
        };

        Island.MouseLeftButtonUp += (_, e) =>
        {
            if (_dragPending)
            {
                EndDrag();
                e.Handled = true;
            }
        };

        Island.LostMouseCapture += (_, _) => EndDrag();
    }

    private void EndDrag()
    {
        if (!_dragPending)
        {
            return;
        }

        var completed = _dragging;
        _dragPending = false;
        _dragging = false;
        if (Island.IsMouseCaptured)
        {
            Island.ReleaseMouseCapture();
        }

        if (completed)
        {
            DragCompleted?.Invoke();
        }
    }
}
