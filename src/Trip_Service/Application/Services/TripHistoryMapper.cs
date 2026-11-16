using Trip_Service.Application.Contracts.Trip;
using Trip_Service.Domain;

namespace Trip_Service.Application.Services;

public static class TripHistoryMapper
{
    public static HistoryTripResponseContract Map(Trip trip) => new()
    {
        Uuid = trip.Uuid,
        PickupName = trip.PickupName,
        DestinationName = trip.DestinationName,
        DistanceKM = trip.DistanceKM,
        DurationMin = trip.DurationMin,
        Price = trip.Price,
        Status = trip.Status.ToString(),
        CreatedAt = trip.CreatedAt,
        UpdatedAt = trip.UpdatedAt
    };
}
