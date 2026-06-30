using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EmployeeLeaveManagementSystem.Services;

public class PublicHolidayService
{
    private const string DefaultCountryCode = "IN";
    private const string DefaultSourceEndpoint = "https://date.nager.at/api/v3/PublicHolidays/{Year}/{CountryCode}";

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<PublicHolidayService> _logger;

    public PublicHolidayService(
        HttpClient httpClient,
        IConfiguration config,
        ILogger<PublicHolidayService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
    }

    public async Task<PublicHolidayLookupResult> GetPublicHolidaysInRangeAsync(
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default)
    {
        var countryCode = GetCountryCode();
        var sourceEndpoint = GetSourceEndpoint(countryCode);

        if (!_config.GetValue("ExternalApis:PublicHolidays:Enabled", true))
        {
            _logger.LogInformation("Public holiday API is disabled by configuration.");
            return PublicHolidayLookupResult.Disabled(countryCode, sourceEndpoint);
        }

        if (fromDate == default || toDate == default || fromDate > toDate)
        {
            _logger.LogWarning("Invalid date range for public holiday lookup: {FromDate} to {ToDate}", fromDate, toDate);
            return PublicHolidayLookupResult.Available(countryCode, sourceEndpoint, Array.Empty<PublicHolidayMatch>());
        }

        try
        {
            var holidays = new List<PublicHolidayMatch>();

            for (var year = fromDate.Year; year <= toDate.Year; year++)
            {
                var requestPath = $"api/v3/PublicHolidays/{year}/{countryCode}";
                var fullUrl = $"{_httpClient.BaseAddress?.ToString().TrimEnd('/')}/{requestPath}";

                _logger.LogInformation(
                    "Fetching public holidays for year {Year}, country {Country}. URL: {FullUrl}",
                    year, countryCode, fullUrl);

                using var response = await _httpClient.GetAsync(requestPath, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError(
                        "Public holiday API returned HTTP {StatusCode}. Endpoint={Endpoint} Body={Body}",
                        (int)response.StatusCode,
                        fullUrl,
                        responseBody);

                    // Return detailed info about the failure
                    return PublicHolidayLookupResult.Unavailable(
                        countryCode,
                        sourceEndpoint,
                        $"Public Holiday API returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). Endpoint: {fullUrl}");
                }

                var yearlyHolidays = await response.Content.ReadFromJsonAsync<List<NagerPublicHoliday>>(
                    cancellationToken: cancellationToken);

                if (yearlyHolidays is null)
                {
                    _logger.LogWarning("Public holiday API returned null response for year {Year}.", year);
                    continue;
                }

                _logger.LogInformation(
                    "Received {Count} public holidays for year {Year}, country {Country}.",
                    yearlyHolidays.Count, year, countryCode);

                holidays.AddRange(yearlyHolidays
                    .Where(holiday => holiday.Date >= fromDate
                        && holiday.Date <= toDate
                        && IsPublicHoliday(holiday))
                    .Select(holiday => new PublicHolidayMatch
                    {
                        Date = holiday.Date,
                        Name = holiday.Name,
                        LocalName = holiday.LocalName
                    }));
            }

            var result = PublicHolidayLookupResult.Available(
                countryCode,
                sourceEndpoint,
                holidays.OrderBy(holiday => holiday.Date).ToList());

            _logger.LogInformation(
                "Public holiday check complete: {Count} holiday(s) found in range {FromDate} to {ToDate}.",
                holidays.Count, fromDate, toDate);

            return result;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "Public holiday API timeout after {Timeout}s. CountryCode={CountryCode} From={FromDate} To={ToDate} Endpoint={SourceEndpoint}",
                _config.GetValue<int?>("ExternalApis:PublicHolidays:TimeoutSeconds") ?? 10,
                countryCode,
                fromDate,
                toDate,
                sourceEndpoint);

            return PublicHolidayLookupResult.Unavailable(
                countryCode,
                sourceEndpoint,
                $"Public Holiday API timeout. The request to {sourceEndpoint} did not respond within the timeout period.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex,
                "Public holiday API request failed. StatusCode={StatusCode} CountryCode={CountryCode} From={FromDate} To={ToDate} Endpoint={SourceEndpoint}",
                ex.StatusCode,
                countryCode,
                fromDate,
                toDate,
                sourceEndpoint);

            return PublicHolidayLookupResult.Unavailable(
                countryCode,
                sourceEndpoint,
                $"Public Holiday API request failed: {(ex.StatusCode.HasValue ? $"HTTP {(int)ex.StatusCode}" : "Connection failure")}. {ex.Message}");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex,
                "Public holiday API response parsing failed. CountryCode={CountryCode} From={FromDate} To={ToDate} Endpoint={SourceEndpoint}",
                countryCode,
                fromDate,
                toDate,
                sourceEndpoint);

            return PublicHolidayLookupResult.Unavailable(
                countryCode,
                sourceEndpoint,
                $"Public Holiday API response parsing error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Public holiday API lookup failed unexpectedly. CountryCode={CountryCode} From={FromDate} To={ToDate} Endpoint={SourceEndpoint}",
                countryCode,
                fromDate,
                toDate,
                sourceEndpoint);

            return PublicHolidayLookupResult.Unavailable(
                countryCode,
                sourceEndpoint,
                $"Public Holiday API unexpected error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private string GetCountryCode()
    {
        var configuredCountryCode = _config["ExternalApis:PublicHolidays:CountryCode"];

        return string.IsNullOrWhiteSpace(configuredCountryCode)
            ? DefaultCountryCode
            : configuredCountryCode.Trim().ToUpperInvariant();
    }

    private string GetSourceEndpoint(string countryCode)
    {
        var baseAddress = _httpClient.BaseAddress?.ToString().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseAddress))
            return DefaultSourceEndpoint.Replace("{CountryCode}", countryCode, StringComparison.OrdinalIgnoreCase);

        return $"{baseAddress}/api/v3/PublicHolidays/{{Year}}/{countryCode}";
    }

    private static bool IsPublicHoliday(NagerPublicHoliday holiday)
    {
        return holiday.Types.Count == 0
            || holiday.Types.Any(type => string.Equals(type, "Public", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class NagerPublicHoliday
    {
        [JsonPropertyName("date")]
        public DateOnly Date { get; set; }

        [JsonPropertyName("localName")]
        public string LocalName { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("types")]
        public List<string> Types { get; set; } = new();
    }
}

public sealed class PublicHolidayLookupResult
{
    public bool IsEnabled { get; init; }

    public bool IsAvailable { get; init; }

    public string CountryCode { get; init; } = string.Empty;

    public string SourceEndpoint { get; init; } = string.Empty;

    public string? ErrorDetail { get; init; }

    public IReadOnlyList<PublicHolidayMatch> Holidays { get; init; } = Array.Empty<PublicHolidayMatch>();

    public static PublicHolidayLookupResult Available(
        string countryCode,
        string sourceEndpoint,
        IReadOnlyList<PublicHolidayMatch> holidays)
    {
        return new PublicHolidayLookupResult
        {
            IsEnabled = true,
            IsAvailable = true,
            CountryCode = countryCode,
            SourceEndpoint = sourceEndpoint,
            Holidays = holidays
        };
    }

    public static PublicHolidayLookupResult Disabled(string countryCode, string sourceEndpoint)
    {
        return new PublicHolidayLookupResult
        {
            IsEnabled = false,
            IsAvailable = false,
            CountryCode = countryCode,
            SourceEndpoint = sourceEndpoint
        };
    }

    public static PublicHolidayLookupResult Unavailable(string countryCode, string sourceEndpoint, string? errorDetail = null)
    {
        return new PublicHolidayLookupResult
        {
            IsEnabled = true,
            IsAvailable = false,
            CountryCode = countryCode,
            SourceEndpoint = sourceEndpoint,
            ErrorDetail = errorDetail
        };
    }
}

public sealed class PublicHolidayMatch
{
    public DateOnly Date { get; init; }

    public string Name { get; init; } = string.Empty;

    public string LocalName { get; init; } = string.Empty;
}
