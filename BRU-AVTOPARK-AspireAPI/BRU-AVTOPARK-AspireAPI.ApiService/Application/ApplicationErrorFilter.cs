using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using TicketSalesApp.Services.Interfaces;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Application;

/// <summary>Maps application failures without exposing reducer payloads or internal exception messages.</summary>
public sealed class ApplicationErrorFilterAttribute : ExceptionFilterAttribute
{
    public static bool Handles(Exception exception) => exception is OperationException or ReducerRejectedException
        or CommitOutcomeUnknownException or DatabaseUnavailableException or TimeoutException;

    public override void OnException(ExceptionContext context)
    {
        if (!Handles(context.Exception)) return;
        var code = context.Exception switch
        {
            OperationException operation => operation.Code,
            ReducerRejectedException => "conflict",
            CommitOutcomeUnknownException => "commit_outcome_unknown",
            TimeoutException => "timeout",
            _ => "database_unavailable"
        };
        var status = code switch
        {
            "forbidden" => 403,
            "not_found" or "unsupported_operation" => 404,
            "conflict" => 409,
            "timeout" => 504,
            "commit_outcome_unknown" or "database_unavailable" => 503,
            _ => 400
        };
        context.Result = new ObjectResult(new { code, traceId = context.HttpContext.TraceIdentifier }) { StatusCode = status };
        context.ExceptionHandled = true;
    }
}
