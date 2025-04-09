using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using Serilog;
using TicketSalesApp.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using SpacetimeDB.Types;
using SpacetimeDB;
using Log = Serilog.Log;
using Fido2NetLib;
using Fido2NetLib.Objects;
using System.Text.Json;
using static OpenIddict.Abstractions.OpenIddictConstants;
using OpenIddict.Abstractions;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthenticationService _authService;
        private readonly IQRAuthenticationService _qrAuthService;
        private readonly IUserService _userService;
        private readonly ITotpService _totpService;
        private readonly IWebAuthnService _webAuthnService;
        private readonly IMagicLinkService _magicLinkService;
        private readonly IOpenIdConnectService _openIdConnectService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthController> _logger;
        private readonly IMemoryCache _cache;
        private readonly ISpacetimeDBService _spacetimeService;

        public AuthController(
            IAuthenticationService authService, 
            IQRAuthenticationService qrAuthService,
            IUserService userService,
            ITotpService totpService,
            IWebAuthnService webAuthnService,
            IMagicLinkService magicLinkService,
            IOpenIdConnectService openIdConnectService,
            IConfiguration configuration,
            ILogger<AuthController> logger,
            IMemoryCache cache,
            ISpacetimeDBService spacetimeService)
        {
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _qrAuthService = qrAuthService ?? throw new ArgumentNullException(nameof(qrAuthService));
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
            _totpService = totpService ?? throw new ArgumentNullException(nameof(totpService));
            _webAuthnService = webAuthnService ?? throw new ArgumentNullException(nameof(webAuthnService));
            _magicLinkService = magicLinkService ?? throw new ArgumentNullException(nameof(magicLinkService));
            _openIdConnectService = openIdConnectService ?? throw new ArgumentNullException(nameof(openIdConnectService));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
        }

        #region HTML Templates

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
            
            /* Yandex ID specific variables */
            --id-color-surface-submerged: #f6f7f8;
            --id-color-surface-elevated-0: #ffffff;
            --id-color-line-normal: #e7e8ea;
            --id-color-default-bg-base: #f5f5f5;
            --id-color-status-negative: #ff3333;
            --id-card-border-radius: 12px;
            --id-typography-heading-l: 500 28px/32px 'YS Text', 'Helvetica Neue', Arial, sans-serif;
            --id-typography-heading-m: 500 20px/24px 'YS Text', 'Helvetica Neue', Arial, sans-serif;
            --id-typography-text-m: 400 16px/20px 'YS Text', 'Helvetica Neue', Arial, sans-serif;
            --id-typography-text-s: 400 14px/18px 'YS Text', 'Helvetica Neue', Arial, sans-serif;
            --id-typography-text-xs: 400 13px/16px 'YS Text', 'Helvetica Neue', Arial, sans-serif;
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
            
            /* Yandex ID dark mode colors */
            --id-color-surface-submerged: #21201f;
            --id-color-surface-elevated-0: #312f2f;
            --id-color-line-normal: #3b3a38;
        }}

        * {{
            margin: 0;
            padding: 0;
            box-sizing: border-box;
            font-family: 'YS Text', 'Helvetica Neue', Arial, sans-serif;
        }}

        body {{
            background-color: var(--background-color);
            color: var(--text-color);
            line-height: 1.5;
            min-height: 100vh;
            transition: all 0.3s ease;
            display: flex;
            flex-direction: column;
            height: 100vh;
        }}

        .auth-page-body {{
            background-color: var(--background-color);
            background-image: url('https://yastatic.net/s3/passport-auth/freezer/_/12l0Lb-3jyLI.jpg');
            background-size: cover;
            background-position: center;
            background-repeat: no-repeat;
        }}

        [data-theme=""dark""] .auth-page-body {{
            background-image: url('https://yastatic.net/s3/passport-auth/freezer/_/12l0Lb-3jyLI.jpg');
        }}

        .navbar {{
            display: flex;
            justify-content: space-between;
            align-items: center;
            padding: 1rem 2rem;
            background-color: var(--card-color);
            box-shadow: var(--shadow);
            position: relative;
            z-index: 10;
        }}

        .logo {{
            font-size: 1.5rem;
            font-weight: 500;
            color: var(--text-color);
            text-decoration: none;
            display: flex;
            align-items: center;
        }}

        .logo::before {{
            content: '';
            display: inline-block;
            width: 24px;
            height: 24px;
            background-color: var(--primary-color);
            border-radius: 4px;
            margin-right: 8px;
        }}

        .theme-toggle {{
            background: none;
            border: none;
            color: var(--text-color);
            cursor: pointer;
            font-size: 1.2rem;
            width: 2.5rem;
            height: 2.5rem;
            border-radius: 50%;
            display: flex;
            align-items: center;
            justify-content: center;
            transition: background-color 0.2s;
        }}

        .theme-toggle:hover {{
            background-color: var(--border-color);
        }}

        .container {{
            max-width: 400px;
            margin: 2rem auto;
            padding: 0 1rem;
            width: 100%;
            flex: 1;
            display: flex;
            flex-direction: column;
            justify-content: center;
        }}

        .login-container {{
            display: flex;
            flex-direction: column;
            justify-content: center;
            align-items: center;
            min-height: calc(100vh - 64px);
            padding: 1rem;
        }}

        .card {{
            background-color: var(--card-color);
            border-radius: 0.75rem;
            box-shadow: var(--shadow);
            overflow: hidden;
            transition: all 0.3s ease;
            width: 100%;
            max-width: 400px;
        }}

        .auth-card {{
            background-color: #21201f;
            color: white;
            border-radius: 1rem;
            box-shadow: 0 4px 20px rgba(0, 0, 0, 0.25);
            max-width: 360px;
        }}

        .card-header {{
            padding: 1.5rem;
            border-bottom: 1px solid var(--border-color);
        }}

        .card-body {{
            padding: 1.5rem;
        }}

        .card-footer {{
            padding: 1rem 1.5rem;
            border-top: 1px solid var(--border-color);
            display: flex;
            justify-content: space-between;
            align-items: center;
        }}

        h1, h2, h3, h4, h5, h6 {{
            color: var(--text-color);
            font-weight: 500;
            margin-bottom: 0.5rem;
        }}

        .auth-card h1, .auth-card h2, .auth-card h3, .auth-card label, .auth-card p {{
            color: white;
        }}

        h1 {{
            font-size: 1.75rem;
        }}

        p {{
            margin-bottom: 1rem;
            color: var(--text-muted);
        }}

        .form-group {{
            margin-bottom: 1.25rem;
        }}

        label {{
            display: block; 
            margin-bottom: 0.5rem;
            font-weight: 400;
        }}

        input, select, textarea {{
            width: 100%;
            padding: 0.75rem;
            border: 1px solid var(--border-color);
            border-radius: 0.5rem;
            font-size: 1rem;
            background-color: var(--card-color);
            color: var(--text-color);
            transition: border-color 0.15s ease-in-out, box-shadow 0.15s ease-in-out;
        }}

        .auth-card input {{
            background-color: rgba(255, 255, 255, 0.1);
            color: white;
            border: none;
        }}

        input:focus, select:focus, textarea:focus {{
            outline: none;
            border-color: var(--primary-color);
            box-shadow: 0 0 0 2px rgba(252, 63, 29, 0.1);
        }}

        button, .btn {{
            display: inline-block;
            width: 100%;
            padding: 0.75rem 1.5rem;
            background-color: var(--primary-color);
            color: white;
            border: none;
            border-radius: 0.5rem;
            font-size: 1rem;
            font-weight: 500;
            cursor: pointer;
            transition: background-color 0.15s ease-in-out, transform 0.1s ease;
            text-align: center;
            text-decoration: none;
        }}

        .auth-card button {{
            background-color: white;
            color: black;
        }}

        button:hover, .btn:hover {{
            background-color: var(--primary-dark);
        }}

        .auth-card button:hover {{
            background-color: #f0f0f0;
        }}

        button:active, .btn:active {{
            transform: translateY(1px);
        }}

        .btn-secondary {{
            background-color: transparent;
            color: var(--primary-color);
            border: 1px solid var(--primary-color);
        }}

        .btn-secondary:hover {{
            background-color: rgba(252, 63, 29, 0.1);
        }}

        .btn-block {{
            display: block;
            width: 100%;
        }}

        .error-message {{
            color: var(--error-color);
            background-color: rgba(239, 68, 68, 0.1);
            padding: 0.75rem;
            border-radius: 0.5rem;
            margin-bottom: 1.5rem;
            font-size: 0.875rem;
            display: flex;
            align-items: center;
        }}

        .success-message {{
            color: var(--success-color);
            background-color: rgba(16, 185, 129, 0.1);
            padding: 0.75rem;
            border-radius: 0.5rem;
            margin-bottom: 1.5rem;
            font-size: 0.875rem;
            display: flex;
            align-items: center;
        }}

        .qr-code {{
            display: flex;
            justify-content: center;
            margin: 2rem 0;
        }}

        .qr-code img {{
            max-width: 200px;
            height: auto;
            padding: 0.5rem;
            background-color: white;
            border-radius: 0.5rem;
        }}

        .code-display {{
            font-family: monospace;
            background-color: rgba(0, 0, 0, 0.05);
            padding: 0.5rem;
            border-radius: 0.25rem;
            word-break: break-all;
            margin: 0.5rem 0;
        }}

        [data-theme=""dark""] .code-display {{
            background-color: rgba(255, 255, 255, 0.05);
        }}

        .text-center {{
            text-align: center;
        }}

        .info-box {{
            background-color: rgba(252, 63, 29, 0.07);
            padding: 1rem;
            border-radius: 0.5rem;
            margin-bottom: 1.5rem;
        }}

        .link {{
            color: var(--primary-color);
            text-decoration: none;
            transition: color 0.15s ease;
        }}

        .auth-card .link {{
            color: #76a6f5;
        }}

        .link:hover {{
            color: var(--primary-dark);
            text-decoration: underline;
        }}

        .text-muted {{
            color: var(--text-muted);
        }}

        .flex {{
            display: flex;
        }}

        .flex-col {{
            flex-direction: column;
        }}

        .flex-wrap {{
            flex-wrap: wrap;
        }}

        .items-center {{
            align-items: center;
        }}

        .justify-center {{
            justify-content: center;
        }}

        .justify-between {{
            justify-content: space-between;
        }}

        .gap-2 {{
            gap: 0.5rem;
        }}

        .gap-4 {{
            gap: 1rem;
        }}

        .my-2 {{
            margin-top: 0.5rem;
            margin-bottom: 0.5rem;
        }}

        .my-4 {{
            margin-top: 1rem;
            margin-bottom: 1rem;
        }}

        .mt-4 {{
            margin-top: 1rem;
        }}

        .mt-8 {{
            margin-top: 2rem;
        }}

        /* Layout styles */
        .page-wrapper {{
            display: flex;
            min-height: 100vh;
        }}

        .sidebar {{
            width: 250px;
            background-color: var(--card-color);
            padding: 1.5rem 0;
            border-right: 1px solid var(--border-color);
        }}

        .sidebar-link {{
            display: flex;
            align-items: center;
            padding: 0.75rem 1.5rem;
            color: var(--text-color);
            text-decoration: none;
            transition: background-color 0.15s ease;
        }}

        .sidebar-link:hover {{
            background-color: var(--background-color);
        }}

        .sidebar-link.active {{
            border-left: 3px solid var(--primary-color);
            background-color: var(--background-color);
        }}

        .main-content {{
            flex: 1;
            padding: 1.5rem;
            overflow-y: auto;
        }}

        .profile-page-wrapper {{
            display: flex;
            min-height: 100vh;
            flex-direction: column;
        }}

        .profile-container {{
            max-width: 1200px;
            margin: 0 auto;
            padding: 1.5rem;
            width: 100%;
        }}

        /* Yandex ID specific styles */
        .profile-content-wrapper {{
            display: flex;
            min-height: calc(100vh - 64px);
        }}

        .profile-main-content {{
            flex-grow: 1;
            padding: 24px;
            background-color: var(--id-color-surface-submerged);
        }}

        .Section_root__zl60G {{
            background: var(--id-color-surface-elevated-0);
            padding: 24px;
            margin-bottom: 6px;
            border-radius: var(--id-card-border-radius);
        }}

        .Section_inner__N7MeR {{
            max-width: 520px;
        }}

        .Heading_root__P0ine {{
            margin-bottom: 16px;
        }}

        .Text_root__J8eOj {{
            display: block;
        }}

        .Text_root__J8eOj[data-variant=""heading-m""] {{
            font: var(--id-typography-heading-m);
        }}

        .Text_root__J8eOj[data-variant=""text-m""] {{
            font: var(--id-typography-text-m);
        }}

        .Text_root__J8eOj[data-variant=""text-s""] {{
            font: var(--id-typography-text-s);
        }}

        .Text_root__J8eOj[data-variant=""text-xs""] {{
            font: var(--id-typography-text-xs);
        }}

        .Text_root__J8eOj[data-color=""secondary""] {{
            color: var(--text-muted);
        }}

        .Text_root__J8eOj[data-color=""tertiary""] {{
            color: #999;
        }}

        .UnstyledListItem_root__xsw4w {{
            padding: 12px 0;
        }}

        .UnstyledListItem_inner__Td3gb {{
            display: flex;
            justify-content: space-between;
            align-items: center;
        }}

        .Slot_root__jYlNI {{
            display: flex;
        }}

        .Slot_direction_vertical__I3MEt {{
            flex-direction: column;
        }}

        .Slot_direction_horizontal__aDFeG {{
            flex-direction: row;
        }}

        .Slot_content__XYDYF {{
            flex: 1;
        }}

        .alignment-center_root__ndulA {{
            align-items: center;
        }}

        .alignment-top_root____eiv {{
            align-items: flex-start;
        }}

        .Button_root__rneDS {{
            font-family: 'YS Text', 'Helvetica Neue', Arial, sans-serif;
            position: relative;
            display: inline-flex;
            align-items: center;
            justify-content: center;
            box-sizing: border-box;
            font-weight: 500;
            cursor: pointer;
            transition: 0.1s ease-out;
            text-decoration: none;
            border-radius: 8px;
        }}

        .text-button_root__doKoA {{
            background: transparent;
            color: var(--primary-color);
            border: none;
            padding: 0;
        }}

        .text-button_root__doKoA:hover {{
            color: var(--primary-dark);
            background: transparent;
        }}

        .size-m_root___r3aA {{
            font-size: 16px;
            line-height: 20px;
        }}

        .size-s_root__CoSn6 {{
            font-size: 14px;
            line-height: 18px;
        }}

        .variant-default_root__xWqkR {{
            background-color: var(--primary-color);
            color: white;
            border: none;
            padding: 13px 20px;
        }}

        .variant-default_root__xWqkR:hover {{
            background-color: var(--primary-dark);
        }}

        .size-l_root__PsIsm {{
            font-size: 18px;
            line-height: 22px;
            padding: 16px 24px;
        }}

        .user-avatar_root__CsKdB {{
            position: relative;
            display: inline-block;
            overflow: hidden;
            border-radius: 50%;
        }}

        .user-avatar_root_isBig__RozUb {{
            --id-avatar-size: 96px;
            width: var(--id-avatar-size);
            height: var(--id-avatar-size);
        }}

        .avatar_root__qDicj {{
            width: 100%;
            height: 100%;
            object-fit: cover;
        }}

        .profile-card_root__hJtgV {{
            display: flex;
            flex-direction: column;
            align-items: center;
            text-align: center;
        }}

        .profile-card_avatar__xb4bd {{
            margin-bottom: 8px;
        }}

        .profile-card_title__zZCrX {{
            font: var(--id-typography-heading-l);
            font-weight: 500;
            margin-bottom: 4px;
        }}

        .profile-card_description__nvlpy {{
            font: var(--id-typography-text-m);
        }}

        .bulleted-list_root__k0lgY {{
            padding: 0;
            margin: 0;
            list-style: none;
        }}

        .bulleted-list-item_root__1Y90C {{
            position: relative;
            padding-left: 0;
        }}

        .bulleted-list-item_root__1Y90C:not(:last-child)::after {{
            content: '•';
            margin: 0 6px;
            color: var(--text-muted);
        }}

        .bulleted-list-item_root__1Y90C:first-child {{
            padding-left: 0;
        }}

        .List_root__yESwN {{
            list-style: none;
            padding: 0;
            margin: 0;
        }}

        .unstyled-badge_root__1gOSr {{
            display: inline-flex;
            align-items: center;
            justify-content: center;
            padding: 4px 8px;
            border-radius: 4px;
            background-color: rgba(0, 0, 0, 0.05);
            margin: 2px;
        }}

        .sidebar-navigation_root__2HXQL {{
            padding: 16px 0;
        }}

        .sidebar-navigation_list__R_7Wh {{
            list-style: none;
            padding: 0;
            margin: 0;
        }}

        .sidebar-navigation_item__GvUUF {{
            margin-bottom: 4px;
        }}

        .base-item_root__Z_6ST {{
            display: flex;
            align-items: center;
            gap: 12px;
            padding: 12px 16px;
            color: var(--text-color);
            text-decoration: none;
            border-radius: 8px;
            margin: 0 8px;
        }}

        .base-item_root__Z_6ST:hover {{
            background-color: rgba(0, 0, 0, 0.05);
        }}

        .navigation-item-link_root_isActive__QZ9Ea {{
            background-color: rgba(252, 63, 29, 0.1);
            color: var(--primary-color);
        }}

        .svg-icon {{
            flex-shrink: 0;
        }}

        @media (max-width: 640px) {{
            .container {{
                margin: 1rem auto;
            }}
            
            .card {{
                border-radius: 0.5rem;
            }}
            
            .card-header, .card-body, .card-footer {{
                padding: 1rem;
            }}

            .sidebar {{
                width: 100%;
                border-right: none;
                border-bottom: 1px solid var(--border-color);
                padding: 0.75rem 0;
            }}

            .page-wrapper {{
                flex-direction: column;
            }}
            
            .profile-content-wrapper {{
                flex-direction: column;
            }}
            
            .profile-main-content {{
                padding: 16px;
            }}
            
            .Section_root__zl60G {{
                padding: 16px;
            }}
        }}

        .fade-in {{
            animation: fadeIn 0.3s ease-in-out;
        }}

        @keyframes fadeIn {{
            from {{ opacity: 0; transform: translateY(10px); }}
            to {{ opacity: 1; transform: translateY(0); }}
        }}

        .loader {{
            border: 2px solid rgba(252, 63, 29, 0.1);
            border-radius: 50%;
            border-top: 2px solid var(--primary-color);
            width: 24px;
            height: 24px;
            animation: spin 1s linear infinite;
            margin: 0 auto;
            display: inline-block;
        }}

        @keyframes spin {{
            0% {{ transform: rotate(0deg); }}
            100% {{ transform: rotate(360deg); }}
        }}

        /* Social login buttons */
        .social-buttons {{
            display: flex;
            justify-content: center;
            gap: 1rem;
            margin-top: 1.5rem;
        }}

        .social-button {{
            width: 40px;
            height: 40px;
            border-radius: 50%;
            display: flex;
            align-items: center;
            justify-content: center;
            background-color: rgba(255, 255, 255, 0.1);
            cursor: pointer;
            transition: background-color 0.15s ease;
        }}

        .social-button:hover {{
            background-color: rgba(255, 255, 255, 0.2);
        }}

        .divider {{
            display: flex;
            align-items: center;
            text-align: center;
            margin: 1.5rem 0;
        }}

        .divider::before,
        .divider::after {{
            content: '';
            flex: 1;
            border-bottom: 1px solid var(--border-color);
        }}

        .divider span {{
            padding: 0 0.75rem;
            color: var(--text-muted);
        }}

        /* Yandex specific elements */
        .yandex-id-header {{
            display: flex;
            align-items: center;
            justify-content: center;
            margin-bottom: 1.5rem;
        }}

        .yandex-id-header img {{
            height: 32px;
        }}

        .auth-footer {{
            text-align: center;
            margin-top: 1.5rem;
            font-size: 0.875rem;
            color: var(--text-muted);
        }}

        /* QR code login styling */
        .qr-login-container {{
            text-align: center;
        }}

        .qr-login-container .qr-code {{
            padding: 1rem;
            background-color: white;
            border-radius: 0.75rem;
            display: inline-flex;
        }}

        .secondary-option {{
            background-color: rgba(255, 255, 255, 0.1);
            color: white;
            border: none;
            border-radius: 0.5rem;
            padding: 0.75rem;
            margin-top: 1rem;
            cursor: pointer;
            transition: background-color 0.15s ease;
            width: 100%;
            text-align: center;
            display: flex;
            align-items: center;
            justify-content: center;
            gap: 0.5rem;
        }}

        .secondary-option:hover {{
            background-color: rgba(255, 255, 255, 0.15);
        }}
    </style>
</head>
<body class=""{2}"">
    <div class=""navbar"">
        <a href=""/"" class=""logo"">BRU AVTOPARK</a>
        <button class=""theme-toggle"" id=""themeToggle"" aria-label=""Toggle dark mode"">🌙</button>
    </div>
    {1}
    <script>
        // Theme toggling functionality
        const themeToggleBtn = document.getElementById('themeToggle');
        const prefersDarkScheme = window.matchMedia('(prefers-color-scheme: dark)');
        
        // Check for saved theme preference or use the system preference
        const currentTheme = localStorage.getItem('theme') || (prefersDarkScheme.matches ? 'dark' : 'light');
        
        // Set initial theme
        if (currentTheme === 'dark') {{
            document.body.setAttribute('data-theme', 'dark');
            themeToggleBtn.textContent = '☀️';
        }} else {{
            document.body.removeAttribute('data-theme');
            themeToggleBtn.textContent = '🌙';
        }}
        
        // Toggle theme when the button is clicked
        themeToggleBtn.addEventListener('click', function() {{
            let theme = 'light';
            
            if (!document.body.hasAttribute('data-theme')) {{
                document.body.setAttribute('data-theme', 'dark');
                themeToggleBtn.textContent = '☀️';
                theme = 'dark';
            }} else {{
                document.body.removeAttribute('data-theme');
                themeToggleBtn.textContent = '🌙';
            }}
            
            localStorage.setItem('theme', theme);
        }});
    </script>
</body>
</html>";

        private string RenderLoginForm(string? error = null, string? message = null) => string.Format(BaseHtmlTemplate, "Login - BRU AVTOPARK", $@"
            <div class=""login-container fade-in"">
                <div class=""auth-card"">
                    <div class=""card-body"">
                        <div class=""yandex-id-header"">
                            <div style=""display: flex; align-items: center;"">
                                <div style=""width: 24px; height: 24px; background-color: var(--primary-color); border-radius: 4px; margin-right: 8px;""></div>
                                <span style=""color: white; font-weight: 500; font-size: 1.5rem;"">BRU ID</span>
                            </div>
                        </div>
                        
                        <h2 style=""text-align: center; margin-bottom: 1.5rem; color: white;"">Войдите с BRU ID</h2>
                        
                        {(error != null ? $@"<div class=""error-message"">{error}</div>" : "")}
                        {(message != null ? $@"<div class=""success-message"">{message}</div>" : "")}
                        
                        <form method=""POST"" action=""/api/auth/login"" id=""loginForm"">
                            <div class=""form-group"">
                                <label for=""username"">Username</label>
                                <input type=""text"" id=""username"" name=""username"" required placeholder=""Enter your username"">
                            </div>
                            <div class=""form-group"">
                                <label for=""password"">Password</label>
                                <input type=""password"" id=""password"" name=""password"" required placeholder=""Enter your password"">
                            </div>
                            <button type=""button"" onclick=""submitLoginForm()"" id=""loginButton"">Log in</button>
                            
                            <div class=""secondary-option"" style=""margin-top: 1rem;"" onclick=""window.location.href='/api/auth/webauthn/login'"">
                                <svg width=""20"" height=""20"" viewBox=""0 0 24 24"" fill=""none"" xmlns=""http://www.w3.org/2000/svg"">
                                    <path d=""M12 12.75C8.83 12.75 6.25 10.17 6.25 7C6.25 3.83 8.83 1.25 12 1.25C15.17 1.25 17.75 3.83 17.75 7C17.75 10.17 15.17 12.75 12 12.75ZM12 2.75C9.66 2.75 7.75 4.66 7.75 7C7.75 9.34 9.66 11.25 12 11.25C14.34 11.25 16.25 9.34 16.25 7C16.25 4.66 14.34 2.75 12 2.75Z"" fill=""white""/>
                                    <path d=""M20.5901 22.75C20.1801 22.75 19.8401 22.41 19.8401 22C19.8401 18.55 16.3601 15.75 12.0001 15.75C7.64008 15.75 4.16008 18.55 4.16008 22C4.16008 22.41 3.82008 22.75 3.41008 22.75C3.00008 22.75 2.66008 22.41 2.66008 22C2.66008 17.73 6.85008 14.25 12.0001 14.25C17.1501 14.25 21.3401 17.73 21.3401 22C21.3401 22.41 21.0001 22.75 20.5901 22.75Z"" fill=""white""/>
                                </svg>
                                <span>Login with Security Key</span>
                            </div>
                            
                            <div class=""secondary-option"" style=""margin-top: 0.5rem;"" onclick=""window.location.href='/api/auth/qr/login'"">
                                <svg width=""20"" height=""20"" viewBox=""0 0 24 24"" fill=""none"" xmlns=""http://www.w3.org/2000/svg"">
                                    <path d=""M3 3H9V9H3V3Z"" stroke=""white"" stroke-width=""2""/>
                                    <path d=""M15 3H21V9H15V3Z"" stroke=""white"" stroke-width=""2""/>
                                    <path d=""M3 15H9V21H3V15Z"" stroke=""white"" stroke-width=""2""/>
                                    <path d=""M15 15H21V21H15V15Z"" stroke=""white"" stroke-width=""2""/>
                                </svg>
                                <span>Login with QR Code</span>
                            </div>
                        </form>
                        
                        <div id=""statusDiv"" class=""mt-3""></div>
                        
                        <div class=""divider"">
                            <span>or</span>
                        </div>
                        
                        <div class=""social-buttons"">
                            <div class=""social-button"">
                                <svg width=""20"" height=""20"" viewBox=""0 0 20 20"" fill=""none"" xmlns=""http://www.w3.org/2000/svg"">
                                    <path d=""M10.0001 1.66667C5.40008 1.66667 1.66675 5.40001 1.66675 10C1.66675 14.6 5.40008 18.3333 10.0001 18.3333C14.6001 18.3333 18.3334 14.6 18.3334 10C18.3334 5.40001 14.6001 1.66667 10.0001 1.66667ZM13.2334 13.9C13.1334 14.0333 13.0001 14.1333 12.8667 14.1667C12.7334 14.2 12.6001 14.2 12.4667 14.1333C11.3334 13.5333 10.3001 12.7333 9.46675 11.7C8.73341 10.8333 8.13341 9.86667 7.70008 8.8C7.56675 8.46667 7.63341 8.1 7.90008 7.83334C8.00008 7.73334 8.10008 7.63334 8.23341 7.56667C8.53341 7.36667 8.90008 7.4 9.16675 7.66667C9.30008 7.8 9.40008 7.96667 9.50008 8.13334C9.66675 8.43334 9.60008 8.76667 9.36675 9.00001C9.30008 9.06667 9.26675 9.13334 9.20008 9.2C9.13341 9.26667 9.13341 9.36667 9.16675 9.46667C9.50008 10.2 10.0001 10.8 10.7001 11.2333C10.8001 11.3 10.9001 11.3333 11.0334 11.2667C11.2334 11.1667 11.3334 11 11.5001 10.9C11.8001 10.7 12.1334 10.7333 12.4001 10.9667C12.7001 11.2333 13.0001 11.5 13.2667 11.8C13.5001 12.1333 13.4667 12.5333 13.2334 12.8667V13.9Z"" fill=""white""/>
                                </svg>
                            </div>
                            <div class=""social-button"">
                                <svg width=""20"" height=""20"" viewBox=""0 0 20 20"" fill=""none"" xmlns=""http://www.w3.org/2000/svg"">
                                    <path d=""M18.1667 10.1828C18.1667 9.48283 18.1167 8.98283 18.0083 8.46616H10.3333V11.4662H14.8833C14.8 12.2328 14.3 13.3828 13.2417 14.1328L13.2267 14.2328L15.7083 16.1328L15.875 16.1495C17.3583 14.7995 18.1667 12.6828 18.1667 10.1828Z"" fill=""white""/>
                                    <path d=""M10.3332 18.3333C12.425 18.3333 14.1582 17.6833 15.4082 16.1333L12.7415 14.1333C12.0582 14.6333 11.1499 14.9833 10.3332 14.9833C8.19987 14.9833 6.40819 13.5833 5.84987 11.7H5.75404L3.16654 13.6833L3.13321 13.7833C4.37487 16.45 7.13321 18.3333 10.3332 18.3333Z"" fill=""white""/>
                                    <path d=""M5.85002 11.7C5.70002 11.1833 5.61669 10.6333 5.61669 10.0667C5.61669 9.5 5.70002 8.95 5.84169 8.43333L5.83669 8.32667L3.21669 6.31667L3.13335 6.35C2.55835 7.46667 2.23335 8.73333 2.23335 10.0667C2.23335 11.4 2.55835 12.6667 3.13335 13.7833L5.85002 11.7Z"" fill=""white""/>
                                    <path d=""M10.3332 5.15C11.6832 5.15 12.5999 5.75 13.1249 6.25L15.5082 3.95C14.1582 2.71667 12.4249 2 10.3332 2C7.13321 2 4.37487 3.88333 3.13321 6.55L5.84154 8.63333C6.40821 6.75 8.19987 5.15 10.3332 5.15Z"" fill=""white""/>
                                </svg>
                            </div>
                        </div>
                    </div>
                </div>
                
                <div class=""auth-footer"">
                    <div style=""margin-top: 2rem; display: flex; justify-content: center;"">
                        <a href=""/api/auth/register"" class=""link"" style=""color: white; margin: 0 0.5rem;"">Create account</a>
                        <span style=""color: #555;"">|</span>
                        <a href=""/api/auth/magic-link"" class=""link"" style=""color: white; margin: 0 0.5rem;"">Magic Link</a>
                    </div>
                    <div style=""margin-top: 1rem; color: #555;"">
                        BRU ID — ключ от всех сервисов
                    </div>
                </div>
            </div>
            <script>
                function submitLoginForm() {{
                    // Disable button to prevent multiple submissions
                    document.getElementById('loginButton').disabled = true;
                    document.getElementById('statusDiv').innerHTML = '<div class=""text-center""><div class=""loader""></div><p>Logging in...</p></div>';
                    
                    // Get form data
                    const username = document.getElementById('username').value;
                    const password = document.getElementById('password').value;
                    
                    // Submit form using fetch instead of form submission to avoid WebSocket issues
                    fetch('/api/auth/login', {{
                        method: 'POST',
                        headers: {{
                            'Content-Type': 'application/x-www-form-urlencoded',
                        }},
                        body: `username=${{encodeURIComponent(username)}}&password=${{encodeURIComponent(password)}}`,
                        credentials: 'same-origin'
                    }})
                    .then(response => {{
                        if (response.redirected) {{
                            // Follow redirect
                            window.location.href = response.url;
                            return;
                        }}
                        return response.json();
                    }})
                    .then(data => {{
                        if (data && data.success) {{
                            // Store token if present
                            if (data.data && data.data.token) {{
                                localStorage.setItem('auth_token', data.data.token);
                            }}
                            
                            // Show success message
                            document.getElementById('statusDiv').innerHTML = '<p class=""success-message"">Login successful! Redirecting...</p>';
                            
                            // Redirect to profile or success page
                            setTimeout(() => {{
                                window.location.href = '/api/auth/profile';
                            }}, 1000);
                        }} else if (data) {{
                            // Show error message
                            document.getElementById('statusDiv').innerHTML = `<p class=""error-message"">${{data.message || 'Login failed'}}</p>`;
                            document.getElementById('loginButton').disabled = false;
                        }}
                    }})
                    .catch(error => {{
                        console.error('Login error:', error);
                        document.getElementById('statusDiv').innerHTML = `<p class=""error-message"">Error: ${{error.message || 'Unknown error'}}</p>`;
                        document.getElementById('loginButton').disabled = false;
                    }});
                    
                    return false;
                }}
                
                // Allow form submission with Enter key
                document.getElementById('loginForm').addEventListener('keypress', function(event) {{
                    if (event.key === 'Enter') {{
                        event.preventDefault();
                        submitLoginForm();
                    }}
                }});
            </script>
        ", "auth-page-body");

        private string RenderTotpSetup(string qrCodeUri, string secretKey) => string.Format(BaseHtmlTemplate, "TOTP Setup - BRU AVTOPARK", $@"
            <div class=""container fade-in"">
                <div class=""card"">
                    <div class=""card-header"">
                        <h1>Two-Factor Authentication Setup</h1>
                    </div>
                    <div class=""card-body"">
                        <div class=""info-box"">
                            <p>Set up two-factor authentication to increase your account security. Scan the QR code with your authenticator app (like Google Authenticator, Authy, or Microsoft Authenticator).</p>
                        </div>
                        <div class=""qr-code"">
                            <img src=""{qrCodeUri}"" alt=""TOTP QR Code"">
                        </div>
                        <div class=""text-center my-4"">
                            <p>Can't scan the QR code? Manually enter this code in your app:</p>
                            <div class=""code-display text-center"">{secretKey}</div>
                        </div>
                        <form method=""POST"" action=""/api/auth/totp/verify"">
                            <div class=""form-group"">
                                <label for=""code"">Enter the 6-digit code from your app</label>
                                <input type=""text"" id=""code"" name=""code"" required pattern=""[0-9]{{6}}"" placeholder=""Enter 6-digit code"" autocomplete=""one-time-code"">
                            </div>
                            <input type=""hidden"" name=""secretKey"" value=""{secretKey}"">
                            <button type=""submit"" class=""btn btn-block"">Verify and Enable</button>
                        </form>
                        <div class=""text-center mt-4"">
                            <a href=""/api/auth/profile"" class=""link"">Back to Profile</a>
                        </div>
                    </div>
                </div>
            </div>
        ", "");

        private string RenderWebAuthnRegistration(string options) => string.Format(BaseHtmlTemplate, "Register Security Key - BRU AVTOPARK", $@"
            <div class=""container fade-in"">
                <div class=""card"">
                    <div class=""card-header"">
                        <h1>Register Security Key</h1>
                    </div>
                    <div class=""card-body"">
                        <div class=""info-box"">
                            <p>Register your security key or biometric authentication (fingerprint, face ID) for passwordless login.</p>
                        </div>
                        <div id=""options"" data-options=""{options}"" class=""hidden""></div>
                        <div class=""flex flex-col items-center gap-4 my-4"">
                            <div id=""status"" class=""text-center"">
                                <p>Click the button below to register your security key.</p>
                            </div>
                            <button onclick=""registerWebAuthn()"" id=""registerButton"" class=""btn"">Register Security Key</button>
                            <div id=""loader"" class=""loader hidden""></div>
                        </div>
                        <div class=""text-center mt-4"">
                            <a href=""/api/auth/profile"" class=""link"">Back to Profile</a>
                        </div>
                        <script>
                            async function registerWebAuthn() {{
                                try {{
                                    document.getElementById('registerButton').disabled = true;
                                    document.getElementById('loader').classList.remove('hidden');
                                    document.getElementById('status').innerHTML = '<p>Please follow your browser\'s instructions to register your security key...</p>';
                                    
                                    const options = JSON.parse(document.getElementById('options').dataset.options);
                                    const credential = await navigator.credentials.create({{
                                        publicKey: options.publicKey
                                    }});
                                    
                                    // Prepare the credential response for the server
                                    const credentialResponse = {{
                                        id: credential.id,
                                        rawId: arrayBufferToBase64(credential.rawId),
                                        type: credential.type,
                                        response: {{
                                            attestationObject: arrayBufferToBase64(credential.response.attestationObject),
                                            clientDataJSON: arrayBufferToBase64(credential.response.clientDataJSON)
                                        }}
                                    }};
                                    
                                    // Send the credential to the server
                                    const response = await fetch('/api/auth/webauthn/register/complete', {{
                                        method: 'POST',
                                        headers: {{ 'Content-Type': 'application/json' }},
                                        body: JSON.stringify({{ attestationResponse: credentialResponse }})
                                    }});
                                    
                                    if (response.ok) {{
                                        document.getElementById('status').innerHTML = '<p class=""success-message"">Security key registered successfully!</p>';
                                        setTimeout(() => {{
                                            window.location.href = '/api/auth/profile';
                                        }}, 1500);
                                    }} else {{
                                        const error = await response.json();
                                        throw new Error(error.message || 'Registration failed');
                                    }}
                                }} catch (error) {{
                                    console.error('WebAuthn registration failed:', error);
                                    document.getElementById('status').innerHTML = `<p class=""error-message"">Failed to register security key: ${{error.message || error}}</p>`;
                                }} finally {{
                                    document.getElementById('registerButton').disabled = false;
                                    document.getElementById('loader').classList.add('hidden');
                                }}
                            }}
                            
                            // Helper function to convert ArrayBuffer to Base64 string
                            function arrayBufferToBase64(buffer) {{
                                const bytes = new Uint8Array(buffer);
                                let binary = '';
                                for (let i = 0; i < bytes.byteLength; i++) {{
                                    binary += String.fromCharCode(bytes[i]);
                                }}
                                return btoa(binary);
                            }}
                        </script>
                        <style>
                            .hidden {{
                                display: none;
                            }}
                        </style>
                    </div>
                </div>
            </div>
        ", "");

        private string RenderMagicLinkForm(string? error = null, string? message = null) => string.Format(BaseHtmlTemplate, "Magic Link - BRU AVTOPARK", $@"
            <div class=""login-container fade-in"">
                <div class=""auth-card"">
                    <div class=""card-body"">
                        <div class=""yandex-id-header"">
                            <div style=""display: flex; align-items: center;"">
                                <div style=""width: 24px; height: 24px; background-color: var(--primary-color); border-radius: 4px; margin-right: 8px;""></div>
                                <span style=""color: white; font-weight: 500; font-size: 1.5rem;"">BRU ID</span>
                            </div>
                        </div>
                        
                        <h2 style=""text-align: center; margin-bottom: 1.5rem; color: white;"">Login with Magic Link</h2>
                        
                        {(error != null ? $@"<div class=""error-message"">{error}</div>" : "")}
                        {(message != null ? $@"<div class=""success-message"">{message}</div>" : "")}
                        
                        <div class=""info-box"" style=""background-color: rgba(255, 255, 255, 0.1); color: #b3b3b3;"">
                            <p>Enter your email address to receive a secure login link. No password required!</p>
                        </div>
                        
                        <form method=""POST"" action=""/api/auth/magic-link/send"">
                            <div class=""form-group"">
                                <label for=""email"">Email Address</label>
                                <input type=""email"" id=""email"" name=""email"" required placeholder=""Enter your email address"">
                            </div>
                            <button type=""submit"">Send Magic Link</button>
                        </form>
                        
                        <div class=""divider"">
                            <span>or</span>
                        </div>
                        
                        <div class=""secondary-option"" onclick=""window.location.href='/api/auth/login'"">
                            <svg width=""20"" height=""20"" viewBox=""0 0 20 20"" fill=""none"" xmlns=""http://www.w3.org/2000/svg"">
                                <path d=""M10 9.58301C12.1047 9.58301 13.8095 7.87818 13.8095 5.77348C13.8095 3.66877 12.1047 1.96394 10 1.96394C7.8953 1.96394 6.19047 3.66877 6.19047 5.77348C6.19047 7.87818 7.8953 9.58301 10 9.58301Z"" stroke=""white"" stroke-width=""1.5"" stroke-linecap=""round"" stroke-linejoin=""round""/>
                                <path d=""M17.1432 18.0357C17.1432 14.9167 13.9384 12.3809 10.0003 12.3809C6.06211 12.3809 2.85742 14.9167 2.85742 18.0357"" stroke=""white"" stroke-width=""1.5"" stroke-linecap=""round"" stroke-linejoin=""round""/>
                            </svg>
                            <span>Back to Login</span>
                        </div>
                    </div>
                </div>
            </div>
        ", "auth-page-body");

        private string RenderQrLogin(string qrCode) => string.Format(BaseHtmlTemplate, "QR Login - BRU AVTOPARK", $@"
            <div class=""login-container fade-in"">
                <div class=""auth-card"">
                    <div class=""card-body"">
                        <div class=""yandex-id-header"">
                            <div style=""display: flex; align-items: center;"">
                                <div style=""width: 24px; height: 24px; background-color: var(--primary-color); border-radius: 4px; margin-right: 8px;""></div>
                                <span style=""color: white; font-weight: 500; font-size: 1.5rem;"">BRU ID</span>
                            </div>
                        </div>
                        
                        <h2 style=""text-align: center; margin-bottom: 1.5rem; color: white;"">Login with QR Code</h2>
                        
                        <div class=""info-box"" style=""background-color: rgba(255, 255, 255, 0.1); color: #b3b3b3;"">
                            <p>Scan this QR code with your mobile device to log in instantly without entering your password.</p>
                        </div>
                        
                        <div class=""qr-code qr-login-container"">
                            <img src=""data:image/png;base64,{qrCode}"" alt=""Login QR Code"">
                        </div>
                        
                        <div id=""status"" class=""text-center my-4"" style=""color: #b3b3b3;"">
                            <p>Waiting for you to scan the QR code...</p>
                            <div class=""loader"" style=""margin-top: 1rem;""></div>
                        </div>
                        
                        <div class=""secondary-option"" onclick=""window.location.href='/api/auth/login'"">
                            <svg width=""20"" height=""20"" viewBox=""0 0 20 20"" fill=""none"" xmlns=""http://www.w3.org/2000/svg"">
                                <path d=""M10 9.58301C12.1047 9.58301 13.8095 7.87818 13.8095 5.77348C13.8095 3.66877 12.1047 1.96394 10 1.96394C7.8953 1.96394 6.19047 3.66877 6.19047 5.77348C6.19047 7.87818 7.8953 9.58301 10 9.58301Z"" stroke=""white"" stroke-width=""1.5"" stroke-linecap=""round"" stroke-linejoin=""round""/>
                                <path d=""M17.1432 18.0357C17.1432 14.9167 13.9384 12.3809 10.0003 12.3809C6.06211 12.3809 2.85742 14.9167 2.85742 18.0357"" stroke=""white"" stroke-width=""1.5"" stroke-linecap=""round"" stroke-linejoin=""round""/>
                            </svg>
                            <span>Back to Login</span>
                        </div>
                    </div>
                </div>

                <script>
                    function checkLoginStatus(deviceId) {{
                        fetch(`/api/auth/qr/direct/check?deviceId=${{deviceId}}`)
                            .then(response => response.json())
                            .then(data => {{
                                if (data.success && data.data && data.data.token) {{
                                    document.getElementById('status').innerHTML = '<p class=""success-message"">Login successful! Redirecting...</p>';
                                    // Store token in localStorage
                                    localStorage.setItem('auth_token', data.data.token);
                                    setTimeout(() => {{
                                        window.location.href = `/api/auth/success?token=${{data.data.token}}`;
                                    }}, 1000);
                                }} else {{
                                    setTimeout(() => checkLoginStatus(deviceId), 2000);
                                }}
                            }})
                            .catch(error => {{
                                console.error('Error checking login status:', error);
                                document.getElementById('status').innerHTML = `<p class=""error-message"">Error checking login status: ${{error.message || 'Unknown error'}}</p>`;
                                setTimeout(() => checkLoginStatus(deviceId), 5000);
                            }});
                    }}
                    
                    // Start polling when page loads
                    const deviceId = new URLSearchParams(window.location.search).get('deviceId');
                    if (deviceId) {{
                        checkLoginStatus(deviceId);
                    }}
                </script>
            </div>
        ", "auth-page-body");

        private bool IsBrowserRequest()
        {
            var accept = Request.Headers.Accept.ToString().ToLower();
            return accept.Contains("text/html") || accept.Contains("*/*");
        }

        // Add this before the end of the HTML Templates region

        private string RenderOAuthLoginForm(string requestId, string clientName, string[] scopes, string? error = null) => string.Format(BaseHtmlTemplate, "Login to " + clientName, $@"
            <div class=""info-box"">
                <p>The application <strong>{clientName}</strong> is requesting access to your account.</p>
            </div>
            
            {(error != null ? $@"<div class=""error-message"">{error}</div>" : "")}
            
            <div class=""card my-4"" style=""box-shadow: none; border: 1px solid var(--border-color);"">
                <div class=""card-header"">
                    <h2>Requested Permissions</h2>
                </div>
                <div class=""card-body"">
                    <ul style=""padding-left: 1.5rem;"">
                        {string.Join("", scopes.Select(scope => $@"<li>{FormatScope(scope)}</li>"))}
                    </ul>
                </div>
            </div>
            
            <form method=""POST"" action=""/api/auth/connect/authorize/callback"">
                <input type=""hidden"" name=""requestId"" value=""{requestId}"">
                
                <div class=""form-group"">
                    <label for=""username"">Username</label>
                    <input type=""text"" id=""username"" name=""username"" required placeholder=""Enter your username"">
                </div>
                <div class=""form-group"">
                    <label for=""password"">Password</label>
                    <input type=""password"" id=""password"" name=""password"" required placeholder=""Enter your password"">
                </div>
                <button type=""submit"" class=""btn btn-block"">Login & Authorize</button>
            </form>
            
            <div class=""text-center mt-4"">
                <p class=""text-muted"">By continuing, you agree to share the requested information with {clientName}.</p>
            </div>
        ");

        // Add this helper method to format OAuth scopes for display
        private string FormatScope(string scope)
        {
            switch (scope.ToLower())
            {
                case "openid":
                    return "Basic access to your account information";
                case "profile":
                    return "Your profile information (username, display name)";
                case "email":
                    return "Your email address";
                case "phone":
                    return "Your phone number";
                case "roles":
                    return "Your role information and permissions";
                case "offline_access":
                    return "Access to your account even when you're not logged in";
                default:
                    return scope;
            }
        }

        #endregion

        #region Traditional Authentication

        [HttpGet("login")]
        [AllowAnonymous]
        public IActionResult LoginPage([FromQuery] string? error = null, [FromQuery] string? message = null)
        {
            if (IsBrowserRequest())
            {
                return Content(RenderLoginForm(error, message), "text/html");
            }
            return Ok(new { message = "Use POST method to login" });
        }

        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromForm] LoginRequest request)
        {
            var result = await ProcessLoginRequest(request);
            
            if (IsBrowserRequest())
            {
                if (result is OkObjectResult okResult && okResult.Value is ApiResponse<LoginResponse> response)
                {
                    // If it's a successful login from a browser, redirect to success page with token
                    return Redirect($"/api/auth/success?token={Uri.EscapeDataString(response.Data!.Token)}");
                }
                
                // For failures, redirect back to login page with error
                string errorMessage = "Invalid credentials";
                if (result is UnauthorizedObjectResult unauthorizedResult && 
                    unauthorizedResult.Value is ApiResponse<object> errorResponse)
                {
                    errorMessage = errorResponse.Message ?? errorMessage;
                }
                
                return Redirect($"/api/auth/login?error={Uri.EscapeDataString(errorMessage)}");
            }
            
            // For API requests, return the original result
            return result;
        }

        protected bool IsAdmin()
        {
            try
            {
                var authHeader = Request.Headers["Authorization"].ToString();
                if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
                {
                    Log.Warning("Missing or invalid Authorization header");
                    return false;
                }

                var token = authHeader.Substring("Bearer ".Length);
                var tokenHandler = new JwtSecurityTokenHandler();
                var jwtToken = tokenHandler.ReadJwtToken(token);

                // Check primary role first (highest priority role)
                var primaryRoleClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "primary_role");
                if (primaryRoleClaim?.Value == "1") // Admin role has legacy ID 1
                {
                    return true;
                }

                // Fallback to checking all role claims
                var roleClaims = jwtToken.Claims.Where(c => c.Type == "role");
                return roleClaims.Any(c => c.Value == "1");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error checking admin status");
                return false;
            }
        }

        protected bool HasPermission(string permissionName)
        {
            try
            {
                var authHeader = Request.Headers["Authorization"].ToString();
                if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
                {
                    Log.Warning("Missing or invalid Authorization header");
                    return false;
                }

                var token = authHeader.Substring("Bearer ".Length);
                var tokenHandler = new JwtSecurityTokenHandler();
                var jwtToken = tokenHandler.ReadJwtToken(token);

                var permissionClaims = jwtToken.Claims.Where(c => c.Type == "permission");
                return permissionClaims.Any(c => c.Value == permissionName);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error checking permission: {Permission}", permissionName);
                return false;
            }
        }

        private async Task<IActionResult> ProcessLoginRequest(LoginRequest request)
        {
            // Existing login logic here
            try
            {
                _logger.LogInformation("Login attempt for user: {Username}", request.Username);

                if (!ModelState.IsValid)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Invalid request data",
                        Errors = ModelState.Values.SelectMany(v => v.Errors.Select(e => e.ErrorMessage)).ToList()
                    });
                }

                // Authenticate user
                var user = await _authService.AuthenticateAsync(request.Username, request.Password);
                if (user == null)
                {
                    _logger.LogWarning("Authentication failed for user: {Username}", request.Username);
                    return Unauthorized(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Invalid username or password"
                    });
                }

                var conn = _spacetimeService.GetConnection();
                
                // Check user settings for 2FA
                var userSettings = conn.Db.UserSettings.Iter()
                    .FirstOrDefault(s => s.UserId.Equals(user.UserId));
                
                if (userSettings == null)
                {
                    _logger.LogWarning("User settings not found for user: {Username}, creating default settings", request.Username);
                    // Create default user settings
                    conn.Reducers.CreateUserSettings(user.UserId);
                    
                    // Wait a moment for the reducer to complete
                    await Task.Delay(100);
                    
                    // Try to get the newly created settings
                    userSettings = conn.Db.UserSettings.Iter().FirstOrDefault(s => s.UserId.Equals(user.UserId));
                    
                    // If still null, proceed without 2FA
                    if (userSettings == null)
                    {
                        _logger.LogWarning("Failed to create user settings, proceeding without 2FA");
                        // Generate JWT token and continue
                        var jwtToken = GenerateJwtToken(user);
                        
                        return Ok(new ApiResponse<LoginResponse>
                        {
                            Success = true,
                            Message = "Authentication successful",
                            Data = new LoginResponse
                            {
                                Token = jwtToken,
                                User = new UserDto
                                {
                                    Id = user.LegacyUserId,
                                    Username = user.Login,
                                    Email = user.Email,
                                    PhoneNumber = user.PhoneNumber,
                                    Role = _authService.GetUserRole(user.UserId)
                                }
                            }
                        });
                    }
                }

                if (userSettings.TotpEnabled && !request.SkipTwoFactor)
                {
                    // Generate temporary token for 2FA
                    var tempToken = GenerateRandomToken();
                    var expiresAt = (ulong)DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeMilliseconds();
                    
                    // Store token in database with expiry
                    conn.Reducers.CreateTwoFactorToken(
                        user.UserId,
                        tempToken,
                        false,
                        expiresAt,
                        Request.Headers["User-Agent"].ToString(),
                        HttpContext.Connection.RemoteIpAddress?.ToString()
                    );
                    
                    return Ok(new ApiResponse<TwoFactorResponse>
                    {
                        Success = true,
                        Message = "Two-factor authentication required",
                        Data = new TwoFactorResponse
                        {
                            RequiresTwoFactor = true,
                            TwoFactorType = "totp",
                            TempToken = tempToken
                        }
                    });
                }
                
                if (userSettings.WebAuthnEnabled && !request.SkipTwoFactor)
                {
                    // Generate temporary token for WebAuthn
                    var tempToken = GenerateRandomToken();
                    var expiresAt = (ulong)DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeMilliseconds();
                    
                    // Store token in database with expiry
                    conn.Reducers.CreateTwoFactorToken(
                        user.UserId,
                        tempToken,
                        false,
                        expiresAt,
                        Request.Headers["User-Agent"].ToString(),
                        HttpContext.Connection.RemoteIpAddress?.ToString()
                    );
                    
                    // Get WebAuthn credentials for the user
                    var credentials = conn.Db.WebAuthnCredential.Iter()
                        .Where(c => c.UserId.Equals(user.UserId) && c.IsActive)
                        .ToList();

                    if (!credentials.Any())
                    {
                        _logger.LogWarning("No WebAuthn credentials found for user: {Username}", request.Username);
                        return BadRequest(new ApiResponse<object>
                        {
                            Success = false,
                            Message = "No WebAuthn credentials found"
                        });
                    }

                    // Create assertion options
                    var (success, options, _) = await _webAuthnService.GetAssertionOptionsAsync(user.Login);
                    if (!success || options == null)
                    {
                        _logger.LogWarning("Failed to create assertion options for user: {Username}", request.Username);
                        return BadRequest(new ApiResponse<object>
                        {
                            Success = false,
                            Message = "Failed to create assertion options"
                        });
                    }
                    
                    return Ok(new ApiResponse<WebAuthnTwoFactorResponse>
                    {
                        Success = true,
                        Message = "WebAuthn authentication required",
                        Data = new WebAuthnTwoFactorResponse
                        {
                            RequiresTwoFactor = true,
                            TwoFactorType = "webauthn",
                            TempToken = tempToken,
                            Options = options
                        }
                    });
                }

                // Generate JWT token
                var token = GenerateJwtToken(user);

                return Ok(new ApiResponse<LoginResponse>
                {
                    Success = true,
                    Message = "Authentication successful",
                    Data = new LoginResponse
                    {
                        Token = token,
                        User = new UserDto
                        {
                            Id = user.LegacyUserId,
                            Username = user.Login,
                            Email = user.Email,
                            PhoneNumber = user.PhoneNumber,
                            Role = _authService.GetUserRole(user.UserId)
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login for user: {Username}", request.Username);
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred during login"
                });
            }
        }

        [HttpPost("register")]
        [AllowAnonymous] // Allow anonymous and check manually
        public async Task<ActionResult<RegisterResponse>> Register([FromBody] RegisterRequest request)
        {
            try
            {
                _logger.LogInformation("Registration attempt for user: {Username}", request.Username);

                // Check admin status manually using JWT token
                bool isAdmin = false;
                string? token = null;

                // Check Authorization header
                if (Request.Headers.Authorization.Count > 0)
                {
                    var authHeader = Request.Headers.Authorization.ToString();
                    if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    {
                        token = authHeader.Substring("Bearer ".Length).Trim();
                    }
                }

                if (!string.IsNullOrEmpty(token))
                {
                    try
                    {
                        var handler = new JwtSecurityTokenHandler();
                        var jwtToken = handler.ReadJwtToken(token);

                        // Check for admin role in claims
                        var primaryRoleClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "primary_role");
                        isAdmin = primaryRoleClaim?.Value == "1" || 
                                jwtToken.Claims.Any(c => c.Type == "role" && c.Value == "1");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error validating JWT token for registration");
                        // Continue with isAdmin = false
                    }
                }

                if (!isAdmin)
                {
                    _logger.LogWarning("Unauthorized attempt to register a user without admin privileges");
                    return StatusCode(403, new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Administrator privileges required for user registration"
                    });
                }

                // Check if user already exists
                var existingUser = await _userService.GetUserByLoginAsync(request.Username);
                if (existingUser != null)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Username already exists"
                    });
                }

                // Check if role is valid - only admins can create admins
                if (request.Role == 1 && !isAdmin)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Only administrators can create administrator accounts"
                    });
                }

                // Register user - pass the identity from the token to ensure proper authorization
                string? identityFromToken = null;
                if (!string.IsNullOrEmpty(token))
                {
                    var handler = new JwtSecurityTokenHandler();
                    var jwtToken = handler.ReadJwtToken(token);
                    identityFromToken = jwtToken.Claims.FirstOrDefault(c => c.Type == "identity")?.Value;
                }

                try
                {
                    // Register user
                    var success = await _authService.RegisterAsync(
                        request.Username,
                        request.Password,
                        request.Role,
                        request.Email,
                        request.PhoneNumber
                    );

                    if (!success)
                    {
                        return BadRequest(new ApiResponse<object>
                        {
                            Success = false,
                            Message = "Failed to register user"
                        });
                    }
                }
                catch (Exception ex)
                {
                    // Check if the error is from SpacetimeDB role assignment
                    if (ex.Message?.Contains("Unauthorized") == true || 
                        ex.InnerException?.Message?.Contains("Unauthorized") == true)
                    {
                        _logger.LogWarning("Role assignment failed due to authorization: {Error}", ex.Message);
                        
                        // Check if the user was actually created despite the role error
                        var newUser = await _userService.GetUserByLoginAsync(request.Username);
                        if (newUser != null)
                        {
                            return Ok(new ApiResponse<RegisterResponse>
                            {
                                Success = true,
                                Message = "User created but could not assign requested role. Default user role applied.",
                                Data = new RegisterResponse
                                {
                                    User = new UserDto
                                    {
                                        Id = newUser.LegacyUserId,
                                        Username = newUser.Login,
                                        Email = newUser.Email,
                                        PhoneNumber = newUser.PhoneNumber,
                                        Role = _authService.GetUserRole(newUser.UserId)
                                    }
                                }
                            });
                        }
                        
                        return BadRequest(new ApiResponse<object>
                        {
                            Success = false,
                            Message = "User creation succeeded but role assignment failed. Try logging in to SpacetimeDB directly to assign a role."
                        });
                    }
                    
                    // For other errors, rethrow to be caught by outer catch
                    throw;
                }

                // Get the newly created user
                var newlyCreatedUser = await _userService.GetUserByLoginAsync(request.Username);
                if (newlyCreatedUser == null)
                {
                    return StatusCode(500, new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User was created but could not be retrieved"
                    });
                }

                _logger.LogInformation("User {Username} registered successfully", request.Username);

                return Ok(new ApiResponse<RegisterResponse>
                {
                    Success = true,
                    Message = "User registered successfully",
                    Data = new RegisterResponse
                    {
                        User = new UserDto
                        {
                            Id = newlyCreatedUser.LegacyUserId,
                            Username = newlyCreatedUser.Login,
                            Email = newlyCreatedUser.Email,
                            PhoneNumber = newlyCreatedUser.PhoneNumber,
                            Role = _authService.GetUserRole(newlyCreatedUser.UserId)
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during registration for user: {Username}", request.Username);
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred during registration"
                });
            }
        }

        #endregion

        #region TOTP (Time-based One-Time Password)

        [HttpGet("totp/setup")]
        [Authorize]
        public async Task<IActionResult> TotpSetup()
        {
            try
            {
                var userId = GetUserIdentity();
                if (userId == null)
                {
                    return Unauthorized(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not authenticated"
                    });
                }

                var user = await GetUserByIdentityAsync(userId);
                if (user == null)
                {
                    return NotFound(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not found"
                    });
                }

                var result = await _totpService.SetupTotpAsync(userId.Value, user.Login);
                if (!result.success || result.secretKey == null || result.qrCodeUri == null)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = result.errorMessage ?? "Failed to set up TOTP"
                    });
                }

                if (IsBrowserRequest())
                {
                    return Content(RenderTotpSetup(result.qrCodeUri, result.secretKey), "text/html");
                }

                return Ok(new ApiResponse<TotpSetupResponse>
                {
                    Success = true,
                    Message = "TOTP setup successful",
                    Data = new TotpSetupResponse
                    {
                        SecretKey = result.secretKey,
                        QrCodeUri = result.qrCodeUri
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting up TOTP");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while setting up TOTP"
                });
            }
        }

        [HttpGet("magic-link")]
        [AllowAnonymous]
        public IActionResult MagicLinkPage([FromQuery] string? error = null, [FromQuery] string? message = null)
        {
            if (IsBrowserRequest())
            {
                return Content(RenderMagicLinkForm(error, message), "text/html");
            }
            return Ok(new { message = "Use POST method to request magic link" });
        }

        [HttpPost("magic-link/send")]
        [AllowAnonymous]
        public async Task<IActionResult> SendMagicLink([FromBody] MagicLinkRequest jsonRequest, [FromForm] MagicLinkRequest? formRequest)
        {
            try
            {
                var request = formRequest ?? jsonRequest;
                var userAgent = Request.Headers["User-Agent"].ToString();
                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

                var (success, errorMessage) = await _magicLinkService.SendMagicLinkAsync(request.Email, userAgent, ipAddress);
                
                if (IsBrowserRequest())
                {
                    if (success)
                    {
                        return Content(RenderMagicLinkForm(null, "Magic link has been sent to your email"), "text/html");
                    }
                    return Content(RenderMagicLinkForm(errorMessage ?? "Failed to send magic link"), "text/html");
                }

                if (!success)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to send magic link"
                    });
                }

                return Ok(new ApiResponse<MagicLinkResponse>
                {
                    Success = true,
                    Message = "Magic link sent successfully",
                    Data = new MagicLinkResponse
                    {
                        Sent = true,
                        Email = request.Email
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending magic link");
                var errorMessage = "An error occurred while sending magic link";
                
                if (IsBrowserRequest())
                {
                    return Content(RenderMagicLinkForm(errorMessage), "text/html");
                }
                
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = errorMessage
                });
            }
        }

        [HttpGet("qr/login")]
        [AllowAnonymous]
        public async Task<IActionResult> QrLoginPage()
        {
            try
            {
                var deviceId = Guid.NewGuid().ToString();
                var (qrCodeBase64, _) = await _qrAuthService.GenerateDirectLoginQRCodeAsync("", "desktop");

                if (IsBrowserRequest())
                {
                    return Content(RenderQrLogin(qrCodeBase64), "text/html");
                }

                return Ok(new ApiResponse<QrCodeResponse>
                {
                    Success = true,
                    Message = "QR code generated successfully",
                    Data = new QrCodeResponse
                    {
                        QrCode = qrCodeBase64
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating QR code for login");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while generating QR code"
                });
            }
        }

        // Add success page renderer
        private string RenderSuccessPage(string token) => string.Format(BaseHtmlTemplate, "Login Successful - BRU AVTOPARK", $@"
            <div class=""success-message"" style=""display: flex; align-items: center; justify-content: center; gap: 10px;"">
                <svg width=""24"" height=""24"" viewBox=""0 0 24 24"" fill=""none"" xmlns=""http://www.w3.org/2000/svg"">
                    <path d=""M12 22C17.5228 22 22 17.5228 22 12C22 6.47715 17.5228 2 12 2C6.47715 2 2 6.47715 2 12C2 17.5228 6.47715 22 12 22Z"" stroke=""var(--success-color)"" stroke-width=""2""/>
                    <path d=""M8 12L11 15L16 9"" stroke=""var(--success-color)"" stroke-width=""2""/>
                </svg>
                <span>You have been successfully logged in!</span>
            </div>
            <div class=""text-center mt-4"">
                <a href=""/api/auth/profile"" class=""btn"">Go to Profile</a>
            </div>
            <script>
                // Store the token securely
                const token = '{token.Replace("'", "\\'")}';
                if (token && token !== 'null') {{
                    localStorage.setItem('auth_token', token);
                    
                    // Set up authorization header for future API requests
                    const authHeader = `Bearer ${{token}}`;
                    
                    // Automatically redirect to profile page after a short delay
                    setTimeout(() => {{
                        const profileUrl = '/api/auth/profile';
                        // Use fetch with the auth token to check if we can access the profile
                        fetch(profileUrl, {{
                            headers: {{
                                'Authorization': authHeader
                            }}
                        }})
                        .then(response => {{
                            if (response.ok) {{
                                window.location.href = profileUrl;
                            }} else {{
                                throw new Error('Failed to verify profile access');
                            }}
                        }})
                        .catch(error => {{
                            console.error('Error:', error);
                            // If there's an error, try redirecting with the token as a query parameter
                            window.location.href = `${{profileUrl}}?token=${{encodeURIComponent(token)}}`;
                        }});
                    }}, 1500);
                }}
            </script>
        ", "");

        [HttpGet("success")]
        [AllowAnonymous]
        public IActionResult SuccessPage([FromQuery] string? token = null)
        {
            if (IsBrowserRequest())
            {
                if (string.IsNullOrEmpty(token))
                {
                    return Redirect("/api/auth/login?error=No authentication token provided");
                }

                try
                {
                    // Validate token format
                    if (!token.Contains('.') || token.Count(c => c == '.') != 2)
                    {
                        return Redirect("/api/auth/login?error=Invalid token format");
                    }

                    return Content(RenderSuccessPage(token), "text/html");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error rendering success page");
                    return Redirect("/api/auth/login?error=Error processing login success");
                }
            }
            
            return Ok(new ApiResponse<object>
            {
                Success = true,
                Message = "Login successful"
            });
        }

        // Add error page renderer
        private string RenderErrorPage(string error) => string.Format(BaseHtmlTemplate, "Error - BRU AVTOPARK", $@"
            <div class=""error-message"" style=""display: flex; align-items: center; justify-content: center; gap: 10px;"">
                <svg width=""24"" height=""24"" viewBox=""0 0 24 24"" fill=""none"" xmlns=""http://www.w3.org/2000/svg"">
                    <path d=""M12 22C17.5228 22 22 17.5228 22 12C22 6.47715 17.5228 2 12 2C6.47715 2 2 6.47715 2 12C2 17.5228 6.47715 22 12 22Z"" stroke=""var(--error-color)"" stroke-width=""2""/>
                    <path d=""M12 8V12"" stroke=""var(--error-color)"" stroke-width=""2"" stroke-linecap=""round""/>
                    <circle cx=""12"" cy=""16"" r=""1"" fill=""var(--error-color)""/>
                </svg>
                <span>{error}</span>
            </div>
            <div class=""text-center mt-4"">
                <a href=""/api/auth/login"" class=""btn"">Back to Login</a>
            </div>");

        [HttpGet("error")]
        [AllowAnonymous]
        public IActionResult ErrorPage([FromQuery] string message)
        {
            if (IsBrowserRequest())
            {
                return Content(RenderErrorPage(message), "text/html");
            }
            return BadRequest(new { error = message });
        }

        [HttpPost("totp/verify")]
        [Authorize]
        public async Task<ActionResult<VerifyTotpResponse>> VerifyTotp([FromBody] VerifyTotpRequest request)
        {
            try
            {
                var userId = GetUserIdentity();
                if (userId == null)
                {
                    return Unauthorized(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not authenticated"
                    });
                }

                // Verify TOTP code
                var result = await _totpService.EnableTotpAsync(userId.Value, request.Code, request.SecretKey);
                bool success = result.success;
                string? errorMessage = result.errorMessage;
                if (!success)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to verify TOTP code"
                    });
                }

                return Ok(new ApiResponse<VerifyTotpResponse>
                {
                    Success = true,
                    Message = "TOTP verification successful",
                    Data = new VerifyTotpResponse
                    {
                        Enabled = true
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying TOTP");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while verifying TOTP"
                });
            }
        }

        [HttpPost("totp/disable")]
        [Authorize]
        public async Task<ActionResult<DisableTotpResponse>> DisableTotp()
        {
            try
            {
                var userId = GetUserIdentity();
                if (userId == null)
                {
                    return Unauthorized(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not authenticated"
                    });
                }

                // Disable TOTP
                var result = await _totpService.DisableTotpAsync(userId.Value);
                bool success = result.success;
                string? errorMessage = result.errorMessage;
                if (!success)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to disable TOTP"
                    });
                }

                return Ok(new ApiResponse<DisableTotpResponse>
                {
                    Success = true,
                    Message = "TOTP disabled successfully",
                    Data = new DisableTotpResponse
                    {
                        Disabled = true
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disabling TOTP");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while disabling TOTP"
                });
            }
        }

        
        [HttpPost("totp/validate")]
        [AllowAnonymous]
        public async Task<ActionResult<ValidateTotpResponse>> ValidateTotp([FromBody] ValidateTotpRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Invalid request data",
                        Errors = ModelState.Values.SelectMany(v => v.Errors.Select(e => e.ErrorMessage)).ToList()
                    });
                }

                var conn = _spacetimeService.GetConnection();
                
                // Find two-factor token
                var twoFactorToken = conn.Db.TwoFactorToken.Iter()
                    .FirstOrDefault(t => t.Token == request.TempToken && !t.IsUsed);
                
                if (twoFactorToken == null)
                {
                    _logger.LogWarning("Invalid or expired two-factor token: {Token}", request.TempToken);
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Invalid or expired token"
                    });
                }

                // Get the user
                var user = conn.Db.UserProfile.Iter()
                    .FirstOrDefault(u => u.UserId.Equals(twoFactorToken.UserId));
                if (user == null)
                {
                    _logger.LogWarning("User not found for two-factor token: {Token}", request.TempToken);
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not found"
                    });
                }

                // Get TOTP secret
                var totpSecret = conn.Db.TotpSecret.Iter()
                    .FirstOrDefault(t => t.UserId.Equals(user.UserId) && t.IsActive);
                if (totpSecret == null)
                {
                    _logger.LogWarning("TOTP secret not found for user: {UserId}", user.UserId);
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "TOTP not set up"
                    });
                }

                // Verify TOTP code
                var isValid = _totpService.VerifyTotpCode(totpSecret.Secret, request.Code);
                if (!isValid)
                {
                    _logger.LogWarning("Invalid TOTP code for user: {UserId}", user.UserId);
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Invalid code"
                    });
                }

                // Mark token as used
                var twoFactorTokenId = twoFactorToken.Id;
                var twoFactorUserId = twoFactorToken.UserId;
                var tokenValue = twoFactorToken.Token;
                var expiresAt = twoFactorToken.ExpiresAt;
                
                // We'll directly update the TwoFactorToken in the database
                // since UpdateTwoFactorToken doesn't exist in the reducers
                conn.Reducers.UpdateTwoFactorToken(
                    twoFactorTokenId,
                    twoFactorUserId,
                    tokenValue,
                    true, // Mark as used
                    expiresAt
                );

                // Generate JWT token
                var token = GenerateJwtToken(user);

                return Ok(new ApiResponse<ValidateTotpResponse>
                {
                    Success = true,
                    Message = "TOTP validation successful",
                    Data = new ValidateTotpResponse
                    {
                        Token = token,
                        User = new UserDto
                        {
                            Id = user.LegacyUserId,
                            Username = user.Login,
                            Email = user.Email,
                            PhoneNumber = user.PhoneNumber,
                            Role = _authService.GetUserRole(user.UserId)
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating TOTP");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while validating TOTP"
                });
            }
        }

        #endregion

        #region WebAuthn (FIDO2)

        [HttpPost("webauthn/register/options")]
        [Authorize]
        public async Task<ActionResult<WebAuthnRegisterOptionsResponse>> GetWebAuthnRegisterOptions()
        {
            try
            {
                var userId = GetUserIdentity();
                if (userId == null)
                {
                    return Unauthorized(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not authenticated"
                    });
                }

                var user = await GetUserByIdentityAsync(userId);
                if (user == null)
                {
                    return NotFound(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not found"
                    });
                }

                // Get WebAuthn registration options
                var result = await _webAuthnService.GetCredentialCreateOptionsAsync(userId.Value, user.Login);
                bool success = result.success;
                CredentialCreateOptions? options = result.options;
                string? errorMessage = result.errorMessage;
                if (!success || options == null)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to get WebAuthn registration options"
                    });
                }

                return Ok(new ApiResponse<WebAuthnRegisterOptionsResponse>
                {
                    Success = true,
                    Message = "WebAuthn registration options generated",
                    Data = new WebAuthnRegisterOptionsResponse
                    {
                        Options = options
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting WebAuthn registration options");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while getting WebAuthn registration options"
                });
            }
        }

        [HttpPost("webauthn/register/complete")]
        [Authorize]
        public async Task<ActionResult<WebAuthnRegisterCompleteResponse>> CompleteWebAuthnRegistration([FromBody] WebAuthnRegisterCompleteRequest request)
        {
            try
            {
                var userId = GetUserIdentity();
                if (userId == null)
                {
                    return Unauthorized(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not authenticated"
                    });
                }

                var user = await GetUserByIdentityAsync(userId);
                if (user == null)
                {
                    return NotFound(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not found"
                    });
                }

                // Complete WebAuthn registration
                var result = await _webAuthnService.CompleteRegistrationAsync(userId.Value, user.Login, request.AttestationResponse);
                bool success = result.success;
                string? errorMessage = result.errorMessage;
                if (!success)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to complete WebAuthn registration"
                    });
                }

                return Ok(new ApiResponse<WebAuthnRegisterCompleteResponse>
                {
                    Success = true,
                    Message = "WebAuthn registration completed successfully",
                    Data = new WebAuthnRegisterCompleteResponse
                    {
                        Registered = true
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing WebAuthn registration");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while completing WebAuthn registration"
                });
            }
        }

        [HttpPost("webauthn/login/options")]
        [AllowAnonymous]
        public async Task<ActionResult<WebAuthnLoginOptionsResponse>> GetWebAuthnLoginOptions([FromBody] WebAuthnLoginOptionsRequest request)
        {
            try
            {
                // Get WebAuthn assertion options
                var (success, options, errorMessage) = await _webAuthnService.GetAssertionOptionsAsync(request.Username);
                if (!success || options == null)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to get WebAuthn login options"
                    });
                }

                return Ok(new ApiResponse<WebAuthnLoginOptionsResponse>
                {
                    Success = true,
                    Message = "WebAuthn login options generated",
                    Data = new WebAuthnLoginOptionsResponse
                    {
                        Options = options
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting WebAuthn login options");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while getting WebAuthn login options"
                });
            }
        }

        [HttpPost("webauthn/login/complete")]
        [AllowAnonymous]
        public async Task<ActionResult<WebAuthnLoginCompleteResponse>> CompleteWebAuthnLogin([FromBody] WebAuthnLoginCompleteRequest request)
        {
            try
            {
                // Complete WebAuthn login
                var (success, user, errorMessage) = await _webAuthnService.CompleteAssertionAsync(request.Username, request.AssertionResponse);
                if (!success || user == null)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to complete WebAuthn login"
                    });
                }

                // Generate JWT token
                var token = GenerateJwtToken(user);

                return Ok(new ApiResponse<WebAuthnLoginCompleteResponse>
                {
                    Success = true,
                    Message = "WebAuthn login completed successfully",
                    Data = new WebAuthnLoginCompleteResponse
                    {
                        Token = token,
                        User = new UserDto
                        {
                            Id = user.LegacyUserId,
                            Username = user.Login,
                            Email = user.Email,
                            PhoneNumber = user.PhoneNumber,
                            Role = _authService.GetUserRole(user.UserId)
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing WebAuthn login");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while completing WebAuthn login"
                });
            }
        }

        [HttpPost("webauthn/validate")]
        [AllowAnonymous]
        public async Task<ActionResult<WebAuthnValidateResponse>> ValidateWebAuthn([FromBody] WebAuthnValidateRequest request)
        {
            try
            {
                // Get user from token
                var conn = _spacetimeService.GetConnection();
                var twoFactorToken = conn.Db.TwoFactorToken.Iter()
                    .FirstOrDefault(t => t.Token == request.TempToken && !t.IsUsed);
                
                if (twoFactorToken == null)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Invalid or expired token"
                    });
                }

                var user = conn.Db.UserProfile.Iter()
                    .FirstOrDefault(u => u.UserId.Equals(twoFactorToken.UserId));
                
                if (user == null)
                {
                    return NotFound(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not found"
                    });
                }

                // Complete WebAuthn assertion
                var (success, _, errorMessage) = await _webAuthnService.CompleteAssertionAsync(user.Login, request.AssertionResponse);
                if (!success)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to validate WebAuthn"
                    });
                }

                // Mark token as used
                var twoFactorTokenId = twoFactorToken.Id;
                var twoFactorUserId = twoFactorToken.UserId;
                var tokenValue = twoFactorToken.Token;
                var expiresAt = twoFactorToken.ExpiresAt;
                
                // We'll directly update the TwoFactorToken in the database
                // since UpdateTwoFactorToken doesn't exist in the reducers
                conn.Reducers.UpdateTwoFactorToken(
                    twoFactorTokenId,
                    twoFactorUserId,
                    tokenValue,
                    true, // Mark as used
                    expiresAt
                );

                // Generate JWT token
                var token = GenerateJwtToken(user);

                return Ok(new ApiResponse<WebAuthnValidateResponse>
                {
                    Success = true,
                    Message = "WebAuthn validation successful",
                    Data = new WebAuthnValidateResponse
                    {
                        Token = token,
                        User = new UserDto
                        {
                            Id = user.LegacyUserId,
                            Username = user.Login,
                            Email = user.Email,
                            PhoneNumber = user.PhoneNumber,
                            Role = _authService.GetUserRole(user.UserId)
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating WebAuthn");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while validating WebAuthn"
                });
            }
        }

        [HttpGet("webauthn/credentials")]
        [Authorize]
        public async Task<ActionResult<WebAuthnCredentialsResponse>> GetWebAuthnCredentials()
        {
            try
            {
                var userId = GetUserIdentity();
                if (userId == null)
                {
                    return Unauthorized(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not authenticated"
                    });
                }

                // Get WebAuthn credentials
                if (userId == null) throw new InvalidOperationException("User ID cannot be null");
                var credentials = await _webAuthnService.GetUserCredentialsAsync(userId.Value);

                return Ok(new ApiResponse<WebAuthnCredentialsResponse>
                {
                    Success = true,
                    Message = "WebAuthn credentials retrieved successfully",
                    Data = new WebAuthnCredentialsResponse
                    {
                        Credentials = credentials.Select(c => new WebAuthnCredentialDto
                        {
                            Id = Convert.ToBase64String(c.CredentialId.ToArray()),
                            CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds((long)c.CreatedAt).DateTime
                        }).ToList()
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting WebAuthn credentials");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while getting WebAuthn credentials"
                });
            }
        }

        [HttpDelete("webauthn/credentials/{id}")]
        [Authorize]
        public async Task<ActionResult<WebAuthnRemoveCredentialResponse>> RemoveWebAuthnCredential(string id)
        {
            try
            {
                var userId = GetUserIdentity();
                if (userId == null)
                {
                    return Unauthorized(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not authenticated"
                    });
                }

                // Remove WebAuthn credential
                var result = await _webAuthnService.RemoveCredentialAsync(userId.Value, id);
                bool success = result.success;
                string? errorMessage = result.errorMessage;
                if (!success)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to remove WebAuthn credential"
                    });
                }

                return Ok(new ApiResponse<WebAuthnRemoveCredentialResponse>
                {
                    Success = true,
                    Message = "WebAuthn credential removed successfully",
                    Data = new WebAuthnRemoveCredentialResponse
                    {
                        Removed = true
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing WebAuthn credential");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while removing WebAuthn credential"
                });
            }
        }

        #endregion

        #region Magic Link

        [HttpGet("validate-magic-link")]
        [AllowAnonymous]
        public async Task<ActionResult> ValidateMagicLink([FromQuery] string token)
        {
            try
            {
                // Validate magic link token
                var (success, user, errorMessage) = await _magicLinkService.ValidateMagicLinkAsync(token);
                if (!success || user == null)
                {
                    // Redirect to error page
                    return Redirect($"/auth/error?message={Uri.EscapeDataString(errorMessage ?? "Invalid or expired magic link")}");
                }

                // Mark token as used
                await _magicLinkService.MarkMagicLinkAsUsedAsync(token);

                // Generate JWT token
                var jwtToken = GenerateJwtToken(user);

                // Redirect to success page with token
                return Redirect($"/auth/success?token={Uri.EscapeDataString(jwtToken)}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating magic link");
                return Redirect($"/auth/error?message={Uri.EscapeDataString("An error occurred while validating magic link")}");
            }
        }

        [HttpPost("validate-magic-link")]
        [AllowAnonymous]
        public async Task<ActionResult<ValidateMagicLinkResponse>> ValidateMagicLinkApi([FromBody] ValidateMagicLinkRequest request)
        {
            try
            {
                // Validate magic link token
                var (success, user, errorMessage) = await _magicLinkService.ValidateMagicLinkAsync(request.Token);
                if (!success || user == null)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Invalid or expired magic link"
                    });
                }

                // Mark token as used
                await _magicLinkService.MarkMagicLinkAsUsedAsync(request.Token);

                // Generate JWT token
                var token = GenerateJwtToken(user);

                return Ok(new ApiResponse<ValidateMagicLinkResponse>
                {
                    Success = true,
                    Message = "Magic link validated successfully",
                    Data = new ValidateMagicLinkResponse
                    {
                        Token = token,
                        User = new UserDto
                        {
                            Id = user.LegacyUserId,
                            Username = user.Login,
                            Email = user.Email,
                            PhoneNumber = user.PhoneNumber,
                            Role = _authService.GetUserRole(user.UserId)
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating magic link");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while validating magic link"
                });
            }
        }

        #endregion

        #region QR Code Authentication

        [HttpGet("qr/generate")]
        [Authorize]
        public async Task<ActionResult<QrCodeResponse>> GenerateQRCode()
        {
            try
            {
                var userId = GetUserIdentity();
                if (userId == null)
                {
                    return Unauthorized(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not authenticated"
                    });
                }

                var user = await GetUserByIdentityAsync(userId);
                if (user == null)
                {
                    return NotFound(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not found"
                    });
                }

                // Generate QR code
                (string qrCodeBase64, string rawData) = await _qrAuthService.GenerateQRCodeWithDataAsync(user);

                return Ok(new ApiResponse<QrCodeResponse>
                {
                    Success = true,
                    Message = "QR code generated successfully",
                    Data = new QrCodeResponse
                    {
                        QrCode = qrCodeBase64,
                        RawData = rawData
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating QR code");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while generating QR code"
                });
            }
        }

        [HttpPost("qr/login")]
        [AllowAnonymous]
        public async Task<ActionResult<QrLoginResponse>> QRLogin([FromBody] QrLoginRequest request)
        {
            try
            {
                // Authenticate with QR code
                var user = await _authService.AuthenticateDirectQRAsync(request.Username, request.Token);
                if (user == null)
                {
                    return Unauthorized(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Invalid QR login credentials"
                    });
                }

                // Generate JWT token
                var token = GenerateJwtToken(user);

                return Ok(new ApiResponse<QrLoginResponse>
                {
                    Success = true,
                    Message = "QR login successful",
                    Data = new QrLoginResponse
                    {
                        Token = token,
                        User = new UserDto
                        {
                            Id = user.LegacyUserId,
                            Username = user.Login,
                            Email = user.Email,
                            PhoneNumber = user.PhoneNumber,
                            Role = _authService.GetUserRole(user.UserId)
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during QR login");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred during QR login"
                });
            }
        }

        [HttpGet("qr/direct/generate")]
        [AllowAnonymous]
        public async Task<ActionResult<DirectQrCodeResponse>> GenerateDirectLoginQRCode([FromQuery] string username, [FromQuery] string deviceType)
        {
            try
            {
                // Validate user exists
                var user = await _userService.GetUserByLoginAsync(username);
                if (user == null)
                {
                    return NotFound(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not found"
                    });
                }

                // Generate direct login QR code
                var (qrCode, rawData) = await _qrAuthService.GenerateDirectLoginQRCodeAsync(username, deviceType);

                return Ok(new ApiResponse<DirectQrCodeResponse>
                {
                    Success = true,
                    Message = "Direct login QR code generated successfully",
                    Data = new DirectQrCodeResponse
                    {
                        QrCode = qrCode,
                        RawData = rawData
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating direct login QR code");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while generating direct login QR code"
                });
            }
        }

        [HttpPost("qr/direct/login")]
        [AllowAnonymous]
        public async Task<ActionResult<DirectQrLoginResponse>> DirectQRLogin([FromBody] DirectQrLoginRequest request)
        {
            try
            {
                // Validate direct login token
                var (success, user, deviceId) = await _qrAuthService.ValidateDirectLoginTokenAsync(request.Token, request.DeviceType);
                if (!success || user == null)
                {
                    return Unauthorized(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Invalid QR login token"
                    });
                }

                // Authenticate user without password
                var authenticatedUser = await _authService.AuthenticateDirectQRAsync(user.Login, deviceId);
                if (authenticatedUser == null)
                {
                    return Unauthorized(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Authentication failed"
                    });
                }

                // Generate JWT token
                var token = GenerateJwtToken(authenticatedUser);

                // If this is a mobile device scanning a desktop QR code, notify the desktop
                if (request.DeviceType == "mobile" && request.IsDesktopLogin)
                {
                    await _qrAuthService.NotifyDeviceLoginSuccessAsync(deviceId, token);
                }

                return Ok(new ApiResponse<DirectQrLoginResponse>
                {
                    Success = true,
                    Message = "Direct QR login successful",
                    Data = new DirectQrLoginResponse
                    {
                        Token = token,
                        DeviceId = deviceId,
                        User = new UserDto
                        {
                            Id = authenticatedUser.LegacyUserId,
                            Username = authenticatedUser.Login,
                            Email = authenticatedUser.Email,
                            PhoneNumber = authenticatedUser.PhoneNumber,
                            Role = _authService.GetUserRole(authenticatedUser.UserId)
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during direct QR login");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred during direct QR login"
                });
            }
        }

        [HttpGet("qr/direct/check")]
        [AllowAnonymous]
        public async Task<ActionResult<CheckQrLoginResponse>> CheckDirectLoginStatus([FromQuery] string deviceId)
        {
            try
            {
                var loginSuccessKey = $"login_success_{deviceId}";
                if (_cache.TryGetValue(loginSuccessKey, out string token))
                {
                    _cache.Remove(loginSuccessKey); // One-time use
                    return Ok(new ApiResponse<CheckQrLoginResponse>
                    {
                        Success = true,
                        Message = "Login successful",
                        Data = new CheckQrLoginResponse
                        {
                            Success = true,
                            Token = token
                        }
                    });
                }

                return Ok(new ApiResponse<CheckQrLoginResponse>
                {
                    Success = true,
                    Message = "No login detected yet",
                    Data = new CheckQrLoginResponse
                    {
                        Success = false
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking direct login status");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while checking login status"
                });
            }
        }

        #endregion

        #region OpenID Connect

        [HttpGet("connect/authorize")]
        [AllowAnonymous]
        public async Task<IActionResult> Authorize([FromQuery] string client_id, [FromQuery] string redirect_uri, [FromQuery] string response_type, [FromQuery] string scope, [FromQuery] string state, [FromQuery] string? nonce = null)
        {
            try
            {
                // Validate client
                var (clientSuccess, application, clientError) = await _openIdConnectService.GetApplicationByClientIdAsync(client_id);
                if (!clientSuccess || application == null)
                {
                    return BadRequest($"Invalid client: {clientError}");
                }

                // Get client display name
                var clientName = await GetDisplayNameAsync(application) ?? client_id;

                // Store the request in cache for later retrieval
                var requestId = Guid.NewGuid().ToString();
                _cache.Set($"oidc_request_{requestId}", new OpenIdConnectRequest
                {
                    ClientId = client_id,
                    RedirectUri = redirect_uri,
                    ResponseType = response_type,
                    Scope = scope,
                    State = state,
                    Nonce = nonce
                }, TimeSpan.FromMinutes(10));

                if (IsBrowserRequest())
                {
                    // Render the login page with OpenID Connect details
                    var scopes = scope.Split(' ');
                    return Content(RenderOAuthLoginForm(requestId, clientName, scopes), "text/html");
                }

                // For API clients, redirect to the login page with the request ID
                return Ok(new { login_url = $"/api/auth/oauth/login?request_id={requestId}" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing OpenID Connect authorization request");
                return StatusCode(500, "An error occurred while processing the authorization request");
            }
        }

        [HttpPost("connect/token")]
        [AllowAnonymous]
        public async Task<ActionResult<TokenResponse>> Token([FromForm] TokenRequest request)
        {
            try
            {
                if (request.GrantType == "authorization_code")
                {
                    // Validate authorization code
                    var codeData = _cache.Get<AuthorizationCodeData>($"auth_code_{request.Code}");
                    if (codeData == null)
                    {
                        return BadRequest(new
                        {
                            error = "invalid_grant",
                            error_description = "The authorization code is invalid or has expired."
                        });
                    }

                    // Get the user's SpacetimeDB Identity from legacy ID
                    var conn = _spacetimeService.GetConnection();
                    var userProfile = conn.Db.UserProfile.Iter()
                        .FirstOrDefault(u => u.LegacyUserId == codeData.UserId);
                    
                    if (userProfile == null) {
                        return BadRequest(new {
                            error = "invalid_grant",
                            error_description = "User not found."
                        });
                    }

                    var user = await GetUserByIdentityAsync(userProfile.UserId);
                    if (user == null)
                    {
                        return BadRequest(new
                        {
                            error = "invalid_grant",
                            error_description = "The user associated with the authorization code no longer exists."
                        });
                    }

                    // Get the application
                    var (appSuccess, application, appError) = await _openIdConnectService.GetApplicationByClientIdAsync(request.ClientId);
                    if (!appSuccess || application == null)
                    {
                        return BadRequest(new
                        {
                            error = "invalid_client",
                            error_description = appError ?? "The client application is invalid."
                        });
                    }

                    // Validate client secret if provided
                    if (!string.IsNullOrEmpty(request.ClientSecret))
                    {
                        // Implement client secret validation here
                    }

                    // Create identity
                    var (identitySuccess, identity, identityError) = await _openIdConnectService.CreateIdentityFromUserAsync(user, codeData.Scopes);
                    if (!identitySuccess || identity == null)
                    {
                        return BadRequest(new
                        {
                            error = "server_error",
                            error_description = identityError ?? "Failed to create identity."
                        });
                    }

                    // Generate JWT token
                    var token = GenerateJwtToken(user);

                    // Remove the authorization code
                    _cache.Remove($"auth_code_{request.Code}");

                    return Ok(new TokenResponse
                    {
                        AccessToken = token,
                        TokenType = "Bearer",
                        ExpiresIn = int.Parse(_configuration["JwtSettings:ExpirationInMinutes"] ?? "120") * 60,
                        Scope = string.Join(" ", codeData.Scopes)
                    });
                }
                else if (request.GrantType == "refresh_token")
                {
                    // Implement refresh token flow
                    return BadRequest(new
                    {
                        error = "unsupported_grant_type",
                        error_description = "Refresh token flow is not implemented yet."
                        
                    });
                }
                else
                {
                    return BadRequest(new
                    {
                        error = "unsupported_grant_type",
                        error_description = "The specified grant type is not supported."
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing token request");
                return StatusCode(500, new
                {
                    error = "server_error",
                    error_description = "An error occurred while processing the token request."
                });
            }
        }

        [HttpPost("connect/authorize/callback")]
        [AllowAnonymous]
        public async Task<IActionResult> AuthorizeCallback([FromForm] AuthorizeCallbackRequest request)
        {
            try
            {
                // Get the original request from cache
                var originalRequest = _cache.Get<OpenIdConnectRequest>($"oidc_request_{request.RequestId}");
                if (originalRequest == null)
                {
                    return BadRequest("Invalid or expired request ID");
                }

                // Authenticate user
                var user = await _authService.AuthenticateAsync(request.Username, request.Password);
                if (user == null)
                {
                    return Redirect($"/oauth/login?request_id={request.RequestId}&error=invalid_credentials");
                }

                // Get the application
                var (appSuccess, application, appError) = await _openIdConnectService.GetApplicationByClientIdAsync(originalRequest.ClientId);
                if (!appSuccess || application == null)
                {
                    return BadRequest($"Invalid client: {appError}");
                }

                // Parse scopes
                var scopes = originalRequest.Scope.Split(' ');

                // Create authorization code
                var code = GenerateRandomToken();
                
                // Store the code data
                _cache.Set($"auth_code_{code}", new AuthorizationCodeData
                {
                    UserId = user.LegacyUserId,
                    Scopes = scopes,
                    RedirectUri = originalRequest.RedirectUri
                }, TimeSpan.FromMinutes(5));

                // Build the redirect URL
                var redirectUrl = $"{originalRequest.RedirectUri}?code={code}&state={originalRequest.State}";
                
                return Redirect(redirectUrl);
                }
                catch (Exception ex)
                {
                _logger.LogError(ex, "Error processing authorization callback");
                return StatusCode(500, "An error occurred while processing the authorization callback");
            }
        }

        [HttpGet("connect/userinfo")]
        [Authorize]
        public async Task<ActionResult<UserInfoResponse>> UserInfo()
        {
            try
            {
                var userId = GetUserIdentity();
                if (userId == null)
                {
                    return Unauthorized(new
                    {
                        error = "invalid_token",
                        error_description = "The access token is invalid or expired."
                    });
                }

                var user = await GetUserByIdentityAsync(userId);
                if (user == null)
                {
                    return NotFound(new
                    {
                        error = "invalid_token",
                        error_description = "The user associated with the access token no longer exists."
                    });
                }

                // Get user roles
                var conn = _spacetimeService.GetConnection();
                var userRoles = conn.Db.UserRole.Iter()
                    .Where(ur => ur.UserId.Equals(userId))
                    .Join(conn.Db.Role.Iter(), ur => ur.RoleId, r => r.RoleId, (ur, r) => r.Name)
                    .ToList();

                return Ok(new UserInfoResponse
                {
                    Sub = user.LegacyUserId.ToString(),
                    Name = user.Login,
                    PreferredUsername = user.Login,
                    Email = user.Email,
                    EmailVerified = (bool)user.EmailConfirmed,
                    PhoneNumber = user.PhoneNumber,
                    
                    Roles = userRoles
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user info");
                return StatusCode(500, new
                {
                    error = "server_error",
                    error_description = "An error occurred while getting user info."
                });
            }
        }

        [HttpPost("connect/registerclient")]
        [Authorize(Roles = "Administrator")]
        public async Task<ActionResult<RegisterClientResponse>> RegisterClient([FromBody] RegisterClientRequest request)
        {
            try// NO NEED FOR TRANSACTION GARBO
            {
                // Register client application
                var (success, errorMessage) = await _openIdConnectService.RegisterClientApplicationAsync(
                    request.ClientId,
                    request.ClientSecret,
                    request.DisplayName,
                    request.RedirectUris,
                    request.PostLogoutRedirectUris,
                    request.AllowedScopes,
                    request.RequireConsent
                );

                if (!success)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to register client application"
                    });
                }

                return Ok(new ApiResponse<RegisterClientResponse>
                {
                    Success = true,
                    Message = "Client application registered successfully",
                    Data = new RegisterClientResponse
                    {
                        ClientId = request.ClientId,
                        DisplayName = request.DisplayName
                    }
                });
                }
                catch (Exception ex)
                {
                _logger.LogError(ex, "Error registering client application");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while registering client application"
                });
            }
        }

        [HttpPut("connect/update-client/{clientId}")]
        [Authorize(Roles = "Administrator")]
        public async Task<ActionResult<UpdateClientResponse>> UpdateClient(string clientId, [FromBody] UpdateClientRequest request)
        {
            try
            {
                // Update client application
                var (success, errorMessage) = await _openIdConnectService.UpdateClientApplicationAsync(
                    clientId,
                    request.ClientSecret,
                    request.DisplayName,
                    request.RedirectUris,
                    request.PostLogoutRedirectUris,
                    request.AllowedScopes,
                    request.RequireConsent
                );

                if (!success)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to update client application"
                    });
                }

                return Ok(new ApiResponse<UpdateClientResponse>
                {
                    Success = true,
                    Message = "Client application updated successfully",
                    Data = new UpdateClientResponse
                    {
                        ClientId = clientId,
                        DisplayName = request.DisplayName ?? "Unknown"
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating client application");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while updating client application"
                });
            }
        }

        [HttpDelete("connect/delete-client/{clientId}")]
        [Authorize(Roles = "Administrator")]
        public async Task<ActionResult<DeleteClientResponse>> DeleteClient(string clientId)
        {
            try
            {
                // Delete client application
                var (success, errorMessage) = await _openIdConnectService.DeleteClientApplicationAsync(clientId);

                if (!success)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to delete client application"
                    });
                }

                return Ok(new ApiResponse<DeleteClientResponse>
                {
                    Success = true,
                    Message = "Client application deleted successfully",
                    Data = new DeleteClientResponse
                    {
                        ClientId = clientId,
                        Deleted = true
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting client application");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while deleting client application"
                });
            }
        }

        [HttpGet("connect/clients")]
        [Authorize(Roles = "Administrator")]
        public async Task<ActionResult<GetClientsResponse>> GetClients()
        {
            try
            {
                // Get all client applications
                var (success, applications, errorMessage) = await _openIdConnectService.GetAllClientApplicationsAsync();

                if (!success || applications == null)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to get client applications"
                    });
                }

                // Convert to DTO
                var clientDtos = new List<ClientDto>();
                foreach (var app in applications)
                {
                    var clientId = await GetClientIdAsync(app);
                    var displayName = await GetDisplayNameAsync(app);
                    
                    clientDtos.Add(new ClientDto
                    {
                        ClientId = clientId,
                        DisplayName = displayName
                    });
                }

                return Ok(new ApiResponse<GetClientsResponse>
                {
                    Success = true,
                    Message = "Client applications retrieved successfully",
                    Data = new GetClientsResponse
                    {
                        Clients = clientDtos
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting client applications");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while getting client applications"
                });
            }
        }

        [HttpGet("connect/client/{clientId}")]
        [Authorize(Roles = "Administrator")]
        public async Task<ActionResult<GetClientResponse>> GetClient(string clientId)
        {
            try
            {
                // Get client application
                var (success, application, errorMessage) = await _openIdConnectService.GetClientApplicationAsync(clientId);

                if (!success || application == null)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to get client application"
                    });
                }

                // Get client details
                var displayName = await GetDisplayNameAsync(application);
                var redirectUris = await GetRedirectUrisAsync(application);
                var postLogoutRedirectUris = await GetPostLogoutRedirectUrisAsync(application);
                var permissions = await GetPermissionsAsync(application);
                var consentType = await GetConsentTypeAsync(application);

                return Ok(new ApiResponse<GetClientResponse>
                {
                    Success = true,
                    Message = "Client application retrieved successfully",
                    Data = new GetClientResponse
                    {
                        ClientId = clientId,
                        DisplayName = displayName,
                        RedirectUris = redirectUris.ToArray(),
                        PostLogoutRedirectUris = postLogoutRedirectUris.ToArray(),
                        AllowedScopes = permissions.ToArray(),
                        RequireConsent = consentType == "explicit"
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting client application");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while getting client application"
                });
            }
        }

        #endregion

        #region Helper Methods
        
        private string GenerateJwtToken(UserProfile userProfile)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var keyString = _configuration["JwtSettings:Secret"] ?? 
                throw new InvalidOperationException("JWT secret is not configured");

            // Ensure the key is at least 32 bytes
            var keyBytes = Encoding.UTF8.GetBytes(keyString);
            if (keyBytes.Length < 32)
            {
                Array.Resize(ref keyBytes, 32);
            }
            else if (keyBytes.Length > 64)
            {
                Array.Resize(ref keyBytes, 64);
            }

            var key = new SymmetricSecurityKey(keyBytes);
            var expirationMinutes = double.Parse(_configuration["JwtSettings:ExpirationInMinutes"] ?? "120");
            
            var conn = _spacetimeService.GetConnection();
            
            // Get user's roles
            var userRoles = conn.Db.UserRole.Iter()
                .Where(ur => ur.UserId.Equals(userProfile.UserId))
                .Select(ur => ur.RoleId)
                .ToList();
            
            // Get role details
            var roles = conn.Db.Role.Iter()
                .Where(r => userRoles.Contains(r.RoleId) && r.IsActive)
                .ToList();
            
            // Get role permissions
            var rolePermissions = conn.Db.RolePermission.Iter()
                .Where(rp => userRoles.Contains(rp.RoleId))
                .Select(rp => rp.PermissionId)
                .Distinct()
                .ToList();
            
            // Get permission details
            var permissions = conn.Db.Permission.Iter()
                .Where(p => rolePermissions.Contains(p.PermissionId) && p.IsActive)
                .ToList();
            
            // Create claims
            var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, userProfile.Login),
                new Claim("sub", userProfile.LegacyUserId.ToString()),
                new Claim("identity", userProfile.UserId.ToString()),
                new Claim("xuid", userProfile.Xuid.ToString() ?? "0")
            };
            
            // Add role claims
            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role.Name));
                claims.Add(new Claim("role", role.LegacyRoleId.ToString())); // Keep legacy role ID for backward compatibility
            }
            
            // Add permission claims
            foreach (var permission in permissions)
            {
                claims.Add(new Claim("permission", permission.Name));
            }
            
            // Add highest priority role for IsAdmin checks
            var highestPriorityRole = roles.OrderByDescending(r => r.Priority).FirstOrDefault();
            if (highestPriorityRole != null)
            {
                claims.Add(new Claim("primary_role", highestPriorityRole.LegacyRoleId.ToString()));
            }

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddMinutes(expirationMinutes),
                SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        private Identity? GetUserIdentity()
        {
            var identityString = User.FindFirst("identity")?.Value;
            if (string.IsNullOrEmpty(identityString))
            {
                return null;
            }

            try
            {
                var conn = _spacetimeService.GetConnection();
                return conn.Db.UserProfile.Iter()
                    .FirstOrDefault(u => u.LegacyUserId.ToString() == identityString)?.UserId;
            }
            catch
            {
                return null;
            }
        }

        private string GenerateRandomToken()
        {
            var randomBytes = new byte[32];
            using (var rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(randomBytes);
            }
            return Convert.ToBase64String(randomBytes);
        }

        private async Task<UserProfile?> GetUserByIdentityAsync(Identity? userId)
        {
            if (userId == null)
                return null;
                
            var conn = _spacetimeService.GetConnection();
            var user = conn.Db.UserProfile.Iter()
                .FirstOrDefault(u => u.UserId.Equals(userId));
                
            return user;
        }

        private async Task<string> GetClientIdAsync(object application)
        {
            // Here, we're assuming the application object has a property named ClientId
            // You may need to adjust this based on the actual implementation
            var propertyInfo = application.GetType().GetProperty("ClientId");
            if (propertyInfo != null)
            {
                var value = propertyInfo.GetValue(application)?.ToString();
                return value ?? string.Empty;
            }
            return string.Empty;
        }

        private async Task<string> GetDisplayNameAsync(object application)
        {
            // Here, we're assuming the application object has a property named DisplayName
            var propertyInfo = application.GetType().GetProperty("DisplayName");
            if (propertyInfo != null)
            {
                var value = propertyInfo.GetValue(application)?.ToString();
                return value ?? string.Empty;
            }
            return string.Empty;
        }

        private async Task<List<string>> GetRedirectUrisAsync(object application)
        {
            // Placeholder implementation
            // You may need to adjust this based on the actual implementation
            var result = new List<string>();
            try
            {
                var propertyInfo = application.GetType().GetProperty("RedirectUris");
                if (propertyInfo != null)
                {
                    var value = propertyInfo.GetValue(application);
                    if (value is IEnumerable<string> uris)
                    {
                        result.AddRange(uris);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting redirect URIs from application object");
            }
            return result;
        }

        private async Task<List<string>> GetPostLogoutRedirectUrisAsync(object application)
        {
            // Placeholder implementation
            // You may need to adjust this based on the actual implementation
            var result = new List<string>();
            try
            {
                var propertyInfo = application.GetType().GetProperty("PostLogoutRedirectUris");
                if (propertyInfo != null)
                {
                    var value = propertyInfo.GetValue(application);
                    if (value is IEnumerable<string> uris)
                    {
                        result.AddRange(uris);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting post-logout redirect URIs from application object");
            }
            return result;
        }

        private async Task<List<string>> GetPermissionsAsync(object application)
        {
            // Placeholder implementation
            // You may need to adjust this based on the actual implementation
            var result = new List<string>();
            try
            {
                var propertyInfo = application.GetType().GetProperty("Permissions");
                if (propertyInfo != null)
                {
                    var value = propertyInfo.GetValue(application);
                    if (value is IEnumerable<string> permissions)
                    {
                        result.AddRange(permissions);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting permissions from application object");
            }
            return result;
        }

        private async Task<string> GetConsentTypeAsync(object application)
        {
            // Placeholder implementation
            // You may need to adjust this based on the actual implementation
            try
            {
                var propertyInfo = application.GetType().GetProperty("ConsentType");
                if (propertyInfo != null)
                {
                    var value = propertyInfo.GetValue(application)?.ToString();
                    return value ?? "implicit";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting consent type from application object");
            }
            return "implicit";
        }

        #endregion

                // Add this new method to render the profile page, styled like Yandex ID
        private string RenderProfilePage(UserProfile user, bool totpEnabled, List<WebAuthnCredentialDto> webAuthnCredentials, List<Role> roles, List<Permission> permissions)
        {
            // Determine if user is admin based on roles
            bool isAdmin = roles.Any(r => r.LegacyRoleId == 1); // Assuming legacy Role ID 1 is Admin

            // Helper function to generate list items for security section
            // Using standard classes like UnstyledListItem_root__xsw4w, Slot_root__jYlNI etc. from Yandex CSS
            string RenderSecurityListItem(string title, string description, string actionHtml) => $@"
                 <div class=""UnstyledListItem_root__xsw4w variant-default_root__vj_1h"" style=""padding: 12px 0;"">
                     <div class=""UnstyledListItem_inner__Td3gb"">
                         <div class=""Slot_root__jYlNI Slot_direction_vertical__I3MEt Slot_content__XYDYF color-primary_root__olFUv alignment-center_root__ndulA"">
                             <span class=""Text_root__J8eOj"" data-variant=""text-m"">{System.Web.HttpUtility.HtmlEncode(title)}</span>
                             <span class=""Text_root__J8eOj"" data-variant=""text-s"" data-color=""secondary"">{System.Web.HttpUtility.HtmlEncode(description)}</span>
                         </div>
                         <div class=""Slot_root__jYlNI Slot_direction_horizontal__aDFeG Slot_after____mkr color-primary_root__olFUv alignment-center_root__ndulA"">
                             {actionHtml} 
                         </div>
                     </div>
                 </div>
                 <hr style='border: none; height: 1px; background-color: var(--id-color-line-normal); margin: 0;'/>";

             // Helper function to render WebAuthn Key List Item
            string RenderWebAuthnKeyItem(WebAuthnCredentialDto c) => $@"
                 <div class=""UnstyledListItem_root__xsw4w variant-default_root__vj_1h"" style=""padding: 12px 0;"">
                    <div class=""UnstyledListItem_inner__Td3gb"">
                        <div class=""Slot_root__jYlNI Slot_direction_vertical__I3MEt Slot_content__XYDYF color-primary_root__olFUv alignment-center_root__ndulA"">
                             <span class=""Text_root__J8eOj"" data-variant=""text-m"">Ключ безопасности</span>
                             <span class=""Text_root__J8eOj"" data-variant=""text-s"" data-color=""secondary"">Добавлен: {c.CreatedAt:dd.MM.yyyy}</span>
                             <span class=""Text_root__J8eOj"" data-variant=""text-xs"" data-color=""tertiary"" style=""word-break: break-all;"">ID: {c.Id.Substring(0, Math.Min(12, c.Id.Length))}...</span> 
                        </div>
                         <div class=""Slot_root__jYlNI Slot_direction_horizontal__aDFeG Slot_after____mkr color-primary_root__olFUv alignment-center_root__ndulA"">
                             <form method=""POST"" action=""/api/auth/webauthn/credentials/{System.Web.HttpUtility.UrlEncode(c.Id)}"" onsubmit=""return confirm('Удалить этот ключ безопасности?');"">
                                 <input type=""hidden"" name=""_method"" value=""DELETE"">
                             
                                 <button type=""submit"" class=""Button_root__rneDS text-button_root__doKoA size-s_root__CoSn6"" style=""color: var(--id-color-status-negative);"">Удалить</button>
                             </form>
                        </div>
                    </div>
                 </div>
                 <hr style='border: none; height: 1px; background-color: var(--id-color-line-normal); margin: 0;'/>";


            return string.Format(BaseHtmlTemplate, "Ваш профиль - BRU AVTOPARK", $@"
            <div class=""profile-content-wrapper"" style=""display: flex;"">
                 {RenderSidebar()} 
                 <main class=""profile-main-content"" style=""flex-grow: 1; padding: 24px; background-color: var(--id-color-surface-submerged);"">
                     
                     <section class=""Section_root__zl60G"" style=""background: var(--id-color-surface-elevated-0); padding: 24px; margin-bottom: 6px; border-radius: var(--id-card-border-radius);"">
                         <div class=""Section_inner__N7MeR"" style=""max-width: 520px; gap: 8px;"">
                             <div class=""profile-card_root__hJtgV profile-card_root_isResponsive__FJgqS"" data-testid=""profile-card"">
                                 <div class=""profile-card_avatar__xb4bd"" style=""margin-bottom: 8px;"">
                                     <div class=""user-avatar_root__CsKdB user-avatar_root_isBig__RozUb"" style=""--id-avatar-size: 96px;"" data-testid=""user-avatar"">
                                         
                                          <img data-testid=""avatar"" aria-hidden=""true"" class=""avatar_root__qDicj user-avatar_avatar__jSrtG"" src=""https://avatars.mds.yandex.net/get-yapic/0/0-0/islands-200"" alt=""Аватар пользователя"" style=""background-color: var(--id-color-default-bg-base);""/>
                                     </div>
                                 </div>
                                 
                                 <span class=""Text_root__J8eOj profile-card_title__zZCrX"" style=""font: var(--id-typography-heading-l); font-weight: 500;"" data-testid=""profile-card-menu-trigger"" data-color=""primary"">
                                     {System.Web.HttpUtility.HtmlEncode(user.Login)}
                                   
                                 </span>
                                 
                                 <span class=""Text_root__J8eOj profile-card_description__nvlpy"" data-testid=""profile-card-description"" data-color=""secondary"" style=""font: var(--id-typography-text-m); text-align: center;"">
                                     <ul class=""bulleted-list_root__k0lgY"" style=""padding: 0; margin: 0;"">
                                         {(!string.IsNullOrEmpty(user.PhoneNumber) ? $@"<li class=""Text_root__J8eOj bulleted-list-item_root__1Y90C"" data-color=""secondary""><bdi data-testid=""phone"">{System.Web.HttpUtility.HtmlEncode(user.PhoneNumber)}</bdi></li>" : "")}
                                         {(!string.IsNullOrEmpty(user.Email) ? $@"<li class=""Text_root__J8eOj bulleted-list-item_root__1Y90C"" data-color=""secondary""><bdi data-testid=""email"">{System.Web.HttpUtility.HtmlEncode(user.Email)}</bdi></li>" : "")}
                                         {(string.IsNullOrEmpty(user.PhoneNumber) && string.IsNullOrEmpty(user.Email) ? $@"<li class=""Text_root__J8eOj bulleted-list-item_root__1Y90C"" data-color=""secondary""><bdi>Контактные данные не указаны</bdi></li>" : "")}
                                     </ul>
                                 </span>
                             </div>
                         </div>
                     </section>

                     
                     <section class=""Section_root__zl60G"" style=""background: var(--id-color-surface-elevated-0); padding: 24px; margin-bottom: 6px; border-radius: var(--id-card-border-radius);"">
                         <div class=""Section_inner__N7MeR"" style=""max-width: 520px;"">
                             <div class=""Heading_root__P0ine Heading_variant_section__p8T1h""><h2 class=""Text_root__J8eOj"" data-variant=""heading-m"">Безопасность</h2></div>
                            
                             <div class=""List_root__yESwN list-style-plain_root__EX_j_"">
                                {RenderSecurityListItem(
                                    "Двухфакторная аутентификация",
                                    totpEnabled ? "Включена • Код из приложения" : "Выключена",
                                    totpEnabled
                                        ? $@"<form method='POST' action='/api/auth/totp/disable' style='display:inline-block;'><button type='submit' class='Button_root__rneDS text-button_root__doKoA size-m_root___r3aA'>Выключить</button></form>"
                                        : $@"<a href='/api/auth/totp/setup' class='Button_root__rneDS text-button_root__doKoA size-m_root___r3aA'>Включить</a>"
                                )}
                                {RenderSecurityListItem(
                                    "Ключи и биометрия",
                                     webAuthnCredentials.Count > 0 ? $"{webAuthnCredentials.Count} {GetNoun(webAuthnCredentials.Count, "ключ", "ключа", "ключей")}" : "Нет ключей",
                                     $@"<a href='/api/auth/webauthn/register/options' class='Button_root__rneDS text-button_root__doKoA size-m_root___r3aA'>{(webAuthnCredentials.Count > 0 ? "Управлять" : "Добавить")}</a>"
                                )}
                                 
                                 {(webAuthnCredentials.Count > 0 ? $@"
                                     <div style='padding: 0; margin-top: -8px;'> 
                                         {string.Join("", webAuthnCredentials.Select(RenderWebAuthnKeyItem))}
                                     </div>
                                     " : "")}
                               </div>
                         </div>
                     </section>

                   
                     <section class=""Section_root__zl60G"" style=""background: var(--id-color-surface-elevated-0); padding: 24px; margin-bottom: 6px; border-radius: var(--id-card-border-radius);"">
                         <div class=""Section_inner__N7MeR"" style=""max-width: 520px;"">
                             <div class=""Heading_root__P0ine Heading_variant_section__p8T1h""><h2 class=""Text_root__J8eOj"" data-variant=""heading-m"">Роли и права</h2></div>
                              <div class=""List_root__yESwN list-style-plain_root__EX_j_"">
                                  <div class=""UnstyledListItem_root__xsw4w variant-default_root__vj_1h"" style=""padding: 12px 0;"">
                                      <div class=""UnstyledListItem_inner__Td3gb"">
                                          <div class=""Slot_root__jYlNI Slot_direction_vertical__I3MEt Slot_content__XYDYF color-primary_root__olFUv alignment-top_root____eiv"">
                                              <span class=""Text_root__J8eOj"" data-variant=""text-m"" style=""margin-bottom: 4px;"">Роли</span>
                                              {(roles.Any() ?
                                                  $@"<span class=""Text_root__J8eOj"" data-variant=""text-s"" data-color=""secondary"">{string.Join(", ", roles.Select(r => System.Web.HttpUtility.HtmlEncode(r.Name)))}</span>"
                                                  : $@"<span class=""Text_root__J8eOj"" data-variant=""text-s"" data-color=""secondary"">Роли не назначены</span>"
                                              )}
                                          </div>
                                      </div>
                                  </div>
                                   <hr style='border: none; height: 1px; background-color: var(--id-color-line-normal); margin: 0;'/>
                                   <div class=""UnstyledListItem_root__xsw4w variant-default_root__vj_1h"" style=""padding: 12px 0;"">
                                      <div class=""UnstyledListItem_inner__Td3gb"">
                                          <div class=""Slot_root__jYlNI Slot_direction_vertical__I3MEt Slot_content__XYDYF color-primary_root__olFUv alignment-top_root____eiv"">
                                              <span class=""Text_root__J8eOj"" data-variant=""text-m"" style=""margin-bottom: 8px;"">Права</span>
                                              {(permissions.Any() ? $@"
                                                  <div class=""flex flex-wrap gap-2"">
                                                      {string.Join("", permissions.Select(permission => $@"
                                                          <span class=""unstyled-badge_root__1gOSr size-m_root__FIFoL variant-default_root__nV3qv"" style='font-weight: 400;'>{System.Web.HttpUtility.HtmlEncode(permission.Name)}</span>
                                                      "))}
                                                  </div>" :
                                                  @"<span class=""Text_root__J8eOj"" data-variant=""text-s"" data-color=""secondary"">Специальные права отсутствуют</span>"
                                              )}
                                          </div>
                                      </div>
                                  </div>
                             </div>
                         </div>
                     </section>

                     
                      {(isAdmin ? $@"
                         <section class=""Section_root__zl60G"" style=""background: var(--id-color-surface-elevated-0); padding: 24px; margin-bottom: 6px; border-radius: var(--id-card-border-radius);"">
                             <div class=""Section_inner__N7MeR"" style=""max-width: 520px;"">
                                 <div class=""Heading_root__P0ine Heading_variant_section__p8T1h""><h2 class=""Text_root__J8eOj"" data-variant=""heading-m"">Администрирование</h2></div>
                                 <div class=""List_root__yESwN list-style-plain_root__EX_j_"" style=""display: flex; gap: 16px; flex-wrap: wrap; margin-top: 16px;"">
                                 
                                      <a href=""/api/auth/register"" class=""UnstyledListItem_root__xsw4w"" style=""padding: 20px; background: var(--id-color-surface-elevated-1); border-radius: 12px; width: calc(50% - 8px); transition: transform 0.2s, box-shadow 0.2s; box-shadow: 0 2px 8px rgba(0,0,0,0.05); display: flex; flex-direction: column; align-items: center; text-align: center; text-decoration: none; border: 1px solid var(--id-color-line-subtle);"" onmouseover=""this.style.transform='translateY(-4px)'; this.style.boxShadow='0 6px 12px rgba(0,0,0,0.1)';"" onmouseout=""this.style.transform=''; this.style.boxShadow='0 2px 8px rgba(0,0,0,0.05)';"">
                                           <div style=""width: 48px; height: 48px; background: var(--id-color-accent-subtle); border-radius: 50%; display: flex; align-items: center; justify-content: center; margin-bottom: 12px;"">
                                               <svg width=""24"" height=""24"" fill=""var(--id-color-accent)"" viewBox=""0 0 24 24"" aria-hidden=""true"" focusable=""false"" role=""img"" class=""svg-icon"">
                                                   <path d=""M12 4a4 4 0 1 0 0 8 4 4 0 0 0 0-8zM6 8a6 6 0 1 1 12 0A6 6 0 0 1 6 8zm2 10a3 3 0 0 0-3 3 1 1 0 1 1-2 0 5 5 0 0 1 5-5h8a5 5 0 0 1 5 5 1 1 0 1 1-2 0 3 3 0 0 0-3-3H8z""></path>
                                                   <path d=""M17 8a1 1 0 0 1 1-1h2a1 1 0 1 1 0 2h-2a1 1 0 0 1-1-1zm1-5a1 1 0 1 0 0 2h2a1 1 0 1 0 0-2h-2z""></path>
                                               </svg>
                                           </div>
                                           <span class=""Text_root__J8eOj"" data-variant=""text-m"" style=""font-weight: 500; color: var(--id-color-text-primary);"">Зарегистрировать пользователя</span>
                                           <span class=""Text_root__J8eOj"" data-variant=""text-s"" style=""color: var(--id-color-text-secondary); margin-top: 4px;"">Создание новых учетных записей</span>
                                       </a>
                                     
                                      <a href=""/api/auth/connect/clients"" class=""UnstyledListItem_root__xsw4w"" style=""padding: 20px; background: var(--id-color-surface-elevated-1); border-radius: 12px; width: calc(50% - 8px); transition: transform 0.2s, box-shadow 0.2s; box-shadow: 0 2px 8px rgba(0,0,0,0.05); display: flex; flex-direction: column; align-items: center; text-align: center; text-decoration: none; color: inherit; border: 1px solid var(--id-color-line-subtle);"" onmouseover=""this.style.transform='translateY(-4px)'; this.style.boxShadow='0 6px 12px rgba(0,0,0,0.1)';"" onmouseout=""this.style.transform=''; this.style.boxShadow='0 2px 8px rgba(0,0,0,0.05)';"">
                                           <div style=""width: 48px; height: 48px; background: var(--id-color-accent-subtle); border-radius: 50%; display: flex; align-items: center; justify-content: center; margin-bottom: 12px;"">
                                               <svg width=""24"" height=""24"" fill=""var(--id-color-accent)"" viewBox=""0 0 24 24"" aria-hidden=""true"" focusable=""false"" role=""img"" class=""svg-icon"">
                                                   <path d=""M12.476 1.748c.237.11 1.304.602 2.737 1.183 1.645.668 3.74 1.441 5.614 1.904a1 1 0 0 1 .76.97c0 4.608-.842 8.201-2.45 10.88-1.623 2.704-3.981 4.4-6.846 5.272-.19.057-.392.057-.582 0-2.865-.872-5.224-2.568-6.846-5.271-1.608-2.68-2.45-6.273-2.45-10.88a1 1 0 0 1 .76-.97c1.874-.464 3.969-1.237 5.615-1.905a63 63 0 0 0 2.736-1.183c.312-.146.638-.147.952 0zM12 8a2 2 0 1 0 0 4 2 2 0 0 0 0-4zm-4 2a4 4 0 1 1 8 0 4 4 0 0 1-8 0z""></path>
                                               </svg>
                                           </div>
                                           <span class=""Text_root__J8eOj"" data-variant=""text-m"" style=""font-weight: 500; color: var(--id-color-text-primary);"">Управление OIDC клиентами</span>
                                           <span class=""Text_root__J8eOj"" data-variant=""text-s"" style=""color: var(--id-color-text-secondary); margin-top: 4px;"">Настройка OAuth клиентов</span>
                                       </a>
                                 </div>
                             </div>
                         </section>
                      " : "")}

                     
                     <div class=""text-center"" style=""margin-top: 32px;"">
                         <a href=""/api/auth/logout"" class=""Button_root__rneDS variant-default_root__xWqkR size-l_root__PsIsm"" style=""min-width: 160px;"">Выйти</a>
                     </div>

                 </main>
             </div>
         ", "");
        }

        // Helper function to get the correct form of a Russian noun based on number
        private string GetNoun(int number, string one, string two, string five)
        {
            var mod100 = number % 100;
            var mod10 = number % 10;
            
            if (mod100 >= 11 && mod100 <= 19)
                return five;
                
            if (mod10 == 1)
                return one;
                
            if (mod10 >= 2 && mod10 <= 4)
                return two;
                
            return five;
        }


        // Helper to render the sidebar (reused and kept simple)
        private string RenderSidebar() => @"
            <nav class=""sidebar-navigation_root__2HXQL"" data-testid=""sidebar-navigation"" aria-label=""Основная навигация"">
                <ul class=""sidebar-navigation_list__R_7Wh"">
                    
                    <li class=""sidebar-navigation_item__GvUUF""><a href=""/api/auth/profile"" aria-current=""page"" class=""base-item_root__Z_6ST navigation-item-link_root_isActive__QZ9Ea""><svg width=""24"" height=""24"" fill=""currentColor"" viewBox=""0 0 24 24"" aria-hidden=""true"" focusable=""false"" role=""img"" class=""svg-icon""><path fill-rule=""evenodd"" d=""M11.333 1.59c-.494.137-.94.494-1.832 1.207l-6 4.8c-.551.441-.827.662-1.025.936a2.5 2.5 0 0 0-.386.803C2 9.662 2 10.015 2 10.721v8.08c0 1.12 0 1.68.218 2.107a2 2 0 0 0 .874.874C3.52 22 4.08 22 5.2 22h13.6c1.12 0 1.68 0 2.108-.218a2 2 0 0 0 .874-.874C22 20.48 22 19.92 22 18.8v-8.08c0-.705 0-1.058-.09-1.384-.08-.289-.21-.56-.386-.803-.198-.274-.474-.495-1.025-.936l-6-4.8c-.892-.713-1.338-1.07-1.832-1.207a2.5 2.5 0 0 0-1.334 0m-7.15 8.172c.075-.108.181-.197.393-.373l6.4-5.333c.364-.304.546-.456.75-.514a1 1 0 0 1 .548 0c.204.058.386.21.75.514l6.4 5.333c.212.176.318.265.394.373q.101.144.148.315c.034.128.034.266.034.541V19.2c0 .28 0 .42-.055.527a.5.5 0 0 1-.218.219C19.62 20 19.48 20 19.2 20H16v-4.6c0-.84 0-1.26-.164-1.58a1.5 1.5 0 0 0-.655-.656C14.861 13 14.441 13 13.6 13h-3.2c-.84 0-1.26 0-1.581.164a1.5 1.5 0 0 0-.656.655C8 14.14 8 14.56 8 15.4V20H4.8c-.28 0-.42 0-.527-.054a.5.5 0 0 1-.218-.219C4 19.62 4 19.48 4 19.2v-8.582c0-.275 0-.413.034-.54a1 1 0 0 1 .148-.316M10 20h4v-4.2c0-.28 0-.42-.055-.527a.5.5 0 0 0-.218-.218C13.62 15 13.48 15 13.2 15h-2.4c-.28 0-.42 0-.527.055a.5.5 0 0 0-.218.218C10 15.38 10 15.52 10 15.8z"" clip-rule=""evenodd""></path></svg>Главная</a></li>
                    
                    <li class=""sidebar-navigation_item__GvUUF""><a href=""/security"" class=""base-item_root__Z_6ST""><svg width=""24"" height=""24"" fill=""currentColor"" viewBox=""0 0 24 24"" aria-hidden=""true"" focusable=""false"" role=""img"" class=""svg-icon""><path fill-rule=""evenodd"" d=""M12.476 1.748c.237.11 1.304.602 2.737 1.183 1.645.668 3.74 1.441 5.614 1.904a1 1 0 0 1 .76.97c0 4.608-.842 8.201-2.45 10.88-1.623 2.704-3.981 4.4-6.846 5.272-.19.057-.392.057-.582 0-2.865-.872-5.224-2.568-6.846-5.271-1.608-2.68-2.45-6.273-2.45-10.88a1 1 0 0 1 .76-.97c1.874-.464 3.969-1.237 5.615-1.905a63 63 0 0 0 2.736-1.183c.312-.146.638-.147.952 0M12 3.73c.491.222 1.373.613 2.46 1.054 1.465.595 3.329 1.292 5.118 1.79-.089 3.993-.876 6.95-2.156 9.082C16.13 17.81 14.296 19.19 12 19.951c-2.296-.762-4.13-2.142-5.422-4.295-1.28-2.132-2.067-5.089-2.156-9.082 1.789-.498 3.653-1.195 5.118-1.79A69 69 0 0 0 12 3.73"" clip-rule=""evenodd""></path></svg>Безопасность</a></li>
                     
                   
                    <li class=""sidebar-navigation_item__GvUUF""><a href=""/api/auth/logout"" class=""base-item_root__Z_6ST""><svg width=""24"" height=""24"" fill=""currentColor"" viewBox=""0 0 24 24"" aria-hidden=""true"" focusable=""false"" role=""img"" class=""svg-icon""><path fill-rule=""evenodd"" d=""M14 4.001a1 1 0 0 1 1-1h3a3 3 0 0 1 3 3v12a3 3 0 0 1-3 3h-3a1 1 0 1 1 0-2h3a1 1 0 0 0 1-1V6a1 1 0 0 0-1-1h-3a1 1 0 0 1-1-1M9.707 3.294a1 1 0 0 1 0 1.414L6.414 8H14a1 1 0 1 1 0 2H6.414l3.293 3.293a1 1 0 0 1-1.414 1.414l-5-5a1 1 0 0 1 0-1.414l5-5a1 1 0 0 1 1.414 0"" clip-rule=""evenodd""></path></svg>Выйти</a></li>
                </ul>
            </nav>
        ";

        // Add this handler for the profile page
        [HttpGet("profile")]
        [AllowAnonymous]
        public async Task<IActionResult> ProfilePage()
        {
            if (!IsBrowserRequest())
            {
                return Ok(new ApiResponse<object>
                {
                    Success = false,
                    Message = "Please use a browser to view your profile"
                });
            }

            string? token = null;

            // Check Authorization header first
            if (Request.Headers.Authorization.Count > 0)
            {
                var authHeader = Request.Headers.Authorization.ToString();
                if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    token = authHeader.Substring("Bearer ".Length).Trim();
                }
            }

            // If no auth header, check query string
            if (string.IsNullOrEmpty(token) && Request.Query.ContainsKey("token"))
            {
                token = Request.Query["token"];
            }

            // If still no token, check localStorage via JavaScript
            if (string.IsNullOrEmpty(token))
            {
                return Content($@"
                    <script>
                        const storedToken = localStorage.getItem('auth_token');
                        if (storedToken) {{
                            window.location.href = '/api/auth/profile?token=' + encodeURIComponent(storedToken);
                        }} else {{
                            window.location.href = '/api/auth/login?error=Please log in to view your profile';
                        }}
                    </script>
                ", "text/html");
            }

            try
            {
                // Validate token format
                if (!token.Contains('.') || token.Count(c => c == '.') != 2)
                {
                    return Redirect("/api/auth/login?error=Invalid token format");
                }

                // Parse token without validation to get the user ID
                var handler = new JwtSecurityTokenHandler();
                var jwtToken = handler.ReadJwtToken(token);
                
                var userIdClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "identity");
                if (userIdClaim == null)
                {
                    return Redirect("/api/auth/login?error=Invalid token claims");
                }

                // Get user information
                var conn = _spacetimeService.GetConnection();
                var user = conn.Db.UserProfile.Iter()
                    .FirstOrDefault(u => u.UserId.ToString() == userIdClaim.Value);

                if (user == null)
                {
                    return Redirect("/api/auth/login?error=User not found");
                }

                // Get user settings
                var userSettings = conn.Db.UserSettings.Iter()
                    .FirstOrDefault(s => s.UserId.Equals(user.UserId));

                if (userSettings == null)
                {
                    // Create default settings if they don't exist
                    conn.Reducers.CreateUserSettings(user.UserId);
                    await Task.Delay(100); // Wait for reducer
                    userSettings = conn.Db.UserSettings.Iter()
                        .FirstOrDefault(s => s.UserId.Equals(user.UserId));
                }

                // Get WebAuthn credentials
                var webAuthnCredentials = conn.Db.WebAuthnCredential.Iter()
                    .Where(c => c.UserId.Equals(user.UserId) && c.IsActive)
                    .Select(c => new WebAuthnCredentialDto
                    {
                        Id = Convert.ToBase64String(c.CredentialId.ToArray()),
                        CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds((long)c.CreatedAt).DateTime
                    })
                    .ToList();

                // Get user roles
                var userRoles = conn.Db.UserRole.Iter()
                    .Where(ur => ur.UserId.Equals(user.UserId))
                    .ToList();

                // Get role details
                var roles = new List<Role>();
                foreach (var ur in userRoles)
                {
                    var role = conn.Db.Role.RoleId.Find(ur.RoleId);
                    if (role != null && role.IsActive)
                    {
                        roles.Add(role);
                    }
                }

                // Get role permissions
                var rolePermissions = conn.Db.RolePermission.Iter()
                    .Where(rp => roles.Select(r => r.RoleId).Contains(rp.RoleId))
                    .ToList();

                // Get permission details
                var permissionIds = rolePermissions.Select(rp => rp.PermissionId).Distinct().ToList();
                var permissions = new List<Permission>();
                foreach (var permissionId in permissionIds)
                {
                    var permission = conn.Db.Permission.PermissionId.Find(permissionId);
                    if (permission != null && permission.IsActive)
                    {
                        permissions.Add(permission);
                    }
                }

                return Content(RenderProfilePage(user, userSettings?.TotpEnabled ?? false, webAuthnCredentials, roles, permissions), "text/html");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading profile page");
                return Redirect($"/api/auth/login?error={Uri.EscapeDataString("Error loading profile: " + ex.Message)}");
            }
        }

        [HttpGet("logout")]
        [AllowAnonymous]
        public IActionResult Logout()
        {
            if (IsBrowserRequest())
            {
                return Content(string.Format(BaseHtmlTemplate, "Logged Out - BRU AVTOPARK", $@"
                    <h1>You Have Been Logged Out</h1>
                    <div class=""success-message"">You have been successfully logged out of your account.</div>
                    <div class=""text-center mt-4"">
                        <a href=""/api/auth/login"" class=""btn"">Login Again</a>
                    </div>
                    <script>
                        // Clear authentication tokens
                        localStorage.removeItem('auth_token');
                        sessionStorage.removeItem('auth_token');
                    </script>
                "), "text/html");
            }
            
            return Ok(new ApiResponse<object>
            {
                Success = true,
                Message = "Logged out successfully"
            });
        }

        // Also add a method to handle WebAuthn credential deletion via browser form
        [HttpPost("webauthn/credentials/{id}")]
        [Authorize]
        public async Task<IActionResult> RemoveWebAuthnCredentialForm(string id, [FromForm] string _method)
        {
            if (_method != "DELETE")
            {
                return BadRequest();
            }

            try
            {
                var userId = GetUserIdentity();
                if (userId == null)
                {
                    return Unauthorized(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not authenticated"
                    });
                }

                // Remove WebAuthn credential
                var result = await _webAuthnService.RemoveCredentialAsync(userId.Value, id);
                bool success = result.success;
                string? errorMessage = result.errorMessage;

                if (IsBrowserRequest())
                {
                    if (!success)
                    {
                        return Redirect($"/api/auth/profile?error={Uri.EscapeDataString(errorMessage ?? "Failed to remove security key")}");
                    }
                    return Redirect("/api/auth/profile?message=Security key removed successfully");
                }

                if (!success)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to remove WebAuthn credential"
                    });
                }

                return Ok(new ApiResponse<WebAuthnRemoveCredentialResponse>
                {
                    Success = true,
                    Message = "WebAuthn credential removed successfully",
                    Data = new WebAuthnRemoveCredentialResponse
                    {
                        Removed = true
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing WebAuthn credential");
                if (IsBrowserRequest())
                {
                    return Redirect($"/api/auth/profile?error={Uri.EscapeDataString("An error occurred while removing security key")}");
                }

                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while removing WebAuthn credential"
                });
            }
        }

        // Add a separate endpoint for the OAuth login page
        [HttpGet("oauth/login")]
        [AllowAnonymous]
        public async Task<IActionResult> OAuthLoginPage([FromQuery] string request_id, [FromQuery] string? error = null)
        {
            if (string.IsNullOrEmpty(request_id))
            {
                return BadRequest("Missing request_id parameter");
            }

            // Get the original request from cache
            var originalRequest = _cache.Get<OpenIdConnectRequest>($"oidc_request_{request_id}");
            if (originalRequest == null)
            {
                return BadRequest("Invalid or expired request_id");
            }

            // Get client display name
            var (clientSuccess, application, clientError) = await _openIdConnectService.GetApplicationByClientIdAsync(originalRequest.ClientId);
            if (!clientSuccess || application == null)
            {
                return BadRequest($"Invalid client: {clientError}");
            }

            var clientName = await GetDisplayNameAsync(application) ?? originalRequest.ClientId;
            var scopes = originalRequest.Scope.Split(' ');

            if (IsBrowserRequest())
            {
                // Render the login page with OpenID Connect details
                return Content(RenderOAuthLoginForm(request_id, clientName, scopes, error), "text/html");
            }

            return Ok(new
            {
                client_id = originalRequest.ClientId,
                redirect_uri = originalRequest.RedirectUri,
                scope = originalRequest.Scope,
                request_id = request_id
            });
        }

        // Add these new methods to render the OIDC admin pages

        private string RenderOidcClientsList(List<ClientDto> clients) => string.Format(BaseHtmlTemplate, "OIDC Clients - BRU AVTOPARK", $@"
            <div class=""flex justify-between items-center mb-4"">
                <h2>OAuth 2.0 / OpenID Connect Clients</h2>
                <a href=""/api/auth/connect/clients/new"" class=""btn-secondary"" style=""width: auto;"">New Client</a>
            </div>
            
            {(clients.Any() ?
                $@"<div class=""card"">
                    <table style=""width: 100%; border-collapse: collapse;"">
                        <thead>
                            <tr style=""border-bottom: 1px solid var(--border-color);"">
                                <th style=""text-align: left; padding: 1rem;"">Client ID</th>
                                <th style=""text-align: left; padding: 1rem;"">Display Name</th>
                                <th style=""padding: 1rem;"">Actions</th>
                            </tr>
                        </thead>
                        <tbody>
                            {string.Join("", clients.Select(client => $@"
                            <tr style=""border-bottom: 1px solid var(--border-color);"">
                                <td style=""padding: 1rem;"">{client.ClientId}</td>
                                <td style=""padding: 1rem;"">{client.DisplayName ?? client.ClientId}</td>
                                <td style=""padding: 1rem; text-align: center;"">
                                    <div class=""flex gap-2 justify-center"">
                                        <a href=""/api/auth/connect/clients/{client.ClientId}"" class=""btn-secondary"" style=""width: auto; padding: 0.25rem 0.5rem; font-size: 0.875rem;"">View</a>
                                        <a href=""/api/auth/connect/clients/{client.ClientId}/edit"" class=""btn-secondary"" style=""width: auto; padding: 0.25rem 0.5rem; font-size: 0.875rem;"">Edit</a>
                                        <form method=""POST"" action=""/api/auth/connect/clients/{client.ClientId}/delete"" onsubmit=""return confirm('Are you sure you want to delete this client?');"">
                                            <button type=""submit"" class=""btn-secondary"" style=""width: auto; padding: 0.25rem 0.5rem; font-size: 0.875rem; background-color: rgba(239, 68, 68, 0.1); color: var(--error-color);"">Delete</button>
                                        </form>
                                    </div>
                                </td>
                            </tr>"))}
                        </tbody>
                    </table>
                </div>" :
                @"<div class=""info-box"">
                    <p>No OAuth clients have been registered yet. <a href=""/api/auth/connect/clients/new"" class=""link"">Create your first client</a>.</p>
                </div>")}
            
            <div class=""mt-4 text-center"">
                <a href=""/api/auth/profile"" class=""link"">Back to Profile</a>
            </div>
        ");

        private string RenderOidcClientDetails(GetClientResponse client) => string.Format(BaseHtmlTemplate, client.ClientId + " - BRU AVTOPARK", $@"
            <div class=""card my-4"">
                <div class=""card-header"">
                    <div class=""flex justify-between items-center"">
                        <h2>Client Details</h2>
                        <a href=""/api/auth/connect/clients/{client.ClientId}/edit"" class=""btn"">Edit</a>
                    </div>
                </div>
                <div class=""card-body"">
                    <div class=""form-group"">
                        <label>Client ID</label>
                        <div class=""code-display"">{client.ClientId}</div>
                    </div>
                    <div class=""form-group"">
                        <label>Display Name</label>
                        <div class=""code-display"">{client.DisplayName}</div>
                    </div>
                    <div class=""form-group"">
                        <label>Redirect URIs</label>
                        <div class=""code-display"">
                            {(client.RedirectUris.Length > 0 ? string.Join("<br>", client.RedirectUris) : "None")}
                        </div>
                    </div>
                    <div class=""form-group"">
                        <label>Post-Logout Redirect URIs</label>
                        <div class=""code-display"">
                            {(client.PostLogoutRedirectUris.Length > 0 ? string.Join("<br>", client.PostLogoutRedirectUris) : "None")}
                        </div>
                    </div>
                    <div class=""form-group"">
                        <label>Allowed Scopes</label>
                        <div class=""code-display"">
                            {(client.AllowedScopes.Length > 0 ? string.Join("<br>", client.AllowedScopes) : "None")}
                        </div>
                    </div>
                    <div class=""form-group"">
                        <label>Require Consent</label>
                        <div class=""code-display"">{(client.RequireConsent ? "Yes" : "No")}</div>
                    </div>
                </div>
            </div>
            
            <div class=""text-center mt-4"">
                <a href=""/api/auth/connect/clients"" class=""btn btn-secondary"">Back to Clients</a>
            </div>
        ");

        private string RenderOidcClientForm(string? clientId = null, GetClientResponse? client = null) => string.Format(BaseHtmlTemplate, (clientId == null ? "New Client" : "Edit Client") + " - BRU AVTOPARK", $@"
            <div class=""info-box"">
                <p>{(clientId == null ? "Register a new OpenID Connect client application." : "Edit OpenID Connect client application settings.")}</p>
            </div>
            
            <form method=""POST"" action=""{(clientId == null ? "/api/auth/connect/register-client" : $"/api/auth/connect/update-client/{clientId}")}"">
                <div class=""form-group"">
                    <label for=""clientId"">Client ID</label>
                    <input type=""text"" id=""clientId"" name=""clientId"" value=""{(client?.ClientId ?? "")}"" {(clientId == null ? "required" : "readonly")}>
                    {(clientId != null ? @"<input type=""hidden"" name=""clientId"" value=""" + clientId + @""">" : "")}
                </div>
                
                <div class=""form-group"">
                    <label for=""displayName"">Display Name</label>
                    <input type=""text"" id=""displayName"" name=""displayName"" value=""{(client?.DisplayName ?? "")}"" required>
                </div>
                
                <div class=""form-group"">
                    <label for=""clientSecret"">Client Secret</label>
                    <input type=""text"" id=""clientSecret"" name=""clientSecret"" {(clientId == null ? "required" : "placeholder=\"Leave blank to keep current secret\"")}>
                    {(clientId != null ? @"<p class=""text-muted"">Leave blank to keep current secret</p>" : "")}
                </div>
                
                <div class=""form-group"">
                    <label for=""redirectUris"">Redirect URIs (one per line)</label>
                    <textarea id=""redirectUris"" name=""redirectUris"" rows=""3"" required>{(client?.RedirectUris != null ? string.Join("\n", client.RedirectUris) : "")}</textarea>
                </div>
                
                <div class=""form-group"">
                    <label for=""postLogoutRedirectUris"">Post-Logout Redirect URIs (one per line)</label>
                    <textarea id=""postLogoutRedirectUris"" name=""postLogoutRedirectUris"" rows=""3"">{(client?.PostLogoutRedirectUris != null ? string.Join("\n", client.PostLogoutRedirectUris) : "")}</textarea>
                </div>
                
                <div class=""form-group"">
                    <label for=""allowedScopes"">Allowed Scopes (one per line)</label>
                    <textarea id=""allowedScopes"" name=""allowedScopes"" rows=""3"" required>{(client?.AllowedScopes != null ? string.Join("\n", client.AllowedScopes) : "openid\nprofile\nemail")}</textarea>
                </div>
                
                <div class=""form-group"">
                    <label>
                        <input type=""checkbox"" id=""requireConsent"" name=""requireConsent"" {(client?.RequireConsent == true ? "checked" : "")}>
                        Require Consent
                    </label>
                    <p class=""text-muted"">If checked, users will be prompted to approve the requested scopes.</p>
                </div>
                
                <button type=""submit"" class=""btn btn-block"">{(clientId == null ? "Create Client" : "Update Client")}</button>
            </form>
            
            <div class=""text-center mt-4"">
                <a href=""{(clientId == null ? "/api/auth/connect/clients" : $"/api/auth/connect/clients/{clientId}")}"" class=""btn btn-secondary"">Cancel</a>
            </div>
        ");

        // Add these endpoint handlers for the OIDC admin pages
        [HttpGet("connect/clients")]
        [Authorize(Roles = "Administrator")]
        public async Task<IActionResult> ClientsListPage()
        {
            try
            {
                // Get all client applications
                var (success, applications, errorMessage) = await _openIdConnectService.GetAllClientApplicationsAsync();

                if (!success || applications == null)
                {
                    if (IsBrowserRequest())
                    {
                        return Redirect($"/api/auth/error?message={Uri.EscapeDataString(errorMessage ?? "Failed to get client applications")}");
                    }

                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to get client applications"
                    });
                }

                // Convert to DTO
                var clientDtos = new List<ClientDto>();
                foreach (var app in applications)
                {
                    var clientId = await GetClientIdAsync(app);
                    var displayName = await GetDisplayNameAsync(app);
                    
                    clientDtos.Add(new ClientDto
                    {
                        ClientId = clientId,
                        DisplayName = displayName
                    });
                }

                if (IsBrowserRequest())
                {
                    return Content(RenderOidcClientsList(clientDtos), "text/html");
                }

                return Ok(new ApiResponse<GetClientsResponse>
                {
                    Success = true,
                    Message = "Client applications retrieved successfully",
                    Data = new GetClientsResponse
                    {
                        Clients = clientDtos
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting client applications");
                if (IsBrowserRequest())
                {
                    return Redirect($"/api/auth/error?message={Uri.EscapeDataString("An error occurred while getting client applications")}");
                }

                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while getting client applications"
                });
            }
        }

        [HttpGet("connect/clients/{clientId}")]
        [Authorize(Roles = "Administrator")]
        public async Task<IActionResult> ClientDetailsPage(string clientId)
        {
            try
            {
                // Get client application
                var (success, application, errorMessage) = await _openIdConnectService.GetClientApplicationAsync(clientId);

                if (!success || application == null)
                {
                    if (IsBrowserRequest())
                    {
                        return Redirect($"/api/auth/error?message={Uri.EscapeDataString(errorMessage ?? "Failed to get client application")}");
                    }

                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to get client application"
                    });
                }

                // Get client details
                var displayName = await GetDisplayNameAsync(application);
                var redirectUris = await GetRedirectUrisAsync(application);
                var postLogoutRedirectUris = await GetPostLogoutRedirectUrisAsync(application);
                var permissions = await GetPermissionsAsync(application);
                var consentType = await GetConsentTypeAsync(application);

                var clientResponse = new GetClientResponse
                {
                    ClientId = clientId,
                    DisplayName = displayName,
                    RedirectUris = redirectUris.ToArray(),
                    PostLogoutRedirectUris = postLogoutRedirectUris.ToArray(),
                    AllowedScopes = permissions.ToArray(),
                    RequireConsent = consentType == "explicit"
                };

                if (IsBrowserRequest())
                {
                    return Content(RenderOidcClientDetails(clientResponse), "text/html");
                }

                return Ok(new ApiResponse<GetClientResponse>
                {
                    Success = true,
                    Message = "Client application retrieved successfully",
                    Data = clientResponse
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting client application");
                if (IsBrowserRequest())
                {
                    return Redirect($"/api/auth/error?message={Uri.EscapeDataString("An error occurred while getting client application")}");
                }

                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while getting client application"
                });
            }
        }

        [HttpGet("connect/clients/new")]
        [Authorize(Roles = "Administrator")]
        public IActionResult NewClientPage()
        {
            if (IsBrowserRequest())
            {
                return Content(RenderOidcClientForm(), "text/html");
            }

            return BadRequest("This endpoint is only available via browser");
        }

        [HttpGet("connect/clients/{clientId}/edit")]
        [Authorize(Roles = "Administrator")]
        public async Task<IActionResult> EditClientPage(string clientId)
        {
            try
            {
                // Get client application
                var (success, application, errorMessage) = await _openIdConnectService.GetClientApplicationAsync(clientId);

                if (!success || application == null)
                {
                    if (IsBrowserRequest())
                    {
                        return Redirect($"/api/auth/error?message={Uri.EscapeDataString(errorMessage ?? "Failed to get client application")}");
                    }

                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to get client application"
                    });
                }

                // Get client details
                var displayName = await GetDisplayNameAsync(application);
                var redirectUris = await GetRedirectUrisAsync(application);
                var postLogoutRedirectUris = await GetPostLogoutRedirectUrisAsync(application);
                var permissions = await GetPermissionsAsync(application);
                var consentType = await GetConsentTypeAsync(application);

                var clientResponse = new GetClientResponse
                {
                    ClientId = clientId,
                    DisplayName = displayName,
                    RedirectUris = redirectUris.ToArray(),
                    PostLogoutRedirectUris = postLogoutRedirectUris.ToArray(),
                    AllowedScopes = permissions.ToArray(),
                    RequireConsent = consentType == "explicit"
                };

                if (IsBrowserRequest())
                {
                    return Content(RenderOidcClientForm(clientId, clientResponse), "text/html");
                }

                return BadRequest("This endpoint is only available via browser");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting client application for editing");
                if (IsBrowserRequest())
                {
                    return Redirect($"/api/auth/error?message={Uri.EscapeDataString("An error occurred while getting client application for editing")}");
                }

                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while getting client application for editing"
                });
            }
        }

        [HttpPost("connect/register-client")]
        [Authorize(Roles = "Administrator")]
        public async Task<IActionResult> RegisterClientSubmit([FromForm] RegisterClientFormRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    if (IsBrowserRequest())
                    {
                        return Content(RenderOidcClientForm(null, new GetClientResponse
                        {
                            ClientId = request.ClientId,
                            DisplayName = request.DisplayName,
                            RedirectUris = SplitTextareaInput(request.RedirectUris),
                            PostLogoutRedirectUris = SplitTextareaInput(request.PostLogoutRedirectUris),
                            AllowedScopes = SplitTextareaInput(request.AllowedScopes),
                            RequireConsent = request.RequireConsent
                        }), "text/html");
                    }

                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Invalid form data",
                        Errors = ModelState.Values.SelectMany(v => v.Errors.Select(e => e.ErrorMessage)).ToList()
                    });
                }

                // Register client application
                var (success, errorMessage) = await _openIdConnectService.RegisterClientApplicationAsync(
                    request.ClientId,
                    request.ClientSecret,
                    request.DisplayName,
                    SplitTextareaInput(request.RedirectUris),
                    SplitTextareaInput(request.PostLogoutRedirectUris),
                    SplitTextareaInput(request.AllowedScopes),
                    request.RequireConsent
                );

                if (!success)
                {
                    if (IsBrowserRequest())
                    {
                        return Redirect($"/api/auth/error?message={Uri.EscapeDataString(errorMessage ?? "Failed to register client application")}");
                    }

                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to register client application"
                    });
                }

                if (IsBrowserRequest())
                {
                    return Redirect("/api/auth/connect/clients");
                }

                return Ok(new ApiResponse<RegisterClientResponse>
                {
                    Success = true,
                    Message = "Client application registered successfully",
                    Data = new RegisterClientResponse
                    {
                        ClientId = request.ClientId,
                        DisplayName = request.DisplayName
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering client application");
                if (IsBrowserRequest())
                {
                    return Redirect($"/api/auth/error?message={Uri.EscapeDataString("An error occurred while registering client application")}");
                }

                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while registering client application"
                });
            }
        }

        [HttpPost("connect/update-client/{clientId}")]
        [Authorize(Roles = "Administrator")]
        public async Task<IActionResult> UpdateClientSubmit(string clientId, [FromForm] UpdateClientFormRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    if (IsBrowserRequest())
                    {
                        return Content(RenderOidcClientForm(clientId, new GetClientResponse
                        {
                            ClientId = clientId,
                            DisplayName = request.DisplayName,
                            RedirectUris = SplitTextareaInput(request.RedirectUris),
                            PostLogoutRedirectUris = SplitTextareaInput(request.PostLogoutRedirectUris),
                            AllowedScopes = SplitTextareaInput(request.AllowedScopes),
                            RequireConsent = request.RequireConsent
                        }), "text/html");
                    }

                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Invalid form data",
                        Errors = ModelState.Values.SelectMany(v => v.Errors.Select(e => e.ErrorMessage)).ToList()
                    });
                }

                // Update client application
                var (success, errorMessage) = await _openIdConnectService.UpdateClientApplicationAsync(
                    clientId,
                    string.IsNullOrEmpty(request.ClientSecret) ? null : request.ClientSecret,
                    request.DisplayName,
                    SplitTextareaInput(request.RedirectUris),
                    SplitTextareaInput(request.PostLogoutRedirectUris),
                    SplitTextareaInput(request.AllowedScopes),
                    request.RequireConsent
                );

                if (!success)
                {
                    if (IsBrowserRequest())
                    {
                        return Redirect($"/api/auth/error?message={Uri.EscapeDataString(errorMessage ?? "Failed to update client application")}");
                    }

                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to update client application"
                    });
                }

                if (IsBrowserRequest())
                {
                    return Redirect($"/api/auth/connect/clients/{clientId}");
                }

                return Ok(new ApiResponse<UpdateClientResponse>
                {
                    Success = true,
                    Message = "Client application updated successfully",
                    Data = new UpdateClientResponse
                    {
                        ClientId = clientId,
                        DisplayName = request.DisplayName
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating client application");
                if (IsBrowserRequest())
                {
                    return Redirect($"/api/auth/error?message={Uri.EscapeDataString("An error occurred while updating client application")}");
                }

                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while updating client application"
                });
            }
        }

        [HttpPost("connect/clients/{clientId}/delete")]
        [Authorize(Roles = "Administrator")]
        public async Task<IActionResult> DeleteClientSubmit(string clientId)
        {
            try
            {
                // Delete client application
                var (success, errorMessage) = await _openIdConnectService.DeleteClientApplicationAsync(clientId);

                if (!success)
                {
                    if (IsBrowserRequest())
                    {
                        return Redirect($"/api/auth/error?message={Uri.EscapeDataString(errorMessage ?? "Failed to delete client application")}");
                    }

                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to delete client application"
                    });
                }

                if (IsBrowserRequest())
                {
                    return Redirect("/api/auth/connect/clients");
                }

                return Ok(new ApiResponse<DeleteClientResponse>
                {
                    Success = true,
                    Message = "Client application deleted successfully",
                    Data = new DeleteClientResponse
                    {
                        ClientId = clientId,
                        Deleted = true
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting client application");
                if (IsBrowserRequest())
                {
                    return Redirect($"/api/auth/error?message={Uri.EscapeDataString("An error occurred while deleting client application")}");
                }

                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while deleting client application"
                });
            }
        }

        // Helper method to split textarea input into string array
        private string[] SplitTextareaInput(string input)
        {
            if (string.IsNullOrEmpty(input))
                return Array.Empty<string>();
                
            return input.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(s => s.Trim())
                       .Where(s => !string.IsNullOrEmpty(s))
                       .ToArray();
        }

        private string RenderRegisterForm(string? error = null, string? message = null, int? adminCheckAttempt = null, bool isAdmin = false) => string.Format(BaseHtmlTemplate, 
            "Register New User - BRU AVTOPARK", // {0} - title
            $@"
            <div class=""card"">
                <div class=""card-header"">
                    <h1>Register New User</h1>
                </div>
                <div class=""card-body"">
                    {(error != null ? $"<div class='error-message'>{error}</div>" : "")}
                    {(message != null ? $"<div class='success-message'>{message}</div>" : "")}

                    <div class=""alert alert-info"">
                        <strong>Note:</strong> Only administrators can register new users. You must be logged in with admin privileges.
                    </div>

                    <div id=""register-status"" class=""card"">
                        <div class=""card-header"">Registration Status</div>
                        <div class=""card-body""></div>
                    </div>
                    <div id=""admin-check-status"" style=""display: {(isAdmin ? "block" : "none")}"" class=""card mt-3"">
                        <div class=""card-header"">Admin Check Status</div>
                        <div class=""card-body"">
                            <div class=""alert alert-success"">
                                <strong>You are logged in as an administrator.</strong>
                            </div>
                        </div>
                    </div>
                    <div id=""admin-check-pending"" style=""display: {(isAdmin ? "none" : (adminCheckAttempt.HasValue ? "block" : "none"))}"" class=""card mt-3"">
                        <div class=""card-header"">Admin Check Pending</div>
                        <div class=""card-body"">
                            <div class=""alert alert-warning"">
                                <strong>Admin check pending... (Attempt {adminCheckAttempt ?? 0}/3)</strong>
                                <p>If you're repeatedly redirected to the login page despite being an administrator, try these steps:</p>
                                <ol>
                                    <li>Ensure you're logged in with an administrator account</li>
                                    <li>Clear your browser cache and cookies</li>
                                    <li>Try using the API directly with Swagger UI</li>
                                </ol>
                                <a href=""/api/auth/login"" class=""btn btn-primary"">Go to Login</a>
                            </div>
                        </div>
                    </div>
                    <form id=""registerForm"">
                        <div class=""form-group"">
                            <label for=""username"">Username</label>
                            <input type=""text"" id=""username"" name=""username"" class=""form-control"" required>
                        </div>

                        <div class=""form-group"">
                            <label for=""password"">Password</label>
                            <input type=""password"" id=""password"" name=""password"" class=""form-control"" required>
                        </div>

                        <div class=""form-group"">
                            <label for=""email"">Email (Optional)</label>
                            <input type=""email"" id=""email"" name=""email"" class=""form-control"">
                        </div>

                        <div class=""form-group"">
                            <label for=""phoneNumber"">Phone Number (Optional)</label>
                            <input type=""tel"" id=""phoneNumber"" name=""phoneNumber"" class=""form-control"">
                        </div>

                        <div class=""form-group"">
                            <label for=""role"">Role</label>
                            <select id=""role"" name=""role"" class=""form-control"" required>
                                <option value=""0"">User</option>
                                <option value=""1"">Administrator</option>
                                <option value=""2"">Manager</option>
                                <option value=""3"">Driver</option>
                                <option value=""4"">Conductor</option>
                                <option value=""5"">Dispatcher</option>
                            </select>
                        </div>

                        <div class=""form-actions"">
                            <button type=""submit"" class=""btn btn-primary"">Register User</button>
                            <a href=""/api/auth/profile"" class=""btn btn-secondary"">Back to Profile</a>
                        </div>
                    </form>
                </div>
            </div>

            <script>
                document.getElementById('registerForm').addEventListener('submit', async (e) => {{
                    e.preventDefault();
                    
                    const statusElement = document.getElementById('register-status').querySelector('.card-body');
                    statusElement.innerHTML = '<div class=""loading-indicator"">Registering user...</div>';
                    
                    const formData = {{
                        username: document.getElementById('username').value,
                        password: document.getElementById('password').value,
                        email: document.getElementById('email').value || null,
                        phoneNumber: document.getElementById('phoneNumber').value || null,
                        role: parseInt(document.getElementById('role').value)
                    }};

                    try {{
                        const token = localStorage.getItem('auth_token');
                        if (!token) {{
                            window.location.href = '/api/auth/login?error=' + encodeURIComponent('Please log in as administrator');
                            return;
                        }}

                        const response = await fetch('/api/auth/register', {{
                            method: 'POST',
                            headers: {{
                                'Content-Type': 'application/json',
                                'Authorization': 'Bearer ' + token
                            }},
                            body: JSON.stringify(formData)
                        }});

                        let result;
                        try {{
                            result = await response.json();
                        }} catch (error) {{
                            // If response is not valid JSON, create a fallback result
                            result = {{
                                success: false,
                                message: 'Invalid response from server'
                            }};
                        }}
                        
                        if (response.ok) {{
                            statusElement.innerHTML = '<div class=""alert alert-success"">User registered successfully!</div>';
                            document.getElementById('registerForm').reset();
                        }} else {{
                            const errorMsg = result.message || 'Registration failed';
                            statusElement.innerHTML = '<div class=""alert alert-danger"">' + errorMsg + '</div>';
                            
                            if (response.status === 403 || response.status === 401) {{
                                document.getElementById('admin-check-status').style.display = 'none';
                                document.getElementById('admin-check-pending').style.display = 'block';
                                
                                // Attempt to refresh the token or redirect to login after 3 seconds
                                setTimeout(() => {{
                                    window.location.href = '/api/auth/login?error=' + encodeURIComponent('Please log in as administrator') + 
                                                           '&returnUrl=' + encodeURIComponent('/api/auth/register');
                                }}, 3000);
                            }}
                        }}
                    }} catch (error) {{
                        console.error('Registration error:', error);
                        statusElement.innerHTML = '<div class=""alert alert-danger"">An error occurred during registration. Please try again.</div>';
                    }}
                }});
            </script>
        ",  // {1} - content
            ""   // {2} - additional body classes
        );

        [HttpGet("register")]
        [AllowAnonymous]
        public async Task<IActionResult> RegisterPage([FromQuery] string? error = null, [FromQuery] string? message = null, [FromQuery] int? attempt = null)
        {
            if (!IsBrowserRequest())
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = "This endpoint is for browser access only. Use POST /api/auth/register for API calls."
                });
            }

            string? token = null;
            bool isAdmin = false;

            try
            {
                // Check Authorization header first
                if (Request.Headers.Authorization.Count > 0)
                {
                    var authHeader = Request.Headers.Authorization.ToString();
                    if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    {
                        token = authHeader.Substring("Bearer ".Length).Trim();
                    }
                }

                // If no auth header, check query string
                if (string.IsNullOrEmpty(token) && Request.Query.ContainsKey("token"))
                {
                    token = Request.Query["token"];
                }

                // If still no token, try to get it from localStorage
                if (string.IsNullOrEmpty(token))
                {
                    return Content(@"
                        <script>
                            const storedToken = localStorage.getItem('auth_token');
                            if (storedToken) {
                                window.location.href = '/api/auth/register?token=' + encodeURIComponent(storedToken);
                            } else {
                                window.location.href = '/api/auth/login?error=' + encodeURIComponent('Please log in as administrator') + 
                                                     '&returnUrl=' + encodeURIComponent('/api/auth/register');
                            }
                        </script>
                    ", "text/html");
                }

                // Validate token format
                if (!token.Contains('.') || token.Count(c => c == '.') != 2)
                {
                    _logger.LogWarning("Invalid token format detected during admin check");
                    return Redirect("/api/auth/login?error=Invalid token format&returnUrl=/api/auth/register");
                }

                try
                {
                    // Parse token and validate claims
                    var handler = new JwtSecurityTokenHandler();
                    var jwtToken = handler.ReadJwtToken(token);

                    // Check for admin role in multiple ways
                    var primaryRoleClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "primary_role");
                    var roleClaims = jwtToken.Claims.Where(c => c.Type == ClaimTypes.Role || c.Type == "role").ToList();
                    
                    isAdmin = primaryRoleClaim?.Value == "1" || 
                             roleClaims.Any(c => c.Value == "1" || c.Value.Equals("Administrator", StringComparison.OrdinalIgnoreCase));

                    // Log the claims for debugging
                    _logger.LogInformation("Token claims during admin check: Primary Role = {PrimaryRole}, Roles = {Roles}",
                        primaryRoleClaim?.Value,
                        string.Join(", ", roleClaims.Select(c => c.Value)));

                    if (!isAdmin)
                    {
                        _logger.LogWarning("User attempted to access register page without admin privileges");
                        
                        // If we haven't tried 3 times yet, increment attempt counter
                        if (!attempt.HasValue || attempt.Value < 3)
                        {
                            int nextAttempt = (attempt ?? 0) + 1;
                            return Content(RenderRegisterForm(
                                "Administrator privileges required. Verifying permissions...", 
                                null, 
                                nextAttempt, 
                                false
                            ), "text/html");
                        }
                        
                        return Redirect("/api/auth/login?error=Administrator privileges required&returnUrl=/api/auth/register");
                    }

                    // If we get here, the user is an admin
                    _logger.LogInformation("Admin access verified for registration page");
                    return Content(RenderRegisterForm(error, message, null, true), "text/html");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error parsing JWT token during admin check");
                    return Redirect("/api/auth/login?error=Invalid token. Please log in again.&returnUrl=/api/auth/register");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during admin check for registration page");
                return Redirect("/api/auth/login?error=Error validating admin access. Please try again.&returnUrl=/api/auth/register");
            }
        }
    }

    #region Request Models

    public class LoginRequest
    {
        public required string Username { get; set; }
        public required string Password { get; set; }
        public bool SkipTwoFactor { get; set; } = false;
    }

    public class RegisterRequest
    {
        public required string Username { get; set; }
        public required string Password { get; set; }
        public int Role { get; set; } = 0;
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
    }

    public class VerifyTotpRequest
    {
        public required string Code { get; set; }
        public required string SecretKey { get; set; }
    }

    public class ValidateTotpRequest
    {
        public required string TempToken { get; set; }
        public required string Code { get; set; }
    }

    public class WebAuthnRegisterCompleteRequest
    {
        public required AuthenticatorAttestationRawResponse AttestationResponse { get; set; }
    }

    public class WebAuthnLoginOptionsRequest
    {
        public required string Username { get; set; }
    }

    public class WebAuthnLoginCompleteRequest
    {
        public required string Username { get; set; }
        public required AuthenticatorAssertionRawResponse AssertionResponse { get; set; }
    }

    public class WebAuthnValidateRequest
    {
        public required string TempToken { get; set; }
        public required AuthenticatorAssertionRawResponse AssertionResponse { get; set; }
    }

    public class MagicLinkRequest
    {
        public required string Email { get; set; }
    }

    public class ValidateMagicLinkRequest
    {
        public required string Token { get; set; }
    }

    public class QrLoginRequest
    {
        public required string Username { get; set; }
        public required string Token { get; set; }
    }

    public class DirectQrLoginRequest
    {
        public required string Token { get; set; }
        public required string DeviceType { get; set; }
        public bool IsDesktopLogin { get; set; }
    }

    public class TokenRequest
    {
        public required string GrantType { get; set; }
        public string? Code { get; set; }
        public string? RefreshToken { get; set; }
        public required string ClientId { get; set; }
        public string? ClientSecret { get; set; }
        public string? RedirectUri { get; set; }
    }

    public class AuthorizeCallbackRequest
    {
        public required string RequestId { get; set; }
        public required string Username { get; set; }
        public required string Password { get; set; }
    }

    public class RegisterClientRequest
    {
        public required string ClientId { get; set; }
        public required string ClientSecret { get; set; }
        public required string DisplayName { get; set; }
        public required string[] RedirectUris { get; set; }
        public required string[] PostLogoutRedirectUris { get; set; }
        public required string[] AllowedScopes { get; set; }
        public bool RequireConsent { get; set; } = false;
    }

    public class UpdateClientRequest
    {
        public string? ClientSecret { get; set; }
        public string? DisplayName { get; set; }
        public string[]? RedirectUris { get; set; }
        public string[]? PostLogoutRedirectUris { get; set; }
        public string[]? AllowedScopes { get; set; }
        public bool? RequireConsent { get; set; }
    }

    #endregion

    #region Response Models

    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<string>? Errors { get; set; }
        public T? Data { get; set; }
    }

    public class UserDto
    {
        public uint Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public int Role { get; set; }
    }

    public class LoginResponse
    {
        public string Token { get; set; } = string.Empty;
        public UserDto User { get; set; } = new UserDto();
    }

    public class RegisterResponse
    {
        public UserDto User { get; set; } = new UserDto();
    }

    public class TwoFactorResponse
    {
        public bool RequiresTwoFactor { get; set; }
        public string TwoFactorType { get; set; } = string.Empty;
        public string TempToken { get; set; } = string.Empty;
    }

    public class WebAuthnTwoFactorResponse : TwoFactorResponse
    {
        public AssertionOptions? Options { get; set; }
    }

    public class TotpSetupResponse
    {
        public string SecretKey { get; set; } = string.Empty;
        public string QrCodeUri { get; set; } = string.Empty;
    }

    public class VerifyTotpResponse
    {
        public bool Enabled { get; set; }
    }

    public class DisableTotpResponse
    {
        public bool Disabled { get; set; }
    }

    public class ValidateTotpResponse
    {
        public string Token { get; set; } = string.Empty;
        public UserDto User { get; set; } = new UserDto();
    }

    public class WebAuthnRegisterOptionsResponse
    {
        public CredentialCreateOptions Options { get; set; } = new CredentialCreateOptions();
    }

    public class WebAuthnRegisterCompleteResponse
    {
        public bool Registered { get; set; }
    }

    public class WebAuthnLoginOptionsResponse
    {
        public AssertionOptions Options { get; set; } = new AssertionOptions();
    }

    public class WebAuthnLoginCompleteResponse
    {
        public string Token { get; set; } = string.Empty;
        public UserDto User { get; set; } = new UserDto();
    }

    public class WebAuthnValidateResponse
    {
        public string Token { get; set; } = string.Empty;
        public UserDto User { get; set; } = new UserDto();
    }

    public class WebAuthnCredentialDto
    {
        public string Id { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class WebAuthnCredentialsResponse
    {
        public List<WebAuthnCredentialDto> Credentials { get; set; } = new List<WebAuthnCredentialDto>();
    }

    public class WebAuthnRemoveCredentialResponse
    {
        public bool Removed { get; set; }
    }

    public class MagicLinkResponse
    {
        public bool Sent { get; set; }
        public string Email { get; set; } = string.Empty;
    }

    public class ValidateMagicLinkResponse
    {
        public string Token { get; set; } = string.Empty;
        public UserDto User { get; set; } = new UserDto();
    }

    public class QrCodeResponse
    {
        public string QrCode { get; set; } = string.Empty;
        public string? RawData { get; set; }
    }

    public class QrLoginResponse
    {
        public string Token { get; set; } = string.Empty;
        public UserDto User { get; set; } = new UserDto();
    }

    public class DirectQrCodeResponse
    {
        public string QrCode { get; set; } = string.Empty;
        public string? RawData { get; set; }
    }

    public class DirectQrLoginResponse
    {
        public string Token { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public UserDto User { get; set; } = new UserDto();
    }

    public class CheckQrLoginResponse
    {
        public bool Success { get; set; }
        public string? Token { get; set; }
    }

    public class TokenResponse
    {
        public string AccessToken { get; set; } = string.Empty;
        public string TokenType { get; set; } = "Bearer";
        public int ExpiresIn { get; set; }
        public string? RefreshToken { get; set; }
        public string? IdToken { get; set; }
        public string Scope { get; set; } = string.Empty;
    }

    public class UserInfoResponse
    {
        public string Sub { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string PreferredUsername { get; set; } = string.Empty;
        public string? Email { get; set; }
        public bool EmailVerified { get; set; }
        public string? PhoneNumber { get; set; }
        public bool PhoneNumberVerified { get; set; }
        public List<string> Roles { get; set; } = new List<string>();
    }

    public class RegisterClientResponse
    {
        public string ClientId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }

    public class UpdateClientResponse
    {
        public string ClientId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }

    public class DeleteClientResponse
    {
        public string ClientId { get; set; } = string.Empty;
        public bool Deleted { get; set; }
    }

    public class ClientDto
    {
        public string? ClientId { get; set; }
        public string? DisplayName { get; set; }
    }

    public class GetClientsResponse
    {
        public List<ClientDto> Clients { get; set; } = new List<ClientDto>();
    }

    public class GetClientResponse
    {
        public string ClientId { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string[] RedirectUris { get; set; } = Array.Empty<string>();
        public string[] PostLogoutRedirectUris { get; set; } = Array.Empty<string>();
        public string[] AllowedScopes { get; set; } = Array.Empty<string>();
        public bool RequireConsent { get; set; }
    }

    #endregion

    #region Helper Classes

    public class OpenIdConnectRequest
    {
        public string ClientId { get; set; } = string.Empty;
        public string RedirectUri { get; set; } = string.Empty;
        public string ResponseType { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string? Nonce { get; set; }
    }

    public class AuthorizationCodeData
    {
        public uint UserId { get; set; }
        public string[] Scopes { get; set; } = Array.Empty<string>();
        public string RedirectUri { get; set; } = string.Empty;
    }

    #endregion

    // Add these form request models at the end of the file
    public class RegisterClientFormRequest
    {
        public required string ClientId { get; set; }
        public required string ClientSecret { get; set; }
        public required string DisplayName { get; set; }
        public required string RedirectUris { get; set; }
        public required string PostLogoutRedirectUris { get; set; }
        public required string AllowedScopes { get; set; }
        public bool RequireConsent { get; set; }
    }

    public class UpdateClientFormRequest
    {
        public required string DisplayName { get; set; }
        public string? ClientSecret { get; set; }
        public required string RedirectUris { get; set; }
        public required string PostLogoutRedirectUris { get; set; }
        public required string AllowedScopes { get; set; }
        public bool RequireConsent { get; set; }
    }
}












