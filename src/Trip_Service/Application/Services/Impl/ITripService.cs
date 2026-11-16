using Trip_Service.Application.Contracts.Trip;
using Trip_Service.Domain;

namespace Trip_Service.Application.Services.Impl;

public interface ITripService
{
    Task<Trip> GetTripByUuidAsync(
        Guid tripUuid,
        CancellationToken cancellationToken = default);

    Task<Trip?> GetActiveTripByRiderAsync(Guid riderUuid, Guid? excludeTripUuid = null, CancellationToken cancellationToken = default);
    Task<Trip?> GetActiveTripByDriverAsync(Guid driverUuid, Guid? excludeTripUuid = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Trip>> GetTripHistoryByRiderAsync(Guid riderUuid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Trip>> GetTripHistoryByDriverAsync(Guid driverUuid, CancellationToken cancellationToken = default);
    Task<TripHistorySummaryContract> GetTripHistorySummaryByRiderAsync(Guid riderUuid, CancellationToken cancellationToken = default);
    Task<TripHistorySummaryContract> GetTripHistorySummaryByDriverAsync(Guid driverUuid, CancellationToken cancellationToken = default);
    Task<TripHistoryPageContract> SearchTripHistoryAsync(
        Guid ownerUuid, bool isDriver, TripHistorySearchContract query, CancellationToken cancellationToken = default);
    Task<byte[]> ExportTripHistoryAsync(
        Guid ownerUuid, bool isDriver, TripHistorySearchContract query, CancellationToken cancellationToken = default);
    Task<TripHistoryInsightsContract> GetTripHistoryInsightsAsync(
        Guid ownerUuid, bool isDriver, DateOnly from, DateOnly to,
        CancellationToken cancellationToken = default);

    Task<Trip> CreateTripAsync(
        Guid riderUuid,
        CreateTripRequestContract contract,
        CancellationToken cancellationToken = default);

    Task UpdateTripAsync(Trip trip, CancellationToken cancellationToken = default);
}
