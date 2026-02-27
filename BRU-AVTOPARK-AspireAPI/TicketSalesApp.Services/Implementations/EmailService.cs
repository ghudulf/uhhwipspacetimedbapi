using System;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using TicketSalesApp.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace TicketSalesApp.Services.Implementations
{
    /// <summary>
    /// Implementation of the Email service
    /// </summary>
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailService> _logger;
        private readonly string _smtpServer;
        private readonly int _smtpPort;
        private readonly string _smtpUsername;
        private readonly string _smtpPassword;
        private readonly string _fromEmail;
        private readonly string _fromName;
        private readonly bool _enableSsl;

        public EmailService(
            IConfiguration configuration,
            ILogger<EmailService> logger)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _smtpServer = _configuration["Email:SmtpServer"] ?? "smtp.example.com";
            _smtpPort = int.Parse(_configuration["Email:SmtpPort"] ?? "587");
            _smtpUsername = _configuration["Email:SmtpUsername"] ?? "username";
            _smtpPassword = _configuration["Email:SmtpPassword"] ?? "password";
            _fromEmail = _configuration["Email:FromEmail"] ?? "noreply@example.com";
            _fromName = _configuration["Email:FromName"] ?? "BRU-AVTOPARK";
            _enableSsl = bool.Parse(_configuration["Email:EnableSsl"] ?? "true");
        }

        /// <summary>
        /// Sends an email
        /// </summary>
        public async Task SendEmailAsync(string to, string subject, string body)
        {
            try
            {
                _logger.LogInformation("Sending email to: {To}, Subject: {Subject}", to, subject);

                var message = new MailMessage
                {
                    From = new MailAddress(_fromEmail, _fromName),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = true
                };
                
                message.To.Add(new MailAddress(to));

                using (var client = new SmtpClient(_smtpServer, _smtpPort))
                {
                    client.EnableSsl = _enableSsl;
                    client.Credentials = new NetworkCredential(_smtpUsername, _smtpPassword);
                    
                    await client.SendMailAsync(message);
                }

                _logger.LogInformation("Email sent successfully to: {To}", to);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending email to: {To}", to);
                throw;
            }
        }
    }
}

