using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace Claxe.Services;

/// <summary>
/// Weather for the new tab widget from Open-Meteo (free, no key, no account). Only the city
/// name the user typed is sent; results are cached for 20 minutes.
/// </summary>
public static class WeatherService
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly Dictionary<string, (DateTime At, object Data)> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(20);

    public static async Task<object> GetAsync(string city, string language)
    {
        city = city.Trim();
        var key = $"{language}|{city}";
        if (Cache.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.At < CacheFor)
            return cached.Data;

        try
        {
            using var geo = await GetJsonAsync(
                $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count=1&language={language}&format=json");
            if (!geo.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
                return new { type = "weather", ok = false, city, error = "notFound" };

            var place = results[0];
            var lat = place.GetProperty("latitude").GetDouble().ToString(CultureInfo.InvariantCulture);
            var lon = place.GetProperty("longitude").GetDouble().ToString(CultureInfo.InvariantCulture);
            using var forecast = await GetJsonAsync(
                $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}" +
                "&current=temperature_2m,apparent_temperature,weather_code,is_day,wind_speed_10m,relative_humidity_2m" +
                "&daily=weather_code,temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=4");

            var current = forecast.RootElement.GetProperty("current");
            var daily = forecast.RootElement.GetProperty("daily");
            var dates = daily.GetProperty("time").EnumerateArray().Select(d => d.GetString()).ToList();
            var codes = daily.GetProperty("weather_code").EnumerateArray().Select(c => c.GetInt32()).ToList();
            var highs = daily.GetProperty("temperature_2m_max").EnumerateArray().Select(c => c.GetDouble()).ToList();
            var lows = daily.GetProperty("temperature_2m_min").EnumerateArray().Select(c => c.GetDouble()).ToList();

            object data = new
            {
                type = "weather",
                ok = true,
                city,
                place = place.GetProperty("name").GetString(),
                country = place.TryGetProperty("country", out var country) ? country.GetString() : null,
                temp = current.GetProperty("temperature_2m").GetDouble(),
                feels = current.GetProperty("apparent_temperature").GetDouble(),
                code = current.GetProperty("weather_code").GetInt32(),
                isDay = current.GetProperty("is_day").GetInt32() == 1,
                wind = current.GetProperty("wind_speed_10m").GetDouble(),
                humidity = current.GetProperty("relative_humidity_2m").GetDouble(),
                days = dates.Select((date, i) => new { date, code = codes[i], max = highs[i], min = lows[i] }).ToList(),
            };
            Cache[key] = (DateTime.UtcNow, data);
            return data;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                       or KeyNotFoundException or InvalidOperationException)
        {
            Log.Info($"Hava durumu alınamadı ({city}): {ex.Message}");
            return new { type = "weather", ok = false, city, error = "offline" };
        }
    }

    private static async Task<JsonDocument> GetJsonAsync(string url)
    {
        using var response = await Http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.TryParseAdd("Sorune/" + UpdateService.CurrentVersion);
        return client;
    }
}
