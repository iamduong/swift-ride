using System.Security.Claims;
using System.Text.Json;
using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftRide.Contracts.Events;
using Trip_Service.Application.Contracts;
using Trip_Service.Application.Contracts.Driver;
using Trip_Service.Application.Contracts.Trip;
using Trip_Service.Application.Services.Impl;
using Trip_Service.Domain;
using Trip_Service.Infrastructure.Persistence.Impl;

namespace Trip_Service.Api;

[ApiController]
[Route("trips")]
public sealed class TripController(
    ITripService tripService,
    IMatchingPricingService matchingPricingClient,
    ISendNotificateService sendNotificateService,
    IAuthService authService,
    IAccountRepository accountRepository,
    IDriverRepository driverRepository,
    IPublishEndpoint publishEndpoint) : ControllerBase
{
    [Authorize]
    [HttpGet("history/summary")]
    public async Task<ActionResult<TripHistorySummaryContract>> GetTripHistorySummaryAsync(
        CancellationToken cancellationToken)
    {
        var session = await authService.AboutMeAsync(GetAccountUuid(), cancellationToken);
        if (session.Profile is not null)
        {
            return Forbid();
        }

        return Ok(await tripService.GetTripHistorySummaryByRiderAsync(
            session.Account.Uuid, cancellationToken));
    }

    [Authorize]
    [HttpGet("history/search")]
    public async Task<ActionResult<TripHistoryPageContract>> SearchTripHistoryAsync(
        [FromQuery] TripHistorySearchContract query,
        CancellationToken cancellationToken)
    {
        var session = await authService.AboutMeAsync(GetAccountUuid(), cancellationToken);
        if (session.Profile is not null)
        {
            return Forbid();
        }

        return Ok(await tripService.SearchTripHistoryAsync(
            session.Account.Uuid, false, query, cancellationToken));
    }

    [Authorize]
    [HttpGet("history/export")]
    public async Task<IActionResult> ExportTripHistoryAsync(
        [FromQuery] TripHistorySearchContract query,
        CancellationToken cancellationToken)
    {
        var session = await authService.AboutMeAsync(GetAccountUuid(), cancellationToken);
        if (session.Profile is not null)
        {
            return Forbid();
        }

        var csv = await tripService.ExportTripHistoryAsync(session.Account.Uuid, false, query, cancellationToken);
        return File(csv, "text/csv; charset=utf-8", "trip-history.csv");
    }

    [Authorize]
    [HttpGet("history/insights")]
    public async Task<ActionResult<TripHistoryInsightsContract>> GetTripHistoryInsightsAsync(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        if (from is null || to is null)
        {
            throw new ArgumentException("Cần cung cấp from và to cho báo cáo.");
        }

        var session = await authService.AboutMeAsync(GetAccountUuid(), cancellationToken);
        if (session.Profile is not null)
        {
            return Forbid();
        }

        return Ok(await tripService.GetTripHistoryInsightsAsync(
            session.Account.Uuid, false, from.Value, to.Value, cancellationToken));
    }

    // ! [Rider] - Trip history
    [Authorize]
    [HttpGet("history")]
    public async Task<ActionResult<IReadOnlyList<HistoryTripResponseContract>>> GetTripHistoryAsync(
        CancellationToken cancellationToken)
    {
        var session = await authService.AboutMeAsync(GetAccountUuid(), cancellationToken);
        if (session.Profile is not null)
        {
            return Forbid();
        }

        var trips = await tripService.GetTripHistoryByRiderAsync(session.Account.Uuid, cancellationToken);
        return Ok(trips.Select(trip => new HistoryTripResponseContract
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
        }));
    }

    // ! [Rider] - Get active trip
    [Authorize]
    [HttpGet("active")]
    public async Task<ActionResult<TripResponseContract?>> GetActiveTripAsync(CancellationToken cancellationToken)
    {
        var session = await authService.AboutMeAsync(GetAccountUuid(), cancellationToken);
        // Check profile, driver will get a profile, rider isn't
        var trip = session.Profile is null
            ? await tripService.GetActiveTripByRiderAsync(session.Account.Uuid, cancellationToken: cancellationToken)
            : await tripService.GetActiveTripByDriverAsync(session.Profile.DriverUuid, cancellationToken: cancellationToken);

        return trip is null ? NoContent() : Ok(await MapDetailsAsync(trip, cancellationToken));
    }

    // ! Get trip detail
    [Authorize]
    [HttpGet("{tripUuid:guid}")]
    public async Task<ActionResult<TripResponseContract>> GetTripAsync(
        [FromRoute] Guid tripUuid,
        CancellationToken cancellationToken)
    {
        var trip = await tripService.GetTripByUuidAsync(tripUuid, cancellationToken);
        var session = await authService.AboutMeAsync(GetAccountUuid(), cancellationToken);
        var isAssignedDriver = trip.DriverUuid is { } driverUuid &&
                               session.Profile?.DriverUuid == driverUuid;

        if (trip.RiderUuid != session.Account.Uuid && !isAssignedDriver)
        {
            return Forbid();
        }

        return Ok(await MapDetailsAsync(trip, cancellationToken));
    }

    // ! [Rider] - Create trip
    [Authorize]
    [HttpPost("")]
    public async Task<ActionResult<CreateTripResponseContract>> CreateTripAsync(
        [FromBody] CreateTripRequestContract contract,
        CancellationToken cancellationToken)
    {
        var trip = await tripService.CreateTripAsync(GetAccountUuid(), contract, cancellationToken);
        
        var quoteRequest = new QuoteRequestContract(
            new LocationContract(
                trip.PickupLatitude,
                trip.PickupLongitude
                ),
                new LocationContract(
                trip.DestinationLatitude,
                trip.DestinationLongitude),
                contract.WeatherCondition
            );

        var quote = await matchingPricingClient.GetQuoteAsync(quoteRequest, cancellationToken);

        trip.DistanceKM = decimal.Round((decimal)quote.DistanceKM, 2, MidpointRounding.AwayFromZero);
        trip.DurationMin = decimal.Round((decimal)quote.DurationMin, 2, MidpointRounding.AwayFromZero);
        trip.Price = decimal.Round(quote.Price, 0, MidpointRounding.AwayFromZero);
        trip.RouteGeometryJson = JsonSerializer.Serialize(quote.Route.Geometry.Coordinates);

        await tripService.UpdateTripAsync(trip, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, MapResponse(trip, quote.Route.Geometry));
    }

    // ! [Rider] - Confirm after checking price, distance and time
    [Authorize]
    [HttpPost("{tripUuid:guid}/confirm")]
    public async Task<ActionResult<MatchingResponseContract>> ConfirmTripAsync(
        [FromRoute] Guid tripUuid,
        CancellationToken cancellationToken)
    {
        var trip = await tripService.GetTripByUuidAsync(tripUuid, cancellationToken);

        if (trip.RiderUuid != GetAccountUuid())
        {
            return Forbid();
        }

        if (trip.Status != TripStatus.Requested)
        {
            throw new InvalidOperationException("Chỉ có thể xác nhận chuyến đi đang ở trạng thái Requested.");
        }

        if (await tripService.GetActiveTripByRiderAsync(trip.RiderUuid, trip.Uuid, cancellationToken) is not null)
        {
            throw new InvalidOperationException("Bạn đang có một chuyến đi khác chưa hoàn thành.");
        }

        var match = await matchingPricingClient.FindAndNotifyNextDriverAsync(trip, cancellationToken);
        return Ok(match);
    }

    // ! [Rider/Driver] - Cancel before picking up the rider
    [Authorize]
    [HttpPost("{tripUuid:guid}/cancel")]
    public async Task<ActionResult<TripResponseContract>> CancelTripAsync(
        [FromRoute] Guid tripUuid,
        CancellationToken cancellationToken)
    {
        var trip = await tripService.GetTripByUuidAsync(tripUuid, cancellationToken);
        var session = await authService.AboutMeAsync(GetAccountUuid(), cancellationToken);
        var isRider = trip.RiderUuid == session.Account.Uuid;
        var isAssignedDriver = trip.DriverUuid is { } assignedDriverUuid &&
                               session.Profile?.DriverUuid == assignedDriverUuid;
        if (!isRider && !isAssignedDriver)
        {
            return Forbid();
        }

        if (trip.Status is not (TripStatus.Requested or TripStatus.DriverAccepted or TripStatus.EnRoute))
        {
            throw new InvalidOperationException("Không thể hủy chuyến sau khi tài xế đã đón khách.");
        }

        var driverUuid = trip.DriverUuid;
        trip.Status = TripStatus.Cancelled;
        await tripService.UpdateTripAsync(trip, cancellationToken);
        sendNotificateService.RemovePendingOffer(trip.Uuid);

        if (driverUuid is { } cancelledDriverUuid)
        {
            await publishEndpoint.Publish(
                new TripCancelled(trip.Uuid, cancelledDriverUuid, DateTime.UtcNow),
                cancellationToken);
        }

        return Ok(await MapDetailsAsync(trip, cancellationToken));
    }

    // ! [Driver] - Update Driver Location
    [Authorize]
    [HttpPost("{tripUuid:guid}/driver-location")]
    public async Task<ActionResult<TripResponseContract>> UpdateDriverLocationAsync(
        [FromRoute] Guid tripUuid,
        [FromBody] UpdateDriverLocationRequestContract request,
        CancellationToken cancellationToken)
    {
        if (!double.IsFinite(request.Latitude) || request.Latitude is < -90 or > 90 ||
            !double.IsFinite(request.Longitude) || request.Longitude is < -180 or > 180)
        {
            throw new ArgumentException("Tọa độ tài xế không hợp lệ.");
        }

        var trip = await GetAssignedDriverTripAsync(tripUuid, cancellationToken);
        if (trip.Status is not (TripStatus.DriverAccepted or TripStatus.EnRoute or TripStatus.PickedUp))
        {
            throw new InvalidOperationException("Không thể cập nhật vị trí cho chuyến đi hiện tại.");
        }

        var driverUuid = trip.DriverUuid!.Value;
        var updatedAt = DateTime.UtcNow;

        if (trip.Status == TripStatus.DriverAccepted)
        {
            trip.Status = TripStatus.EnRoute;
        }

        await tripService.UpdateTripAsync(trip, cancellationToken);

        await publishEndpoint.Publish(
            new TripDriverLocationUpdated(
                TripUuid: trip.Uuid,
                DriverUuid: driverUuid,
                Longitude: request.Longitude,
                Latitude: request.Latitude,
                UpdatedAt: updatedAt),
            cancellationToken);

        return Ok(await MapDetailsAsync(trip, cancellationToken));
    }

    private async Task<Trip> GetAssignedDriverTripAsync(
        Guid tripUuid,
        CancellationToken cancellationToken)
    {
        var trip = await tripService.GetTripByUuidAsync(tripUuid, cancellationToken);
        var session = await authService.AboutMeAsync(GetAccountUuid(), cancellationToken);
        if (session.Profile?.DriverUuid != trip.DriverUuid)
        {
            throw new UnauthorizedAccessException("Chuyến đi không được giao cho tài xế hiện tại.");
        }

        return trip;
    }

    private Guid GetAccountUuid()
    {
        if (Guid.TryParse(User.FindFirst("uuid")?.Value, out var accountUuid))
        {
            return accountUuid;
        }

        throw new UnauthorizedAccessException("Token không chứa Uuid hợp lệ.");
    }

    private async Task<TripResponseContract> MapDetailsAsync(
        Trip trip,
        CancellationToken cancellationToken)
    {
        TripDriverContract? driverView = null;
        DriverLocationResponseContract? driverLocation = null;
        if (trip.DriverUuid is { } driverUuid)
        {
            driverLocation = await matchingPricingClient.GetDriverLocation(driverUuid, cancellationToken);
            var profile = await driverRepository.FindByUuidAsync(driverUuid, cancellationToken);
            if (profile is not null)
            {
                var account = await accountRepository.FindByUuidAsync(profile.AccountUuid, cancellationToken);
                if (account is not null)
                {
                    driverView = new TripDriverContract
                    {
                        Uuid = driverUuid,
                        Fullname = account.Fullname,
                        Phone = account.Phone,
                        VehicleName = profile.VehicleName,
                        LicensePlate = profile.LicensePlate
                    };
                }
            }
        }

        double[][]? coordinates = null;
        if (!string.IsNullOrWhiteSpace(trip.RouteGeometryJson))
        {
            coordinates = JsonSerializer.Deserialize<double[][]>(trip.RouteGeometryJson);
        }

        return new TripResponseContract
        {
            Uuid = trip.Uuid,
            RiderUuid = trip.RiderUuid,
            DriverUuid = trip.DriverUuid,
            PaymentUuid = trip.PaymentUuid,
            QrUrl = trip.QrUrl,
            PickupName = trip.PickupName,
            PickupLatitude = trip.PickupLatitude,
            PickupLongitude = trip.PickupLongitude,
            DestinationName = trip.DestinationName,
            DestinationLatitude = trip.DestinationLatitude,
            DestinationLongitude = trip.DestinationLongitude,
            DistanceKM = trip.DistanceKM,
            DurationMin = trip.DurationMin,
            Price = trip.Price,
            Status = trip.Status.ToString(),
            DriverLatitude = driverLocation?.Latitude,
            DriverLongitude = driverLocation?.Longitude,
            DriverLocationUpdatedAt = driverLocation?.UpdatedAt,
            Driver = driverView,
            Geometry = coordinates is null ? null : new TripRouteContract { Coordinates = coordinates },
            CreatedAt = trip.CreatedAt,
            UpdatedAt = trip.UpdatedAt
        };
    }

    private static CreateTripResponseContract MapResponse(Trip trip, OSRMGeometryContract geometry) => new()
    {
        Uuid = trip.Uuid,
        RiderUuid = trip.RiderUuid,
        Pickup = new CreateTripLocationContract
        {
            Name = trip.PickupName,
            Latitude = trip.PickupLatitude,
            Longitude = trip.PickupLongitude
        },
        Destination = new CreateTripLocationContract
        {
            Name = trip.DestinationName,
            Latitude = trip.DestinationLatitude,
            Longitude = trip.DestinationLongitude
        },
        Geometry = new CreateTripGeometryContract
        {
            Type = geometry.Type,
            Coordinates = geometry.Coordinates
        },
        Status = trip.Status.ToString(),
        DistanceKM = trip.DistanceKM,
        DurationMin = trip.DurationMin,
        Price = trip.Price,
        CreatedAt = trip.CreatedAt
    };
}
