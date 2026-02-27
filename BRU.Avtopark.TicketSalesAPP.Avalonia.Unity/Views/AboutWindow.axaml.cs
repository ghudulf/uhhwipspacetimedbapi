using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Classic.CommonControls.Dialogs;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views;

public partial class AboutWindow : Window
{
    private readonly ItemsControl technologyList;
    private readonly TextBlock runtimeVersion;
    private readonly TextBlock osVersion;
    private readonly TextBlock avaloniaVersion;

    public AboutWindow()
    {
        InitializeComponent();

        technologyList = this.FindControl<ItemsControl>("TechnologyList");
        runtimeVersion = this.FindControl<TextBlock>("RuntimeVersion");
        osVersion = this.FindControl<TextBlock>("OsVersion");
        avaloniaVersion = this.FindControl<TextBlock>("AvaloniaVersion");

        InitializeSystemInfo();
        InitializeTechnologyList();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void InitializeSystemInfo()
    {
        runtimeVersion.Text = $".NET Runtime: {RuntimeInformation.FrameworkDescription}";
        osVersion.Text = $"ОС: {RuntimeInformation.OSDescription}";
        avaloniaVersion.Text = $"Avalonia UI: {typeof(Application).Assembly.GetName().Version}";
    }

    private void InitializeTechnologyList()
    {
        var technologies = new List<string>
        {
            "Avalonia UI - Кроссплатформенный UI фреймворк",
            "C# 12.0 - Язык программирования",
            ".NET 9.0 - Платформа разработки",
            "MVVM Architecture - Архитектурный паттерн",
            "Entity Framework Core - ORM для работы с БД",
            "Microsoft SQL Server - СУБД",
            "Git - Система контроля версий",
            "Visual Studio 2022 - IDE"
        };

        technologyList.ItemsSource = technologies;
    }

    private async void OnSystemInfo(object sender, RoutedEventArgs e)
    {
        var info = new StringBuilder();
        info.AppendLine("Системная информация:");
        info.AppendLine($"Процессор: {RuntimeInformation.ProcessArchitecture}");
        info.AppendLine($"ОС: {RuntimeInformation.OSDescription}");
        info.AppendLine($"Платформа: {RuntimeInformation.OSArchitecture}");
        info.AppendLine($".NET Runtime: {RuntimeInformation.FrameworkDescription}");
        info.AppendLine($"Avalonia UI: {typeof(Application).Assembly.GetName().Version}");

        var dialog = new ModalDialog
        {
            Title = "Системная информация",
            Message = info.ToString(),
            DialogType = ModalDialogType.Information
        };

        await dialog.ShowDialog(this);
    }

    private async void OnCopyInfo(object sender, RoutedEventArgs e)
    {
        var info = new StringBuilder();
        info.AppendLine("Автопарк автобусов - Система управления автобусным парком");
        info.AppendLine("Версия: 0.5A");
        info.AppendLine("Разработчик: Савич Андрей Олегович");
        info.AppendLine("Группа: АСОИСЗ-241");
        info.AppendLine("Год: 2025");
        info.AppendLine();
        info.AppendLine("Системная информация:");
        info.AppendLine($"ОС: {RuntimeInformation.OSDescription}");
        info.AppendLine($".NET Runtime: {RuntimeInformation.FrameworkDescription}");
        info.AppendLine($"Avalonia UI: {typeof(Application).Assembly.GetName().Version}");

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(info.ToString());
            
            var dialog = new ModalDialog
            {
                Title = "Копирование",
                Message = "Информация скопирована в буфер обмена",
                DialogType = ModalDialogType.Information
            };

            await dialog.ShowDialog(this);
        }
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        Close();
    }
}