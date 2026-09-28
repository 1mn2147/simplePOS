using System.Windows.Input;
using Pos.Core;

namespace Pos.App.ViewModels;

public abstract class VirtualInputViewModel : ObservableObject
{
    private static readonly Dictionary<char, char> ShiftedJamo = new()
    {
        ['ㄱ'] = 'ㄲ',
        ['ㄷ'] = 'ㄸ',
        ['ㅂ'] = 'ㅃ',
        ['ㅅ'] = 'ㅆ',
        ['ㅈ'] = 'ㅉ',
        ['ㅐ'] = 'ㅒ',
        ['ㅔ'] = 'ㅖ',
    };

    private readonly KoreanInputComposer _composer = new();
    private bool _isInputPanelVisible;
    private bool _isKoreanKeyboardVisible;
    private bool _isNumericKeypadVisible;
    private bool _isKoreanShift;
    private string? _activeTarget;

    protected VirtualInputViewModel()
    {
        ShowKoreanKeyboardCommand = new RelayCommand<string>(ShowKoreanKeyboard);
        ShowNumericKeypadCommand = new RelayCommand<string>(ShowNumericKeypad);
        DismissInputPanelCommand = new RelayCommand(DismissInputPanel);
        ConfirmInputCommand = new RelayCommand(ConfirmInput);
        KoreanKeyCommand = new RelayCommand<string>(EnterKoreanKey);
        ToggleKoreanShiftCommand = new RelayCommand(() => _isKoreanShift = !_isKoreanShift);
        KoreanBackspaceCommand = new RelayCommand(BackspaceKorean);
        KoreanClearCommand = new RelayCommand(ClearKorean);
        NumericKeyCommand = new RelayCommand<string>(EnterNumericKey);
        NumericBackspaceCommand = new RelayCommand(BackspaceNumeric);
        NumericClearCommand = new RelayCommand(ClearNumeric);
    }

    public bool IsInputPanelVisible
    {
        get => _isInputPanelVisible;
        private set => SetProperty(ref _isInputPanelVisible, value);
    }

    public bool IsKoreanKeyboardVisible
    {
        get => _isKoreanKeyboardVisible;
        private set => SetProperty(ref _isKoreanKeyboardVisible, value);
    }

    public bool IsNumericKeypadVisible
    {
        get => _isNumericKeypadVisible;
        private set => SetProperty(ref _isNumericKeypadVisible, value);
    }

    public ICommand ShowKoreanKeyboardCommand { get; }
    public ICommand ShowNumericKeypadCommand { get; }
    public ICommand DismissInputPanelCommand { get; }
    public ICommand ConfirmInputCommand { get; }
    public ICommand KoreanKeyCommand { get; }
    public ICommand ToggleKoreanShiftCommand { get; }
    public ICommand KoreanBackspaceCommand { get; }
    public ICommand KoreanClearCommand { get; }
    public ICommand NumericKeyCommand { get; }
    public ICommand NumericBackspaceCommand { get; }
    public ICommand NumericClearCommand { get; }

    protected abstract string GetInputValue(string target);
    protected abstract void SetInputValue(string target, string value);

    private void ShowKoreanKeyboard(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        _activeTarget = target;
        _composer.SetText(GetInputValue(target));
        _isKoreanShift = false;
        IsKoreanKeyboardVisible = true;
        IsNumericKeypadVisible = false;
        IsInputPanelVisible = true;
    }

    private void ShowNumericKeypad(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        _activeTarget = target;
        IsKoreanKeyboardVisible = false;
        IsNumericKeypadVisible = true;
        IsInputPanelVisible = true;
    }

    private void EnterKoreanKey(string? key)
    {
        if (_activeTarget is null || string.IsNullOrEmpty(key))
        {
            return;
        }

        string text;
        if (key == " ")
        {
            text = _composer.Space();
        }
        else
        {
            var jamo = key[0];
            if (_isKoreanShift && ShiftedJamo.TryGetValue(jamo, out var shifted))
            {
                jamo = shifted;
            }

            text = _composer.InputJamo(jamo);
            _isKoreanShift = false;
        }

        SetInputValue(_activeTarget, text);
    }

    private void BackspaceKorean()
    {
        if (_activeTarget is not null)
        {
            SetInputValue(_activeTarget, _composer.Backspace());
        }
    }

    private void ClearKorean()
    {
        if (_activeTarget is null)
        {
            return;
        }

        _composer.Clear();
        SetInputValue(_activeTarget, string.Empty);
    }

    private void EnterNumericKey(string? key)
    {
        if (_activeTarget is null || string.IsNullOrEmpty(key) || key.Any(character => !char.IsDigit(character)))
        {
            return;
        }

        var current = GetInputValue(_activeTarget);
        var next = current == "0" ? key.TrimStart('0') : current + key;
        SetInputValue(_activeTarget, next.Length == 0 ? "0" : next);
    }

    private void BackspaceNumeric()
    {
        if (_activeTarget is null)
        {
            return;
        }

        var current = GetInputValue(_activeTarget);
        SetInputValue(_activeTarget, current.Length > 0 ? current[..^1] : string.Empty);
    }

    private void ClearNumeric()
    {
        if (_activeTarget is not null)
        {
            SetInputValue(_activeTarget, string.Empty);
        }
    }

    private void ConfirmInput()
    {
        if (_activeTarget is not null && IsKoreanKeyboardVisible)
        {
            SetInputValue(_activeTarget, _composer.Commit());
        }

        DismissInputPanel();
    }

    private void DismissInputPanel()
    {
        IsInputPanelVisible = false;
        IsKoreanKeyboardVisible = false;
        IsNumericKeypadVisible = false;
        _activeTarget = null;
        _isKoreanShift = false;
    }
}
