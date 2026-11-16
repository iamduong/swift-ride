using System.Text.Json;
using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Trip_Service.Application.Contracts;
using Trip_Service.Application.Contracts.Driver;
using Trip_Service.Application.Contracts.Trip;
using Trip_Service.Application.Services.Impl;
using Trip_Service.Domain;
using Trip_Service.Infrastructure.Persistence.Impl;
using SwiftRide.Contracts.Events;

namespace Trip_Service.Api;

[ApiController]
[Authorize]
[Route("driver")]
public sealed class DriverController(
    IDriverRepository driverRepository,
    IAccountRepository accountRepository,
    ITripService tripService,
    IMatchingPricingService matchingPricingService,
    IAuthService authService,
    ISendNotificateService sendNotificateService,
    ITripDriverRejectionRepository rejectionRepository,
    // Rabbitmq 
    IPublishEndpoint publishEndpoint) : ControllerBase
{
    [HttpGet("history/summary")]
    public async Task<ActionResult<TripHistorySummaryContract>> GetTripHistorySummaryAsync(
        CancellationToken cancellationToken)
    {
        var session = await authService.AboutMeAsync(GetAccountUuid(), cancellationToken);
        var driverUuid = session.Profile?.DriverUuid
            ?? throw new UnauthorizedAccessException("Tài khoản không phải là tài xế.");

        return Ok(await tripService.GetTripHistorySummaryByDriverAsync(driverUuid, cancellationToken));
    }

    [HttpGet("history/search")]
    public async Task<ActionResult<TripHistoryPageContract>> SearchTripHistoryAsync(
        [FromQuery] TripHistorySearchContract query,
        CancellationToken cancellationToken)
    {
        var session = await authService.AboutMeAsync(GetAccountUuid(), cancellationToken);
        var driverUuid = session.Profile?.DriverUuid
            ?? throw new UnauthorizedAccessException("Tài khoản không phải là tài xế.");

        return Ok(await tripService.SearchTripHistoryAsync(driverUuid, true, query, cancellationToken));
    }

    [HttpGet("history/export")]
    public async Task<IActionResult> ExportTripHistoryAsync(
        [FromQuery] TripHistorySearchContract query,
        CancellationToken cancellationToken)
    {
        var session = await authService.AboutMeAsync(GetAccountUuid(), cancellationToken);
        var driverUuid = session.Profile?.DriverUuid
            ?? throw new UnauthorizedAccessException("Tài khoản không phải là tài xế.");

        var csv = await tripService.ExportTripHistoryAsync(driverUuid, true, query, cancellationToken);
        return File(csv, "text/csv; charset=utf-8", "driver-trip-history.csv");
    }

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
        var driverUuid = session.Profile?.DriverUuid
            ?? throw new UnauthorizedAccessException("Tài khoản không phải là tài xế.");

        return Ok(await tripService.GetTripHistoryInsightsAsync(
            driverUuid, true, from.Value, to.Value, cancellationToken));
    }

    [HttpGet("history")]
    public async Task<ActionResult<IReadOnlyList<HistoryTripResponseContract>>> GetTripHistoryAsync(
        CancellationToken cancellationToken)
    {
        var session = await authService.AboutMeAsync(GetAccountUuid(), cancellationToken);
        var driverUuid = session.Profile?.DriverUuid
            ?? throw new UnauthorizedAccessException("Tài khoản không phải là tài xế.");
        var trips = await tripService.GetTripHistoryByDriverAsync(driverUuid, cancellationToken);

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

    [HttpPatch("{driverUuid:guid}/online")]
    public async Task<IActionResult> UpdateOnlineStatusAsync(
        [FromRoute] Guid driverUuid,
        UpdateDriverStatusContract contract,
        CancellationToken cancellationToken)
    {
        var session = await authService.AboutMeAsync(GetAccountUuid(), cancellationToken);
        if (session.Profile?.DriverUuid != driverUuid)
        {
            throw new UnauthorizedAccessException("Không thể cập nhật trạng thái của tài xế khác.");
        }

        // * Use RabbitMQ to update driver online status
        await publishEndpoint.Publish(
            new DriverOnline(driverUuid, contract.IsOnline),
            cancellationToken);

        return Accepted();
    }

    // ! [Driver] - Accept trip
    [HttpPost("trip/{tripUuid:guid}/accept")]
    public async Task<ActionResult<Trip>> AcceptTripAsync(
        [FromRoute] Guid tripUuid,
        CancellationToken cancellationToken)
    {
        var (trip, driverUuid) = await GetPendingTripOfferAsync(tripUuid, cancellationToken);
        if (trip.Status != TripStatus.Requested)
        {
            throw new InvalidOperationException("Chuyến đi không còn chờ tài xế xác nhận.");
        }

        if (await tripService.GetActiveTripByDriverAsync(
                driverUuid,
                trip.Uuid,
                cancellationToken) is not null)
        {
            throw new InvalidOperationException("Bạn đang có một chuyến đi khác chưa hoàn thành.");
        }

        trip.DriverUuid = driverUuid;
        trip.Status = TripStatus.DriverAccepted;

        await tripService.UpdateTripAsync(trip, cancellationToken);

        sendNotificateService.RemovePendingOffer(trip.Uuid);

        // * Use RabbitMQ to send to MatchingPricing_Service to update driver available
        await publishEndpoint.Publish(new TripAccepted(trip.Uuid, driverUuid, DateTime.UtcNow), cancellationToken);

        return Ok(trip);
    }

    // ! [Driver] - Reject trip
    [HttpPost("trip/{tripUuid:guid}/reject")]
    public async Task<ActionResult<Trip>> RejectTripAsync(
        [FromRoute] Guid tripUuid,
        CancellationToken cancellationToken)
    {
        var (trip, driverUuid) = await GetPendingTripOfferAsync(tripUuid, cancellationToken);
        if (trip.Status != TripStatus.Requested)
        {
            throw new InvalidOperationException("Chuyến đi không còn chờ tài xế xác nhận.");
        }

        await rejectionRepository.AddAsync(new TripDriverRejection
        {
            Uuid = Guid.NewGuid(),
            TripUuid = trip.Uuid,
            DriverUuid = driverUuid,
            RejectedAt = DateTime.UtcNow
        }, cancellationToken);

        sendNotificateService.RemovePendingOffer(trip.Uuid);
        await matchingPricingService.FindAndNotifyNextDriverAsync(trip, cancellationToken);
        return Ok(trip);
    }

    // ! [Driver] - Pick up rider
    [HttpPost("trip/{tripUuid:guid}/pickup")]
    public async Task<ActionResult<TripResponseContract>> PickupTripAsync(
        [FromRoute] Guid tripUuid,
        CancellationToken cancellationToken)
    {
        var trip = await GetAssignedDriverTripAsync(tripUuid, cancellationToken);
        if (trip.Status is not (TripStatus.DriverAccepted or TripStatus.EnRoute))
        {
            throw new InvalidOperationException("Chuyến đi chưa sẵn sàng đón khách.");
        }

        var driverUuid = trip.DriverUuid!.Value;
        var updatedAt = DateTime.UtcNow;
        trip.Status = TripStatus.PickedUp;
        await tripService.UpdateTripAsync(trip, cancellationToken);

        // await publishEndpoint.Publish(
        //     new TripDriverLocationUpdated(
        //         TripUuid: trip.Uuid,
        //         DriverUuid: driverUuid,
        //         Longitude: trip.PickupLongitude,
        //         Latitude: trip.PickupLatitude,
        //         UpdatedAt: updatedAt),
        //     cancellationToken);

        return Ok(await MapDetailsAsync(trip, cancellationToken));
    }

    // ! [Driver] - Drop off rider
    [HttpPost("trip/{tripUuid:guid}/dropoff")]
    public async Task<ActionResult<TripResponseContract>> DropoffTripAsync(
        [FromRoute] Guid tripUuid,
        CancellationToken cancellationToken)
    {
        var trip = await GetAssignedDriverTripAsync(tripUuid, cancellationToken);
        if (trip.Status != TripStatus.PickedUp)
        {
            throw new InvalidOperationException("Cần đón khách trước khi kết thúc chuyến.");
        }

        var driverUuid = trip.DriverUuid!.Value;
        var updatedAt = DateTime.UtcNow;
        trip.Status = TripStatus.DroppedOff;
        await tripService.UpdateTripAsync(trip, cancellationToken);

        // await publishEndpoint.Publish(
        //     new TripDriverLocationUpdated(
        //         TripUuid: trip.Uuid,
        //         DriverUuid: driverUuid,
        //         Longitude: trip.DestinationLongitude,
        //         Latitude: trip.DestinationLatitude,
        //         UpdatedAt: updatedAt),
        //     cancellationToken);

        // * Use RabbitMQ to send to MatchingPricing_Service to update driver available
        await publishEndpoint.Publish(
            new TripDroppedOff(trip.Uuid, driverUuid, updatedAt),
            cancellationToken);
        
        // * Use RabbitMQ to send to Payment_Service to create payment record
        await publishEndpoint.Publish(
            new RideCompleted(
                trip.Uuid,
                trip.RiderUuid,
                driverUuid,
                trip.Price ?? throw new InvalidOperationException("Chuyến đi chưa có giá.")),
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

    private async Task<(Trip Trip, Guid DriverUuid)> GetPendingTripOfferAsync(
        Guid tripUuid,
        CancellationToken cancellationToken)
    {
        var session = await authService.AboutMeAsync(GetAccountUuid(), cancellationToken);
        var driverUuid = session.Profile?.DriverUuid
            ?? throw new UnauthorizedAccessException("Tài khoản không phải là tài xế.");

        if (!sendNotificateService.IsPendingOffer(driverUuid, tripUuid))
        {
            throw new UnauthorizedAccessException("Yêu cầu chuyến đi không được gửi tới tài xế hiện tại.");
        }

        var trip = await tripService.GetTripByUuidAsync(tripUuid, cancellationToken);
        return (trip, driverUuid);
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
            driverLocation = await matchingPricingService.GetDriverLocation(driverUuid, cancellationToken);
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
}
