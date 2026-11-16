using Trip_Service.Application.Contracts.Trip;
using Trip_Service.Application.Services.Impl;
using Trip_Service.Domain;
using Trip_Service.Infrastructure.Persistence.Impl;

namespace Trip_Service.Application.Services;

public sealed class TripService(
    IAccountRepository accountRepository,
    ITripRepository tripRepository) : ITripService
{
    public async Task<Trip> GetTripByUuidAsync(
        Guid tripUuid,
        CancellationToken cancellationToken = default)
    {
        return await tripRepository.FindByUuidAsync(tripUuid, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy chuyến đi.");
    }

    public Task<Trip?> GetActiveTripByRiderAsync(
        Guid riderUuid,
        Guid? excludeTripUuid = null,
        CancellationToken cancellationToken = default) =>
        tripRepository.FindActiveByRiderAsync(riderUuid, excludeTripUuid, cancellationToken);

    public Task<Trip?> GetActiveTripByDriverAsync(
        Guid driverUuid,
        Guid? excludeTripUuid = null,
        CancellationToken cancellationToken = default) =>
        tripRepository.FindActiveByDriverAsync(driverUuid, excludeTripUuid, cancellationToken);

    public Task<IReadOnlyList<Trip>> GetTripHistoryByRiderAsync(
        Guid riderUuid,
        CancellationToken cancellationToken = default) =>
        tripRepository.FindHistoryByRiderAsync(riderUuid, cancellationToken);

    public Task<IReadOnlyList<Trip>> GetTripHistoryByDriverAsync(
        Guid driverUuid,
        CancellationToken cancellationToken = default) =>
        tripRepository.FindHistoryByDriverAsync(driverUuid, cancellationToken);

    public async Task<TripHistorySummaryContract> GetTripHistorySummaryByRiderAsync(
        Guid riderUuid,
        CancellationToken cancellationToken = default) =>
        Summarize(await tripRepository.FindHistoryByRiderAsync(riderUuid, cancellationToken));

    public async Task<TripHistorySummaryContract> GetTripHistorySummaryByDriverAsync(
        Guid driverUuid,
        CancellationToken cancellationToken = default) =>
        Summarize(await tripRepository.FindHistoryByDriverAsync(driverUuid, cancellationToken));

    private static TripHistorySummaryContract Summarize(IReadOnlyList<Trip> trips) => new()
    {
        TotalTrips = trips.Count,
        PaidTrips = trips.Count(trip => trip.Status == TripStatus.Paid),
        CancelledTrips = trips.Count(trip => trip.Status == TripStatus.Cancelled),
        TotalPaidAmount = trips.Where(trip => trip.Status == TripStatus.Paid)
            .Sum(trip => trip.Price ?? 0m),
        TotalPaidDistanceKM = trips.Where(trip => trip.Status == TripStatus.Paid)
            .Sum(trip => trip.DistanceKM ?? 0m)
    };

    public async Task<TripHistoryPageContract> SearchTripHistoryAsync(
        Guid ownerUuid,
        bool isDriver,
        TripHistorySearchContract query,
        CancellationToken cancellationToken = default)
    {
        query.Validate();
        var (items, count) = await tripRepository.SearchHistoryAsync(ownerUuid, isDriver, query, cancellationToken);
        return new TripHistoryPageContract
        {
            Items = items.Select(TripHistoryMapper.Map).ToArray(),
            TotalItems = count,
            Page = query.Page,
            PageSize = query.PageSize
        };
    }

    public async Task<byte[]> ExportTripHistoryAsync(
        Guid ownerUuid,
        bool isDriver,
        TripHistorySearchContract query,
        CancellationToken cancellationToken = default)
    {
        query.Validate();
        var exported = new List<HistoryTripResponseContract>();
        const int maxRows = 1000;
        const int batchSize = 100;

        for (var page = 1; exported.Count < maxRows; page++)
        {
            var batchQuery = new TripHistorySearchContract
            {
                Status = query.Status,
                From = query.From,
                To = query.To,
                Search = query.Search,
                Page = page,
                PageSize = batchSize
            };
            var batch = await SearchTripHistoryAsync(ownerUuid, isDriver, batchQuery, cancellationToken);
            exported.AddRange(batch.Items);
            if (exported.Count >= batch.TotalItems || batch.Items.Count == 0)
            {
                break;
            }
        }

        return TripHistoryCsv.Create(exported);
    }

    public async Task<TripHistoryInsightsContract> GetTripHistoryInsightsAsync(
        Guid ownerUuid,
        bool isDriver,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        if (to < from || to.DayNumber - from.DayNumber > 30 || to == DateOnly.MaxValue)
        {
            throw new ArgumentException("Khoảng báo cáo phải từ 1 đến 31 ngày.");
        }

        var trips = await tripRepository.FindHistoryForPeriodAsync(
            ownerUuid, isDriver,
            from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            cancellationToken);
        return TripHistoryInsights.Create(trips, from, to);
    }

    public async Task<Trip> CreateTripAsync(
        Guid riderUuid,
        CreateTripRequestContract contract,
        CancellationToken cancellationToken = default)
    {
        if (await tripRepository.FindActiveByRiderAsync(riderUuid, cancellationToken: cancellationToken) is not null)
        {
            throw new InvalidOperationException("Bạn đang có một chuyến đi chưa hoàn thành.");
        }

        var rider = await accountRepository.FindByUuidAsync(riderUuid, cancellationToken);
        if (rider is null)
        {
            throw new KeyNotFoundException("Không tìm thấy tài khoản khách.");
        }

        if (rider.Role != Role.Rider)
        {
            throw new InvalidOperationException("Chỉ tài khoản khách mới có thể tạo chuyến đi.");
        }

        if (contract.Pickup.Latitude == contract.Destination.Latitude &&
            contract.Pickup.Longitude == contract.Destination.Longitude)
        {
            throw new ArgumentException("Điểm đón và điểm đến không được trùng nhau.");
        }

        var now = DateTime.UtcNow;
        var trip = new Trip
        {
            Uuid = Guid.NewGuid(),
            RiderUuid = riderUuid,
            PickupName = contract.Pickup.Name.Trim(),
            PickupLatitude = contract.Pickup.Latitude,
            PickupLongitude = contract.Pickup.Longitude,
            DestinationName = contract.Destination.Name.Trim(),
            DestinationLatitude = contract.Destination.Latitude,
            DestinationLongitude = contract.Destination.Longitude,
            Status = TripStatus.Requested,
            CreatedAt = now,
            UpdatedAt = now
        };

        await tripRepository.AddAsync(trip, cancellationToken);
        return trip;
    }

    public async Task UpdateTripAsync(
        Trip trip,
        CancellationToken cancellationToken = default)
    {
        trip.UpdatedAt = DateTime.UtcNow;
        await tripRepository.UpdateAsync(trip, cancellationToken);
    }
}
