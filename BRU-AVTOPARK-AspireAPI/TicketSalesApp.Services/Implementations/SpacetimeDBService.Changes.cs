using SpacetimeDB.Types;
using TicketSalesApp.Services.Interfaces;
namespace TicketSalesApp.Services.Implementations;

public partial class SpacetimeDBService
{
    public DatabaseChangeJournal Changes { get; } = new();
    private void ObserveApplicationChanges(DbConnection connection)
    {
        Changes.Reset();
        connection.Db.Bus.OnInsert += (context, row) => Changes.Publish("buses");
        connection.Db.Bus.OnUpdate += (context, previous, row) => Changes.Publish("buses");
        connection.Db.Bus.OnDelete += (context, row) => Changes.Publish("buses");
        connection.Db.BusRevision.OnInsert += (context, row) => Changes.Publish("buses");
        connection.Db.BusRevision.OnUpdate += (context, previous, row) => Changes.Publish("buses");
        connection.Db.BusRevision.OnDelete += (context, row) => Changes.Publish("buses");
        connection.Db.Route.OnInsert += (context, row) => Changes.Publish("routes");
        connection.Db.Route.OnUpdate += (context, previous, row) => Changes.Publish("routes");
        connection.Db.Route.OnDelete += (context, row) => Changes.Publish("routes");
        connection.Db.Maintenance.OnInsert += (context, row) => Changes.Publish("maintenance");
        connection.Db.Maintenance.OnUpdate += (context, previous, row) => Changes.Publish("maintenance");
        connection.Db.Maintenance.OnDelete += (context, row) => Changes.Publish("maintenance");
        connection.Db.Employee.OnInsert += (context, row) => Changes.Publish("employees");
        connection.Db.Employee.OnUpdate += (context, previous, row) => Changes.Publish("employees");
        connection.Db.Employee.OnDelete += (context, row) => Changes.Publish("employees");
        connection.Db.Job.OnInsert += (context, row) => Changes.Publish("jobs");
        connection.Db.Job.OnUpdate += (context, previous, row) => Changes.Publish("jobs");
        connection.Db.Job.OnDelete += (context, row) => Changes.Publish("jobs");
        connection.Db.RouteSchedule.OnInsert += (context, row) => Changes.Publish("schedules");
        connection.Db.RouteSchedule.OnUpdate += (context, previous, row) => Changes.Publish("schedules");
        connection.Db.RouteSchedule.OnDelete += (context, row) => Changes.Publish("schedules");
        connection.Db.Ticket.OnInsert += (context, row) => Changes.Publish("tickets");
        connection.Db.Ticket.OnUpdate += (context, previous, row) => Changes.Publish("tickets");
        connection.Db.Ticket.OnDelete += (context, row) => Changes.Publish("tickets");
        connection.Db.Sale.OnInsert += (context, row) => Changes.Publish("sales");
        connection.Db.Sale.OnUpdate += (context, previous, row) => Changes.Publish("sales");
        connection.Db.Sale.OnDelete += (context, row) => Changes.Publish("sales");
        connection.Db.SaleRevision.OnInsert += (context, row) => Changes.Publish("sales");
        connection.Db.SaleRevision.OnUpdate += (context, previous, row) => Changes.Publish("sales");
        connection.Db.SaleRevision.OnDelete += (context, row) => Changes.Publish("sales");
        connection.Db.UserProfile.OnInsert += (context, row) => Changes.Publish("access");
        connection.Db.UserProfile.OnUpdate += (context, previous, row) => Changes.Publish("access");
        connection.Db.UserProfile.OnDelete += (context, row) => Changes.Publish("access");
        connection.Db.UserRole.OnInsert += (context, row) => Changes.Publish("access");
        connection.Db.UserRole.OnUpdate += (context, previous, row) => Changes.Publish("access");
        connection.Db.UserRole.OnDelete += (context, row) => Changes.Publish("access");
        connection.Db.Role.OnInsert += (context, row) => Changes.Publish("access");
        connection.Db.Role.OnUpdate += (context, previous, row) => Changes.Publish("access");
        connection.Db.Role.OnDelete += (context, row) => Changes.Publish("access");
        connection.Db.Permission.OnInsert += (context, row) => Changes.Publish("access");
        connection.Db.Permission.OnUpdate += (context, previous, row) => Changes.Publish("access");
        connection.Db.Permission.OnDelete += (context, row) => Changes.Publish("access");
        connection.Db.RolePermission.OnInsert += (context, row) => Changes.Publish("access");
        connection.Db.RolePermission.OnUpdate += (context, previous, row) => Changes.Publish("access");
        connection.Db.RolePermission.OnDelete += (context, row) => Changes.Publish("access");
    }
}
