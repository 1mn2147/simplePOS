using System.Collections;
using System.ComponentModel;

namespace Pos.App.ViewModels;

public abstract class InputValidationViewModel : ObservableObject, INotifyDataErrorInfo
{
    private readonly Dictionary<string, string> _errors = [];
    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;
    public event EventHandler<string>? FocusFieldRequested;
    public bool HasErrors => _errors.Count > 0;
    public int ErrorCount => _errors.Count;
    public virtual string? FirstInvalidField => _errors.Keys.FirstOrDefault();

    public virtual IEnumerable GetErrors(string? propertyName) => string.IsNullOrEmpty(propertyName)
        ? _errors.Values.ToArray()
        : _errors.TryGetValue(propertyName, out var error) ? new[] { error } : Array.Empty<string>();

    protected string GetError(string propertyName) => _errors.GetValueOrDefault(propertyName, string.Empty);

    protected void SetValidationErrors(IReadOnlyDictionary<string, string> errors)
    {
        var changed = _errors.Keys.Union(errors.Keys)
            .Where(key => GetError(key) != errors.GetValueOrDefault(key, string.Empty)).ToArray();
        if (changed.Length == 0) return;
        _errors.Clear();
        foreach (var pair in errors.Where(pair => pair.Value.Length > 0)) _errors[pair.Key] = pair.Value;
        foreach (var field in changed) RaiseErrorsChanged(field);
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(ErrorCount));
        OnPropertyChanged(nameof(FirstInvalidField));
        OnValidationChanged();
    }

    protected void RaiseErrorsChanged(string field) => ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(field));
    protected virtual void OnValidationChanged() { }
    protected void FocusFirstError()
    {
        if (FirstInvalidField is { } field) FocusField(field);
    }
    protected void FocusField(string field) => FocusFieldRequested?.Invoke(this, field);
}
