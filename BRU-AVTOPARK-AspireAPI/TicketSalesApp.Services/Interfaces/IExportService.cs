// Services/Interfaces/IDataService.cs
using System.Collections.Generic;
using System.Threading.Tasks;


namespace TicketSalesApp.Services.Interfaces
{
    // Services/Interfaces/IExportService.cs
    public interface IExportService
    {
        Task<byte[]> ExportToExcelAsync<T>(IEnumerable<T> data, string sheetName);
        Task<byte[]> ExportToPdfAsync<T>(IEnumerable<T> data, string title);
        Task<byte[]> ExportToCsvAsync<T>(IEnumerable<T> data);
        Task<string> ExportToJsonAsync<T>(IEnumerable<T> data);
    }
}
