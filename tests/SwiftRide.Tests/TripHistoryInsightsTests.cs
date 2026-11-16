using Moq;
using Trip_Service.Application.Services;
using Trip_Service.Domain;
using Trip_Service.Infrastructure.Persistence.Impl;

namespace SwiftRide.Tests;

public sealed class TripHistoryInsightsTests
{
    [Fact]
    public void Insights_fill_missing_days_and_only_add_paid_fares()
    {
        var from = new DateOnly(2026, 11, 1);
        var to = new DateOnly(2026, 11, 3);
        var trips = new List<Trip>
        {
            TripOn(1, TripStatus.Paid, 25_000m, 2.5m),
            TripOn(1, TripStatus.Cancelled, 30_000m, 3m),
            TripOn(1, TripStatus.Paid, 15_000m, 1.5m),
            TripOn(3, TripStatus.Failed),
            TripOn(3, TripStatus.PaymentPending, 50_000m, 8m)
        };

        var result = TripHistoryInsights.Create(trips, from, to);

        Assert.Equal(3, result.Days.Count);
        Assert.Equal(3, result.Days[0].Trips);
        Assert.Equal(2, result.Days[0].PaidTrips);
        Assert.Equal(40_000m, result.Days[0].PaidAmount);
        Assert.Equal(4m, result.Days[0].PaidDistanceKM);
        Assert.Equal(0, result.Days[1].Trips);
        Assert.Equal(new DateOnly(2026, 11, 2), result.Days[1].Date);
        Assert.Equal(2, result.Days[2].Trips);
        Assert.Equal(0m, result.Days[2].PaidAmount);
        Assert.Equal(5, result.TotalTrips);
        Assert.Equal(2, result.PaidTrips);
        Assert.Equal(1, result.CancelledTrips);
        Assert.Equal(1, result.FailedTrips);
        Assert.Equal(40_000m, result.TotalPaidAmount);
        Assert.Equal(20_000m, result.AveragePaidAmount);
        Assert.Equal(4m, result.TotalPaidDistanceKM);
    }

    [Fact]
    public void Insights_on_empty_period_have_zero_totals()
    {
        var day = new DateOnly(2026, 11, 1);

        var result = TripHistoryInsights.Create([], day, day);

        Assert.Single(result.Days);
        Assert.Equal(0, result.TotalTrips);
        Assert.Equal(0, result.PaidTrips);
        Assert.Equal(0m, result.TotalPaidAmount);
        Assert.Equal(0m, result.AveragePaidAmount);
    }

    [Theory]
    [InlineData(2026, 11, 2, 2026, 11, 1)]
    [InlineData(2026, 11, 1, 2026, 12, 2)]
    [InlineData(9999, 12, 31, 9999, 12, 31)]
    public void Insights_reject_invalid_periods(
        int fromYear, int fromMonth, int fromDay,
        int toYear, int toMonth, int toDay)
    {
        var from = new DateOnly(fromYear, fromMonth, fromDay);
        var to = new DateOnly(toYear, toMonth, toDay);

        Assert.Throws<ArgumentException>(() => TripHistoryInsights.Create([], from, to));
    }

    [Fact]
    public async Task Insights_service_requests_only_authenticated_owners_period()
    {
        var driverUuid = Guid.NewGuid();
        var from = new DateOnly(2026, 11, 1);
        var to = new DateOnly(2026, 11, 3);
        var repository = new Mock<ITripRepository>();
        var expectedFrom = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var expectedTo = new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc);
        repository.Setup(x => x.FindHistoryForPeriodAsync(driverUuid, true,
                expectedFrom, expectedTo, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Trip> { TripOn(2, TripStatus.Paid, 35_000m, 4m) });
        var service = new TripService(Mock.Of<IAccountRepository>(), repository.Object);

        var result = await service.GetTripHistoryInsightsAsync(driverUuid, true, from, to);

        Assert.Equal(1, result.TotalTrips);
        Assert.Equal(35_000m, result.TotalPaidAmount);
        repository.Verify(x => x.FindHistoryForPeriodAsync(driverUuid, true,
            expectedFrom, expectedTo, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Invalid_insights_period_does_not_query_database()
    {
        var repository = new Mock<ITripRepository>();
        var service = new TripService(Mock.Of<IAccountRepository>(), repository.Object);

        await Assert.ThrowsAsync<ArgumentException>(() => service.GetTripHistoryInsightsAsync(
            Guid.NewGuid(), false, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1)));

        repository.Verify(x => x.FindHistoryForPeriodAsync(
            It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<DateTime>(),
            It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Trip TripOn(int day, TripStatus status, decimal? price = null,
        decimal? distanceKM = null) => new()
    {
        CreatedAt = new DateTime(2026, 11, day, 12, 0, 0, DateTimeKind.Utc),
        Status = status,
        Price = price,
        DistanceKM = distanceKM
    };
}
