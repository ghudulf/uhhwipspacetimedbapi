// Services/Interfaces/IDataService.cs
using System.Collections.Generic;
using System.Threading.Tasks;
using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Interfaces
{
    public interface IDataService
    {
        Task<T?> GetAsync<T>(uint id) where T : class;
        Task<List<T>> GetAllAsync<T>() where T : class;
        Task<T?> AddAsync<T>(T entity) where T : class;
        Task<T?> UpdateAsync<T>(T entity) where T : class;
        Task<bool> DeleteAsync<T>(uint id) where T : class;
    }
}