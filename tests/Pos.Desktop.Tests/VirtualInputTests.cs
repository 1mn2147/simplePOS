using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Pos.Desktop.Tests;

public sealed class VirtualInputTests
{
    [AvaloniaFact]
    public async Task KoreanKeyboard_ReplacesSelectionAndRestoresFocusAfterConfirm()
    {
        var owner = new Window();
        var target = new TextBox { Text = "상품 이름표" };
        owner.Content = target;
        owner.Show();
        target.SelectionStart = 3;
        target.SelectionEnd = 5;

        try
        {
            var editing = VirtualInputDialog.EditAsync(owner, target, VirtualInputMode.Korean, "상품명");
            await WaitForAsync(() => owner.OwnedWindows.OfType<VirtualInputDialog>().SingleOrDefault() is not null);
            var dialog = owner.OwnedWindows.OfType<VirtualInputDialog>().Single();
            Click(FindButton(dialog, "ㅎ"));
            Click(FindButton(dialog, "ㅏ"));
            Click(FindButton(dialog, "ㄴ"));
            Click(FindButton(dialog, "완료"));

            Assert.True(await editing);
            Assert.Equal("상품 한표", target.Text);
            Dispatcher.UIThread.RunJobs();
            Assert.True(target.IsFocused);
        }
        finally
        {
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task NumericKeyboard_InsertsAtCaretAndCancelLeavesOriginalText()
    {
        var owner = new Window();
        var target = new TextBox { Text = "1200" };
        owner.Content = target;
        owner.Show();
        target.CaretIndex = 2;

        try
        {
            var editing = VirtualInputDialog.EditAsync(owner, target, VirtualInputMode.Numeric, "가격");
            await WaitForAsync(() => owner.OwnedWindows.OfType<VirtualInputDialog>().SingleOrDefault() is not null);
            var dialog = owner.OwnedWindows.OfType<VirtualInputDialog>().Single();
            Click(FindButton(dialog, "3"));
            Click(FindButton(dialog, "완료"));
            Assert.True(await editing);
            Assert.Equal("12300", target.Text);
            Assert.Equal(3, target.CaretIndex);

            target.SelectAll();
            editing = VirtualInputDialog.EditAsync(owner, target, VirtualInputMode.Numeric, "가격");
            await WaitForAsync(() => owner.OwnedWindows.OfType<VirtualInputDialog>().SingleOrDefault() is not null);
            dialog = owner.OwnedWindows.OfType<VirtualInputDialog>().Single();
            Click(FindButton(dialog, "9"));
            Click(dialog.FindControl<Button>("CancelButton")!);
            Assert.False(await editing);
            Assert.Equal("12300", target.Text);
        }
        finally
        {
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task KoreanKeyboard_ShiftAndBackspaceUpdateComposedSyllable()
    {
        var owner = new Window();
        var target = new TextBox();
        owner.Content = target;
        owner.Show();

        try
        {
            var editing = VirtualInputDialog.EditAsync(owner, target, VirtualInputMode.Korean, "상품명");
            await WaitForAsync(() => owner.OwnedWindows.OfType<VirtualInputDialog>().SingleOrDefault() is not null);
            var dialog = owner.OwnedWindows.OfType<VirtualInputDialog>().Single();
            Click(dialog.FindControl<Button>("ShiftButton")!);
            Click(FindButton(dialog, "ㄲ"));
            Click(FindButton(dialog, "ㅏ"));
            Click(FindButton(dialog, "지우기"));
            Click(FindButton(dialog, "ㅏ"));
            Click(FindButton(dialog, "완료"));
            Assert.True(await editing);
            Assert.Equal("까", target.Text);
        }
        finally
        {
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task KoreanKeyboard_ClearRemovesTextOnBothSidesOfCaret()
    {
        var owner = new Window();
        var target = new TextBox { Text = "앞뒤 문자열" };
        owner.Content = target;
        owner.Show();
        target.CaretIndex = 2;

        try
        {
            var editing = VirtualInputDialog.EditAsync(owner, target, VirtualInputMode.Korean, "상품명");
            await WaitForAsync(() => owner.OwnedWindows.OfType<VirtualInputDialog>().SingleOrDefault() is not null);
            var dialog = owner.OwnedWindows.OfType<VirtualInputDialog>().Single();
            Click(FindButton(dialog, "전체 지우기"));
            Click(FindButton(dialog, "완료"));

            Assert.True(await editing);
            Assert.Equal(string.Empty, target.Text);
            Assert.Equal(0, target.CaretIndex);
            Assert.Equal(0, target.SelectionStart);
            Assert.Equal(0, target.SelectionEnd);
        }
        finally
        {
            owner.Close();
        }
    }

    private static Button FindButton(Window window, string content) => window
        .GetVisualDescendants()
        .OfType<Button>()
        .First(button => string.Equals(button.Content?.ToString(), content, StringComparison.Ordinal));

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Assert.True(condition());
    }
}
