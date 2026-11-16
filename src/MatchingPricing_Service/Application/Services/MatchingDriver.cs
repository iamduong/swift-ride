using MatchingPricing_Service.Application.Contracts;
using MatchingPricing_Service.Application.Services.Impl;
using MatchingPricing_Service.Infrastructure.Persistence.Impl;
using Microsoft.Extensions.Options;

namespace MatchingPricing_Service.Application.Services;

public sealed class MatchingDriver(
    IDriverLocationRepository driverLocationRepository,
    IOptions<MatchingSetting> options) : IMatchingService
{
    public async Task<MatchingResponseContract> MatchAsync(
        MatchingRequestContract contract,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(contract.Pickup);

        if (contract.TripUuid == Guid.Empty)
        {
            throw new ArgumentException("TripUuid không hợp lệ.", nameof(contract.TripUuid));
        }

        ValidateLocation(contract.Pickup);

        // Chỉ tìm tài xế khả dụng trong bán kính cấu hình, bỏ qua những người đã được thử.
        var driver = await driverLocationRepository.FindNearestAvailableAsync(
            contract.Pickup,
            contract.ExcludeDriverUuids ?? [],
            options.Value.MaxDistanceMeters,
            cancellationToken);

        // Guid.Empty báo cho bên gọi biết rằng không tìm được tài xế phù hợp.
        return new MatchingResponseContract
        {
            TripUuid = contract.TripUuid,
            DriverUuid = driver?.DriverUuid ?? Guid.Empty,
            IsSuccess = driver is not null
        };
    }

    private static void ValidateLocation(LocationContract location)
    {
        if (!double.IsFinite(location.Latitude) || location.Latitude is < -90 or > 90)
        {
            throw new ArgumentOutOfRangeException(
                nameof(location.Latitude),
                "Latitude phải nằm trong khoảng từ -90 đến 90.");
        }

        if (!double.IsFinite(location.Longitude) || location.Longitude is < -180 or > 180)
        {
            throw new ArgumentOutOfRangeException(
                nameof(location.Longitude),
                "Longitude phải nằm trong khoảng từ -180 đến 180.");
        }
    }
}

public sealed class MatchingSetting
{
    public double MaxDistanceMeters { get; set; } = 5000;
}
