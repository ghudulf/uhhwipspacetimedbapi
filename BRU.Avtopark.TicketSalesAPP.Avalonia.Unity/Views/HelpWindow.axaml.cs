using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using Classic.CommonControls.Dialogs;
using System.Runtime.InteropServices;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views;

public partial class HelpWindow : Window
{
    private TextBox? searchBox;
    private TreeView? helpTreeView;
    private TextBlock? currentPath;
    private ListBox? recentTopics;
    private ListBox? popularTopics;
    private StackPanel? contentPanel;
    private Button? quickStartGuideButton;
    private Button? basicOperationsButton;
    private Button? faqButton;

    private ObservableCollection<string> recentTopicsList;
    private ObservableCollection<string> popularTopicsList;
    private Dictionary<string, HelpContent> helpContent;

    private class HelpContent
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public List<string> Steps { get; set; }
        public List<string> Notes { get; set; }
        public List<string> RelatedTopics { get; set; }

        public HelpContent(string title, string description, List<string>? steps = null, List<string>? notes = null, List<string>? relatedTopics = null)
        {
            Title = title;
            Description = description;
            Steps = steps ?? new List<string>();
            Notes = notes ?? new List<string>();
            RelatedTopics = relatedTopics ?? new List<string>();
        }
    }

    public HelpWindow()
    {
        InitializeComponent();
#if DEBUG
        this.AttachDevTools();
#endif
        
        // Initialize collections
        recentTopicsList = new ObservableCollection<string>
        {
            "Установка системы",
            "Первый запуск",
            "Управление пользователями"
        };

        popularTopicsList = new ObservableCollection<string>
        {
            "Продажа билетов",
            "Управление маршрутами",
            "Формирование отчетов",
            "Настройка системы",
            "Горячие клавиши"
        };

        InitializeHelpContent();
        InitializeControls();
        SetupEventHandlers();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void InitializeHelpContent()
    {
        helpContent = new Dictionary<string, HelpContent>
        {
            // Quick Start Guide
            {"Руководство по началу работы", new HelpContent(
                "Руководство по началу работы",
                "Это руководство поможет вам начать работу с системой управления автобусным парком.",
                new List<string>
                {
                    "1. Установите систему, следуя инструкциям установщика",
                    "2. Запустите приложение и войдите под учетной записью администратора",
                    "3. Настройте основные параметры системы",
                    "4. Создайте пользователей и назначьте им роли",
                    "5. Добавьте информацию об автобусах и маршрутах"
                },
                new List<string>
                {
                    "• Убедитесь, что у вас есть права администратора для установки",
                    "• Сохраните резервную копию базы данных перед обновлением",
                    "• Ознакомьтесь с документацией по безопасности"
                },
                new List<string>
                {
                    "Системные требования",
                    "Установка системы",
                    "Первый запуск"
                }
            )},

            // Basic Operations
            {"Основные операции", new HelpContent(
                "Основные операции",
                "Обзор основных операций, доступных в системе.",
                new List<string>
                {
                    "Продажа билетов",
                    "Управление маршрутами",
                    "Формирование отчетов",
                    "Администрирование пользователей",
                    "Настройка системы"
                },
                new List<string>
                {
                    "• Все операции доступны через главное меню",
                    "• Используйте горячие клавиши для быстрого доступа",
                    "• Права доступа к операциям зависят от роли пользователя"
                }
            )},

            // FAQ
            {"Часто задаваемые вопросы", new HelpContent(
                "Часто задаваемые вопросы",
                "Ответы на часто задаваемые вопросы пользователей системы.",
                new List<string>
                {
                    "Как изменить пароль?",
                    "Как создать новый маршрут?",
                    "Как сформировать отчет?",
                    "Что делать при ошибке входа?",
                    "Как настроить права доступа?"
                }
            )},

            // System Requirements
            {"Системные требования", new HelpContent(
                "Системные требования",
                "Минимальные и рекомендуемые требования для работы системы.",
                new List<string>
                {
                    "Минимальные требования:",
                    "• Windows 10 или новее",
                    "• 4 ГБ оперативной памяти",
                    "• 2 ГГц процессор",
                    "• 1 ГБ свободного места на диске",
                    "",
                    "Рекомендуемые требования:",
                    "• Windows 10/11",
                    "• 8 ГБ оперативной памяти",
                    "• 4 ГГц процессор",
                    "• 2 ГБ свободного места на диске"
                }
            )},

            // Installation Process
            {"Процесс установки", new HelpContent(
                "Процесс установки",
                "Пошаговое руководство по установке системы.",
                new List<string>
                {
                    "1. Загрузите установочный пакет с официального сайта",
                    "2. Запустите установщик от имени администратора",
                    "3. Примите лицензионное соглашение",
                    "4. Выберите компоненты для установки",
                    "5. Укажите путь установки",
                    "6. Настройте параметры подключения к базе данных",
                    "7. Завершите установку"
                },
                new List<string>
                {
                    "• Убедитесь, что у вас есть права администратора",
                    "• Закройте все работающие приложения перед установкой",
                    "• Создайте резервную копию данных при обновлении"
                }
            )},

            // User Management
            {"Управление пользователями", new HelpContent(
                "Управление пользователями",
                "Руководство по управлению пользователями системы.",
                new List<string>
                {
                    "1. Откройте раздел Администрирование",
                    "2. Выберите 'Управление пользователями'",
                    "3. Для создания пользователя нажмите 'Добавить'",
                    "4. Заполните обязательные поля",
                    "5. Назначьте роли и права доступа",
                    "6. Сохраните изменения"
                }
            )},

            // Ticket Sales
            {"Продажа билетов", new HelpContent(
                "Продажа билетов",
                "Процесс продажи билетов в системе.",
                new List<string>
                {
                    "1. Откройте форму продажи билетов",
                    "2. Выберите маршрут и дату",
                    "3. Укажите количество билетов",
                    "4. Выберите места",
                    "5. Введите данные пассажира",
                    "6. Выберите способ оплаты",
                    "7. Подтвердите продажу"
                },
                new List<string>
                {
                    "• Проверяйте правильность введенных данных",
                    "• Информируйте пассажира о правилах возврата",
                    "• Распечатайте билет после продажи"
                }
            )}

            // ... Add more help content for other topics
        };
    }

    private void InitializeControls()
    {
        searchBox = this.FindControl<TextBox>("SearchBox");
        helpTreeView = this.FindControl<TreeView>("HelpTreeView");
        currentPath = this.FindControl<TextBlock>("CurrentPath");
        recentTopics = this.FindControl<ListBox>("RecentTopics");
        popularTopics = this.FindControl<ListBox>("PopularTopics");
        contentPanel = this.FindControl<StackPanel>("ContentPanel");
        quickStartGuideButton = this.FindControl<Button>("QuickStartGuideButton");
        basicOperationsButton = this.FindControl<Button>("BasicOperationsButton");
        faqButton = this.FindControl<Button>("FAQButton");

        if (recentTopics != null)
            recentTopics.ItemsSource = recentTopicsList;
        
        if (popularTopics != null)
            popularTopics.ItemsSource = popularTopicsList;
    }

    private void SetupEventHandlers()
    {
        if (searchBox != null)
            searchBox.TextChanged += SearchBox_TextChanged;

        if (helpTreeView != null)
            helpTreeView.SelectionChanged += HelpTreeView_SelectionChanged;

        if (recentTopics != null)
            recentTopics.SelectionChanged += RecentTopics_SelectionChanged;

        if (popularTopics != null)
            popularTopics.SelectionChanged += PopularTopics_SelectionChanged;

        if (quickStartGuideButton != null)
            quickStartGuideButton.Click += (s, e) => LoadContent("Руководство по началу работы");

        if (basicOperationsButton != null)
            basicOperationsButton.Click += (s, e) => LoadContent("Основные операции");

        if (faqButton != null)
            faqButton.Click += (s, e) => LoadContent("Часто задаваемые вопросы");

        // Add handlers for footer buttons
        var sysInfoButton = this.FindControl<Button>("SystemInfoButton");
        var feedbackButton = this.FindControl<Button>("FeedbackButton");
        var printButton = this.FindControl<Button>("PrintButton");
        var exportButton = this.FindControl<Button>("ExportButton");

        if (sysInfoButton != null) sysInfoButton.Click += ShowSystemInfo;
        if (feedbackButton != null) feedbackButton.Click += ShowFeedback;
        if (printButton != null) printButton.Click += PrintContent;
        if (exportButton != null) exportButton.Click += ExportContent;
    }

    private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (helpTreeView == null) return;

        if (string.IsNullOrWhiteSpace(searchBox?.Text))
        {
            // Reset all items visibility
            ResetTreeViewItemsVisibility(helpTreeView.ItemCount > 0 ? helpTreeView.Items : null);
            return;
        }

        SearchHelp(searchBox.Text);
    }

    private void SearchHelp(string searchText)
    {
        if (helpTreeView == null) return;

        // Search in TreeView items
        bool foundAnyMatch = FilterTreeViewItems(helpTreeView.ItemCount > 0 ? helpTreeView.Items : null, searchText);

        // Search in help content
        var contentResults = helpContent
            .Where(kvp => 
                kvp.Key.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                kvp.Value.Description.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                kvp.Value.Steps.Any(step => step.Contains(searchText, StringComparison.OrdinalIgnoreCase)) ||
                kvp.Value.Notes.Any(note => note.Contains(searchText, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // If we found content matches but no visible tree items, show the first content match
        if (contentResults.Any() && !foundAnyMatch)
        {
            LoadContent(contentResults.First().Key);
        }
    }

    private bool FilterTreeViewItems(IEnumerable<object>? items, string searchText)
    {
        if (items == null) return false;
        bool foundMatch = false;

        foreach (object item in items)
        {
            if (item is TreeViewItem treeItem)
            {
                // Search in current item's header
                bool headerMatch = treeItem.Header?.ToString()?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false;
                
                // Search in child items
                bool childrenMatch = FilterTreeViewItems(treeItem.ItemCount > 0 ? treeItem.Items : null, searchText);

                // Show item if it matches or has matching children
                treeItem.IsVisible = headerMatch || childrenMatch;
                
                // If this item matches, expand its parent items
                if (headerMatch || childrenMatch)
                {
                    foundMatch = true;
                    ExpandParents(treeItem);
                }
            }
        }

        return foundMatch;
    }

    private void ResetTreeViewItemsVisibility(IEnumerable<object>? items)
    {
        if (items == null) return;

        foreach (object item in items)
        {
            if (item is TreeViewItem treeItem)
            {
                treeItem.IsVisible = true;
                ResetTreeViewItemsVisibility(treeItem.ItemCount > 0 ? treeItem.Items : null);
            }
        }
    }

    private void ExpandParents(TreeViewItem item)
    {
        var parent = item.Parent as TreeViewItem;
        while (parent != null)
        {
            parent.IsExpanded = true;
            parent = parent.Parent as TreeViewItem;
        }
    }

    private void HelpTreeView_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is TreeViewItem selectedItem)
        {
            UpdateCurrentPath(selectedItem);
            LoadContent(selectedItem.Header?.ToString() ?? string.Empty);
            AddToRecentTopics(selectedItem.Header?.ToString());
        }
    }

    private void UpdateCurrentPath(TreeViewItem item)
    {
        if (currentPath == null) return;

        var path = new List<string>();
        var current = item;

        while (current != null)
        {
            if (current.Header != null)
                path.Insert(0, current.Header.ToString() ?? string.Empty);
            
            if (current.Parent is TreeViewItem parent)
                current = parent;
            else
                break;
        }

        currentPath.Text = string.Join(" > ", path);
    }

    private void LoadContent(string topic)
    {
        if (contentPanel == null) return;

        // Clear existing content except the quick start section
        while (contentPanel.Children.Count > 3)
        {
            contentPanel.Children.RemoveAt(3);
        }

        if (helpContent.TryGetValue(topic, out HelpContent? content))
        {
            var contentStack = new StackPanel { Margin = new Thickness(0, 20, 0, 0) };

            // Add title
            contentStack.Children.Add(new TextBlock
            {
                Text = content.Title,
                Classes = { "header" }
            });

            // Add description
            contentStack.Children.Add(new TextBlock
            {
                Text = content.Description,
                Classes = { "content" },
                Margin = new Thickness(0, 10)
            });

            // Add steps if any
            if (content.Steps.Any())
            {
                contentStack.Children.Add(new TextBlock
                {
                    Text = "Шаги:",
                    Classes = { "subheader" },
                    Margin = new Thickness(0, 20, 0, 10)
                });

                foreach (var step in content.Steps)
                {
                    contentStack.Children.Add(new TextBlock
                    {
                        Text = step,
                        Classes = { "content" }
                    });
                }
            }

            // Add notes if any
            if (content.Notes.Any())
            {
                contentStack.Children.Add(new TextBlock
                {
                    Text = "Примечания:",
                    Classes = { "subheader" },
                    Margin = new Thickness(0, 20, 0, 10)
                });

                foreach (var note in content.Notes)
                {
                    contentStack.Children.Add(new TextBlock
                    {
                        Text = note,
                        Classes = { "content" }
                    });
                }
            }

            // Add related topics if any
            if (content.RelatedTopics.Any())
            {
                contentStack.Children.Add(new TextBlock
                {
                    Text = "Связанные темы:",
                    Classes = { "subheader" },
                    Margin = new Thickness(0, 20, 0, 10)
                });

                foreach (var relatedTopic in content.RelatedTopics)
                {
                    var button = new Button
                    {
                        Content = relatedTopic,
                        Classes = { "nav-button" }
                    };
                    button.Click += (s, e) => LoadContent(topic);
                    contentStack.Children.Add(button);
                }
            }

            contentPanel.Children.Add(contentStack);
            AddToRecentTopics(topic);
        }
    }

    private void AddToRecentTopics(string? topic)
    {
        if (string.IsNullOrEmpty(topic)) return;

        try
        {
            // Create a new list to avoid modifying collection while it's being used
            var updatedList = new List<string>(recentTopicsList);
            
            // Remove if already exists
            updatedList.Remove(topic);
            
            // Add to beginning
            updatedList.Insert(0, topic);
            
            // Keep only last 5 items
            while (updatedList.Count > 5)
                updatedList.RemoveAt(updatedList.Count - 1);

            // Clear and update the observable collection
            recentTopicsList.Clear();
            foreach (var item in updatedList)
                recentTopicsList.Add(item);
        }
        catch (Exception)
        {
            // Silently handle any collection modification errors
        }
    }

    private void RecentTopics_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is string topic && !string.IsNullOrEmpty(topic))
        {
            // Clear selection to prevent re-entry
            if (recentTopics != null)
                recentTopics.SelectedItem = null;
                
            LoadContent(topic);
        }
    }

    private void PopularTopics_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is string topic && !string.IsNullOrEmpty(topic))
        {
            // Clear selection to prevent re-entry
            if (popularTopics != null)
                popularTopics.SelectedItem = null;
                
            LoadContent(topic);
        }
    }

    public async void ShowSystemInfo(object? sender, RoutedEventArgs e)
    {
        var info = new StringBuilder();
        info.AppendLine("Системная информация:");
        info.AppendLine($"ОС: {RuntimeInformation.OSDescription}");
        info.AppendLine($"Версия .NET: {RuntimeInformation.FrameworkDescription}");
        info.AppendLine($"Архитектура: {RuntimeInformation.ProcessArchitecture}");
        info.AppendLine($"Версия приложения: 0.5A");

        await MessageBox.ShowDialog(
            this,
            info.ToString(),
            "Системная информация",
            MessageBoxButtons.Ok,
            MessageBoxIcon.Information);
    }

    public async void ShowFeedback(object? sender, RoutedEventArgs e)
    {
        await MessageBox.ShowDialog(
            this,
            "Для отправки отзыва или сообщения об ошибке, пожалуйста, напишите на email:\nsupport@buspark.example.com",
            "Обратная связь",
            MessageBoxButtons.Ok,
            MessageBoxIcon.Information);
    }

    public async void PrintContent(object? sender, RoutedEventArgs e)
    {
        await MessageBox.ShowDialog(
            this,
            "Функция печати будет доступна в следующей версии приложения.",
            "Печать",
            MessageBoxButtons.Ok,
            MessageBoxIcon.Information);
    }

    public async void ExportContent(object? sender, RoutedEventArgs e)
    {
        await MessageBox.ShowDialog(
            this,
            "Функция экспорта будет доступна в следующей версии приложения.",
            "Экспорт",
            MessageBoxButtons.Ok,
            MessageBoxIcon.Information);
    }
} 