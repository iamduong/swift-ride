using Trip_Service.Domain;
using Trip_Service.Application.Contracts.Trip;

namespace Trip_Service.Infrastructure.Persistence.Impl;

public interface ITripRepository
{
    Task<Trip?> FindByUuidAsync(Guid uuid, CancellationToken cancellationToken = default);
    Task<Trip?> FindActiveByRiderAsync(Guid riderUuid, Guid? excludeTripUuid = null, CancellationToken cancellationToken = default);
    Task<Trip?> FindActiveByDriverAsync(Guid driverUuid, Guid? excludeTripUuid = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Trip>> FindHistoryByRiderAsync(Guid riderUuid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Trip>> FindHistoryByDriverAsync(Guid driverUuid, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<Trip> Items, int TotalItems)> SearchHistoryAsync(
        Guid ownerUuid, bool isDriver, TripHistorySearchContract query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Trip>> FindHistoryForPeriodAsync(
        Guid ownerUuid, bool isDriver, DateTime from, DateTime toExclusive,
        CancellationToken cancellationToken = default);
    Task AddAsync(Trip trip, CancellationToken cancellationToken = default);
    Task UpdateAsync(Trip trip, CancellationToken cancellationToken = default);
}
