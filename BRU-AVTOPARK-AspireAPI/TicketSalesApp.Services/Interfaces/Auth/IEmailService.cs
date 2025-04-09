using System.Threading.Tasks;

namespace TicketSalesApp.Services.Interfaces
{
    /// <summary>
    /// Service for sending emails
    /// </summary>
    public interface IEmailService
    {
        Task SendEmailAsync(string to, string subject, string body);
    }
}

