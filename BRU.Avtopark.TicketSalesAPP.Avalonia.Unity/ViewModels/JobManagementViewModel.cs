using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReactiveUI;
using Avalonia.Controls;
using System.Linq;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;
using Serilog;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;
using System.Collections.Generic;
using Avalonia.Controls.ApplicationLifetimes;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using SpacetimeDB.Types;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels
{
    public partial class JobManagementViewModel : ReactiveObject
    {
        private HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly JsonSerializerOptions _jsonOptions;

        private ObservableCollection<Job> _jobs = new();
        public ObservableCollection<Job> Jobs
        {
            get => _jobs;
            set => this.RaiseAndSetIfChanged(ref _jobs, value);
        }

        private Job? _selectedJob;
        public Job? SelectedJob
        {
            get => _selectedJob;
            set => this.RaiseAndSetIfChanged(ref _selectedJob, value);
        }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                this.RaiseAndSetIfChanged(ref _searchText, value);
                OnSearchTextChanged(value);
            }
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set => this.RaiseAndSetIfChanged(ref _isBusy, value);
        }

        private string _errorMessage = string.Empty;
        public string ErrorMessage
        {
            get => _errorMessage;
            set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
        }

        private bool _hasError;
        public bool HasError
        {
            get => _hasError;
            set => this.RaiseAndSetIfChanged(ref _hasError, value);
        }

        public JobManagementViewModel()
        {
            _httpClient = ApiClientService.Instance.CreateClient();
            _baseUrl = "http://localhost:5000/api";
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve
            };

            // Subscribe to auth token changes
            ApiClientService.Instance.OnAuthTokenChanged += (sender, token) =>
            {
                // Create a new client with the updated token
                _httpClient.Dispose();
                _httpClient = ApiClientService.Instance.CreateClient();
                // Reload data with the new token
                LoadData().ConfigureAwait(false);
            };

            LoadData().ConfigureAwait(false);
        }

        [RelayCommand]
        private async Task LoadData()
        {
            try
            {
                IsBusy = true;
                HasError = false;
                ErrorMessage = string.Empty;

                var response = await _httpClient.GetAsync($"{_baseUrl}/Jobs");
                if (response.IsSuccessStatusCode)
                {
                    var jsonString = await response.Content.ReadAsStringAsync();
                    var loadedJobs = JsonSerializer.Deserialize<List<Job>>(jsonString, _jsonOptions);
                    if (loadedJobs != null)
                    {
                        Jobs = new ObservableCollection<Job>(loadedJobs);
                    }
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    Log.Error("Failed to load jobs. Status: {StatusCode}, Error: {Error}", 
                        response.StatusCode, error);
                    throw new Exception($"Failed to load jobs. Status: {response.StatusCode}, Error: {error}");
                }
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = $"Error loading jobs: {ex.Message}";
                Log.Error(ex, "Error loading jobs");
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task Add()
        {
            try
            {
                var dialog = new Window
                {
                    Title = "Добавить должность",
                    Width = 400,
                    Height = 300,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                var grid = new Grid
                {
                    RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"),
                    Margin = new Thickness(10)
                };

                var jobTitleBox = new TextBox { Watermark = "Название должности" };
                var internshipBox = new TextBox { Watermark = "Требования к стажировке" };

                var addButton = new Button
                {
                    Content = "Добавить",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 10, 0, 0)
                };

                grid.Children.Add(jobTitleBox);
                Grid.SetRow(jobTitleBox, 0);
                grid.Children.Add(internshipBox);
                Grid.SetRow(internshipBox, 1);
                grid.Children.Add(addButton);
                Grid.SetRow(addButton, 3);

                dialog.Content = grid;

                addButton.Click += async (s, e) =>
                {
                    if (string.IsNullOrWhiteSpace(jobTitleBox.Text))
                    {
                        var errorDialog = MessageBoxManager
                            .GetMessageBoxStandard(
                                "Ошибка",
                                "Название должности обязательно",
                                ButtonEnum.Ok,
                                Icon.Error);

                        var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime
                            ? lifetime.MainWindow
                            : null;

                        if (mainWindow != null)
                        {
                            await errorDialog.ShowAsync();
                        }
                        return;
                    }

                    var newJob = new Job
                    {
                        JobTitle = jobTitleBox.Text,
                        Internship = internshipBox.Text ?? string.Empty
                    };

                    var json = JsonSerializer.Serialize(newJob);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var response = await _httpClient.PostAsync($"{_baseUrl}/Jobs", content);
                    if (response.IsSuccessStatusCode)
                    {
                        await LoadData();
                        dialog.Close();
                    }
                    else
                    {
                        var error = await response.Content.ReadAsStringAsync();
                        var errorDialog = MessageBoxManager
                            .GetMessageBoxStandard(
                                "Ошибка",
                                $"Не удалось добавить должность: {error}",
                                ButtonEnum.Ok,
                                Icon.Error);

                        var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime app
                            ? app.MainWindow
                            : null;

                        if (mainWindow != null)
                        {
                            await errorDialog.ShowAsync();
                        }
                    }
                };

                var ownerWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null;

                if (ownerWindow != null)
                {
                    await dialog.ShowDialog(ownerWindow);
                }
                else
                {
                    Log.Error("Could not find main window for dialog");
                }
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = $"Error adding job: {ex.Message}";
                Log.Error(ex, "Error adding job");
            }
        }

        [RelayCommand]
        private async Task Edit()
        {
            if (SelectedJob == null) return;

            try
            {
                var dialog = new Window
                {
                    Title = "Редактировать должность",
                    Width = 400,
                    Height = 300,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                var grid = new Grid
                {
                    RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"),
                    Margin = new Thickness(10)
                };

                var jobTitleBox = new TextBox 
                { 
                    Text = SelectedJob.JobTitle,
                    Watermark = "Название должности" 
                };
                var internshipBox = new TextBox 
                { 
                    Text = SelectedJob.Internship,
                    Watermark = "Требования к стажировке" 
                };

                var updateButton = new Button
                {
                    Content = "Обновить",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 10, 0, 0)
                };

                grid.Children.Add(jobTitleBox);
                Grid.SetRow(jobTitleBox, 0);
                grid.Children.Add(internshipBox);
                Grid.SetRow(internshipBox, 1);
                grid.Children.Add(updateButton);
                Grid.SetRow(updateButton, 3);

                dialog.Content = grid;

                updateButton.Click += async (s, e) =>
                {
                    if (string.IsNullOrWhiteSpace(jobTitleBox.Text))
                    {
                        var errorDialog = MessageBoxManager
                            .GetMessageBoxStandard(
                                "Ошибка",
                                "Название должности обязательно",
                                ButtonEnum.Ok,
                                Icon.Error);

                        var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime
                            ? lifetime.MainWindow
                            : null;

                        if (mainWindow != null)
                        {
                            await errorDialog.ShowAsync();
                        }
                        return;
                    }

                    var updatedJob = new Job
                    {
                        JobId = SelectedJob.JobId,
                        JobTitle = jobTitleBox.Text,
                        Internship = internshipBox.Text ?? string.Empty
                    };

                    var json = JsonSerializer.Serialize(updatedJob);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var response = await _httpClient.PutAsync($"{_baseUrl}/Jobs/{SelectedJob.JobId}", content);
                    if (response.IsSuccessStatusCode)
                    {
                        await LoadData();
                        dialog.Close();
                    }
                    else
                    {
                        var error = await response.Content.ReadAsStringAsync();
                        var errorDialog = MessageBoxManager
                            .GetMessageBoxStandard(
                                "Ошибка",
                                $"Не удалось обновить должность: {error}",
                                ButtonEnum.Ok,
                                Icon.Error);

                        var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime app
                            ? app.MainWindow
                            : null;

                        if (mainWindow != null)
                        {
                            await errorDialog.ShowAsync();
                        }
                    }
                };

                var ownerWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null;

                if (ownerWindow != null)
                {
                    await dialog.ShowDialog(ownerWindow);
                }
                else
                {
                    Log.Error("Could not find main window for dialog");
                }
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = $"Error updating job: {ex.Message}";
                Log.Error(ex, "Error updating job");
            }
        }

        [RelayCommand]
        private async Task Delete()
        {
            if (SelectedJob == null) return;

            try
            {
                var confirmDialog = MessageBoxManager
                    .GetMessageBoxStandard(
                        "Подтверждение",
                        $"Вы уверены, что хотите удалить должность {SelectedJob.JobTitle}?",
                        ButtonEnum.YesNo,
                        Icon.Question);

                var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null;

                if (mainWindow != null)
                {
                    var result = await confirmDialog.ShowAsync();
                    if (result == ButtonResult.Yes)
                    {
                        var response = await _httpClient.DeleteAsync($"{_baseUrl}/Jobs/{SelectedJob.JobId}");
                        if (response.IsSuccessStatusCode)
                        {
                            await LoadData();
                        }
                        else
                        {
                            var error = await response.Content.ReadAsStringAsync();
                            var errorDialog = MessageBoxManager
                                .GetMessageBoxStandard(
                                    "Ошибка",
                                    $"Не удалось удалить должность: {error}",
                                    ButtonEnum.Ok,
                                    Icon.Error);

                            await errorDialog.ShowAsync();
                        }
                    }
                }
                else
                {
                    Log.Error("Could not find main window for dialog");
                }
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = $"Error deleting job: {ex.Message}";
                Log.Error(ex, "Error deleting job");
            }
        }

        private void OnSearchTextChanged(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                LoadData().ConfigureAwait(false);
                return;
            }

            var filteredJobs = Jobs.Where(j => 
                j.JobTitle.Contains(value, StringComparison.OrdinalIgnoreCase) ||
                j.Internship.Contains(value, StringComparison.OrdinalIgnoreCase)
            ).ToList();

            Jobs = new ObservableCollection<Job>(filteredJobs);
        }
    }
} 