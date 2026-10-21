using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using BRU_AVTOPARK.Models.Requests;
using BRU_AVTOPARK_AspireAPI.ApiService.Controllers;

internal static class SecurityScanRegressionTests
{
    public static async Task RunAsync()
    {
        // No service dependency may be touched when a form supplies its own forged admin token.
        // The verified Bearer principal, never ReadJwtToken on form data, grants administration.
        var controller = (AuthController)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(AuthController));
        controller.ControllerContext = new() { HttpContext = new DefaultHttpContext() };
        var forged = "eyJhbGciOiJub25lIn0.eyJyb2xlIjoiQWRtaW5pc3RyYXRvciIsImV4cCI6NDEwMjQ0NDgwMH0.";
        foreach (var name in new[] { nameof(AuthController.RegisterClientSubmit), nameof(AuthController.UpdateClientSubmit), nameof(AuthController.DeleteClientSubmit) })
        {
            var method = typeof(AuthController).GetMethod(name)!;
            var auth = method.GetCustomAttribute<AuthorizeAttribute>();
            Check(auth?.Roles == "Administrator" && auth.AuthenticationSchemes == "Bearer" && method.GetCustomAttribute<AllowAnonymousAttribute>() == null, name + " authentication metadata");
        }
        var register = new RegisterClientFormRequest { Token = forged, ClientId = "synthetic", ClientSecret = "synthetic", DisplayName = "Synthetic" };
        var update = new UpdateClientFormRequest { Token = forged, DisplayName = "Synthetic" };
        Check(await controller.RegisterClientSubmit(register) is UnauthorizedResult, "forged register form reached service");
        Check(await controller.UpdateClientSubmit("client", update) is UnauthorizedResult, "forged update form reached service");
        Check(await controller.DeleteClientSubmit("client", forged) is UnauthorizedResult, "forged delete form reached service");
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "User")], "Bearer"));
        Check(await controller.RegisterClientSubmit(register) is ForbidResult, "nonadministrator form reached service");
        Check(await controller.UpdateClientSubmit("client", update) is ForbidResult, "nonadministrator update reached service");
        Check(await controller.DeleteClientSubmit("client", forged) is ForbidResult, "nonadministrator delete reached service");
        Console.WriteLine("PASS security scan regressions: OAuth client form mutations require verified Bearer administrator; forged form tokens and authenticated nonadministrators rejected before services");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception("Security scan regression: " + message); }
}
