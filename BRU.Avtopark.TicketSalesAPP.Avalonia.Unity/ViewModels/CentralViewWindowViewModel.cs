using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using ReDocking;
using System;
using System.Linq;
using System.Reactive.Linq;

using Avalonia.Controls;
using Avalonia.Layout;
using FluentAvalonia.UI.Controls;
using System.Collections.ObjectModel;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views.ManagementToolWindowsViews;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;


public class CentralViewWindowViewModel : IDisposable
{
    private TabView? _mainTabView;
    private ObservableCollection<TabViewItem> _tabItems = new();

   

    public CentralViewWindowViewModel()
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

        // Add management tool windows
        LeftUpperTopTools.Add(new ToolWindowViewModel("Билеты", "\uE8F5", new TicketManagementToolWindow()));
        LeftUpperTopTools.Add(new ToolWindowViewModel("Автобусы", "\uE806", new BusManagementToolWindow()));
        LeftUpperTopTools.Add(new ToolWindowViewModel("Маршруты", "\uE707", new RouteManagementToolWindow()));
        LeftUpperTopTools.Add(new ToolWindowViewModel("Обслуживание", "\uE7C0", new MaintenanceManagementToolWindow()));
        
        // Add statistics and reporting tool windows
        RightUpperTopTools.Add(new ToolWindowViewModel("Статистика продаж", "\uE9D9", new SalesStatisticsToolWindow()));
        RightUpperTopTools.Add(new ToolWindowViewModel("Статистика доходов", "\uE9F9", new IncomeReportToolWindow()));
        
        // Add sales management tool window
        RightUpperBottomTools.Add(new ToolWindowViewModel("Продажи билетов", "\uE8FB", new SalesManagementToolWindow()));

        // Add employee and job management tool windows
        LeftLowerTopTools.Add(new ToolWindowViewModel("Управление пользователями", "\uE779", new UserManagementViewModel()));
        LeftLowerTopTools.Add(new ToolWindowViewModel("Сотрудники", "\uE77B", new EmployeeManagementToolWindow()));
        LeftLowerTopTools.Add(new ToolWindowViewModel("Должности", "\uE779", new JobManagementViewModel()));
        LeftLowerTopTools.Add(new ToolWindowViewModel("Расписание", "\uE779", new RouteSchedulesManagementToolWindow()));
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