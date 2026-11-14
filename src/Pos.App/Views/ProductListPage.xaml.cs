using System.Windows.Controls;
using Pos.App.Services;

namespace Pos.App.Views;

public partial class ProductListPage : UserControl
{
    public ProductListPage()
    {
        InitializeComponent();
        VirtualInputFocus.Attach(this);
    }
}