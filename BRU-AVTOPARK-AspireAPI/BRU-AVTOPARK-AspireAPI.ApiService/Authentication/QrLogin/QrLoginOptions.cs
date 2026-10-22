namespace BRU_AVTOPARK_AspireAPI.ApiService.Authentication;

public sealed class QrLoginOptions
{
    private int seconds = 120, sessions = 2000, perAddress = 5;
    public int SessionSeconds { get => seconds; set => seconds = Math.Clamp(value, 30, 300); }
    public int MaxSessions { get => sessions; set => sessions = Math.Clamp(value, 1, 20000); }
    public int MaxSessionsPerAddress { get => perAddress; set => perAddress = Math.Clamp(value, 1, 100); }
}
