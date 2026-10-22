using System.Globalization;
using BRU.Avtopark.Contracts;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Authorization;
using TicketSalesApp.Services.Implementations;
using TicketSalesApp.Services.Interfaces;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Application;

public sealed partial class ApplicationOperations
{
    private void RegisterSalesReads(ISpacetimeDBService database)
    {
        Register<PageRequest>("sales.list", "sales.view", false, async (actor, request, _, ct) =>
        {
            ValidatePage(request);
            if (request.EndDate < request.StartDate || request.MinPrice < 0 || request.MaxPrice < request.MinPrice || request.BuyerName?.Length > 200) throw new OperationException("validation_failed");
            var access = await _access.GetAsync(actor, ct);
            return await database.ReadAsync(c =>
            {
                var rows = c.Db.Sale.Iter().OrderBy(s => s.SaleDate).ThenBy(s => s.SaleId)
                    .Where(s => SalePresentation.MatchesFilters(SalePresentation.Map(c, s), request.StartDate?.UtcDateTime, request.EndDate?.UtcDateTime, request.MinPrice, request.MaxPrice, request.BuyerName))
                    .Where(s => Matches(request.Search, s.SaleId.ToString(), s.TicketId.ToString(), s.TicketSoldToUser, s.TicketSoldToUserPhone,
                        Timestamp(s.SaleDate)?.ToString("O", CultureInfo.InvariantCulture), c.Db.Ticket.TicketId.Find(s.TicketId)?.TicketPrice.ToString(CultureInfo.InvariantCulture)))
                    .ToArray();
                return new CatalogPageResult<SaleRow>(Page(rows, request).Select(s => ProjectSale(c, s, access)).ToArray(), request.Page, request.PageSize, rows.Length,
                    access.Allows("sales.create"), access.Allows("sales.edit"), access.Allows("sales.delete"), rows.Sum(s => SalePresentation.Amount(c, s) ?? 0), rows.Count(s => !SalePresentation.Amount(c, s).HasValue));
            }, ct);
        });
        RegisterEntityRead(database, "sales", "sales.view", (c, id, access) => ProjectSale(c, c.Db.Sale.SaleId.Find(id) ?? throw new OperationException("not_found"), access));
    }
    private static SaleRow ProjectSale(DbConnection c, Sale s, UserAccessSnapshot access) => new(s.SaleId, Timestamp(s.SaleDate), s.TicketId,
        s.TicketSoldToUser, s.TicketSoldToUserPhone, s.SellerId?.ToString(), SalePresentation.Amount(c, s), c.Db.SaleRevision.SaleId.Find(s.SaleId)?.Revision ?? 0,
        access.Allows("users.view") && s.SellerId.HasValue ? c.Db.UserProfile.UserId.Find(s.SellerId.Value)?.Login : null);
}
