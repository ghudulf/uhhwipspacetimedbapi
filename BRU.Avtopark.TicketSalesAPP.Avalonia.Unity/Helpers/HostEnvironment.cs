using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using System;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Helpers;

/// <summary>
/// Runtime host-environment detection helper.
/// Replaces compile-time #if DESKTOP / #if !DESKTOP guards in the shared library,
/// which cannot work because the DESKTOP constant is only defined in the Desktop
/// entry-point .csproj — not in the shared library project itself.
/// </summary>
public static class HostEnvironment
{
    /// <summary>
    /// True when the app is running as a standalone Avalonia desktop application
    /// (launched via the Desktop entry-point, NOT embedded inside a MAUI host).
    /// </summary>
    public static bool IsStandaloneDesktop =>
        !IsMauiHost &&
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime;

    /// <summary>
    /// True when the Avalonia UI is embedded inside a MAUI host process.
    /// Set by MauiProgram.cs via AppContext.SetData("MAUI_HOST", true).
    /// </summary>
    public static bool IsMauiHost =>
        AppContext.GetData("MAUI_HOST") is true;

    /// <summary>
    /// True when the runtime supports desktop-only APIs such as BeginMoveDrag,
    /// WindowDecorations, and multi-window dialogs.
    /// Equivalent to IsStandaloneDesktop for now.
    /// </summary>
    public static bool IsDesktopCapable => IsStandaloneDesktop;
}
