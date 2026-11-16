using System.Text;
using Moq;
using Trip_Service.Application.Contracts.Trip;
using Trip_Service.Application.Services;
using Trip_Service.Domain;
using Trip_Service.Infrastructure.Persistence.Impl;

namespace SwiftRide.Tests;

public sealed class TripHistoryFeatureTests
{
    [Theory]
    [InlineData(0, 20)]
    [InlineData(-1, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    [InlineData(int.MaxValue, 100)]
    public void Search_rejects_invalid_pagination(int page, int pageSize)
    {
        var query = new TripHistorySearchContract { Page = page, PageSize = pageSize };

        Assert.Throws<ArgumentException>(query.Validate);
    }

    [Fact]
    public void Search_rejects_invalid_date_range()
    {
        var query = new TripHistorySearchContract
        {
            From = new DateTime(2026, 11, 3), To = new DateTime(2026, 11, 2)
        };

        Assert.Throws<ArgumentException>(query.Validate);
    }

    [Fact]
    public void Search_rejects_unknown_status()
    {
        var query = new TripHistorySearchContract { Status = (TripStatus)123 };

        Assert.Throws<ArgumentException>(query.Validate);
    }

    [Fact]
    public void Search_rejects_oversized_keyword()
    {
        var query = new TripHistorySearchContract { Search = new string('x', 101) };

        Assert.Throws<ArgumentException>(query.Validate);
    }

    [Fact]
    public void Search_trims_keyword_and_accepts_boundary_values()
    {
        var query = new TripHistorySearchContract
        {
            Search = "  airport  ", Page = 1, PageSize = 100,
            From = new DateTime(2026, 11, 2), To = new DateTime(2026, 11, 2)
        };

        query.Validate();

        Assert.Equal("airport", query.Search);
    }

    [Fact]
    public void Page_calculates_total_pages_including_empty_and_partial_pages()
    {
        TripHistoryPageContract Page(int total) => new()
        {
            Items = [], TotalItems = total, Page = 1, PageSize = 20
        };

        Assert.Equal(0, Page(0).TotalPages);
        Assert.Equal(1, Page(20).TotalPages);
        Assert.Equal(2, Page(21).TotalPages);
    }

    [Fact]
    public async Task Search_passes_owner_role_and_filter_to_repository_and_maps_page()
    {
        var owner = Guid.NewGuid();
        var trip = new Trip
        {
            Uuid = Guid.NewGuid(), PickupName = "A", DestinationName = "B",
            Price = 35_000m, Status = TripStatus.Paid
        };
        var repository = new Mock<ITripRepository>();
        repository.Setup(x => x.SearchHistoryAsync(
                owner, true, It.IsAny<TripHistorySearchContract>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<Trip>)[trip], 42));
        var service = new TripService(Mock.Of<IAccountRepository>(), repository.Object);
        var query = new TripHistorySearchContract { Search = " A ", Page = 2, PageSize = 10 };

        var result = await service.SearchTripHistoryAsync(owner, true, query);

        Assert.Equal(42, result.TotalItems);
        Assert.Equal(5, result.TotalPages);
        Assert.Equal(2, result.Page);
        Assert.Equal(10, result.PageSize);
        var item = Assert.Single(result.Items);
        Assert.Equal(trip.Uuid, item.Uuid);
        Assert.Equal("A", item.PickupName);
        Assert.Equal("B", item.DestinationName);
        Assert.Equal("Paid", item.Status);
        Assert.Equal(35_000m, item.Price);
        repository.Verify(x => x.SearchHistoryAsync(owner, true,
            It.Is<TripHistorySearchContract>(q => q.Search == "A" && q.Page == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Invalid_search_does_not_query_repository()
    {
        var repository = new Mock<ITripRepository>();
        var service = new TripService(Mock.Of<IAccountRepository>(), repository.Object);

        await Assert.ThrowsAsync<ArgumentException>(() => service.SearchTripHistoryAsync(
            Guid.NewGuid(), false, new TripHistorySearchContract { PageSize = 101 }));

        repository.Verify(x => x.SearchHistoryAsync(
            It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<TripHistorySearchContract>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Export_fetches_subsequent_pages_with_same_filter()
    {
        var owner = Guid.NewGuid();
        var repository = new Mock<ITripRepository>();
        var first = Enumerable.Range(0, 100).Select(i => new Trip
        {
            Uuid = Guid.NewGuid(), PickupName = $"Pickup {i}", DestinationName = "B"
        }).ToArray();
        var second = new Trip { Uuid = Guid.NewGuid(), PickupName = "Last", DestinationName = "B" };
        repository.Setup(x => x.SearchHistoryAsync(owner, false,
                It.Is<TripHistorySearchContract>(q => q.Page == 1 && q.Search == "airport"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<Trip>)first, 101));
        repository.Setup(x => x.SearchHistoryAsync(owner, false,
                It.Is<TripHistorySearchContract>(q => q.Page == 2 && q.Search == "airport"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<Trip>)[second], 101));
        var service = new TripService(Mock.Of<IAccountRepository>(), repository.Object);

        var result = await service.ExportTripHistoryAsync(owner, false,
            new TripHistorySearchContract { Search = " airport " });
        var csv = Encoding.UTF8.GetString(result);

        Assert.Contains("Pickup 0", csv);
        Assert.Contains("Last", csv);
        Assert.Equal(102, csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        repository.Verify(x => x.SearchHistoryAsync(owner, false,
            It.IsAny<TripHistorySearchContract>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Export_stops_at_one_thousand_rows()
    {
        var repository = new Mock<ITripRepository>();
        repository.Setup(x => x.SearchHistoryAsync(It.IsAny<Guid>(), false,
                It.IsAny<TripHistorySearchContract>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<Trip>)Enumerable.Range(0, 100).Select(_ => new Trip()).ToArray(), 1200));
        var service = new TripService(Mock.Of<IAccountRepository>(), repository.Object);

        var csv = await service.ExportTripHistoryAsync(Guid.NewGuid(), false, new TripHistorySearchContract());

        Assert.Equal(1001, Encoding.UTF8.GetString(csv).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        repository.Verify(x => x.SearchHistoryAsync(It.IsAny<Guid>(), false,
            It.IsAny<TripHistorySearchContract>(), It.IsAny<CancellationToken>()), Times.Exactly(10));
    }

    [Fact]
    public void Csv_escapes_quotes_commas_newlines_and_spreadsheet_formulas()
    {
        var trip = new HistoryTripResponseContract
        {
            Uuid = Guid.NewGuid(), PickupName = "=SUM(1,2)",
            DestinationName = "A, \"B\"\nC", Status = "Paid",
            Price = 12.5m, DistanceKM = 1.25m,
            CreatedAt = DateTime.UnixEpoch, UpdatedAt = DateTime.UnixEpoch
        };

        var result = Encoding.UTF8.GetString(TripHistoryCsv.Create([trip]));

        Assert.StartsWith("\uFEFFUuid,PickupName", result);
        Assert.Contains("\"'=SUM(1,2)\"", result);
        Assert.Contains("\"A, \"\"B\"\"\nC\"", result);
        Assert.Contains(",1.25,,12.5,", result);
        Assert.Contains("1970-01-01T00:00:00.0000000Z", result);
    }

    [Theory]
    [InlineData(" +1+1", "' +1+1")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("\t=1+1", "'\t=1+1")]
    [InlineData("Safe pickup", "Safe pickup")]
    public void Csv_protects_spreadsheet_cells(string pickup, string expected)
    {
        var trip = new HistoryTripResponseContract
        {
            PickupName = pickup, DestinationName = "Destination", Status = "Paid"
        };

        var csv = Encoding.UTF8.GetString(TripHistoryCsv.Create([trip]));

        Assert.Contains($"\"{expected}\"", csv);
    }
}
