﻿using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using ReDocking;
using System;
using System.Linq;
using System.Reactive.Linq;

using Avalonia.Controls;
using Avalonia.Layout;
using FluentAvalonia.UI.Controls;
using System.Collections.ObjectModel;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;

public class MainWindowViewModel : IDisposable
{
    private TabView? _mainTabView;
    private ObservableCollection<TabViewItem> _tabItems = new();

   

    public MainWindowViewModel()
    {
        void ConfigureToolsList(ReactiveCollection<ToolWindowViewModel> list,
            ReactiveProperty<ToolWindowViewModel?> selected)
        {
            selected.Subscribe(x =>
                list.ToObservable()
                    .Where(y => y != x && y.DisplayMode.Value == DockableDisplayMode.Docked)
                    .Subscribe(y => y.IsSelected.Value = false));

            list.ObserveAddChanged()
                .Select(x => x.IsSelected.Select(y => (x, y)))
                .Subscribe(z =>
                {
                    z.Subscribe(w =>
                    {
                        if (w is { y: true, x.DisplayMode.Value: DockableDisplayMode.Docked })
                        {
                            selected.Value = w.x;
                        }
                        else
                        {
                            selected.Value = list.FirstOrDefault(xx =>
                                xx.IsSelected.Value && xx.DisplayMode.Value == DockableDisplayMode.Docked);
                        }
                    });
                });

            list.ObserveRemoveChanged()
                .Subscribe(x => x.Dispose());
        }

        ConfigureToolsList(LeftUpperTopTools, SelectedLeftUpperTopTool);
        ConfigureToolsList(LeftUpperBottomTools, SelectedLeftUpperBottomTool);
        ConfigureToolsList(LeftLowerTopTools, SelectedLeftLowerTopTool);
        ConfigureToolsList(LeftLowerBottomTools, SelectedLeftLowerBottomTool);
        ConfigureToolsList(RightUpperTopTools, SelectedRightUpperTopTool);
        ConfigureToolsList(RightUpperBottomTools, SelectedRightUpperBottomTool);
        ConfigureToolsList(RightLowerTopTools, SelectedRightLowerTopTool);
        ConfigureToolsList(RightLowerBottomTools, SelectedRightLowerBottomTool);

       
    }

    public ReactiveCollection<ToolWindowViewModel> LeftUpperTopTools { get; } = [];

    public ReactiveProperty<ToolWindowViewModel?> SelectedLeftUpperTopTool { get; } = new();

    public ReactiveCollection<ToolWindowViewModel> LeftUpperBottomTools { get; } = [];

    public ReactiveProperty<ToolWindowViewModel?> SelectedLeftUpperBottomTool { get; } = new();

    public ReactiveCollection<ToolWindowViewModel> LeftLowerTopTools { get; } = [];

    public ReactiveProperty<ToolWindowViewModel?> SelectedLeftLowerTopTool { get; } = new();

    public ReactiveCollection<ToolWindowViewModel> LeftLowerBottomTools { get; } = [];

    public ReactiveProperty<ToolWindowViewModel?> SelectedLeftLowerBottomTool { get; } = new();

    public ReactiveCollection<ToolWindowViewModel> RightUpperTopTools { get; } = [];

    public ReactiveProperty<ToolWindowViewModel?> SelectedRightUpperTopTool { get; } = new();

    public ReactiveCollection<ToolWindowViewModel> RightUpperBottomTools { get; } = [];

    public ReactiveProperty<ToolWindowViewModel?> SelectedRightUpperBottomTool { get; } = new();

    public ReactiveCollection<ToolWindowViewModel> RightLowerTopTools { get; } = [];

    public ReactiveProperty<ToolWindowViewModel?> SelectedRightLowerTopTool { get; } = new();

    public ReactiveCollection<ToolWindowViewModel> RightLowerBottomTools { get; } = [];

    public ReactiveProperty<ToolWindowViewModel?> SelectedRightLowerBottomTool { get; } = new();

    public ReactiveCollection<ToolWindowViewModel> FloatingWindows { get; } = [];

    public void Dispose()
    {
        SelectedLeftUpperTopTool.Dispose();
        SelectedLeftUpperBottomTool.Dispose();
        SelectedLeftLowerTopTool.Dispose();
        SelectedLeftLowerBottomTool.Dispose();
        SelectedRightUpperTopTool.Dispose();
        SelectedRightUpperBottomTool.Dispose();
        SelectedRightLowerTopTool.Dispose();
        SelectedRightLowerBottomTool.Dispose();
    }
}