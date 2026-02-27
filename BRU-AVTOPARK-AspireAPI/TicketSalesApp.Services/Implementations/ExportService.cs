// Services/Implementations/ExportService.cs
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using TicketSalesApp.Services.Interfaces;

namespace TicketSalesApp.Services.Implementations
{
    public class ExportService : IExportService
    {
        private readonly ILogger<ExportService> _logger;

        public ExportService(ILogger<ExportService> logger)
        {
            _logger = logger;
        }

        public async Task<byte[]> ExportToExcelAsync<T>(IEnumerable<T> data, string sheetName)
        {
            try
            {
                var sb = new StringBuilder();
                var properties = typeof(T).GetProperties();

                // Add headers
                sb.AppendLine(string.Join(",", Array.ConvertAll(properties, p => p.Name)));

                // Add data rows
                foreach (var item in data)
                {
                    var values = new List<string>();
                    foreach (var prop in properties)
                    {
                        var value = prop.GetValue(item)?.ToString() ?? "";
                        // Escape commas and quotes in CSV
                        if (value.Contains(",") || value.Contains("\""))
                        {
                            value = $"\"{value.Replace("\"", "\"\"")}\"";
                        }
                        values.Add(value);
                    }
                    sb.AppendLine(string.Join(",", values));
                }

                return Encoding.UTF8.GetBytes(sb.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting data to Excel format");
                throw;
            }
        }

        public async Task<byte[]> ExportToPdfAsync<T>(IEnumerable<T> data, string title)
        {
            try
            {
                var sb = new StringBuilder();
                var properties = typeof(T).GetProperties();

                // Create HTML document
                sb.AppendLine("<!DOCTYPE html>");
                sb.AppendLine("<html>");
                sb.AppendLine("<head>");
                sb.AppendLine("<meta charset=\"UTF-8\">");
                sb.AppendLine("<style>");
                sb.AppendLine("body { font-family: Arial, sans-serif; margin: 20px; }");
                sb.AppendLine("table { border-collapse: collapse; width: 100%; margin-top: 20px; }");
                sb.AppendLine("th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }");
                sb.AppendLine("th { background-color: #f2f2f2; }");
                sb.AppendLine("h1 { color: #333; }");
                sb.AppendLine("</style>");
                sb.AppendLine("</head>");
                sb.AppendLine("<body>");

                // Add title
                sb.AppendLine($"<h1>{title}</h1>");

                // Create table
                sb.AppendLine("<table>");
                
                // Add headers
                sb.AppendLine("<tr>");
                foreach (var prop in properties)
                {
                    sb.AppendLine($"<th>{prop.Name}</th>");
                }
                sb.AppendLine("</tr>");

                // Add data rows
                foreach (var item in data)
                {
                    sb.AppendLine("<tr>");
                    foreach (var prop in properties)
                    {
                        var value = prop.GetValue(item)?.ToString() ?? "";
                        sb.AppendLine($"<td>{value}</td>");
                    }
                    sb.AppendLine("</tr>");
                }

                sb.AppendLine("</table>");
                sb.AppendLine("</body>");
                sb.AppendLine("</html>");

                return Encoding.UTF8.GetBytes(sb.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting data to PDF format");
                throw;
            }
        }

        public async Task<byte[]> ExportToCsvAsync<T>(IEnumerable<T> data)
        {
            try
            {
                var sb = new StringBuilder();
                var properties = typeof(T).GetProperties();

                // Add headers
                sb.AppendLine(string.Join(",", Array.ConvertAll(properties, p => p.Name)));

                // Add data rows
                foreach (var item in data)
                {
                    var values = new List<string>();
                    foreach (var prop in properties)
                    {
                        var value = prop.GetValue(item)?.ToString() ?? "";
                        // Escape commas and quotes in CSV
                        if (value.Contains(",") || value.Contains("\""))
                        {
                            value = $"\"{value.Replace("\"", "\"\"")}\"";
                        }
                        values.Add(value);
                    }
                    sb.AppendLine(string.Join(",", values));
                }

                return Encoding.UTF8.GetBytes(sb.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting data to CSV format");
                throw;
            }
        }

        public async Task<string> ExportToJsonAsync<T>(IEnumerable<T> data)
        {
            try
            {
                return JsonSerializer.Serialize(data, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting data to JSON");
                throw;
            }
        }
    }
}
