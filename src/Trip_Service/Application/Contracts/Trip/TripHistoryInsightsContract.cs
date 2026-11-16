namespace Trip_Service.Application.Contracts.Trip;

public sealed class TripHistoryInsightsContract
{
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public required IReadOnlyList<TripHistoryDayContract> Days { get; init; }
    public int TotalTrips { get; init; }
    public int PaidTrips { get; init; }
    public int CancelledTrips { get; init; }
    public int FailedTrips { get; init; }
    public decimal TotalPaidAmount { get; init; }
    public decimal TotalPaidDistanceKM { get; init; }
    public decimal AveragePaidAmount => PaidTrips == 0 ? 0 : TotalPaidAmount / PaidTrips;
}

public sealed class TripHistoryDayContract
{
    public DateOnly Date { get; init; }
    public int Trips { get; init; }
    public int PaidTrips { get; init; }
    public decimal PaidAmount { get; init; }
    public decimal PaidDistanceKM { get; init; }
}
