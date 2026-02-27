using System;
using System.Net.Http;
using System.Net.Http.Headers;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services
{
    public class ApiClientService
    {
        private static ApiClientService? _instance;
        private static readonly object _lock = new();
        private string? _authToken;
        private bool? _isAdmin;
        private int? _userRole;
        private string? _roleName;

        public static ApiClientService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new ApiClientService();
                    }
                }
                return _instance;
            }
        }

        private ApiClientService()
        {
        }

        public string? AuthToken
        {
            get => _authToken;
            set
            {
                if (_authToken != value)
                {
                    Serilog.Log.Information("ApiClientService.AuthToken: Setting new token");
                    if (value != null)
                    {
                        Serilog.Log.Debug("ApiClientService.AuthToken: New token length: {Length}, preview: {Preview}...", 
                            value.Length, value.Length > 20 ? value.Substring(0, 20) : value);
                    }
                    else
                    {
                        Serilog.Log.Information("ApiClientService.AuthToken: Token cleared (set to null)");
                    }
                    _authToken = value;
                    OnAuthTokenChanged?.Invoke(this, value);
                }
                else
                {
                    Serilog.Log.Debug("ApiClientService.AuthToken: Token value unchanged, skipping event");
                }
            }
        }

        public bool? IsAdmin
        {
            get => _isAdmin;
            set
            {
                _isAdmin = value;
                OnAdminStatusChanged?.Invoke(this, value);
            }
        }

        public int? UserRole
        {
            get => _userRole;
            set
            {
                _userRole = value;
                OnUserRoleChanged?.Invoke(this, value);
                // Update role name when role changes
                RoleName = GetRussianRoleName(value);
            }
        }

        public string? RoleName
        {
            get => _roleName;
            private set
            {
                _roleName = value;
                OnRoleNameChanged?.Invoke(this, value);
            }
        }

        public event EventHandler<string?> OnAuthTokenChanged;
        public event EventHandler<bool?> OnAdminStatusChanged;
        public event EventHandler<int?> OnUserRoleChanged;
        public event EventHandler<string?> OnRoleNameChanged;

        public HttpClient CreateClient()
        {
            var client = new HttpClient
            {
                BaseAddress = new Uri("http://localhost:5000/api/")
            };

            if (!string.IsNullOrEmpty(_authToken))
            {
                client.DefaultRequestHeaders.Authorization = 
                    new AuthenticationHeaderValue("Bearer", _authToken);
                Serilog.Log.Debug("ApiClientService.CreateClient: Authorization header added with token (length: {Length})", _authToken.Length);
                Serilog.Log.Debug("ApiClientService.CreateClient: Token preview: {TokenPreview}...", 
                    _authToken.Length > 20 ? _authToken.Substring(0, 20) : _authToken);
            }
            else
            {
                Serilog.Log.Warning("ApiClientService.CreateClient: No auth token available, Authorization header NOT added");
                Serilog.Log.Warning("ApiClientService.CreateClient: _authToken is null or empty. Current value: {TokenValue}", 
                    _authToken ?? "(null)");
            }

            return client;
        }

        private string? GetRussianRoleName(int? role)
        {
            return role switch
            {
                0 => "Пользователь",
                1 => "Администратор",
                2 => "Менеджер",
                3 => "Диспетчер",
                4 => "Кассир",
                5 => "Водитель",
                6 => "Кондуктор",
                7 => "Механик",
                8 => "Инженер",
                9 => "Контролер",
                10 => "Инспектор",
                _ => null
            };
        }
    }
} 