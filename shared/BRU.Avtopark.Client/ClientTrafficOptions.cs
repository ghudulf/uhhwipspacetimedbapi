using BRU.Avtopark.Contracts;

namespace BRU.Avtopark.Client;

public static class ClientTrafficOptions
{
    public static TrafficOptions Current
    {
        get
        {
            var level = TrafficOptions.ParseLevel(Environment.GetEnvironmentVariable("BRU_DEBUG_TRAFFIC"));
            var raw = Environment.GetEnvironmentVariable("BRU_DEBUG_TRAFFIC_MAX_BYTES");
            var cap = string.IsNullOrWhiteSpace(raw) ? 65536 : int.TryParse(raw, out var value) && value is > 0 and <= 16777216
                ? value : throw new ArgumentException("BRU_DEBUG_TRAFFIC_MAX_BYTES must be a positive integer no greater than 16777216.");
            return new(level, cap);
        }
    }
}
