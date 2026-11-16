using Trip_Service.Application.Contracts.Trip;
using Trip_Service.Domain;

namespace Trip_Service.Application.Services;

public static class TripHistoryInsights
{
    public static TripHistoryInsightsContract Create(
        IReadOnlyList<Trip> trips,
        DateOnly from,
        DateOnly to)
    {
        if (to < from || to.DayNumber - from.DayNumber > 30 || to == DateOnly.MaxValue)
        {
            throw new ArgumentException("Khoảng báo cáo phải từ 1 đến 31 ngày.");
        }

        var byDay = trips.GroupBy(trip => DateOnly.FromDateTime(trip.CreatedAt))
            .ToDictionary(group => group.Key, group => group.ToArray());
        var days = new List<TripHistoryDayContract>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            byDay.TryGetValue(day, out var dailyTrips);
            dailyTrips ??= [];
            var paid = dailyTrips.Where(trip => trip.Status == TripStatus.Paid).ToArray();
            days.Add(new TripHistoryDayContract
            {
                Date = day,
                Trips = dailyTrips.Length,
                PaidTrips = paid.Length,
                PaidAmount = paid.Sum(trip => trip.Price ?? 0m),
                PaidDistanceKM = paid.Sum(trip => trip.DistanceKM ?? 0m)
            });
        }

        return new TripHistoryInsightsContract
        {
            From = from,
            To = to,
            Days = days,
            TotalTrips = days.Sum(day => day.Trips),
            PaidTrips = days.Sum(day => day.PaidTrips),
            CancelledTrips = trips.Count(trip => trip.Status == TripStatus.Cancelled),
            FailedTrips = trips.Count(trip => trip.Status == TripStatus.Failed),
            TotalPaidAmount = days.Sum(day => day.PaidAmount),
            TotalPaidDistanceKM = days.Sum(day => day.PaidDistanceKM)
        };
    }
}
