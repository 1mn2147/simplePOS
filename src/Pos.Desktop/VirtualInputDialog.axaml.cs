using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Pos.Core;

namespace Pos.Desktop;

public enum VirtualInputMode
{
    Korean,
    Numeric,
}

public sealed partial class VirtualInputDialog : Window
{
    private static readonly IReadOnlyDictionary<char, char> ShiftedJamo = new Dictionary<char, char>
    {
        ['ㄱ'] = 'ㄲ', ['ㄷ'] = 'ㄸ', ['ㅂ'] = 'ㅃ', ['ㅅ'] = 'ㅆ',
        ['ㅈ'] = 'ㅉ', ['ㅐ'] = 'ㅒ', ['ㅔ'] = 'ㅖ',
    };

    private readonly TextBox? _target;
    private readonly VirtualInputMode _mode;
    private readonly KoreanInputComposer _composer;
    private string _suffix;
    private string _numericText;
    private int _numericCaret;
    private bool _shift;

    public VirtualInputDialog()
        : this(null, VirtualInputMode.Korean, "입력")
    {
    }

    public VirtualInputDialog(TextBox? target, VirtualInputMode mode, string label)
    {
        InitializeComponent();
        _target = target;
        _mode = mode;
        var original = target?.Text ?? string.Empty;
        var rawStart = target?.SelectionStart ?? original.Length;
        var rawEnd = target?.SelectionEnd ?? original.Length;
        if (rawStart == rawEnd && target is not null)
        {
            rawStart = target.CaretIndex;
            rawEnd = target.CaretIndex;
        }
        var selectionStart = Math.Clamp(Math.Min(rawStart, rawEnd), 0, original.Length);
        var selectionEnd = Math.Clamp(Math.Max(rawStart, rawEnd), selectionStart, original.Length);
        var prefix = original[..selectionStart];
        _suffix = original[selectionEnd..];
        _composer = new KoreanInputComposer(prefix);
        _numericText = prefix + _suffix;
        _numericCaret = prefix.Length;

        InputLabel.Text = label;
        KoreanPanel.IsVisible = mode == VirtualInputMode.Korean;
        NumericPanel.IsVisible = mode == VirtualInputMode.Numeric;
        UpdatePreview();
        Opened += (_, _) => FocusFirstKey();
        Closed += (_, _) => Dispatcher.UIThread.Post(() => _target?.Focus());
    }

    public static async Task<bool> EditAsync(
        Window owner,
        TextBox target,
        VirtualInputMode mode,
        string label)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(target);
        var dialog = new VirtualInputDialog(target, mode, label);
        return await dialog.ShowDialog<bool>(owner);
    }

    private void OnKoreanKey(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string { Length: > 0 } value }) return;
        var jamo = value[0];
        if (_shift && ShiftedJamo.TryGetValue(jamo, out var shifted)) jamo = shifted;
        _composer.InputJamo(jamo);
        SetShift(false);
        UpdatePreview();
    }

    private void OnNumericKey(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Content: string digits } || digits.Any(character => !char.IsDigit(character))) return;
        _numericText = _numericText.Insert(_numericCaret, digits);
        _numericCaret += digits.Length;
        UpdatePreview();
    }

    private void OnShift(object? sender, RoutedEventArgs e) => SetShift(!_shift);

    private void OnBackspace(object? sender, RoutedEventArgs e)
    {
        if (_mode == VirtualInputMode.Korean)
        {
            _composer.Backspace();
        }
        else if (_numericCaret > 0)
        {
            _numericText = _numericText.Remove(_numericCaret - 1, 1);
            _numericCaret--;
        }
        UpdatePreview();
    }

    private void OnClear(object? sender, RoutedEventArgs e)
    {
        if (_mode == VirtualInputMode.Korean)
        {
            _composer.Clear();
            _suffix = string.Empty;
        }
        else
        {
            _numericText = string.Empty;
            _numericCaret = 0;
        }
        SetShift(false);
        UpdatePreview();
    }

    private void OnSpace(object? sender, RoutedEventArgs e)
    {
        _composer.Space();
        SetShift(false);
        UpdatePreview();
    }

    private void OnConfirm(object? sender, RoutedEventArgs e) => Confirm();

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close(false);
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Confirm();
        }
    }

    private void Confirm()
    {
        var text = _mode == VirtualInputMode.Korean
            ? _composer.Commit() + _suffix
            : _numericText;
        if (_target is not null)
        {
            _target.Text = text;
            var caret = _mode == VirtualInputMode.Korean ? _composer.Text.Length : _numericCaret;
            _target.CaretIndex = Math.Clamp(caret, 0, text.Length);
            _target.SelectionStart = _target.CaretIndex;
            _target.SelectionEnd = _target.CaretIndex;
        }
        Close(true);
    }

    private void SetShift(bool value)
    {
        _shift = value;
        ShiftButton.Content = value ? "Shift 켜짐" : "Shift 꺼짐";
        foreach (var button in KoreanPanel.GetVisualDescendants().OfType<Button>())
        {
            if (button.Tag is string { Length: > 0 } text)
            {
                var jamo = text[0];
                button.Content = value && ShiftedJamo.TryGetValue(jamo, out var shifted)
                    ? shifted.ToString()
                    : text;
            }
        }
    }

    private void UpdatePreview()
    {
        var text = _mode == VirtualInputMode.Korean ? _composer.Text + _suffix : _numericText;
        InputPreview.Text = text;
        InputPreview.CaretIndex = Math.Clamp(
            _mode == VirtualInputMode.Korean ? _composer.Text.Length : _numericCaret,
            0,
            text.Length);
    }

    private void FocusFirstKey()
    {
        Control panel = _mode == VirtualInputMode.Korean ? KoreanPanel : NumericPanel;
        panel.GetVisualDescendants().OfType<Button>().FirstOrDefault()?.Focus();
    }
}
