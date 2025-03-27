// AdminServer/Options/RateLimitOptions.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Threading.Tasks;

using TicketSalesApp.Services.Interfaces;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;

namespace TicketSalesApp.AdminServer.Configuration
{
    // AdminServer/Options/RateLimitOptions.cs
    public class RateLimitOptions
    {
        public const string RateLimit = "RateLimit";

        public int PermitLimit { get; set; } = 100;
        public int Window { get; set; } = 60;
        public int QueueLimit { get; set; } = 2;
        public int SegmentsPerWindow { get; set; } = 8;
        public int TokenLimit { get; set; } = 100;
        public int TokensPerPeriod { get; set; } = 50;
        public int ReplenishmentPeriod { get; set; } = 10;
        public int ConcurrencyLimit { get; set; } = 10;
    }
}