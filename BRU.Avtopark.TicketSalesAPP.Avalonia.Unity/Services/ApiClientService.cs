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
                _authToken = value;
                OnAuthTokenChanged?.Invoke(this, value);
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