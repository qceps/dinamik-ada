using System.Windows.Controls;
using System.Windows.Input;
using DynamicIsland.App.Services;
using DynamicIsland.Core.Settings;

namespace DynamicIsland.App.Settings;

/// <summary>Kısayol yakalama kutusu: tıklayıp kombinasyona basılır; Backspace/Delete kısayolu kapatır, Esc vazgeçer.</summary>
public sealed class HotkeyBox : TextBox
{
    private HotkeyGesture _gesture = HotkeyGesture.None;

    public HotkeyBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        Cursor = Cursors.Hand;
        IsUndoEnabled = false;
        ContextMenu = null;
        MinWidth = 170;
        // Alt sınıf, TextBox'ın örtük (Fluent) stilini otomatik almaz.
        SetResourceReference(StyleProperty, typeof(TextBox));
    }

    public event Action<HotkeyGesture>? GestureCaptured;

    /// <summary>Yakalama başlarken/bitince (genel kısayolları geçici askıya almak için).</summary>
    public event Action<bool>? CaptureStateChanged;

    public HotkeyGesture Gesture
    {
        get => _gesture;
        set
        {
            _gesture = value;
            if (!IsKeyboardFocused)
            {
                Text = HotkeyService.Format(value);
            }
        }
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        Text = "Tuş kombinasyonuna basın…";
        CaptureStateChanged?.Invoke(true);
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        Text = HotkeyService.Format(_gesture);
        CaptureStateChanged?.Invoke(false);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = CurrentModifiers();

        switch (key)
        {
            case Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin:
                Text = modifiers == HotkeyModifiers.None ? "Tuş kombinasyonuna basın…" : HotkeyService.Format(new HotkeyGesture(modifiers, 0)).Replace(" + Yok", string.Empty).Replace("Yok", string.Empty) + " + …";
                return;
            case Key.Escape when modifiers == HotkeyModifiers.None:
                MoveFocusAway();
                return;
            case Key.Tab when modifiers == HotkeyModifiers.None:
                e.Handled = false;
                return;
            case Key.Back or Key.Delete when modifiers == HotkeyModifiers.None:
                Commit(HotkeyGesture.None);
                return;
        }

        var vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0)
        {
            return;
        }

        Commit(new HotkeyGesture(modifiers, vk));
    }

    private void Commit(HotkeyGesture gesture)
    {
        _gesture = gesture;
        GestureCaptured?.Invoke(gesture);
        MoveFocusAway();
    }

    private void MoveFocusAway()
    {
        var scope = FocusManager.GetFocusScope(this);
        FocusManager.SetFocusedElement(scope, null);
        Keyboard.ClearFocus();
        Text = HotkeyService.Format(_gesture);
    }

    private static HotkeyModifiers CurrentModifiers()
    {
        var m = HotkeyModifiers.None;
        var k = Keyboard.Modifiers;
        if (k.HasFlag(ModifierKeys.Control)) m |= HotkeyModifiers.Control;
        if (k.HasFlag(ModifierKeys.Alt)) m |= HotkeyModifiers.Alt;
        if (k.HasFlag(ModifierKeys.Shift)) m |= HotkeyModifiers.Shift;
        if (k.HasFlag(ModifierKeys.Windows) || Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) m |= HotkeyModifiers.Win;
        return m;
    }
}
