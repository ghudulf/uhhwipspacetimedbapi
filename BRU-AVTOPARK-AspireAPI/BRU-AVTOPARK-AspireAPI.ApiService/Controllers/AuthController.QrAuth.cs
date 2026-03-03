using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BRU_AVTOPARK_AspireAPI.ApiService.Models.Auth;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Controllers
{
    public partial class AuthController
    {
        // ── GET /api/auth/qr/login (HTML page) ─────────────────────────

        [HttpGet("qr/login")]
        [AllowAnonymous]
        public async Task<IActionResult> QrLoginPage()
        {
            try
            {
                // Generate a direct QR code for desktop login
                var (qrCode, rawData) = await _qrAuthService.GenerateDirectLoginQRCodeAsync("", "desktop");
                var deviceId = rawData; // Simplification; actual implementation may differ

                if (IsBrowserRequest())
                {
                    // Pass deviceId as query param so the page JS can poll
                    return Redirect($"/api/auth/qr/login/page?deviceId={Uri.EscapeDataString(deviceId)}&qr={Uri.EscapeDataString(qrCode)}");
                }

                return Ok(new ApiResponse<DirectQrCodeResponse>
                {
                    Success = true,
                    Data = new DirectQrCodeResponse { QrCode = qrCode, RawData = rawData }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating QR login");
                return StatusCode(500, new ApiResponse<object> { Success = false, Message = "Error generating QR login" });
            }
        }

        // ── GET /api/auth/qr/generate ───────────────────────────────────

        [HttpGet("qr/generate")]
        [AllowAnonymous]
        public async Task<IActionResult> QrGenerate()
        {
            try
            {
                var identity = GetUserIdentity();
                if (identity == null)
                    return Unauthorized(new ApiResponse<object> { Success = false, Message = "Authentication required" });

                var user = GetUserByIdentity(identity);
                if (user == null)
                    return Unauthorized(new ApiResponse<object> { Success = false, Message = "User not found" });

                var (qrCode, rawData) = await _qrAuthService.GenerateQRCodeWithDataAsync(user);

                return Ok(new ApiResponse<QrCodeResponse>
                {
                    Success = true,
                    Data = new QrCodeResponse { QrCode = qrCode, RawData = rawData }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating QR code");
                return StatusCode(500, new ApiResponse<object> { Success = false, Message = "Error generating QR code" });
            }
        }

        // ── POST /api/auth/qr/login ────────────────────────────────────

        [HttpPost("qr/login")]
        [AllowAnonymous]
        public async Task<IActionResult> QrLogin([FromBody] QrLoginRequest request)
        {
            try
            {
                var (success, user) = await _qrAuthService.ValidateQRLoginTokenAsync(request.Token);

                if (!success || user == null)
                    return Unauthorized(new ApiResponse<object> { Success = false, Message = "Invalid QR token" });

                var token = _tokenService.GenerateJwtToken(user);

                return Ok(new ApiResponse<QrLoginResponse>
                {
                    Success = true,
                    Message = "QR login successful",
                    Data = new QrLoginResponse { Token = token, User = ToUserDto(user) }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during QR login");
                return StatusCode(500, new ApiResponse<object> { Success = false, Message = "Error during QR login" });
            }
        }

        // ── GET /api/auth/qr/direct/generate ────────────────────────────

        [HttpGet("qr/direct/generate")]
        [AllowAnonymous]
        public async Task<IActionResult> QrDirectGenerate([FromQuery] string? deviceType)
        {
            try
            {
                var identity = GetUserIdentity();
                if (identity == null)
                    return Unauthorized(new ApiResponse<object> { Success = false, Message = "Authentication required" });

                var user = GetUserByIdentity(identity);
                if (user == null)
                    return Unauthorized(new ApiResponse<object> { Success = false, Message = "User not found" });

                var (qrCode, rawData) = await _qrAuthService.GenerateDirectLoginQRCodeAsync(
                    user.Login, deviceType ?? "desktop");

                return Ok(new ApiResponse<DirectQrCodeResponse>
                {
                    Success = true,
                    Data = new DirectQrCodeResponse { QrCode = qrCode, RawData = rawData }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating direct QR code");
                return StatusCode(500, new ApiResponse<object> { Success = false, Message = "Error generating QR code" });
            }
        }

        // ── POST /api/auth/qr/direct/login ──────────────────────────────

        [HttpPost("qr/direct/login")]
        [AllowAnonymous]
        public async Task<IActionResult> QrDirectLogin([FromBody] DirectQrLoginRequest request)
        {
            try
            {
                var (success, user, deviceId) =
                    await _qrAuthService.ValidateDirectLoginTokenAsync(request.Token, request.DeviceType);

                if (!success || user == null)
                    return Unauthorized(new ApiResponse<object> { Success = false, Message = "Invalid direct QR token" });

                var token = _tokenService.GenerateJwtToken(user);

                // Notify the waiting device
                if (request.IsDesktopLogin)
                    await _qrAuthService.NotifyDeviceLoginSuccessAsync(deviceId, token);

                return Ok(new ApiResponse<DirectQrLoginResponse>
                {
                    Success = true,
                    Message = "Direct QR login successful",
                    Data = new DirectQrLoginResponse
                    {
                        Token = token,
                        DeviceId = deviceId,
                        User = ToUserDto(user)
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during direct QR login");
                return StatusCode(500, new ApiResponse<object> { Success = false, Message = "Error during direct QR login" });
            }
        }

        // ── GET /api/auth/qr/direct/check ───────────────────────────────

        [HttpGet("qr/direct/check")]
        [AllowAnonymous]
        public IActionResult QrDirectCheck([FromQuery] string deviceId)
        {
            try
            {
                // Check memory cache for a completed login
                if (_cache.TryGetValue($"qr_login_{deviceId}", out string? token) && token != null)
                {
                    _cache.Remove($"qr_login_{deviceId}");
                    return Ok(new ApiResponse<CheckQrLoginResponse>
                    {
                        Success = true,
                        Data = new CheckQrLoginResponse { Success = true, Token = token }
                    });
                }

                return Ok(new ApiResponse<CheckQrLoginResponse>
                {
                    Success = true,
                    Data = new CheckQrLoginResponse { Success = false }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking QR login status");
                return StatusCode(500, new ApiResponse<object> { Success = false, Message = "Error checking status" });
            }
        }
    }
}
