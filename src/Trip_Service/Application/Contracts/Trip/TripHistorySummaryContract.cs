namespace Trip_Service.Application.Contracts.Trip;

public sealed class TripHistorySummaryContract
{
    public int TotalTrips { get; init; }
    public int PaidTrips { get; init; }
    public int CancelledTrips { get; init; }
    public decimal TotalPaidAmount { get; init; }
    public decimal TotalPaidDistanceKM { get; init; }
}
