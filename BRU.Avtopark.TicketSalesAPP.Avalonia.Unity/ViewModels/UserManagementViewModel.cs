using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Windows.Input;
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
using Microsoft.Extensions.Logging;
using Serilog;

using System.Collections.Generic;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using MsBox.Avalonia.Enums;
using MsBox.Avalonia;
using Avalonia.Data;
using Avalonia.Markup.Xaml.Templates;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Helpers;
using SpacetimeDB.Types;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Globalization;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels
{
    public partial class UserDisplayModel : ObservableObject
    {
        [ObservableProperty]
        private UserProfile _user;

        public UserDisplayModel(UserProfile user)
        {
            _user = user;
            Log.Debug("UserDisplayModel created for user: LegacyId={LegacyId}, Login={Login}", user.LegacyUserId, user.Login);
        }

        public uint LegacyUserId
        {
            get
            {
                Log.Verbose("UserDisplayModel.LegacyUserId accessed: {Value}", User.LegacyUserId);
                return User.LegacyUserId;
            }
        }

        public string Login
        {
            get
            {
                Log.Verbose("UserDisplayModel.Login accessed: {Value}", User.Login);
                return User.Login;
            }
        }

        public string? Email
        {
            get
            {
                Log.Verbose("UserDisplayModel.Email accessed: {Value}", User.Email);
                return User.Email;
            }
        }

        public string? PhoneNumber
        {
            get
            {
                Log.Verbose("UserDisplayModel.PhoneNumber accessed: {Value}", User.PhoneNumber);
                return User.PhoneNumber;
            }
        }

        public bool IsActive
        {
            get
            {
                Log.Verbose("UserDisplayModel.IsActive accessed: {Value}", User.IsActive);
                return User.IsActive;
            }
        }

        public string CreatedAtDisplay
        {
            get
            {
                if (User.CreatedAt == 0) return "Не указано";
                try
                {
                    var date = DateTimeOffset.FromUnixTimeMilliseconds((long)User.CreatedAt);
                    var formatted = date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
                    Log.Verbose("UserDisplayModel.CreatedAtDisplay accessed: {Value}", formatted);
                    return formatted;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to format CreatedAt timestamp {Timestamp}", User.CreatedAt);
                    return "Ошибка даты";
                }
            }
        }

        public string LastLoginAtDisplay
        {
            get
            {
                if (!User.LastLoginAt.HasValue || User.LastLoginAt.Value == 0) return "Никогда";
                try
                {
                    var date = DateTimeOffset.FromUnixTimeMilliseconds((long)User.LastLoginAt.Value);
                    var formatted = date.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
                    Log.Verbose("UserDisplayModel.LastLoginAtDisplay accessed: {Value}", formatted);
                    return formatted;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to format LastLoginAt timestamp {Timestamp}", User.LastLoginAt);
                    return "Ошибка даты";
                }
            }
        }

        // Method to create UserProfile from this DisplayModel
        public UserProfile ToUserProfile()
        {
            Log.Debug("UserDisplayModel.ToUserProfile called for user: {Login}", User.Login);
            return User;
        }
    }

    public partial class RoleDisplayModel : ObservableObject
    {
        [ObservableProperty]
        private Role _role;

        public RoleDisplayModel(Role role)
        {
            _role = role;
            Log.Debug("RoleDisplayModel created for role: RoleId={RoleId}, Name={Name}", role.RoleId, role.Name);
        }

        public uint RoleId
        {
            get
            {
                Log.Verbose("RoleDisplayModel.RoleId accessed: {Value}", Role.RoleId);
                return Role.RoleId;
            }
        }

        public int LegacyRoleId
        {
            get
            {
                Log.Verbose("RoleDisplayModel.LegacyRoleId accessed: {Value}", Role.LegacyRoleId);
                return Role.LegacyRoleId;
            }
        }

        public string Name
        {
            get
            {
                Log.Verbose("RoleDisplayModel.Name accessed: {Value}", Role.Name);
                return Role.Name;
            }
        }

        public string Description
        {
            get
            {
                Log.Verbose("RoleDisplayModel.Description accessed: {Value}", Role.Description);
                return Role.Description;
            }
        }

        public bool IsSystem
        {
            get
            {
                Log.Verbose("RoleDisplayModel.IsSystem accessed: {Value}", Role.IsSystem);
                return Role.IsSystem;
            }
        }

        public uint Priority
        {
            get
            {
                Log.Verbose("RoleDisplayModel.Priority accessed: {Value}", Role.Priority);
                return Role.Priority;
            }
        }

        public bool IsActive
        {
            get
            {
                Log.Verbose("RoleDisplayModel.IsActive accessed: {Value}", Role.IsActive);
                return Role.IsActive;
            }
        }

        public ulong CreatedAt => Role.CreatedAt;
        public ulong UpdatedAt => Role.UpdatedAt;
        public string? CreatedBy => Role.CreatedBy;
        public string? UpdatedBy => Role.UpdatedBy;
        public string? NormalizedName => Role.NormalizedName;

        public string CreatedAtDisplay
        {
            get
            {
                if (Role.CreatedAt == 0) return "Не указано";
                try
                {
                    var date = DateTimeOffset.FromUnixTimeMilliseconds((long)Role.CreatedAt);
                    var formatted = date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
                    Log.Verbose("RoleDisplayModel.CreatedAtDisplay accessed: {Value}", formatted);
                    return formatted;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to format CreatedAt timestamp {Timestamp}", Role.CreatedAt);
                    return "Ошибка даты";
                }
            }
        }

        public string UpdatedAtDisplay
        {
            get
            {
                if (Role.UpdatedAt == 0) return "Не указано";
                try
                {
                    var date = DateTimeOffset.FromUnixTimeMilliseconds((long)Role.UpdatedAt);
                    var formatted = date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
                    Log.Verbose("RoleDisplayModel.UpdatedAtDisplay accessed: {Value}", formatted);
                    return formatted;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to format UpdatedAt timestamp {Timestamp}", Role.UpdatedAt);
                    return "Ошибка даты";
                }
            }
        }

        public Role ToRole()
        {
            Log.Debug("RoleDisplayModel.ToRole called for role: {Name}", Role.Name);
            return new Role
            {
                RoleId = Role.RoleId,
                LegacyRoleId = Role.LegacyRoleId,
                Name = Role.Name,
                Description = Role.Description,
                IsSystem = Role.IsSystem,
                Priority = Role.Priority,
                IsActive = Role.IsActive,
                CreatedAt = Role.CreatedAt,
                UpdatedAt = Role.UpdatedAt,
                CreatedBy = Role.CreatedBy,
                UpdatedBy = Role.UpdatedBy,
                NormalizedName = Role.NormalizedName
            };
        }
    }

    public partial class PermissionDisplayModel : ObservableObject
    {
        [ObservableProperty]
        private Permission _permission;

        public PermissionDisplayModel(Permission permission)
        {
            _permission = permission;
            Log.Debug("PermissionDisplayModel created for permission: PermissionId={PermissionId}, Name={Name}", permission.PermissionId, permission.Name);
        }

        public uint PermissionId
        {
            get
            {
                Log.Verbose("PermissionDisplayModel.PermissionId accessed: {Value}", Permission.PermissionId);
                return Permission.PermissionId;
            }
        }

        public string Name
        {
            get
            {
                Log.Verbose("PermissionDisplayModel.Name accessed: {Value}", Permission.Name);
                return Permission.Name;
            }
        }

        public string Description
        {
            get
            {
                Log.Verbose("PermissionDisplayModel.Description accessed: {Value}", Permission.Description);
                return Permission.Description;
            }
        }

        public string Category
        {
            get
            {
                Log.Verbose("PermissionDisplayModel.Category accessed: {Value}", Permission.Category);
                return Permission.Category;
            }
        }

        public bool IsActive
        {
            get
            {
                Log.Verbose("PermissionDisplayModel.IsActive accessed: {Value}", Permission.IsActive);
                return Permission.IsActive;
            }
        }

        public ulong CreatedAt => Permission.CreatedAt;

        public string CreatedAtDisplay
        {
            get
            {
                if (Permission.CreatedAt == 0) return "Не указано";
                try
                {
                    var date = DateTimeOffset.FromUnixTimeMilliseconds((long)Permission.CreatedAt);
                    var formatted = date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
                    Log.Verbose("PermissionDisplayModel.CreatedAtDisplay accessed: {Value}", formatted);
                    return formatted;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to format CreatedAt timestamp {Timestamp}", Permission.CreatedAt);
                    return "Ошибка даты";
                }
            }
        }

        public Permission ToPermission()
        {
            Log.Debug("PermissionDisplayModel.ToPermission called for permission: {Name}", Permission.Name);
            return new Permission
            {
                PermissionId = Permission.PermissionId,
                Name = Permission.Name,
                Description = Permission.Description,
                Category = Permission.Category,
                IsActive = Permission.IsActive,
                CreatedAt = Permission.CreatedAt
            };
        }
    }

    public partial class UserManagementViewModel : ReactiveObject
    {
        private HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly JsonSerializerOptions _jsonOptions;

        private List<UserDisplayModel> _allUsers = new();
        private ObservableCollection<UserDisplayModel> _users = new();
        public ObservableCollection<UserDisplayModel> Users
        {
            get => _users;
            set => this.RaiseAndSetIfChanged(ref _users, value);
        }

        private List<RoleDisplayModel> _allRoles = new();
        private ObservableCollection<RoleDisplayModel> _roles = new();
        public ObservableCollection<RoleDisplayModel> Roles
        {
            get => _roles;
            set => this.RaiseAndSetIfChanged(ref _roles, value);
        }

        private List<PermissionDisplayModel> _allPermissions = new();
        private ObservableCollection<PermissionDisplayModel> _permissions = new();
        public ObservableCollection<PermissionDisplayModel> Permissions
        {
            get => _permissions;
            set => this.RaiseAndSetIfChanged(ref _permissions, value);
        }

        private UserDisplayModel? _selectedUser;
        public UserDisplayModel? SelectedUser
        {
            get => _selectedUser;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedUser, value);
                LoadUserRolesAndPermissions().ConfigureAwait(false);
            }
        }

        private RoleDisplayModel? _selectedRole;
        public RoleDisplayModel? SelectedRole
        {
            get => _selectedRole;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedRole, value);
                LoadRolePermissions().ConfigureAwait(false);
            }
        }

        private ObservableCollection<PermissionDisplayModel> _selectedUserPermissions = new();
        public ObservableCollection<PermissionDisplayModel> SelectedUserPermissions
        {
            get => _selectedUserPermissions;
            set => this.RaiseAndSetIfChanged(ref _selectedUserPermissions, value);
        }

        private ObservableCollection<PermissionDisplayModel> _selectedRolePermissions = new();
        public ObservableCollection<PermissionDisplayModel> SelectedRolePermissions
        {
            get => _selectedRolePermissions;
            set => this.RaiseAndSetIfChanged(ref _selectedRolePermissions, value);
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

        public UserManagementViewModel()
        {
            _httpClient = ApiClientService.Instance.CreateClient();
            _baseUrl = ApiClientService.Instance.CurrentBaseUrl?.TrimEnd('/') ?? "http://localhost:5000/api";
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            };

            ApiClientService.Instance.OnAuthTokenChanged += (sender, token) =>
            {
                Log.Information("Auth token changed in UserManagementViewModel. Recreating HttpClient and reloading data.");
                _httpClient.Dispose();
                _httpClient = ApiClientService.Instance.CreateClient();
                LoadData().ConfigureAwait(false);
            };

            // Only load data if token is already set, otherwise wait for OnAuthTokenChanged event
            if (!string.IsNullOrEmpty(ApiClientService.Instance.AuthToken))
            {
                Log.Information("Token already set in UserManagementViewModel constructor, loading data");
                LoadData().ConfigureAwait(false);
            }
            else
            {
                Log.Warning("Token not set in UserManagementViewModel constructor, waiting for OnAuthTokenChanged event");
            }
        }

        [RelayCommand]
        private async Task LoadData()
        {
            Log.Information("Starting LoadData for UserManagementViewModel");
            try
            {
                IsBusy = true;
                HasError = false;
                ErrorMessage = string.Empty;

                Log.Debug("Initiating API calls for Users, Roles, Permissions");
                Task<HttpResponseMessage> usersTask = _httpClient.GetAsync($"{_baseUrl}/Users");
                Task<HttpResponseMessage> rolesTask = _httpClient.GetAsync($"{_baseUrl}/Roles");
                Task<HttpResponseMessage> permissionsTask = _httpClient.GetAsync($"{_baseUrl}/Permissions");

                await Task.WhenAll(usersTask, rolesTask, permissionsTask);
                Log.Debug("All API calls completed for UserManagementViewModel.");

                // --- 1. Process Users Response ---
                List<UserDisplayModel> loadedUsers = new();
                var usersResponse = await usersTask;
                Log.Information("Processing Users response. Status: {StatusCode}", usersResponse.StatusCode);
                var usersJsonString = await usersResponse.Content.ReadAsStringAsync();
                Log.Verbose("Raw Users response received: {RawResponse}", usersJsonString);

                if (usersResponse.IsSuccessStatusCode)
                {
                    try
                    {
                        var usersArray = JsonReferenceHelper.ParseArrayWithReferences(usersJsonString, "User");
                        if (usersArray != null)
                        {
                            Log.Information("Parsing {Count} user objects from JSON array.", usersArray.Count);
                            foreach (var userNode in usersArray)
                            {
                                if (userNode is JsonObject userObj)
                                {
                                    Log.Verbose("--- Parsing User Object: {UserJson} ---", userObj.ToJsonString());

                                    var user = userObj.ParseUserProfile();
                                    if (user == null)
                                    {
                                        Log.Warning("Failed to parse user object, skipping");
                                        continue;
                                    }

                                    loadedUsers.Add(new UserDisplayModel(user));
                                    Log.Verbose("Parsed User: LegacyId={LegacyId}, Login='{Login}', Active={IsActive}",
                                        user.LegacyUserId, user.Login, user.IsActive);
                                }
                                else
                                {
                                    Log.Warning("Item in users array was not a JSON object: {Node}", userNode?.ToJsonString());
                                }
                            }
                            Log.Information("Successfully parsed {Count} valid users.", loadedUsers.Count);
                        }
                        else
                        {
                            Log.Error("Users JSON could not be parsed as array. Raw JSON: {RawJson}", usersJsonString);
                        }
                    }
                    catch (JsonException jsonEx)
                    {
                        Log.Error(jsonEx, "Failed to parse Users JSON: {RawJson}", usersJsonString);
                        throw new Exception("Failed to parse user data.", jsonEx);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Unexpected error during manual user parsing.");
                        throw;
                    }
                }
                else
                {
                    var error = await usersResponse.Content.ReadAsStringAsync();
                    Log.Error("Failed to load users. Status: {StatusCode}, Error: {Error}", usersResponse.StatusCode, error);
                    throw new Exception($"Failed to load primary user data. Status: {usersResponse.StatusCode}");
                }

                // --- 2. Process Roles Response ---
                var rolesResponse = await rolesTask;
                Log.Information("Processing Roles response. Status: {StatusCode}", rolesResponse.StatusCode);
                var rolesJsonString = await rolesResponse.Content.ReadAsStringAsync();
                Log.Verbose("Raw Roles response received: {RawResponse}", rolesJsonString);
                List<RoleDisplayModel> loadedRoles = new();
                if (rolesResponse.IsSuccessStatusCode)
                {
                    try
                    {
                        var rolesArray = JsonReferenceHelper.ParseArrayWithReferences(rolesJsonString, "Role");
                        if (rolesArray != null)
                        {
                            Log.Information("Parsing {Count} role objects from JSON array.", rolesArray.Count);
                            foreach (var roleNode in rolesArray)
                            {
                                if (roleNode is JsonObject roleObj)
                                {
                                    Log.Verbose("--- Parsing Role Object: {RoleJson} ---", roleObj.ToJsonString());
                                    
                                    var role = roleObj.ParseRole();
                                    if (role == null)
                                    {
                                        Log.Warning("Failed to parse role object, skipping");
                                        continue;
                                    }

                                    loadedRoles.Add(new RoleDisplayModel(role));
                                    Log.Verbose("Parsed Role: Id={RoleId}, Name='{Name}', LegacyId={LegacyId}, IsActive={IsActive}",
                                        role.RoleId, role.Name, role.LegacyRoleId, role.IsActive);
                                }
                                else
                                {
                                    Log.Warning("Item in roles array was not a JSON object: {Node}", roleNode?.ToJsonString());
                                }
                            }
                            Log.Information("Successfully parsed {Count} valid roles.", loadedRoles.Count);
                        }
                        else
                        {
                            Log.Error("Roles JSON could not be parsed as array. Raw JSON: {RawJson}", rolesJsonString);
                        }
                    }
                    catch (JsonException jsonEx)
                    {
                        Log.Error(jsonEx, "Failed to parse Roles JSON: {RawJson}", rolesJsonString);
                        HasError = true;
                        ErrorMessage = "Ошибка загрузки списка ролей.";
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Unexpected error during manual role parsing.");
                         HasError = true;
                         ErrorMessage = "Непредвиденная ошибка загрузки списка ролей.";
                    }
                }
                else
                {
                    var error = await rolesResponse.Content.ReadAsStringAsync();
                    Log.Warning("Failed to load roles. Status: {StatusCode}, Error: {Error}. Role list may be inaccurate.", rolesResponse.StatusCode, error);
                     HasError = true;
                     ErrorMessage = $"Ошибка загрузки ролей: {rolesResponse.StatusCode}";
                }

                // --- 3. Process Permissions Response ---
                var permissionsResponse = await permissionsTask;
                Log.Information("Processing Permissions response. Status: {StatusCode}", permissionsResponse.StatusCode);
                var permissionsJsonString = await permissionsResponse.Content.ReadAsStringAsync();
                Log.Verbose("Raw Permissions response received: {RawResponse}", permissionsJsonString);
                List<PermissionDisplayModel> loadedPermissions = new();
                if (permissionsResponse.IsSuccessStatusCode)
                {
                    try
                    {
                        var permissionsArray = JsonReferenceHelper.ParseArrayWithReferences(permissionsJsonString, "Permission");
                        if (permissionsArray != null)
                        {
                            Log.Information("Parsing {Count} permission objects from JSON array.", permissionsArray.Count);
                            foreach (var permNode in permissionsArray)
                            {
                                if (permNode is JsonObject permObj)
                                {
                                    Log.Verbose("--- Parsing Permission Object: {PermissionJson} ---", permObj.ToJsonString());
                                    
                                    var permission = permObj.ParsePermission();
                                    if (permission == null)
                                    {
                                        Log.Warning("Failed to parse permission object, skipping");
                                        continue;
                                    }

                                    loadedPermissions.Add(new PermissionDisplayModel(permission));
                                    Log.Verbose("Parsed Permission: Id={PermId}, Name='{Name}', Category='{Category}', IsActive={IsActive}",
                                        permission.PermissionId, permission.Name, permission.Category, permission.IsActive);
                                }
                                else
                                {
                                    Log.Warning("Item in permissions array was not a JSON object: {Node}", permNode?.ToJsonString());
                                }
                            }
                            Log.Information("Successfully parsed {Count} valid permissions.", loadedPermissions.Count);
                        }
                        else
                        {
                            Log.Error("Permissions JSON could not be parsed as array. Raw JSON: {RawJson}", permissionsJsonString);
                        }
                    }
                    catch (JsonException jsonEx)
                    {
                        Log.Error(jsonEx, "Failed to parse Permissions JSON: {RawJson}", permissionsJsonString);
                         HasError = true;
                         ErrorMessage = "Ошибка загрузки списка разрешений.";
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Unexpected error during manual permission parsing.");
                         HasError = true;
                         ErrorMessage = "Непредвиденная ошибка загрузки списка разрешений.";
                    }
                }
                else
                {
                    var error = await permissionsResponse.Content.ReadAsStringAsync();
                    Log.Warning("Failed to load permissions. Status: {StatusCode}, Error: {Error}. Permission list may be inaccurate.", permissionsResponse.StatusCode, error);
                     HasError = true;
                     ErrorMessage = $"Ошибка загрузки разрешений: {permissionsResponse.StatusCode}";
                }

                // Update ObservableCollections
                _allUsers = loadedUsers;
                Users = new ObservableCollection<UserDisplayModel>(_allUsers);
                _allRoles = loadedRoles;
                Roles = new ObservableCollection<RoleDisplayModel>(_allRoles);
                _allPermissions = loadedPermissions;
                Permissions = new ObservableCollection<PermissionDisplayModel>(_allPermissions);

                Log.Information("Finished processing data. Displaying {UserCount} users, {RoleCount} roles, {PermissionCount} permissions.", Users.Count, Roles.Count, Permissions.Count);
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = $"Критическая ошибка загрузки данных: {ex.Message}";
                Log.Fatal(ex, "Fatal error loading data in UserManagementViewModel");
                // Clear collections on fatal error
                _allUsers = new List<UserDisplayModel>();
                Users = new ObservableCollection<UserDisplayModel>();
                _allRoles = new List<RoleDisplayModel>();
                Roles = new ObservableCollection<RoleDisplayModel>();
                _allPermissions = new List<PermissionDisplayModel>();
                Permissions = new ObservableCollection<PermissionDisplayModel>();
                SelectedUserPermissions = new ObservableCollection<PermissionDisplayModel>();
                SelectedRolePermissions = new ObservableCollection<PermissionDisplayModel>();
            }
            finally
            {
                IsBusy = false;
                Log.Information("LoadData finished for UserManagementViewModel.");
            }
        }

        private async Task LoadUserRolesAndPermissions()
        {
            if (SelectedUser == null)
            {
                 Log.Debug("SelectedUser is null, clearing user-specific roles/permissions.");
                 SelectedUserPermissions = new ObservableCollection<PermissionDisplayModel>();
                return;
            }

             Log.Information("Loading roles and permissions for selected user: {UserId} ({Login})", SelectedUser.LegacyUserId, SelectedUser.Login);
            IsBusy = true;

            try
            {
                 Log.Debug("Initiating API calls for user roles and permissions for User {UserId}", SelectedUser.LegacyUserId);
                Task<HttpResponseMessage> userPermissionsTask = _httpClient.GetAsync($"{_baseUrl}/Users/{SelectedUser.LegacyUserId}/permissions");
                Task<HttpResponseMessage> userRolesTask = _httpClient.GetAsync($"{_baseUrl}/Users/{SelectedUser.LegacyUserId}/roles");

                 await Task.WhenAll(userPermissionsTask, userRolesTask);
                 Log.Debug("User roles and permissions API calls completed for User {UserId}", SelectedUser.LegacyUserId);

                 // Process User Permissions
                 var permissionsResponse = await userPermissionsTask;
                 Log.Information("Processing User Permissions response for User {UserId}. Status: {StatusCode}", SelectedUser.LegacyUserId, permissionsResponse.StatusCode);
                 List<PermissionDisplayModel> loadedUserPermissions = new();
                if (permissionsResponse.IsSuccessStatusCode)
                {
                     var permissionsJsonString = await permissionsResponse.Content.ReadAsStringAsync();
                      Log.Verbose("Raw User Permissions response received for User {UserId}: {RawResponse}", SelectedUser.LegacyUserId, permissionsJsonString);
                     try
                     {
                         JsonNode? permissionsNode = JsonNode.Parse(permissionsJsonString);
                         if (permissionsNode is JsonObject rootObject && rootObject.TryGetPropertyValue("$values", out var permValuesNode) && permValuesNode is JsonArray permissionsArray)
                         {
                              Log.Information("Parsing {Count} permission objects for User {UserId}", permissionsArray.Count, SelectedUser.LegacyUserId);
                             foreach (var permNode in permissionsArray)
                             {
                                 if (permNode is JsonObject permObj)
                                 {
                                     uint permissionId = permObj["permissionId"]?.GetValue<uint>() ?? 0;
                                     if (permissionId > 0)
                                     {
                                          var fullPermission = _allPermissions.FirstOrDefault(p => p.PermissionId == permissionId);
                                          if (fullPermission != null)
                    {
                                               loadedUserPermissions.Add(fullPermission);
                                                Log.Verbose("Added permission {PermId} ('{PermName}') to SelectedUserPermissions", permissionId, fullPermission.Name);
                                          }
                                          else { Log.Warning("Could not find full permission object for ID {PermId} from user permissions response.", permissionId); }
                                     }
                                      else { Log.Warning("Permission object in user permissions response had ID 0."); }
                                 }
                                  else { Log.Warning("Item in user permissions array was not a JSON object: {Node}", permNode?.ToJsonString()); }
                             }
                              Log.Information("Successfully parsed {Count} permissions for User {UserId}", loadedUserPermissions.Count, SelectedUser.LegacyUserId);
                         }
                         else { Log.Error("User Permissions JSON root was not an object with a '$values' array. Raw: {RawJson}", permissionsJsonString); }
                     }
                     catch (JsonException jsonEx)
                     {
                         Log.Error(jsonEx, "Failed to parse User Permissions JSON for User {UserId}: {RawJson}", SelectedUser.LegacyUserId, permissionsJsonString);
                    }
                     catch (Exception ex) { Log.Error(ex, "Unexpected error parsing user permissions for User {UserId}", SelectedUser.LegacyUserId); }
                }
                else
                {
                     var error = await permissionsResponse.Content.ReadAsStringAsync();
                     Log.Warning("Failed to load permissions for user {UserId}. Status: {StatusCode}, Error: {Error}",
                         SelectedUser.LegacyUserId, permissionsResponse.StatusCode, error);
                }
                 SelectedUserPermissions = new ObservableCollection<PermissionDisplayModel>(loadedUserPermissions);
                 Log.Debug("Set SelectedUserPermissions count: {Count}", SelectedUserPermissions.Count);

                // Process User Roles
                 var rolesResponse = await userRolesTask;
                 Log.Information("Processing User Roles response for User {UserId}. Status: {StatusCode}", SelectedUser.LegacyUserId, rolesResponse.StatusCode);
                 List<uint> userRoleIds = new();
                if (rolesResponse.IsSuccessStatusCode)
                {
                     var rolesJsonString = await rolesResponse.Content.ReadAsStringAsync();
                      Log.Verbose("Raw User Roles response received for User {UserId}: {RawResponse}", SelectedUser.LegacyUserId, rolesJsonString);
                     try
                     {
                         JsonNode? rolesNode = JsonNode.Parse(rolesJsonString);
                         if (rolesNode is JsonObject rootObject && rootObject.TryGetPropertyValue("$values", out var roleValuesNode) && roleValuesNode is JsonArray rolesArray)
                    {
                              Log.Information("Parsing {Count} role objects for User {UserId}", rolesArray.Count, SelectedUser.LegacyUserId);
                             foreach (var roleNode in rolesArray)
                             {
                                 if (roleNode is JsonObject roleObj)
                                 {
                                     uint roleId = roleObj["roleId"]?.GetValue<uint>() ?? 0;
                                     if (roleId > 0)
                                     {
                                         userRoleIds.Add(roleId);
                                          Log.Verbose("Found assigned RoleId {RoleId} for User {UserId}", roleId, SelectedUser.LegacyUserId);
                                     }
                                      else { Log.Warning("Role object in user roles response had ID 0."); }
                                 }
                                  else { Log.Warning("Item in user roles array was not a JSON object: {Node}", roleNode?.ToJsonString()); }
                             }
                              Log.Information("Successfully parsed {Count} role IDs for User {UserId}", userRoleIds.Count, SelectedUser.LegacyUserId);
                         }
                         else { Log.Error("User Roles JSON root was not an object with a '$values' array. Raw: {RawJson}", rolesJsonString); }
                     }
                     catch (JsonException jsonEx)
                     {
                         Log.Error(jsonEx, "Failed to parse User Roles JSON for User {UserId}: {RawJson}", SelectedUser.LegacyUserId, rolesJsonString);
                     }
                     catch (Exception ex) { Log.Error(ex, "Unexpected error parsing user roles for User {UserId}", SelectedUser.LegacyUserId); }
                }
                else
                {
                     var error = await rolesResponse.Content.ReadAsStringAsync();
                     Log.Warning("Failed to load roles for user {UserId}. Status: {StatusCode}, Error: {Error}",
                         SelectedUser.LegacyUserId, rolesResponse.StatusCode, error);
                }

                 // Update the Roles collection IsActive state based on userRoleIds
                 foreach (var roleDisplay in _allRoles)
                 {
                      roleDisplay.Role.IsActive = userRoleIds.Contains(roleDisplay.RoleId);
                 }
                 var currentRoles = Roles.ToList();
                 Roles = new ObservableCollection<RoleDisplayModel>(currentRoles);

                 Log.Information("Finished loading roles and permissions for User {UserId}. Assigned Roles Count: {RoleCount}, Permissions Count: {PermCount}", SelectedUser.LegacyUserId, userRoleIds.Count, SelectedUserPermissions.Count);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error loading user roles and permissions for user {UserId}", SelectedUser.LegacyUserId);
                HasError = true;
                ErrorMessage = $"Ошибка загрузки ролей/разрешений пользователя: {ex.Message}";
                SelectedUserPermissions = new ObservableCollection<PermissionDisplayModel>();
            }
            finally
            {
                 IsBusy = false;
                  Log.Debug("LoadUserRolesAndPermissions finished for User {UserId}", SelectedUser?.LegacyUserId ?? 0);
            }
        }

        private async Task LoadRolePermissions()
        {
            if (SelectedRole == null)
            {
                 Log.Debug("SelectedRole is null, clearing role-specific permissions.");
                 SelectedRolePermissions = new ObservableCollection<PermissionDisplayModel>();
                return;
            }

             Log.Information("Loading permissions for selected role: {RoleId} ({RoleName})", SelectedRole.RoleId, SelectedRole.Name);
             IsBusy = true;

            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/Roles/{SelectedRole.RoleId}/permissions");
                Log.Information("Processing Role Permissions response for Role {RoleId}. Status: {StatusCode}", SelectedRole.RoleId, response.StatusCode);
                List<PermissionDisplayModel> loadedRolePermissions = new();
                if (response.IsSuccessStatusCode)
                {
                    var jsonString = await response.Content.ReadAsStringAsync();
                     Log.Verbose("Raw Role Permissions response received for Role {RoleId}: {RawResponse}", SelectedRole.RoleId, jsonString);
                     try
                     {
                         JsonNode? permissionsNode = JsonNode.Parse(jsonString);
                         if (permissionsNode is JsonObject rootObject && rootObject.TryGetPropertyValue("$values", out var permValuesNode) && permValuesNode is JsonArray permissionsArray)
                         {
                              Log.Information("Parsing {Count} permission objects for Role {RoleId}", permissionsArray.Count, SelectedRole.RoleId);
                             foreach (var permNode in permissionsArray)
                             {
                                 if (permNode is JsonObject permObj)
                                 {
                                     uint permissionId = permObj["permissionId"]?.GetValue<uint>() ?? 0;
                                     if (permissionId > 0)
                                     {
                                          var fullPermission = _allPermissions.FirstOrDefault(p => p.PermissionId == permissionId);
                                          if (fullPermission != null)
                    {
                                               loadedRolePermissions.Add(fullPermission);
                                               Log.Verbose("Added permission {PermId} ('{PermName}') to SelectedRolePermissions", permissionId, fullPermission.Name);
                                          }
                                          else { Log.Warning("Could not find full permission object for ID {PermId} from role permissions response.", permissionId); }
                                     }
                                      else { Log.Warning("Permission object in role permissions response had ID 0."); }
                                 }
                                  else { Log.Warning("Item in role permissions array was not a JSON object: {Node}", permNode?.ToJsonString()); }
                             }
                              Log.Information("Successfully parsed {Count} permissions for Role {RoleId}", loadedRolePermissions.Count, SelectedRole.RoleId);
                         }
                         else { Log.Error("Role Permissions JSON root was not an object with a '$values' array. Raw: {RawJson}", jsonString); }
                     }
                     catch (JsonException jsonEx)
                     {
                         Log.Error(jsonEx, "Failed to parse Role Permissions JSON for Role {RoleId}: {RawJson}", SelectedRole.RoleId, jsonString);
                     }
                     catch (Exception ex) { Log.Error(ex, "Unexpected error parsing role permissions for Role {RoleId}", SelectedRole.RoleId); }
                }
                else
                {
                     var error = await response.Content.ReadAsStringAsync();
                    Log.Warning("Failed to load permissions for role {RoleId}. Status: {StatusCode}, Error: {Error}",
                        SelectedRole.RoleId, response.StatusCode, error);
                }
                SelectedRolePermissions = new ObservableCollection<PermissionDisplayModel>(loadedRolePermissions);
                 Log.Debug("Set SelectedRolePermissions count: {Count}", SelectedRolePermissions.Count);

            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error loading role permissions for role {RoleId}", SelectedRole.RoleId);
                HasError = true;
                ErrorMessage = $"Ошибка загрузки разрешений роли: {ex.Message}";
                 SelectedRolePermissions = new ObservableCollection<PermissionDisplayModel>();
            }
             finally
             {
                 IsBusy = false;
                  Log.Debug("LoadRolePermissions finished for Role {RoleId}", SelectedRole?.RoleId ?? 0);
            }
        }

        [RelayCommand]
        private async Task AssignRole(RoleDisplayModel roleDisplay)
        {
            if (SelectedUser == null || roleDisplay == null) return;

            var role = roleDisplay.Role;

            try
            {
                var model = new { RoleId = role.RoleId };
                var json = JsonSerializer.Serialize(model);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(
                    $"{_baseUrl}/Users/{SelectedUser.LegacyUserId}/roles", 
                    content);

                if (response.IsSuccessStatusCode)
                {
                    await LoadUserRolesAndPermissions();
                    Log.Information("Successfully assigned role {RoleId} to user {UserId}", 
                        role.RoleId, SelectedUser.LegacyUserId);
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    Log.Warning("Failed to assign role {RoleId} to user {UserId}. Error: {Error}", 
                        role.RoleId, SelectedUser.LegacyUserId, error);
                    HasError = true;
                    ErrorMessage = $"Failed to assign role: {error}";

                    var errorDialog = MessageBoxManager
                        .GetMessageBoxStandard(
                            "Ошибка",
                            $"Не удалось назначить роль: {error}",
                            ButtonEnum.Ok,
                            Icon.Error);

                    var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                        ? desktop.MainWindow
                        : null;

                    if (mainWindow != null)
                    {
                        await errorDialog.ShowAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error assigning role {RoleId} to user {UserId}", 
                    role.RoleId, SelectedUser.LegacyUserId);
                HasError = true;
                ErrorMessage = $"Error assigning role: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task RemoveRole(RoleDisplayModel roleDisplay)
        {
            if (SelectedUser == null || roleDisplay == null) return;

            var role = roleDisplay.Role;

            try
            {
                var response = await _httpClient.DeleteAsync(
                    $"{_baseUrl}/Users/{SelectedUser.LegacyUserId}/roles/{role.RoleId}");

                if (response.IsSuccessStatusCode)
                {
                    await LoadUserRolesAndPermissions();
                    Log.Information("Successfully removed role {RoleId} from user {UserId}", 
                        role.RoleId, SelectedUser.LegacyUserId);
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    Log.Warning("Failed to remove role {RoleId} from user {UserId}. Error: {Error}", 
                        role.RoleId, SelectedUser.LegacyUserId, error);
                    HasError = true;
                    ErrorMessage = $"Failed to remove role: {error}";

                    var errorDialog = MessageBoxManager
                        .GetMessageBoxStandard(
                            "Ошибка",
                            $"Не удалось удалить роль: {error}",
                            ButtonEnum.Ok,
                            Icon.Error);

                    var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                        ? desktop.MainWindow
                        : null;

                    if (mainWindow != null)
                    {
                        await errorDialog.ShowAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error removing role {RoleId} from user {UserId}", 
                    role.RoleId, SelectedUser.LegacyUserId);
                HasError = true;
                ErrorMessage = $"Error removing role: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task Add()
        {
            try
            {
                var dialog = new Window
                {
                    Title = "Add User",
                    Width = 400,
                    Height = 350,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                var grid = new Grid
                {
                    RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto"),
                    Margin = new Thickness(10)
                };

                // Login field
                var loginLabel = new TextBlock { Text = "Login:", Margin = new Thickness(0, 0, 0, 5) };
                var loginBox = new TextBox { PlaceholderText = "Enter login" };
                grid.Children.Add(loginLabel);
                Grid.SetRow(loginLabel, 0);
                grid.Children.Add(loginBox);
                Grid.SetRow(loginBox, 1);

                // Password field
                var passwordLabel = new TextBlock { Text = "Password:", Margin = new Thickness(0, 10, 0, 5) };
                var passwordBox = new TextBox { PlaceholderText = "Enter password", PasswordChar = '*' };
                grid.Children.Add(passwordLabel);
                Grid.SetRow(passwordLabel, 2);
                grid.Children.Add(passwordBox);
                Grid.SetRow(passwordBox, 3);

                // Role selection
                var rolePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 10), Spacing = 10 };
                
                // Legacy role selection
                var legacyRolePanel = new StackPanel { Orientation = Orientation.Vertical, Width = 180 };
                var legacyRoleLabel = new TextBlock { Text = "Legacy Role:", Margin = new Thickness(0, 0, 0, 5) };
                var legacyRoleComboBox = new ComboBox();
                var legacyRoles = new[] { "User", "Admin" };
                foreach (var role in legacyRoles)
                {
                    legacyRoleComboBox.Items.Add(role);
                }
                legacyRoleComboBox.SelectedIndex = 0;
                legacyRolePanel.Children.Add(legacyRoleLabel);
                legacyRolePanel.Children.Add(legacyRoleComboBox);

                // New role selection
                var newRolePanel = new StackPanel { Orientation = Orientation.Vertical, Width = 180 };
                var newRoleLabel = new TextBlock { Text = "New Role:", Margin = new Thickness(0, 0, 0, 5) };
                var newRoleComboBox = new ComboBox 
                { 
                    MinWidth = 150,
                    MaxDropDownHeight = 300,
                    ItemsSource = Roles,
                    DisplayMemberBinding = new Binding("Name")
                };

                newRoleComboBox.SelectedIndex = 0;
                newRolePanel.Children.Add(newRoleLabel);
                newRolePanel.Children.Add(newRoleComboBox);

                rolePanel.Children.Add(legacyRolePanel);
                rolePanel.Children.Add(newRolePanel);
                grid.Children.Add(rolePanel);
                Grid.SetRow(rolePanel, 4);

                // Add button
                var addButton = new Button
                {
                    Content = "Add User",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 10, 0, 0)
                };
                grid.Children.Add(addButton);
                Grid.SetRow(addButton, 5);

                dialog.Content = grid;

                addButton.Click += async (s, e) =>
                {
                    if (string.IsNullOrWhiteSpace(loginBox.Text) || string.IsNullOrWhiteSpace(passwordBox.Text))
                    {
                        ErrorMessage = "Login and password are required";
                        return;
                    }

                    var selectedRole = newRoleComboBox.SelectedItem as Role;
                    if (selectedRole == null)
                    {
                        ErrorMessage = "Please select a role";
                        return;
                    }

                    var newUser = new
                    {
                        Login = loginBox.Text,
                        Password = passwordBox.Text,
                        Role = selectedRole.LegacyRoleId,
                    };

                    var json = JsonSerializer.Serialize(newUser, _jsonOptions);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var response = await _httpClient.PostAsync($"{_baseUrl}/Users", content);
                    if (response.IsSuccessStatusCode)
                    {
                        await LoadData();
                        dialog.Close();
                    }
                    else
                    {
                        var error = await response.Content.ReadAsStringAsync();
                        ErrorMessage = $"Failed to add user: {error}";

                        var errorDialog = MessageBoxManager
                            .GetMessageBoxStandard(
                                "Ошибка",
                                $"Не удалось создать пользователя: {error}",
                                ButtonEnum.Ok,
                                Icon.Error);

                        var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                            ? desktop.MainWindow
                            : null;

                        if (mainWindow != null)
                        {
                            await errorDialog.ShowAsync();
                        }
                    }
                };

                // Get the main window as owner
                var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null;

                if (mainWindow != null)
                {
                    await dialog.ShowDialog(mainWindow);
                }
                else
                {
                    Log.Error("Could not find main window for dialog");
                }
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = $"Error adding user: {ex.Message}";
                Log.Error(ex, "Error adding user");
            }
        }

        [RelayCommand]
        private async Task Edit()
        {
            if (SelectedUser == null) return;

            try
            {
                var dialog = new Window
                {
                    Title = "Редактировать пользователя",
                    Width = 400,
                    Height = 400,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                var grid = new Grid
                {
                    RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto"),
                    Margin = new Thickness(10),
                    ColumnDefinitions = new ColumnDefinitions("Auto,*")
                };

                // Login
                grid.Children.Add(new TextBlock { Text = "Логин:", Margin = new Thickness(0, 5) });
                var loginBox = new TextBox { Text = SelectedUser.Login };
                Grid.SetRow(loginBox, 0); Grid.SetColumn(loginBox, 1);
                grid.Children.Add(loginBox);

                // Password
                grid.Children.Add(new TextBlock { Text = "Новый пароль:", Margin = new Thickness(0, 5) });
                var passwordBox = new TextBox { PlaceholderText = "Оставьте пустым, чтобы не менять", PasswordChar = '*' };
                Grid.SetRow(passwordBox, 1); Grid.SetColumn(passwordBox, 1);
                grid.Children.Add(passwordBox);

                // Legacy Role (Keep for now if API still uses it, otherwise remove)
                grid.Children.Add(new TextBlock { Text = "Legacy Role:", Margin = new Thickness(0, 5) });
                var legacyRoleComboBox = new ComboBox();
                var legacyRoles = new[] { "User", "Admin" };
                //legacyRoleComboBox.Items = legacyRoles;
                //legacyRoleComboBox.SelectedIndex = SelectedUser.Role;
                Grid.SetRow(legacyRoleComboBox, 2); Grid.SetColumn(legacyRoleComboBox, 1);
                grid.Children.Add(legacyRoleComboBox);

                // New Role
                grid.Children.Add(new TextBlock { Text = "Роль:", Margin = new Thickness(0, 5) });
                var newRoleComboBox = new ComboBox
                {
                    ItemsSource = Roles,
                    DisplayMemberBinding = new Binding("Name"),
                    //SelectedItem = Roles.FirstOrDefault(r => r.RoleId == SelectedUser.RoleId)
                };
                Grid.SetRow(newRoleComboBox, 3); Grid.SetColumn(newRoleComboBox, 1);
                grid.Children.Add(newRoleComboBox);

                // Email
                grid.Children.Add(new TextBlock { Text = "Email:", Margin = new Thickness(0, 5) });
                var emailBox = new TextBox { Text = SelectedUser.Email ?? "" };
                Grid.SetRow(emailBox, 4); Grid.SetColumn(emailBox, 1);
                grid.Children.Add(emailBox);

                // Phone Number
                grid.Children.Add(new TextBlock { Text = "Телефон:", Margin = new Thickness(0, 5) });
                var phoneBox = new TextBox { Text = SelectedUser.PhoneNumber ?? "" };
                Grid.SetRow(phoneBox, 5); Grid.SetColumn(phoneBox, 1);
                grid.Children.Add(phoneBox);

                // Is Active
                grid.Children.Add(new TextBlock { Text = "Активен:", Margin = new Thickness(0, 5) });
                var isActiveCheckBox = new CheckBox { IsChecked = SelectedUser.IsActive };
                Grid.SetRow(isActiveCheckBox, 6); Grid.SetColumn(isActiveCheckBox, 1);
                grid.Children.Add(isActiveCheckBox);

                var updateButton = new Button
                {
                    Content = "Обновить",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 15, 0, 0)
                };
                 Grid.SetRow(updateButton, 8); Grid.SetColumnSpan(updateButton, 2);
                grid.Children.Add(updateButton);

                dialog.Content = grid;

                updateButton.Click += async (s, e) =>
                {
                    if (string.IsNullOrWhiteSpace(loginBox.Text))
                    {
                        var errorDialog = MessageBoxManager
                            .GetMessageBoxStandard(
                                "Ошибка",
                                "Логин обязателен",
                                ButtonEnum.Ok,
                                Icon.Error);

                        // Get the main window for error dialog
                        var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime
                            ? lifetime.MainWindow
                            : null;

                        if (mainWindow != null)
                        {
                            await errorDialog.ShowAsync();
                        }
                        return;
                    }

                    var selectedRole = newRoleComboBox.SelectedItem as Role;
                    if (selectedRole == null)
                    {
                        // Handle error: Role must be selected
                        return;
                    }

                    var updateUser = new
                    {
                        Login = loginBox.Text,
                        Password = string.IsNullOrWhiteSpace(passwordBox.Text) ? null : passwordBox.Text,
                        Role = selectedRole.LegacyRoleId,
                        Email = string.IsNullOrWhiteSpace(emailBox.Text) ? null : emailBox.Text,
                        PhoneNumber = string.IsNullOrWhiteSpace(phoneBox.Text) ? null : phoneBox.Text,
                        IsActive = isActiveCheckBox.IsChecked ?? false
                    };

                    var json = JsonSerializer.Serialize(updateUser, _jsonOptions);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var response = await _httpClient.PutAsync($"{_baseUrl}/Users/{SelectedUser.LegacyUserId}", content);
                    if (response.IsSuccessStatusCode)
                    {
                        await LoadData();
                        dialog.Close();
                    }
                    else
                    {
                        var error = await response.Content.ReadAsStringAsync();
                        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                        {
                            var errorDialog = MessageBoxManager
                                .GetMessageBoxStandard(
                                    "Ошибка",
                                    error,
                                    ButtonEnum.Ok,
                                    Icon.Error);

                            // Get the main window for error dialog
                            var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime app
                                ? app.MainWindow
                                : null;

                            if (mainWindow != null)
                            {
                                await errorDialog.ShowAsync();
                            }
                        }
                        else
                        {
                            ErrorMessage = $"Failed to update user: {error}";
                        }
                    }
                };

                // Get the main window as owner for edit dialog
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
                ErrorMessage = $"Error updating user: {ex.Message}";
                Log.Error(ex, "Error updating user");
            }
        }

        [RelayCommand]
        private async Task Delete()
        {
            if (SelectedUser == null) return;

            try
            {
                // Get current user ID from stored token
                var handler = new JwtSecurityTokenHandler();
                var token = handler.ReadJwtToken(ApiClientService.Instance.AuthToken);
                var currentUserIdStr = token.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;

                // Prevent deleting yourself
                if (SelectedUser.LegacyUserId.ToString() == currentUserIdStr)
                {
                    var errorDialog = MessageBoxManager
                        .GetMessageBoxStandard(
                            "Ошибка",
                            "Вы не можете удалить свою собственную учетную запись.",
                            ButtonEnum.Ok,
                            Icon.Error);

                    // Get the main window as owner
                    var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                        ? desktop.MainWindow
                        : null;

                    if (mainWindow != null)
                    {
                        await errorDialog.ShowAsync();
                    }
                    return;
                }

                var dialog = new Window
                {
                    Title = "Подтверждение удаления",
                    Width = 400,
                    Height = 200,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                var grid = new Grid
                {
                    RowDefinitions = new RowDefinitions("Auto,Auto"),
                    Margin = new Thickness(20)
                };

                var messageText = new TextBlock
                {
                    Text = $"Вы уверены, что хотите удалить пользователя {SelectedUser.Login}?",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 20)
                };

                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Spacing = 10
                };

                var yesButton = new Button { Content = "Да" };
                var noButton = new Button { Content = "Нет" };

                buttonPanel.Children.Add(yesButton);
                buttonPanel.Children.Add(noButton);

                grid.Children.Add(messageText);
                Grid.SetRow(messageText, 0);
                grid.Children.Add(buttonPanel);
                Grid.SetRow(buttonPanel, 1);

                dialog.Content = grid;

                yesButton.Click += async (s, e) =>
                {
                    var response = await _httpClient.DeleteAsync($"{_baseUrl}/Users/{SelectedUser.LegacyUserId}");
                    if (response.IsSuccessStatusCode)
                    {
                        await LoadData();
                        dialog.Close();
                    }
                    else
                    {
                        var error = await response.Content.ReadAsStringAsync();
                        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                        {
                            var errorMessageBox = MessageBoxManager
                                .GetMessageBoxStandard(
                                    "Ошибка",
                                    error,
                                    ButtonEnum.Ok,
                                    Icon.Error);

                            // Get the main window as owner for error dialog
                            var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime
                                ? lifetime.MainWindow
                                : null;

                            if (mainWindow != null)
                            {
                                await errorMessageBox.ShowAsync();
                            }
                        }
                        else
                        {
                            ErrorMessage = $"Failed to delete user: {error}";
                        }
                    }
                };

                noButton.Click += (s, e) => dialog.Close();

                // Get the main window as owner for confirmation dialog
                var ownerWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime app
                    ? app.MainWindow
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
                ErrorMessage = $"Error deleting user: {ex.Message}";
                Log.Error(ex, "Error deleting user");
            }
        }

        private void OnSearchTextChanged(string value)
        {
             Log.Debug("Search text changed: '{SearchText}'", value);
            if (string.IsNullOrWhiteSpace(value))
            {
                 Log.Debug("Search text is empty, resetting filter.");
                Users = new ObservableCollection<UserDisplayModel>(_allUsers);
                return;
            }

             var lowerCaseValue = value.ToLowerInvariant();
            var filteredUsers = _allUsers.Where(u =>
                (u.Login?.ToLowerInvariant().Contains(lowerCaseValue) ?? false) ||
                (u.LegacyUserId.ToString().Contains(lowerCaseValue)) ||
                (u.Email?.ToLowerInvariant().Contains(lowerCaseValue) ?? false) ||
                (u.PhoneNumber?.ToLowerInvariant().Contains(lowerCaseValue) ?? false)
            ).ToList();

             Log.Information("Filtering complete. Found {Count} users matching '{SearchText}'", filteredUsers.Count, value);
            Users = new ObservableCollection<UserDisplayModel>(filteredUsers);
        }
    }
}
