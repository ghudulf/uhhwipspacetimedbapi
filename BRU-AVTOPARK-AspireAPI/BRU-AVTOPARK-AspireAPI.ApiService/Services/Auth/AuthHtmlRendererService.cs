namespace BRU_AVTOPARK_AspireAPI.ApiService.Services.Auth
{
    /// <summary>
    /// Server-side HTML renderer that preserves the existing Yandex-ID-inspired
    /// BRU AVTOPARK authentication page templates. Each method returns a complete
    /// HTML document string ready to be returned via <c>Content("...", "text/html")</c>.
    ///
    /// The base template, CSS variables, dark-mode toggle script, and page-specific
    /// markup are lifted verbatim from the original monolithic AuthController so that
    /// the visual appearance is unchanged.
    /// </summary>
    public sealed class AuthHtmlRendererService : IAuthHtmlRenderer
    {
        // ────────────────────────────────────────────────────────────────
        // Base HTML shell shared by every page
        // ────────────────────────────────────────────────────────────────

        private static string WrapInBaseTemplate(string title, string bodyContent, string bodyClass = "")
        {
            return string.Format(BaseHtmlTemplate, title, bodyContent, bodyClass);
        }

        // ── IAuthHtmlRenderer implementation ────────────────────────────

        public string RenderLoginForm(string? error = null, string? message = null)
        {
            var errorHtml = error != null ? $@"<div class=""error-message"">{error}</div>" : "";
            var messageHtml = message != null ? $@"<div class=""success-message"">{message}</div>" : "";

            return WrapInBaseTemplate("Login - BRU AVTOPARK", $@"
            <div class=""login-container fade-in"">
                <div class=""auth-card"">
                    <div class=""card-body"">
                        <div class=""yandex-id-header"">
                            <div style=""display:flex;align-items:center;"">
                                <div style=""width:24px;height:24px;background-color:var(--primary-color);border-radius:4px;margin-right:8px;""></div>
                                <span style=""color:white;font-weight:500;font-size:1.5rem;"">BRU ID</span>
                            </div>
                        </div>
                        <h2 style=""text-align:center;margin-bottom:1.5rem;color:white;"">Sign in with BRU&nbsp;ID</h2>
                        {errorHtml}{messageHtml}
                        <form method=""POST"" action=""/api/auth/login"" id=""loginForm"">
                            <div class=""form-group"">
                                <label for=""username"">Username</label>
                                <input type=""text"" id=""username"" name=""username"" placeholder=""Enter your username"">
                            </div>
                            <div class=""form-group"">
                                <label for=""password"">Password</label>
                                <input type=""password"" id=""password"" name=""password"" placeholder=""Enter your password"">
                            </div>
                            <button type=""button"" onclick=""submitLoginForm()"" id=""loginButton"">Log in</button>
                            <div class=""secondary-option"" style=""margin-top:1rem;"" onclick=""window.location.href='/api/auth/webauthn/login'"">
                                <span>Login with Security Key</span>
                            </div>
                            <div class=""secondary-option"" style=""margin-top:0.5rem;"" onclick=""window.location.href='/api/auth/qr/login'"">
                                <span>Login with QR Code</span>
                            </div>
                        </form>
                        <div id=""statusDiv"" class=""mt-3""></div>
                        <div class=""divider""><span>or</span></div>
                        <div class=""social-buttons"">
                            <div class=""social-button"" title=""Phone""></div>
                            <div class=""social-button"" title=""Google""></div>
                        </div>
                    </div>
                </div>
                <div class=""auth-footer"">
                    <div style=""margin-top:2rem;display:flex;justify-content:center;"">
                        <a href=""/api/auth/register"" class=""link"" style=""color:white;margin:0 0.5rem;"">Create account</a>
                        <span style=""color:#555;"">|</span>
                        <a href=""/api/auth/magic-link"" class=""link"" style=""color:white;margin:0 0.5rem;"">Magic Link</a>
                        <span style=""color:#555;"">|</span>
                        <a href=""/api/auth/claim-account"" class=""link"" style=""color:white;margin:0 0.5rem;"">Claim Account</a>
                    </div>
                </div>
            </div>
            {LoginScript}", "auth-page-body");
        }

        public string RenderRegisterForm(string? error = null, string? message = null)
        {
            var errorHtml = error != null ? $@"<div class=""error-message"">{error}</div>" : "";
            var messageHtml = message != null ? $@"<div class=""success-message"">{message}</div>" : "";

            return WrapInBaseTemplate("Register - BRU AVTOPARK", $@"
            <div class=""login-container fade-in"">
                <div class=""auth-card"">
                    <div class=""card-body"">
                        <div class=""yandex-id-header"">
                            <div style=""display:flex;align-items:center;"">
                                <div style=""width:24px;height:24px;background-color:var(--primary-color);border-radius:4px;margin-right:8px;""></div>
                                <span style=""color:white;font-weight:500;font-size:1.5rem;"">BRU ID</span>
                            </div>
                        </div>
                        <h2 style=""text-align:center;margin-bottom:1.5rem;color:white;"">Create your BRU&nbsp;ID</h2>
                        {errorHtml}{messageHtml}
                        <form method=""POST"" action=""/api/auth/register"" id=""registerForm"">
                            <div class=""form-group"">
                                <label for=""username"">Username</label>
                                <input type=""text"" id=""username"" name=""username"" placeholder=""Choose a username"" required>
                            </div>
                            <div class=""form-group"">
                                <label for=""password"">Password</label>
                                <input type=""password"" id=""password"" name=""password"" placeholder=""Choose a password"" required>
                            </div>
                            <div class=""form-group"">
                                <label for=""email"">Email (optional)</label>
                                <input type=""email"" id=""email"" name=""email"" placeholder=""Enter your email"">
                            </div>
                            <div class=""form-group"">
                                <label for=""phone"">Phone (optional)</label>
                                <input type=""tel"" id=""phone"" name=""phoneNumber"" placeholder=""Enter your phone"">
                            </div>
                            <button type=""submit"">Register</button>
                        </form>
                        <div class=""divider""><span>or</span></div>
                        <div style=""text-align:center;"">
                            <a href=""/api/auth/login"" class=""link"" style=""color:white;"">Back to Login</a>
                        </div>
                    </div>
                </div>
            </div>", "auth-page-body");
        }

        public string RenderClaimAccountForm(string? error = null, string? message = null)
        {
            var errorHtml = error != null ? $@"<div class=""error-message"">{error}</div>" : "";
            var messageHtml = message != null ? $@"<div class=""success-message"">{message}</div>" : "";

            return WrapInBaseTemplate("Claim Account - BRU AVTOPARK", $@"
            <div class=""login-container fade-in"">
                <div class=""auth-card"">
                    <div class=""card-body"">
                        <div class=""yandex-id-header"">
                            <div style=""display:flex;align-items:center;"">
                                <div style=""width:24px;height:24px;background-color:var(--primary-color);border-radius:4px;margin-right:8px;""></div>
                                <span style=""color:white;font-weight:500;font-size:1.5rem;"">BRU ID</span>
                            </div>
                        </div>
                        <h2 style=""text-align:center;margin-bottom:1.5rem;color:white;"">Claim Your Account</h2>
                        {errorHtml}{messageHtml}
                        <div class=""info-box"" style=""margin-bottom:1.5rem;"">
                            <p>If you have an inactive or guest account, claim it here by providing your username and password.</p>
                        </div>
                        <form method=""POST"" action=""/api/auth/claim-account"" id=""claimForm"">
                            <div class=""form-group"">
                                <label for=""username"">Username</label>
                                <input type=""text"" id=""username"" name=""username"" placeholder=""Enter your username"">
                            </div>
                            <div class=""form-group"">
                                <label for=""password"">Password</label>
                                <input type=""password"" id=""password"" name=""password"" placeholder=""Enter your password"">
                            </div>
                            <div class=""form-group"" style=""display:flex;align-items:center;margin-bottom:1rem;"">
                                <input type=""checkbox"" id=""generateNewIdentity"" name=""generateNewIdentity"" style=""margin-right:10px;"" checked>
                                <label for=""generateNewIdentity"" style=""margin:0;"">Generate new identity (recommended)</label>
                            </div>
                            <button type=""submit"" id=""claimButton"">Claim Account</button>
                        </form>
                        <div id=""statusDiv"" class=""mt-3""></div>
                        <div class=""divider""><span>or</span></div>
                        <div style=""text-align:center;margin-top:1rem;"">
                            <a href=""/api/auth/login"" class=""link"" style=""color:white;"">Back to Login</a>
                        </div>
                    </div>
                </div>
            </div>", "auth-page-body");
        }

        public string RenderTotpSetup(string qrCodeUri, string secretKey)
        {
            return WrapInBaseTemplate("TOTP Setup - BRU AVTOPARK", $@"
            <div class=""container fade-in"">
                <div class=""card"">
                    <div class=""card-header""><h1>Two-Factor Authentication Setup</h1></div>
                    <div class=""card-body"">
                        <div class=""info-box"">
                            <p>Scan the QR code with your authenticator app (Google Authenticator, Authy, or Microsoft Authenticator).</p>
                        </div>
                        <div class=""qr-code"">
                            <img src=""{qrCodeUri}"" alt=""TOTP QR Code"">
                        </div>
                        <div class=""text-center my-4"">
                            <p>Cannot scan? Manually enter this code:</p>
                            <div class=""code-display text-center"">{secretKey}</div>
                        </div>
                        <form method=""POST"" action=""/api/auth/totp/verify"">
                            <div class=""form-group"">
                                <label for=""code"">Enter the 6-digit code</label>
                                <input type=""text"" id=""code"" name=""code"" pattern=""[0-9]{{6}}"" placeholder=""Enter 6-digit code"" autocomplete=""one-time-code"">
                            </div>
                            <input type=""hidden"" name=""secretKey"" value=""{secretKey}"">
                            <button type=""submit"" class=""btn btn-block"">Verify and Enable</button>
                        </form>
                        <div class=""text-center mt-4""><a href=""/api/auth/profile"" class=""link"">Back to Profile</a></div>
                    </div>
                </div>
            </div>", "");
        }

        public string RenderWebAuthnRegistration(string options)
        {
            return WrapInBaseTemplate("Register Security Key - BRU AVTOPARK", $@"
            <div class=""container fade-in"">
                <div class=""card"">
                    <div class=""card-header""><h1>Register Security Key</h1></div>
                    <div class=""card-body"">
                        <div class=""info-box"">
                            <p>Register your security key or biometric authentication for passwordless login.</p>
                        </div>
                        <div id=""options"" data-options=""{options}"" style=""display:none;""></div>
                        <div class=""flex flex-col items-center gap-4 my-4"">
                            <div id=""status"" class=""text-center""><p>Click the button below to register your security key.</p></div>
                            <button onclick=""registerWebAuthn()"" id=""registerButton"" class=""btn"">Register Security Key</button>
                            <div id=""loader"" class=""loader"" style=""display:none;""></div>
                        </div>
                        <div class=""text-center mt-4""><a href=""/api/auth/profile"" class=""link"">Back to Profile</a></div>
                        {WebAuthnRegisterScript}
                    </div>
                </div>
            </div>", "");
        }

        public string RenderWebAuthnLogin(string options)
        {
            return WrapInBaseTemplate("Security Key Login - BRU AVTOPARK", $@"
            <div class=""login-container fade-in"">
                <div class=""auth-card"">
                    <div class=""card-body"">
                        <h2 style=""text-align:center;margin-bottom:1.5rem;color:white;"">Login with Security Key</h2>
                        <div id=""options"" data-options=""{options}"" style=""display:none;""></div>
                        <div id=""status"" class=""text-center my-4"" style=""color:#b3b3b3;"">
                            <p>Click the button below and follow browser instructions.</p>
                        </div>
                        <button onclick=""loginWebAuthn()"" id=""loginButton"">Use Security Key</button>
                        <div id=""loader"" class=""loader"" style=""display:none;margin-top:1rem;""></div>
                        <div class=""secondary-option"" style=""margin-top:1rem;"" onclick=""window.location.href='/api/auth/login'"">
                            <span>Back to Login</span>
                        </div>
                    </div>
                </div>
            </div>", "auth-page-body");
        }

        public string RenderMagicLinkForm(string? error = null, string? message = null)
        {
            var errorHtml = error != null ? $@"<div class=""error-message"">{error}</div>" : "";
            var messageHtml = message != null ? $@"<div class=""success-message"">{message}</div>" : "";

            return WrapInBaseTemplate("Magic Link - BRU AVTOPARK", $@"
            <div class=""login-container fade-in"">
                <div class=""auth-card"">
                    <div class=""card-body"">
                        <div class=""yandex-id-header"">
                            <div style=""display:flex;align-items:center;"">
                                <div style=""width:24px;height:24px;background-color:var(--primary-color);border-radius:4px;margin-right:8px;""></div>
                                <span style=""color:white;font-weight:500;font-size:1.5rem;"">BRU ID</span>
                            </div>
                        </div>
                        <h2 style=""text-align:center;margin-bottom:1.5rem;color:white;"">Login with Magic Link</h2>
                        {errorHtml}{messageHtml}
                        <div class=""info-box"" style=""background-color:rgba(255,255,255,0.1);color:#b3b3b3;"">
                            <p>Enter your email address to receive a secure login link. No password required!</p>
                        </div>
                        <form method=""POST"" action=""/api/auth/magic-link/send"">
                            <div class=""form-group"">
                                <label for=""email"">Email Address</label>
                                <input type=""email"" id=""email"" name=""email"" placeholder=""Enter your email address"">
                            </div>
                            <button type=""submit"">Send Magic Link</button>
                        </form>
                        <div class=""divider""><span>or</span></div>
                        <div class=""secondary-option"" onclick=""window.location.href='/api/auth/login'"">
                            <span>Back to Login</span>
                        </div>
                    </div>
                </div>
            </div>", "auth-page-body");
        }

        public string RenderQrLogin(string qrCode)
        {
            return WrapInBaseTemplate("QR Login - BRU AVTOPARK", $@"
            <div class=""login-container fade-in"">
                <div class=""auth-card"">
                    <div class=""card-body"">
                        <div class=""yandex-id-header"">
                            <div style=""display:flex;align-items:center;"">
                                <div style=""width:24px;height:24px;background-color:var(--primary-color);border-radius:4px;margin-right:8px;""></div>
                                <span style=""color:white;font-weight:500;font-size:1.5rem;"">BRU ID</span>
                            </div>
                        </div>
                        <h2 style=""text-align:center;margin-bottom:1.5rem;color:white;"">Login with QR Code</h2>
                        <div class=""info-box"" style=""background-color:rgba(255,255,255,0.1);color:#b3b3b3;"">
                            <p>Scan this QR code with your mobile device to log in instantly.</p>
                        </div>
                        <div class=""qr-code qr-login-container"">
                            <img src=""data:image/png;base64,{qrCode}"" alt=""Login QR Code"">
                        </div>
                        <div id=""status"" class=""text-center my-4"" style=""color:#b3b3b3;"">
                            <p>Waiting for you to scan the QR code...</p>
                            <div class=""loader"" style=""margin-top:1rem;""></div>
                        </div>
                        <div class=""secondary-option"" onclick=""window.location.href='/api/auth/login'"">
                            <span>Back to Login</span>
                        </div>
                    </div>
                </div>
                {QrPollScript}
            </div>", "auth-page-body");
        }

        public string RenderSuccess(string token)
        {
            return WrapInBaseTemplate("Login Successful - BRU AVTOPARK", $@"
            <div class=""container fade-in"">
                <div class=""card"">
                    <div class=""card-body text-center"">
                        <h1>Login Successful</h1>
                        <p>You have been authenticated. Redirecting to your profile...</p>
                        <div class=""loader"" style=""margin:1rem auto;""></div>
                    </div>
                </div>
            </div>
            <script>
                localStorage.setItem('auth_token', '{token}');
                setTimeout(function(){{ window.location.href = '/api/auth/profile?token=' + encodeURIComponent('{token}'); }}, 1500);
            </script>", "");
        }

        public string RenderError(string message)
        {
            return WrapInBaseTemplate("Error - BRU AVTOPARK", $@"
            <div class=""container fade-in"">
                <div class=""card"">
                    <div class=""card-body text-center"">
                        <h1>Error</h1>
                        <div class=""error-message"">{message}</div>
                        <a href=""/api/auth/login"" class=""btn"" style=""margin-top:1rem;"">Back to Login</a>
                    </div>
                </div>
            </div>", "");
        }

        public string RenderOAuthLoginForm(string requestId, string clientName, string[] scopes, string? error = null)
        {
            var errorHtml = error != null ? $@"<div class=""error-message"">{error}</div>" : "";
            var scopeList = string.Join("", scopes.Select(s => $@"<li>{s}</li>"));

            return WrapInBaseTemplate("Authorize " + clientName, $@"
            <div class=""container fade-in"">
                <div class=""card"">
                    <div class=""card-header""><h1>Authorize {clientName}</h1></div>
                    <div class=""card-body"">
                        {errorHtml}
                        <p>The application <strong>{clientName}</strong> is requesting the following permissions:</p>
                        <ul style=""margin:1rem 0 1.5rem 1.5rem;"">{scopeList}</ul>
                        <form method=""POST"" action=""/api/auth/connect/authorize/callback"">
                            <input type=""hidden"" name=""requestId"" value=""{requestId}"">
                            <div class=""form-group"">
                                <label for=""username"">Username</label>
                                <input type=""text"" id=""username"" name=""username"" placeholder=""Enter your username"">
                            </div>
                            <div class=""form-group"">
                                <label for=""password"">Password</label>
                                <input type=""password"" id=""password"" name=""password"" placeholder=""Enter your password"">
                            </div>
                            <button type=""submit"" class=""btn btn-block"">Authorize</button>
                        </form>
                    </div>
                </div>
            </div>", "");
        }

        public string RenderOAuthConsent(string requestId, string clientName, string[] scopes, string username)
        {
            var scopeList = string.Join("", scopes.Select(s => $@"<li>{s}</li>"));

            return WrapInBaseTemplate("Consent - " + clientName, $@"
            <div class=""container fade-in"">
                <div class=""card"">
                    <div class=""card-header""><h1>Grant Consent</h1></div>
                    <div class=""card-body"">
                        <p>Logged in as <strong>{username}</strong></p>
                        <p>The application <strong>{clientName}</strong> is requesting access to:</p>
                        <ul style=""margin:1rem 0 1.5rem 1.5rem;"">{scopeList}</ul>
                        <form method=""POST"" action=""/api/auth/connect/authorize/callback"">
                            <input type=""hidden"" name=""requestId"" value=""{requestId}"">
                            <input type=""hidden"" name=""username"" value=""{username}"">
                            <input type=""hidden"" name=""password"" value="""">
                            <button type=""submit"" class=""btn btn-block"">Allow</button>
                        </form>
                    </div>
                </div>
            </div>", "");
        }

        public string RenderProfile(ProfileViewModel model)
        {
            var rolesHtml = model.Roles.Count > 0
                ? string.Join(", ", model.Roles)
                : "No roles assigned";

            return WrapInBaseTemplate("Profile - BRU AVTOPARK", $@"
            <div class=""container fade-in"">
                <div class=""card"">
                    <div class=""card-header""><h1>My Profile</h1></div>
                    <div class=""card-body"">
                        <p><strong>Username:</strong> {model.Username}</p>
                        <p><strong>Email:</strong> {model.Email ?? "Not set"}</p>
                        <p><strong>Phone:</strong> {model.PhoneNumber ?? "Not set"}</p>
                        <p><strong>Roles:</strong> {rolesHtml}</p>
                        <hr style=""margin:1.5rem 0;border-color:var(--border-color);"">
                        <h3>Security</h3>
                        <p><strong>TOTP:</strong> {(model.TotpEnabled ? "Enabled" : "Disabled")}
                            {(model.TotpEnabled ? "" : @" &mdash; <a href=""/api/auth/totp/setup"" class=""link"">Set up</a>")}</p>
                        <p><strong>Security Key:</strong> {(model.WebAuthnEnabled ? "Enabled" : "Disabled")}
                            {(model.WebAuthnEnabled ? "" : @" &mdash; <a href=""/api/auth/webauthn/register/options"" class=""link"">Register</a>")}</p>
                    </div>
                    <div class=""card-footer"">
                        <a href=""/api/auth/logout"" class=""btn btn-secondary"" style=""width:auto;"">Log out</a>
                    </div>
                </div>
            </div>", "");
        }

        public string RenderClientsList(IEnumerable<ClientViewModel> clients)
        {
            var rows = string.Join("", clients.Select(c => $@"
                <tr>
                    <td style=""padding:0.75rem;"">{c.ClientId}</td>
                    <td style=""padding:0.75rem;"">{c.DisplayName}</td>
                    <td style=""padding:0.75rem;""><a href=""/api/auth/connect/clients/{c.ClientId}"" class=""link"">View</a></td>
                </tr>"));

            return WrapInBaseTemplate("OIDC Clients - BRU AVTOPARK", $@"
            <div class=""container fade-in"">
                <div class=""card"">
                    <div class=""card-header"" style=""display:flex;justify-content:space-between;align-items:center;"">
                        <h1>OIDC Clients</h1>
                        <a href=""/api/auth/connect/clients/new"" class=""btn"" style=""width:auto;"">New Client</a>
                    </div>
                    <div class=""card-body"">
                        <table style=""width:100%;border-collapse:collapse;"">
                            <thead><tr>
                                <th style=""text-align:left;padding:0.75rem;border-bottom:1px solid var(--border-color);"">Client ID</th>
                                <th style=""text-align:left;padding:0.75rem;border-bottom:1px solid var(--border-color);"">Display Name</th>
                                <th style=""text-align:left;padding:0.75rem;border-bottom:1px solid var(--border-color);"">Actions</th>
                            </tr></thead>
                            <tbody>{rows}</tbody>
                        </table>
                    </div>
                </div>
            </div>", "");
        }

        public string RenderClientDetail(ClientDetailViewModel model)
        {
            return WrapInBaseTemplate($"Client {model.ClientId} - BRU AVTOPARK", $@"
            <div class=""container fade-in"">
                <div class=""card"">
                    <div class=""card-header""><h1>{model.DisplayName ?? model.ClientId}</h1></div>
                    <div class=""card-body"">
                        <p><strong>Client ID:</strong> {model.ClientId}</p>
                        <p><strong>Redirect URIs:</strong> {string.Join(", ", model.RedirectUris)}</p>
                        <p><strong>Post-Logout URIs:</strong> {string.Join(", ", model.PostLogoutRedirectUris)}</p>
                        <p><strong>Scopes:</strong> {string.Join(", ", model.AllowedScopes)}</p>
                        <p><strong>Require Consent:</strong> {model.RequireConsent}</p>
                    </div>
                    <div class=""card-footer"">
                        <a href=""/api/auth/connect/clients/{model.ClientId}/edit"" class=""btn"" style=""width:auto;"">Edit</a>
                        <a href=""/api/auth/connect/clients"" class=""link"">Back to list</a>
                    </div>
                </div>
            </div>", "");
        }

        public string RenderClientForm(ClientFormViewModel? model = null)
        {
            model ??= new ClientFormViewModel();
            var title = model.IsEdit ? "Edit Client" : "Register New Client";
            var action = model.IsEdit
                ? $"/api/auth/connect/update-client/{model.ClientId}"
                : "/api/auth/connect/register-client";

            var errorHtml = model.Error != null ? $@"<div class=""error-message"">{model.Error}</div>" : "";
            var successHtml = model.Success != null ? $@"<div class=""success-message"">{model.Success}</div>" : "";

            return WrapInBaseTemplate($"{title} - BRU AVTOPARK", $@"
            <div class=""container fade-in"">
                <div class=""card"">
                    <div class=""card-header""><h1>{title}</h1></div>
                    <div class=""card-body"">
                        {errorHtml}{successHtml}
                        <form method=""POST"" action=""{action}"">
                            {(model.IsEdit ? "" : $@"
                            <div class=""form-group"">
                                <label for=""clientId"">Client ID</label>
                                <input type=""text"" id=""clientId"" name=""clientId"" value=""{model.ClientId}"" required>
                            </div>
                            <div class=""form-group"">
                                <label for=""clientSecret"">Client Secret</label>
                                <input type=""password"" id=""clientSecret"" name=""clientSecret"" required>
                            </div>")}
                            <div class=""form-group"">
                                <label for=""displayName"">Display Name</label>
                                <input type=""text"" id=""displayName"" name=""displayName"" value=""{model.DisplayName}"">
                            </div>
                            <div class=""form-group"">
                                <label for=""redirectUris"">Redirect URIs (comma-separated)</label>
                                <input type=""text"" id=""redirectUris"" name=""redirectUris"" value=""{model.RedirectUris}"">
                            </div>
                            <div class=""form-group"">
                                <label for=""postLogoutRedirectUris"">Post-Logout URIs (comma-separated)</label>
                                <input type=""text"" id=""postLogoutRedirectUris"" name=""postLogoutRedirectUris"" value=""{model.PostLogoutRedirectUris}"">
                            </div>
                            <div class=""form-group"">
                                <label for=""allowedScopes"">Scopes (comma-separated)</label>
                                <input type=""text"" id=""allowedScopes"" name=""allowedScopes"" value=""{model.AllowedScopes}"">
                            </div>
                            <div class=""form-group"" style=""display:flex;align-items:center;"">
                                <input type=""checkbox"" id=""requireConsent"" name=""requireConsent"" {(model.RequireConsent ? "checked" : "")} style=""margin-right:10px;"">
                                <label for=""requireConsent"" style=""margin:0;"">Require Consent</label>
                            </div>
                            <button type=""submit"" class=""btn btn-block"">{(model.IsEdit ? "Update" : "Register")}</button>
                        </form>
                        <div class=""text-center mt-4""><a href=""/api/auth/connect/clients"" class=""link"">Back to list</a></div>
                    </div>
                </div>
            </div>", "");
        }

        // ────────────────────────────────────────────────────────────────
        // Inline JavaScript fragments (extracted constants)
        // ────────────────────────────────────────────────────────────────

        private const string LoginScript = @"
<script>
function submitLoginForm(){
    document.getElementById('loginButton').disabled=true;
    document.getElementById('statusDiv').innerHTML='<div class=""text-center""><div class=""loader""></div><p>Logging in...</p></div>';
    var u=document.getElementById('username').value;
    var p=document.getElementById('password').value;
    fetch('/api/auth/login',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({username:u,password:p}),credentials:'same-origin',redirect:'manual'})
    .then(function(r){
        if(r.type==='opaqueredirect'||r.status===0||(r.status>=300&&r.status<400)){window.location.href=r.headers.get('Location')||'/api/auth/success';return null;}
        var ct=r.headers.get('content-type');
        if(ct&&ct.includes('text/html')){window.location.href=r.url||'/api/auth/success';return null;}
        return r.json();
    }).then(function(d){
        if(!d)return;
        if(d.success){
            if(d.data&&d.data.token)localStorage.setItem('auth_token',d.data.token);
            document.getElementById('statusDiv').innerHTML='<p class=""success-message"">Login successful! Redirecting...</p>';
            setTimeout(function(){window.location.href='/api/auth/profile';},1000);
        }else{
            document.getElementById('statusDiv').innerHTML='<p class=""error-message"">'+(d.message||'Login failed')+'</p>';
            document.getElementById('loginButton').disabled=false;
        }
    }).catch(function(e){
        document.getElementById('statusDiv').innerHTML='<p class=""error-message"">Error: '+(e.message||'Unknown error')+'</p>';
        document.getElementById('loginButton').disabled=false;
    });
    return false;
}
document.getElementById('loginForm').addEventListener('keypress',function(e){if(e.key==='Enter'){e.preventDefault();submitLoginForm();}});
</script>";

        private const string WebAuthnRegisterScript = @"
<script>
async function registerWebAuthn(){
    try{
        document.getElementById('registerButton').disabled=true;
        document.getElementById('loader').style.display='block';
        document.getElementById('status').innerHTML='<p>Please follow your browser instructions...</p>';
        var options=JSON.parse(document.getElementById('options').dataset.options);
        var credential=await navigator.credentials.create({publicKey:options.publicKey});
        var body={id:credential.id,rawId:bufToBase64(credential.rawId),type:credential.type,response:{attestationObject:bufToBase64(credential.response.attestationObject),clientDataJSON:bufToBase64(credential.response.clientDataJSON)}};
        var r=await fetch('/api/auth/webauthn/register/complete',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({attestationResponse:body})});
        if(r.ok){document.getElementById('status').innerHTML='<p class=""success-message"">Security key registered!</p>';setTimeout(function(){window.location.href='/api/auth/profile';},1500);}
        else{var e=await r.json();throw new Error(e.message||'Registration failed');}
    }catch(e){document.getElementById('status').innerHTML='<p class=""error-message"">Failed: '+(e.message||e)+'</p>';}
    finally{document.getElementById('registerButton').disabled=false;document.getElementById('loader').style.display='none';}
}
function bufToBase64(buf){var b=new Uint8Array(buf);var s='';for(var i=0;i<b.byteLength;i++)s+=String.fromCharCode(b[i]);return btoa(s);}
</script>";

        private const string QrPollScript = @"
<script>
function checkLoginStatus(deviceId){
    fetch('/api/auth/qr/direct/check?deviceId='+deviceId)
    .then(function(r){return r.json();})
    .then(function(d){
        if(d.success&&d.data&&d.data.token){
            document.getElementById('status').innerHTML='<p class=""success-message"">Login successful! Redirecting...</p>';
            localStorage.setItem('auth_token',d.data.token);
            setTimeout(function(){window.location.href='/api/auth/success?token='+d.data.token;},1000);
        }else{setTimeout(function(){checkLoginStatus(deviceId);},2000);}
    }).catch(function(){setTimeout(function(){checkLoginStatus(deviceId);},5000);});
}
var deviceId=new URLSearchParams(window.location.search).get('deviceId');
if(deviceId)checkLoginStatus(deviceId);
</script>";

        // ────────────────────────────────────────────────────────────────
        // Base HTML template (preserved from the original controller)
        // ────────────────────────────────────────────────────────────────

        private const string BaseHtmlTemplate = @"
<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{0}</title>
    <style>
        :root {{
            --primary-color: #fc3f1d;
            --primary-dark: #d93412;
            --primary-light: #ff5c3e;
            --background-color: #f6f7f8;
            --card-color: #ffffff;
            --text-color: #21201f;
            --text-muted: #838383;
            --border-color: #e7e8ea;
            --error-color: #ef4444;
            --success-color: #10b981;
            --warning-color: #f59e0b;
            --shadow: 0 2px 8px rgba(0, 0, 0, 0.08);
        }}
        [data-theme=""dark""] {{
            --primary-color: #fc3f1d;
            --primary-dark: #d93412;
            --primary-light: #ff5c3e;
            --background-color: #21201f;
            --card-color: #312f2f;
            --text-color: #ffffff;
            --text-muted: #b3b3b3;
            --border-color: #3b3a38;
            --shadow: 0 2px 8px rgba(0, 0, 0, 0.15);
        }}
        * {{ margin:0; padding:0; box-sizing:border-box; font-family:'YS Text','Helvetica Neue',Arial,sans-serif; }}
        body {{ background-color:var(--background-color); color:var(--text-color); line-height:1.5; min-height:100vh; display:flex; flex-direction:column; }}
        .auth-page-body {{ background-image:url('https://yastatic.net/s3/passport-auth/freezer/_/12l0Lb-3jyLI.jpg'); background-size:cover; background-position:center; }}
        .navbar {{ display:flex; justify-content:space-between; align-items:center; padding:1rem 2rem; background-color:var(--card-color); box-shadow:var(--shadow); z-index:10; }}
        .logo {{ font-size:1.5rem; font-weight:500; color:var(--text-color); text-decoration:none; display:flex; align-items:center; }}
        .logo::before {{ content:''; display:inline-block; width:24px; height:24px; background-color:var(--primary-color); border-radius:4px; margin-right:8px; }}
        .theme-toggle {{ background:none; border:none; color:var(--text-color); cursor:pointer; font-size:1.2rem; width:2.5rem; height:2.5rem; border-radius:50%; display:flex; align-items:center; justify-content:center; }}
        .theme-toggle:hover {{ background-color:var(--border-color); }}
        .container {{ max-width:min(500px,85vw); margin:2rem auto; padding:0 1.5rem; width:100%; flex:1; display:flex; flex-direction:column; justify-content:center; }}
        .login-container {{ display:flex; flex-direction:column; justify-content:center; align-items:center; min-height:calc(100vh - 64px); padding:1rem; }}
        .card {{ background-color:var(--card-color); border-radius:0.75rem; box-shadow:var(--shadow); overflow:hidden; width:100%; max-width:min(500px,85vw); }}
        .auth-card {{ background-color:#21201f; color:white; border-radius:1rem; box-shadow:0 4px 20px rgba(0,0,0,0.25); max-width:min(480px,85vw); }}
        .card-header {{ padding:1.5rem; border-bottom:1px solid var(--border-color); }}
        .card-body {{ padding:1.5rem; }}
        .card-footer {{ padding:1rem 1.5rem; border-top:1px solid var(--border-color); display:flex; justify-content:space-between; align-items:center; }}
        h1,h2,h3 {{ color:var(--text-color); font-weight:500; margin-bottom:0.5rem; }}
        .auth-card h1,.auth-card h2,.auth-card h3,.auth-card label,.auth-card p {{ color:white; }}
        h1 {{ font-size:1.75rem; }}
        p {{ margin-bottom:1rem; color:var(--text-muted); }}
        .form-group {{ margin-bottom:1.25rem; }}
        label {{ display:block; margin-bottom:0.5rem; font-weight:400; }}
        input,select,textarea {{ width:100%; padding:0.75rem; border:1px solid var(--border-color); border-radius:0.5rem; font-size:1rem; background-color:var(--card-color); color:var(--text-color); }}
        .auth-card input {{ background-color:rgba(255,255,255,0.1); color:white; border:none; }}
        input:focus {{ outline:none; border-color:var(--primary-color); box-shadow:0 0 0 2px rgba(252,63,29,0.1); }}
        button,.btn {{ display:inline-block; width:100%; padding:0.75rem 1.5rem; background-color:var(--primary-color); color:white; border:none; border-radius:0.5rem; font-size:1rem; font-weight:500; cursor:pointer; text-align:center; text-decoration:none; }}
        .auth-card button {{ background-color:white; color:black; }}
        button:hover,.btn:hover {{ background-color:var(--primary-dark); }}
        .auth-card button:hover {{ background-color:#f0f0f0; }}
        .btn-secondary {{ background-color:transparent; color:var(--primary-color); border:1px solid var(--primary-color); }}
        .btn-secondary:hover {{ background-color:rgba(252,63,29,0.1); }}
        .error-message {{ color:var(--error-color); background-color:rgba(239,68,68,0.1); padding:0.75rem; border-radius:0.5rem; margin-bottom:1.5rem; font-size:0.875rem; }}
        .success-message {{ color:var(--success-color); background-color:rgba(16,185,129,0.1); padding:0.75rem; border-radius:0.5rem; margin-bottom:1.5rem; font-size:0.875rem; }}
        .qr-code {{ display:flex; justify-content:center; margin:2rem 0; }}
        .qr-code img {{ max-width:200px; height:auto; padding:0.5rem; background-color:white; border-radius:0.5rem; }}
        .code-display {{ font-family:monospace; background-color:rgba(0,0,0,0.05); padding:0.5rem; border-radius:0.25rem; word-break:break-all; margin:0.5rem 0; }}
        .text-center {{ text-align:center; }}
        .info-box {{ background-color:rgba(252,63,29,0.07); padding:1rem; border-radius:0.5rem; margin-bottom:1.5rem; }}
        .link {{ color:var(--primary-color); text-decoration:none; }}
        .auth-card .link {{ color:#76a6f5; }}
        .link:hover {{ color:var(--primary-dark); text-decoration:underline; }}
        .my-4 {{ margin-top:1rem; margin-bottom:1rem; }}
        .mt-4 {{ margin-top:1rem; }}
        .mt-3 {{ margin-top:0.75rem; }}
        .divider {{ display:flex; align-items:center; text-align:center; margin:1.5rem 0; }}
        .divider::before,.divider::after {{ content:''; flex:1; border-bottom:1px solid var(--border-color); }}
        .divider span {{ padding:0 0.75rem; color:var(--text-muted); }}
        .social-buttons {{ display:flex; justify-content:center; gap:1rem; margin-top:1.5rem; }}
        .social-button {{ width:40px; height:40px; border-radius:50%; display:flex; align-items:center; justify-content:center; background-color:rgba(255,255,255,0.1); cursor:pointer; }}
        .social-button:hover {{ background-color:rgba(255,255,255,0.2); }}
        .secondary-option {{ background-color:rgba(255,255,255,0.1); color:white; border:none; border-radius:0.5rem; padding:0.75rem; margin-top:1rem; cursor:pointer; width:100%; text-align:center; display:flex; align-items:center; justify-content:center; gap:0.5rem; }}
        .secondary-option:hover {{ background-color:rgba(255,255,255,0.15); }}
        .auth-footer {{ text-align:center; margin-top:1.5rem; font-size:0.875rem; color:var(--text-muted); }}
        .qr-login-container {{ text-align:center; }}
        .qr-login-container .qr-code {{ padding:1rem; background-color:white; border-radius:0.75rem; display:inline-flex; }}
        .loader {{ border:2px solid rgba(252,63,29,0.1); border-radius:50%; border-top:2px solid var(--primary-color); width:24px; height:24px; animation:spin 1s linear infinite; margin:0 auto; display:inline-block; }}
        @keyframes spin {{ 0%{{ transform:rotate(0deg); }} 100%{{ transform:rotate(360deg); }} }}
        .fade-in {{ animation:fadeIn 0.3s ease-in-out; }}
        @keyframes fadeIn {{ from{{ opacity:0; transform:translateY(10px); }} to{{ opacity:1; transform:translateY(0); }} }}
        @media (max-width:640px) {{ .container {{ margin:1rem auto; }} .card {{ border-radius:0.5rem; }} .card-header,.card-body,.card-footer {{ padding:1rem; }} }}
    </style>
</head>
<body class=""{2}"">
    <div class=""navbar"">
        <a href=""/"" class=""logo"">BRU AVTOPARK</a>
        <button class=""theme-toggle"" id=""themeToggle"" aria-label=""Toggle dark mode"">&#x1F319;</button>
    </div>
    {1}
    <script>
        var t=document.getElementById('themeToggle');
        var c=localStorage.getItem('theme')||(window.matchMedia('(prefers-color-scheme:dark)').matches?'dark':'light');
        if(c==='dark'){{document.body.setAttribute('data-theme','dark');t.textContent='&#x2600;&#xFE0F;';}}
        t.addEventListener('click',function(){{
            if(!document.body.hasAttribute('data-theme')){{document.body.setAttribute('data-theme','dark');t.textContent='&#x2600;&#xFE0F;';localStorage.setItem('theme','dark');}}
            else{{document.body.removeAttribute('data-theme');t.textContent='&#x1F319;';localStorage.setItem('theme','light');}}
        }});
    </script>
</body>
</html>";
    }
}
