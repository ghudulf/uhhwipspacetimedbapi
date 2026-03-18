using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;
using Material.Icons;
using Material.Icons.Avalonia;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Controls;

/// <summary>
/// Builds the per-step content controls for the traditional login wizard.
/// Shared between <see cref="Views.AuthWindow"/> and <see cref="TraditionalLoginControl"/>
/// so the UI is identical in both hosting contexts.
/// All colors are resolved from the application theme resources so both
/// Light and Dark variants are supported automatically.
/// </summary>
public sealed class AuthWindowContentFactory
{
    private readonly AuthViewModel _vm;

    public AuthWindowContentFactory(AuthViewModel vm) => _vm = vm;

    // ── Theme-aware resource helper ──────────────────────────────────────

    /// <summary>
    /// Binds an AvaloniaProperty on a StyledElement to a DynamicResource key.
    /// DynamicResourceExtension is NOT a BindingBase in Avalonia 12, so we cannot
    /// use the [!Prop] = value indexer. Instead we call SetValue with the extension
    /// object directly — Avalonia's property system recognises IBinding/markup
    /// extensions passed to SetValue and wires them up correctly.
    /// </summary>
    private static void BindDynRes(StyledElement target, AvaloniaProperty property, string key)
    {
        target.Bind(property, new DynamicResourceExtension(key));
    }

    // ── Step 1: Username / Password ──────────────────────────────────────

    public Control CreateLoginStep()
    {
        var panel = new Grid { Margin = new Thickness(15) };
        panel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        panel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        panel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        panel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        panel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        panel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        panel.RowDefinitions.Add(new RowDefinition(GridLength.Star));

        var descText = new TextBlock
        {
            Text = "Введите ваши учетные данные для доступа к системе БРУ Автопарк.",
            TextWrapping = TextWrapping.Wrap
        };
        BindDynRes(descText, TextBlock.ForegroundProperty, "BruTitleForeground");

        var descBorder = new Border
        {
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 20),
            Child = descText
        };
        BindDynRes(descBorder, Border.BackgroundProperty, "BruHeaderBackground");
        BindDynRes(descBorder, Border.BorderBrushProperty, "BruHeaderBorder");
        Grid.SetRow(descBorder, 0);
        panel.Children.Add(descBorder);

        var usernameLabel = new TextBlock
        {
            Text = "Имя пользователя:",
            Margin = new Thickness(0, 0, 0, 4)
        };
        BindDynRes(usernameLabel, TextBlock.ForegroundProperty, "BruTitleForeground");
        Grid.SetRow(usernameLabel, 1);
        panel.Children.Add(usernameLabel);

        var usernameBox = new TextBox
        {
            PlaceholderText = "Логин",
            Margin = new Thickness(0, 0, 0, 12),
            [!TextBox.TextProperty] = new Binding("Username") { Mode = BindingMode.TwoWay }
        };
        Grid.SetRow(usernameBox, 2);
        panel.Children.Add(usernameBox);

        var passwordLabel = new TextBlock
        {
            Text = "Пароль:",
            Margin = new Thickness(0, 0, 0, 4)
        };
        BindDynRes(passwordLabel, TextBlock.ForegroundProperty, "BruTitleForeground");
        Grid.SetRow(passwordLabel, 3);
        panel.Children.Add(passwordLabel);

        var passwordBox = new TextBox
        {
            PlaceholderText = "Пароль",
            PasswordChar = '•',
            Margin = new Thickness(0, 0, 0, 15),
            [!TextBox.TextProperty] = new Binding("Password") { Mode = BindingMode.TwoWay }
        };
        passwordBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && _vm.CanGoForward && _vm.GoToNextStepCommand.CanExecute(null))
                _vm.GoToNextStepCommand.Execute(null);
        };
        Grid.SetRow(passwordBox, 4);
        panel.Children.Add(passwordBox);

        var noteIcon = new MaterialIcon
        {
            Kind = MaterialIconKind.Information,
            Width = 16, Height = 16
        };
        BindDynRes(noteIcon, MaterialIcon.ForegroundProperty, "BruLoadingCircle");

        var notePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            Margin = new Thickness(0, 15, 0, 0),
            Children =
            {
                noteIcon,
                new TextBlock
                {
                    Text = "Для доступа к системе используйте учетную запись администратора.",
                    FontSize = 11,
                    Opacity = 0.85,
                    Margin = new Thickness(5, 0, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };
        Grid.SetRow(notePanel, 5);
        panel.Children.Add(notePanel);

        return panel;
    }

    // ── Step 2: Validation progress ──────────────────────────────────────

    public Control CreateAuthorizationStep()
    {
        var syncIcon = new MaterialIcon
        {
            Kind = MaterialIconKind.CloudSync,
            Width = 48, Height = 48,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        BindDynRes(syncIcon, MaterialIcon.ForegroundProperty, "BruLoadingCircle");

        var checkingLabel = new TextBlock
        {
            Text = "Проверка учетных данных...",
            HorizontalAlignment = HorizontalAlignment.Center,
            FontWeight = FontWeight.SemiBold,
            TextAlignment = TextAlignment.Center
        };
        BindDynRes(checkingLabel, TextBlock.ForegroundProperty, "BruTitleForeground");

        var statusLabel = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            FontSize = 11,
            Margin = new Thickness(0, 5, 0, 0),
            [!TextBlock.TextProperty] = new Binding("StatusMessage")
        };

        var processingContent = new StackPanel
        {
            Spacing = 15,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                syncIcon,
                checkingLabel,
                new ProgressBar
                {
                    IsIndeterminate = true,
                    Width = 200, Height = 15,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    CornerRadius = new CornerRadius(0)
                },
                statusLabel
            }
        };

        var processingBorder = new Border
        {
            BorderThickness = new Thickness(1),
            Padding = new Thickness(20),
            Width = 300,
            Child = processingContent
        };
        BindDynRes(processingBorder, Border.BackgroundProperty, "BruHeaderBackground");
        BindDynRes(processingBorder, Border.BorderBrushProperty, "BruHeaderBorder");

        return new StackPanel
        {
            Spacing = 20,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(20),
            Children = { processingBorder }
        };
    }

    // ── Step 3: 2FA ──────────────────────────────────────────────────────

    public Control CreateTwoFactorStep()
    {
        var twoFaText = new TextBlock
        {
            Text = $"Требуется двухфакторная аутентификация ({_vm.TwoFactorType}).\nВведите код подтверждения.",
            TextWrapping = TextWrapping.Wrap
        };
        BindDynRes(twoFaText, TextBlock.ForegroundProperty, "BruTitleForeground");

        var twoFaBorder = new Border
        {
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 15),
            Child = twoFaText
        };
        BindDynRes(twoFaBorder, Border.BackgroundProperty, "BruHeaderBackground");
        BindDynRes(twoFaBorder, Border.BorderBrushProperty, "BruHeaderBorder");

        var codeLabel = new TextBlock
        {
            Text = "Код подтверждения:",
            Margin = new Thickness(0, 0, 0, 4)
        };
        BindDynRes(codeLabel, TextBlock.ForegroundProperty, "BruTitleForeground");

        return new StackPanel
        {
            Spacing = 15,
            Margin = new Thickness(15),
            Children =
            {
                twoFaBorder,
                codeLabel,
                new TextBox
                {
                    PlaceholderText = "000000",
                    MaxLength = 6,
                    Margin = new Thickness(0, 0, 0, 12)
                }
            }
        };
    }

    // ── Step 4: Success ──────────────────────────────────────────────────

    public Control CreateSuccessStep()
    {
        var successTitle = new TextBlock
        {
            Text = "Вход выполнен успешно!",
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        BindDynRes(successTitle, TextBlock.ForegroundProperty, "BruTitleForeground");

        return new StackPanel
        {
            Spacing = 15,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(20),
            Children =
            {
                new MaterialIcon
                {
                    Kind = MaterialIconKind.CheckCircle,
                    Width = 64, Height = 64,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = new SolidColorBrush(Color.Parse("#00AA00")) // success green — same in both themes
                },
                successTitle,
                new TextBlock
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontSize = 13,
                    [!TextBlock.TextProperty] = new Binding("UserInfo")
                }
            }
        };
    }
}
