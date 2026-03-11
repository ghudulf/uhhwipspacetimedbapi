namespace BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Options;

public sealed class RealtimeEventOptions
{
    public const string SectionName = "Realtime";

    public int BufferCapacity { get; set; } = 4096;
    public int RecentEventLimit { get; set; } = 500;
    public int PublishTimeoutMs { get; set; } = 250;
    public string[] AllowedOrigins { get; set; } = [];
}
