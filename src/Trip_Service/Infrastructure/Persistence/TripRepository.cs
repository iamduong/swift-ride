using Microsoft.EntityFrameworkCore;
using Trip_Service.Application.Contracts.Trip;
using Trip_Service.Domain;
using Trip_Service.Infrastructure.Persistence.Impl;

namespace Trip_Service.Infrastructure.Persistence.Repositories;



public sealed class TripRepository(TripDBContext dbContext) : ITripRepository
{
    public Task<Trip?> FindByUuidAsync(Guid uuid, CancellationToken cancellationToken = default) =>
        dbContext.Trips.AsNoTracking()
            .SingleOrDefaultAsync(trip => trip.Uuid == uuid, cancellationToken);

    public Task<Trip?> FindActiveByRiderAsync(
        Guid riderUuid,
        Guid? excludeTripUuid = null,
        CancellationToken cancellationToken = default) =>
        dbContext.Trips.AsNoTracking()
            .OrderByDescending(trip => trip.UpdatedAt)
            .FirstOrDefaultAsync(trip =>
                trip.RiderUuid == riderUuid &&
                trip.Uuid != excludeTripUuid &&
                trip.Status >= TripStatus.Requested &&
                trip.Status <= TripStatus.PaymentPending,
                cancellationToken);

    public Task<Trip?> FindActiveByDriverAsync(
        Guid driverUuid,
        Guid? excludeTripUuid = null,
        CancellationToken cancellationToken = default) =>
        dbContext.Trips.AsNoTracking()
            .OrderByDescending(trip => trip.UpdatedAt)
            .FirstOrDefaultAsync(trip =>
                trip.DriverUuid == driverUuid &&
                trip.Uuid != excludeTripUuid &&
                trip.Status >= TripStatus.Requested &&
                trip.Status <= TripStatus.PaymentPending,
                cancellationToken);

    public async Task<IReadOnlyList<Trip>> FindHistoryByRiderAsync(
        Guid riderUuid,
        CancellationToken cancellationToken = default) =>
        await dbContext.Trips.AsNoTracking()
            .Where(trip =>
                trip.RiderUuid == riderUuid &&
                (trip.Status == TripStatus.DroppedOff ||
                 trip.Status == TripStatus.Paid ||
                 trip.Status == TripStatus.Cancelled ||
                 trip.Status == TripStatus.Failed))
            .OrderByDescending(trip => trip.UpdatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Trip>> FindHistoryByDriverAsync(
        Guid driverUuid,
        CancellationToken cancellationToken = default) =>
        await dbContext.Trips.AsNoTracking()
            .Where(trip =>
                trip.DriverUuid == driverUuid &&
                (trip.Status == TripStatus.DroppedOff ||
                 trip.Status == TripStatus.PaymentPending ||
                 trip.Status == TripStatus.Paid))
            .OrderByDescending(trip => trip.UpdatedAt)
            .ToListAsync(cancellationToken);

    public async Task<(IReadOnlyList<Trip> Items, int TotalItems)> SearchHistoryAsync(
        Guid ownerUuid,
        bool isDriver,
        TripHistorySearchContract query,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Trip> filtered = HistoryFor(ownerUuid, isDriver);

        if (query.Status is { } status)
        {
            filtered = filtered.Where(trip => trip.Status == status);
        }

        if (query.From is { } from)
        {
            filtered = filtered.Where(trip => trip.CreatedAt >= from);
        }

        if (query.To is { } to)
        {
            filtered = filtered.Where(trip => trip.CreatedAt <= to);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search;
            filtered = filtered.Where(trip =>
                trip.PickupName.Contains(search) || trip.DestinationName.Contains(search));
        }

        var count = await filtered.CountAsync(cancellationToken);
        var items = await filtered.OrderByDescending(trip => trip.UpdatedAt)
            .ThenByDescending(trip => trip.Uuid)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return (items, count);
    }

    public async Task<IReadOnlyList<Trip>> FindHistoryForPeriodAsync(
        Guid ownerUuid,
        bool isDriver,
        DateTime from,
        DateTime toExclusive,
        CancellationToken cancellationToken = default)
    {
        return await HistoryFor(ownerUuid, isDriver)
            .Where(trip => trip.CreatedAt >= from && trip.CreatedAt < toExclusive)
            .ToListAsync(cancellationToken);
    }

    private IQueryable<Trip> HistoryFor(Guid ownerUuid, bool isDriver)
    {
        var trips = dbContext.Trips.AsNoTracking();
        return isDriver
            ? trips.Where(trip => trip.DriverUuid == ownerUuid &&
                (trip.Status == TripStatus.DroppedOff ||
                 trip.Status == TripStatus.PaymentPending || trip.Status == TripStatus.Paid))
            : trips.Where(trip => trip.RiderUuid == ownerUuid &&
                (trip.Status == TripStatus.DroppedOff || trip.Status == TripStatus.Paid ||
                 trip.Status == TripStatus.Cancelled || trip.Status == TripStatus.Failed));
    }

    public async Task AddAsync(Trip trip, CancellationToken cancellationToken = default)
    {
        dbContext.Trips.Add(trip);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
    public async Task UpdateAsync(Trip trip, CancellationToken cancellationToken = default)
    {
        dbContext.Trips.Update(trip);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
