using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Pos.App.ViewModels;

namespace Pos.App.Services;

internal static class VirtualInputFocus
{
    public static void Attach(UserControl page)
    {
        InputValidationViewModel? subscribed = null;
        string? target = null;
        void FocusTarget(string? field)
        {
            var tag = field switch { "PriceText" => "Price", "StockText" => "Stock", "CategoryText" => "Category", "QuantityText" => "Quantity", _ => field };
            page.Dispatcher.BeginInvoke(() =>
            {
                if (!page.IsVisible) return;
                var control = FindInputs(page).FirstOrDefault(control => control.Tag as string == tag);
                if (control is null) return;
                control.BringIntoView();
                control.Focus();
                if (control is TextBox box) box.SelectAll();
                else if (control is ComboBox combo)
                {
                    combo.ApplyTemplate();
                    if (combo.Template.FindName("PART_EditableTextBox", combo) is TextBox editable) { editable.Focus(); editable.SelectAll(); }
                }
            });
        }
        void OnFocusRequested(object? sender, string field) => FocusTarget(field);
        void OnChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (subscribed is not VirtualInputViewModel input || (args.PropertyName != nameof(VirtualInputViewModel.IsInputPanelVisible)
                && !(args.PropertyName == nameof(VirtualInputViewModel.ActiveInputLabel) && input.IsInputPanelVisible))) return;
            if (input.IsInputPanelVisible)
            {
                target = input.ActiveInputTarget;
                page.Dispatcher.BeginInvoke(() =>
                {
                    if (page.IsVisible && input.IsInputPanelVisible)
                        FindInputs(page).FirstOrDefault(box => box.Tag as string == "ActiveInput" && box.IsVisible)?.Focus();
                });
                return;
            }
            FocusTarget(target);
        }
        void Subscribe()
        {
            if (subscribed is not null) { subscribed.PropertyChanged -= OnChanged; subscribed.FocusFieldRequested -= OnFocusRequested; }
            subscribed = page.IsLoaded ? page.DataContext as InputValidationViewModel : null;
            if (subscribed is not null) { subscribed.PropertyChanged += OnChanged; subscribed.FocusFieldRequested += OnFocusRequested; }
        }
        page.Loaded += (_, _) => Subscribe();
        page.Unloaded += (_, _) =>
        {
            if (subscribed is not null) { subscribed.PropertyChanged -= OnChanged; subscribed.FocusFieldRequested -= OnFocusRequested; }
            subscribed = null;
        };
        page.DataContextChanged += (_, _) => Subscribe();
    }

    private static IEnumerable<Control> FindInputs(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is TextBox or ComboBox) yield return (Control)child;
            foreach (var descendant in FindInputs(child)) yield return descendant;
        }
    }
}
