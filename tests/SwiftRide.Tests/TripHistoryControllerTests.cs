using System.Security.Claims;
using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Trip_Service.Api;
using Trip_Service.Application.Contracts.Trip;
using Trip_Service.Application.Services.Impl;
using Trip_Service.Domain;
using Trip_Service.Infrastructure.Persistence.Impl;

namespace SwiftRide.Tests;

public sealed class TripHistoryControllerTests
{
    [Fact]
    public async Task Rider_search_scopes_query_to_signed_in_rider()
    {
        var rider = new Account { Uuid = Guid.NewGuid(), Role = Role.Rider };
        var services = new Mock<ITripService>();
        var query = new TripHistorySearchContract { Search = "airport" };
        var expected = new TripHistoryPageContract { Items = [], Page = 1, PageSize = 20 };
        services.Setup(x => x.SearchTripHistoryAsync(rider.Uuid, false, query,
                It.IsAny<CancellationToken>())).ReturnsAsync(expected);
        var controller = RiderController(rider, null, services);

        var response = await controller.SearchTripHistoryAsync(query, CancellationToken.None);

        Assert.Same(expected, Assert.IsType<OkObjectResult>(response.Result).Value);
        services.Verify(x => x.SearchTripHistoryAsync(rider.Uuid, false, query,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Driver_search_scopes_query_to_profile_not_account_uuid()
    {
        var driver = new Account { Uuid = Guid.NewGuid(), Role = Role.Driver };
        var profile = new DriverProfile { AccountUuid = driver.Uuid, DriverUuid = Guid.NewGuid() };
        var services = new Mock<ITripService>();
        var query = new TripHistorySearchContract();
        var expected = new TripHistoryPageContract { Items = [], Page = 1, PageSize = 20 };
        services.Setup(x => x.SearchTripHistoryAsync(profile.DriverUuid, true, query,
                It.IsAny<CancellationToken>())).ReturnsAsync(expected);
        var controller = DriverController(driver, profile, services);

        var response = await controller.SearchTripHistoryAsync(query, CancellationToken.None);

        Assert.Same(expected, Assert.IsType<OkObjectResult>(response.Result).Value);
        services.Verify(x => x.SearchTripHistoryAsync(profile.DriverUuid, true, query,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Driver_cannot_read_rider_history()
    {
        var driver = new Account { Uuid = Guid.NewGuid(), Role = Role.Driver };
        var profile = new DriverProfile { AccountUuid = driver.Uuid, DriverUuid = Guid.NewGuid() };
        var services = new Mock<ITripService>();
        var controller = RiderController(driver, profile, services);

        var response = await controller.SearchTripHistoryAsync(
            new TripHistorySearchContract(), CancellationToken.None);

        Assert.IsType<ForbidResult>(response.Result);
        services.Verify(x => x.SearchTripHistoryAsync(It.IsAny<Guid>(), It.IsAny<bool>(),
            It.IsAny<TripHistorySearchContract>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Rider_cannot_read_driver_history()
    {
        var rider = new Account { Uuid = Guid.NewGuid(), Role = Role.Rider };
        var services = new Mock<ITripService>();
        var controller = DriverController(rider, null, services);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            controller.SearchTripHistoryAsync(new TripHistorySearchContract(), CancellationToken.None));

        services.Verify(x => x.SearchTripHistoryAsync(It.IsAny<Guid>(), It.IsAny<bool>(),
            It.IsAny<TripHistorySearchContract>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Rider_export_returns_csv_and_owner_specific_filename()
    {
        var rider = new Account { Uuid = Guid.NewGuid(), Role = Role.Rider };
        var services = new Mock<ITripService>();
        var query = new TripHistorySearchContract();
        var bytes = new byte[] { 1, 2, 3 };
        services.Setup(x => x.ExportTripHistoryAsync(rider.Uuid, false, query,
            It.IsAny<CancellationToken>())).ReturnsAsync(bytes);
        var controller = RiderController(rider, null, services);

        var response = await controller.ExportTripHistoryAsync(query, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(response);
        Assert.Equal("trip-history.csv", file.FileDownloadName);
        Assert.Equal("text/csv; charset=utf-8", file.ContentType);
        Assert.Equal(bytes, file.FileContents);
    }

    [Fact]
    public async Task Driver_export_returns_csv_for_driver_profile()
    {
        var driver = new Account { Uuid = Guid.NewGuid(), Role = Role.Driver };
        var profile = new DriverProfile { AccountUuid = driver.Uuid, DriverUuid = Guid.NewGuid() };
        var services = new Mock<ITripService>();
        var query = new TripHistorySearchContract();
        services.Setup(x => x.ExportTripHistoryAsync(profile.DriverUuid, true, query,
            It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var controller = DriverController(driver, profile, services);

        var response = await controller.ExportTripHistoryAsync(query, CancellationToken.None);

        Assert.Equal("driver-trip-history.csv", Assert.IsType<FileContentResult>(response).FileDownloadName);
    }

    [Fact]
    public async Task Rider_insights_require_both_dates()
    {
        var rider = new Account { Uuid = Guid.NewGuid(), Role = Role.Rider };
        var services = new Mock<ITripService>();
        var controller = RiderController(rider, null, services);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            controller.GetTripHistoryInsightsAsync(null, DateOnly.FromDateTime(DateTime.UtcNow), CancellationToken.None));

        services.Verify(x => x.GetTripHistoryInsightsAsync(It.IsAny<Guid>(), It.IsAny<bool>(),
            It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Driver_insights_use_profile_and_requested_period()
    {
        var driver = new Account { Uuid = Guid.NewGuid(), Role = Role.Driver };
        var profile = new DriverProfile { AccountUuid = driver.Uuid, DriverUuid = Guid.NewGuid() };
        var from = new DateOnly(2026, 11, 1);
        var to = new DateOnly(2026, 11, 7);
        var expected = new TripHistoryInsightsContract { From = from, To = to, Days = [] };
        var services = new Mock<ITripService>();
        services.Setup(x => x.GetTripHistoryInsightsAsync(profile.DriverUuid, true, from, to,
                It.IsAny<CancellationToken>())).ReturnsAsync(expected);
        var controller = DriverController(driver, profile, services);

        var response = await controller.GetTripHistoryInsightsAsync(from, to, CancellationToken.None);

        Assert.Same(expected, Assert.IsType<OkObjectResult>(response.Result).Value);
    }

    private static TripController RiderController(
        Account account, DriverProfile? profile, Mock<ITripService> services) =>
        WithUser(new TripController(services.Object, Mock.Of<IMatchingPricingService>(),
            Mock.Of<ISendNotificateService>(), Auth(account, profile),
            Mock.Of<IAccountRepository>(), Mock.Of<IDriverRepository>(),
            Mock.Of<IPublishEndpoint>()), account);

    private static DriverController DriverController(
        Account account, DriverProfile? profile, Mock<ITripService> services) =>
        WithUser(new DriverController(Mock.Of<IDriverRepository>(), Mock.Of<IAccountRepository>(),
            services.Object, Mock.Of<IMatchingPricingService>(), Auth(account, profile),
            Mock.Of<ISendNotificateService>(), Mock.Of<ITripDriverRejectionRepository>(),
            Mock.Of<IPublishEndpoint>()), account);

    private static IAuthService Auth(Account account, DriverProfile? profile)
    {
        var service = new Mock<IAuthService>();
        service.Setup(x => x.AboutMeAsync(account.Uuid, It.IsAny<CancellationToken>()))
            .ReturnsAsync((account, profile));
        return service.Object;
    }

    private static T WithUser<T>(T controller, Account account) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim("uuid", account.Uuid.ToString())], "test"))
            }
        };
        return controller;
    }
}
