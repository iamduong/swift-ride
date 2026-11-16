using System.Globalization;
using MatchingPricing_Service.Application.Contracts;

namespace MatchingPricing_Service.Application.Services.Util;

public interface IOSRMService
{
    Task<OSRMResponseContract> GetRouteAsync(
        IReadOnlyList<LocationContract> locations,
        CancellationToken cancellationToken = default);
}

public class OSRMService(HttpClient httpClient) : IOSRMService
{
    private const string BaseUrl = "https://router.project-osrm.org/route/v1/driving/";

    public async Task<OSRMResponseContract> GetRouteAsync(
        IReadOnlyList<LocationContract> locations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(locations);

        if (locations.Count < 2)
        {
            throw new ArgumentException(
                "At least two locations are required to calculate a route.",
                nameof(locations));
        }

        foreach (var location in locations)
        {
            ValidateLocation(location);
        }

        // OSRM yêu cầu mỗi điểm theo thứ tự kinh độ, vĩ độ.
        var coordinates = string.Join(';', locations.Select(location => string.Create(
            CultureInfo.InvariantCulture,
            $"{location.Longitude},{location.Latitude}")));

        var requestUrl = $"{BaseUrl}{coordinates}?geometries=geojson";
        using var response = await httpClient.GetAsync(requestUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<OSRMResponseContract>(
                   cancellationToken: cancellationToken)
               ?? throw new HttpRequestException("OSRM returned an empty response.");
    }

    private static void ValidateLocation(LocationContract location)
    {
        ArgumentNullException.ThrowIfNull(location);

        if (!double.IsFinite(location.Latitude) || location.Latitude is < -90 or > 90)
        {
            throw new ArgumentOutOfRangeException(
                nameof(location.Latitude),
                "Latitude must be between -90 and 90.");
        }

        if (!double.IsFinite(location.Longitude) || location.Longitude is < -180 or > 180)
        {
            throw new ArgumentOutOfRangeException(
                nameof(location.Longitude),
                "Longitude must be between -180 and 180.");
        }
    }
}