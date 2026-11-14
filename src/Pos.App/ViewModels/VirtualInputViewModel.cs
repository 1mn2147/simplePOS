using System.Windows.Input;
using Pos.Core;

namespace Pos.App.ViewModels;

public abstract class VirtualInputViewModel : InputValidationViewModel
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
    private static readonly IReadOnlyDictionary<string, string> NormalKeyLabels = ShiftedJamo.Keys.ToDictionary(key => key.ToString(), key => key.ToString());
    private static readonly IReadOnlyDictionary<string, string> ShiftedKeyLabels = ShiftedJamo.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value.ToString());

    private readonly KoreanInputComposer _composer = new();
    private bool _isInputPanelVisible;
    private bool _isKoreanKeyboardVisible;
    private bool _isNumericKeypadVisible;
    private bool _isKoreanShift;
    private string? _activeTarget;

    protected VirtualInputViewModel()
    {
        PropertyChanged += (_, args) =>
        {
            if (_activeTarget is not null && args.PropertyName != nameof(ActiveInputValue) && args.PropertyName != nameof(ActiveInputLabel))
                OnPropertyChanged(nameof(ActiveInputValue));
        };
        ShowKoreanKeyboardCommand = new RelayCommand<string>(ShowKoreanKeyboard);
        ShowNumericKeypadCommand = new RelayCommand<string>(ShowNumericKeypad);
        DismissInputPanelCommand = new RelayCommand(DismissInputPanel);
        ConfirmInputCommand = new RelayCommand(ConfirmInput);
        KoreanKeyCommand = new RelayCommand<string>(EnterKoreanKey);
        ToggleKoreanShiftCommand = new RelayCommand(() => IsKoreanShift = !IsKoreanShift);
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
    public bool IsKoreanShift
    {
        get => _isKoreanShift;
        private set
        {
            if (!SetProperty(ref _isKoreanShift, value)) return;
            OnPropertyChanged(nameof(KoreanKeyLabels));
            OnPropertyChanged(nameof(KoreanShiftStateText));
        }
    }
    public IReadOnlyDictionary<string, string> KoreanKeyLabels => IsKoreanShift ? ShiftedKeyLabels : NormalKeyLabels;
    public string KoreanShiftStateText => IsKoreanShift ? "켜짐" : "꺼짐";

    public string? ActiveInputTarget => _activeTarget;
    public string ActiveInputLabel => _activeTarget switch
    {
        "Search" => "상품 검색", "ProductName" => "상품명", "Supplier" => "공급사",
        "Price" => "가격 (원)", "Stock" => "재고", "Quantity" => "담을 수량", "Category" => "카테고리", _ => "입력",
    };

    public string ActiveInputValue
    {
        get => _activeTarget is null ? string.Empty : GetInputValue(_activeTarget);
        set
        {
            if (_activeTarget is null) return;
            SetInputValue(_activeTarget, value);
            _composer.SetText(value);
            OnPropertyChanged();
        }
    }

    public override System.Collections.IEnumerable GetErrors(string? propertyName)
    {
        if (propertyName != nameof(ActiveInputValue)) return base.GetErrors(propertyName);
        if (_activeTarget is null) return Array.Empty<string>();
        return base.GetErrors(_activeTarget switch
        {
            "Price" => "PriceText", "Stock" => "StockText", "Search" => "SearchText", "Category" => "CategoryText", "Quantity" => "QuantityText", _ => _activeTarget,
        });
    }

    protected override void OnValidationChanged()
    {
        base.OnValidationChanged();
        RaiseErrorsChanged(nameof(ActiveInputValue));
    }

    protected abstract string GetInputValue(string target);
    protected abstract void SetInputValue(string target, string value);

    private void ShowKoreanKeyboard(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        _activeTarget = target;
        OnPropertyChanged(nameof(ActiveInputLabel));
        OnPropertyChanged(nameof(ActiveInputValue));
        RaiseErrorsChanged(nameof(ActiveInputValue));
        _composer.SetText(GetInputValue(target));
        IsKoreanShift = false;
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
        IsKoreanShift = false;
        OnPropertyChanged(nameof(ActiveInputLabel));
        OnPropertyChanged(nameof(ActiveInputValue));
        RaiseErrorsChanged(nameof(ActiveInputValue));
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

        if (_composer.Text != GetInputValue(_activeTarget)) _composer.SetText(GetInputValue(_activeTarget));

        string text;
        if (key == " ")
        {
            text = _composer.Space();
        }
        else
        {
            var jamo = key[0];
            if (IsKoreanShift && ShiftedJamo.TryGetValue(jamo, out var shifted))
            {
                jamo = shifted;
            }

            text = _composer.InputJamo(jamo);
        }

        IsKoreanShift = false;
        SetInputValue(_activeTarget, text);
    }

    private void BackspaceKorean()
    {
        if (_activeTarget is not null)
        {
            if (_composer.Text != GetInputValue(_activeTarget)) _composer.SetText(GetInputValue(_activeTarget));
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
        IsKoreanShift = false;
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
        IsKoreanShift = false;
    }
}
