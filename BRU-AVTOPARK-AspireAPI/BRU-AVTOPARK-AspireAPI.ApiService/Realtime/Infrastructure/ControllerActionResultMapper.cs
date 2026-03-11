using Microsoft.AspNetCore.Mvc;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Infrastructure;

public static class ControllerActionResultMapper
{
    public static object Map(IActionResult actionResult)
    {
        return actionResult switch
        {
            ObjectResult objectResult => new
            {
                StatusCode = objectResult.StatusCode ?? StatusCodes.Status200OK,
                Payload = objectResult.Value
            },
            JsonResult jsonResult => new
            {
                StatusCode = StatusCodes.Status200OK,
                Payload = jsonResult.Value
            },
            StatusCodeResult statusCodeResult => new
            {
                StatusCode = statusCodeResult.StatusCode,
                Payload = (object?)null
            },
            _ => new
            {
                StatusCode = StatusCodes.Status200OK,
                Payload = (object?)null
            }
        };
    }
}
