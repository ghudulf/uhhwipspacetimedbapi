using System;

using Avalonia.Controls;
using Avalonia.Data;

using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views;

public class ToolWindow : Window
{
    private readonly IDisposable _disposable;
    private readonly Window _owner;

    public ToolWindow(ToolWindowViewModel viewModel, Window owner)
    {
        _owner = owner;
        DataContext = viewModel;
        Width = 300;
        Height = 300;
        ShowInTaskbar = false;
        Content = new ContentControl { [!ContentProperty] = new Binding("Content.Value") };
        _disposable = viewModel.IsSelected.Subscribe(OnSelectedChanged);
    }

    public ToolWindowViewModel ViewModel => (ToolWindowViewModel)DataContext!;

    private void OnSelectedChanged(bool isSelected)
    {
        if (!isSelected)
        {
            Hide();
        }
        else
        {
            Show(_owner);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _disposable.Dispose();
    }
}