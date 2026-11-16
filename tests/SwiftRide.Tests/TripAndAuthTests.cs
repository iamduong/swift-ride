using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Trip_Service.Application.Contracts.Auth;
using Trip_Service.Application.Contracts.Trip;
using Trip_Service.Application.Services;
using Trip_Service.Application.Services.Impl;
using Trip_Service.Domain;
using Trip_Service.Infrastructure.Authentication;
using Trip_Service.Infrastructure.Persistence.Impl;

namespace SwiftRide.Tests;

public sealed class TripAndAuthTests
{
    [Fact]
    public async Task Register_normalizes_input_hashes_password_and_persists()
    {
        var accounts = new Mock<IAccountRepository>();
        var passwords = new Mock<IPasswordService>();
        passwords.Setup(x => x.Hash(It.IsAny<Account>(), "secret1")).Returns("hashed");
        var service = CreateAuth(accounts, passwordService: passwords);

        var result = await service.RegisterAsync(new RegisterRequestContract
        {
            Fullname = "  Luan Chenh  ", Phone = "  ab-123  ", Password = "secret1"
        });

        Assert.Equal("Luan Chenh", result.Fullname);
        Assert.Equal("AB-123", result.Phone);
        Assert.Equal("hashed", result.Password);
        Assert.Equal(Role.Rider, result.Role);
        Assert.NotEqual(Guid.Empty, result.Uuid);
        accounts.Verify(x => x.AddAsync(result, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Register_rejects_duplicate_phone()
    {
        var accounts = new Mock<IAccountRepository>();
        accounts.Setup(x => x.PhoneExistsAsync("0909", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var service = CreateAuth(accounts);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegisterAsync(
            new RegisterRequestContract { Fullname = "User", Phone = " 0909 ", Password = "secret1" }));
    }

    [Fact]
    public async Task Register_translates_database_duplicate_error()
    {
        var accounts = new Mock<IAccountRepository>();
        accounts.Setup(x => x.AddAsync(It.IsAny<Account>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException());
        var service = CreateAuth(accounts);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegisterAsync(
            new RegisterRequestContract { Fullname = "User", Phone = "0909", Password = "secret1" }));

        Assert.IsType<DbUpdateException>(exception.InnerException);
    }

    [Fact]
    public async Task Login_rejects_unknown_account_or_bad_password()
    {
        var accounts = new Mock<IAccountRepository>();
        var service = CreateAuth(accounts);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.LoginAsync(
            new LoginRequestContract { Phone = "0909", Password = "badpass" }));
    }

    [Fact]
    public async Task Login_returns_token_for_valid_credentials()
    {
        var account = new Account { Uuid = Guid.NewGuid(), Phone = "0909", Password = "hash" };
        var profile = new DriverProfile { DriverUuid = Guid.NewGuid(), AccountUuid = account.Uuid };
        var accounts = new Mock<IAccountRepository>();
        accounts.Setup(x => x.FindByPhoneAsync("0909", It.IsAny<CancellationToken>())).ReturnsAsync(account);
        var drivers = new Mock<IDriverRepository>();
        drivers.Setup(x => x.FindByAccountUuidAsync(account.Uuid, It.IsAny<CancellationToken>())).ReturnsAsync(profile);
        var passwords = new Mock<IPasswordService>();
        passwords.Setup(x => x.Verify(account, "hash", "secret1")).Returns(true);
        var jwt = new Mock<IJwtService>();
        jwt.Setup(x => x.Create(account, profile)).Returns(("token", 10, 20));
        var service = new AuthService(accounts.Object, drivers.Object, passwords.Object, jwt.Object);

        var result = await service.LoginAsync(new LoginRequestContract { Phone = "0909", Password = "secret1" });

        Assert.Equal("token", result.Token);
        Assert.Equal(account, result.Account);
        Assert.Equal(10, result.IssuedAt);
        Assert.Equal(20, result.ExpiresAt);
    }

    [Fact]
    public async Task RegisterDriver_normalizes_fields_and_promotes_account()
    {
        var account = new Account { Uuid = Guid.NewGuid(), Role = Role.Rider };
        var accounts = new Mock<IAccountRepository>();
        accounts.Setup(x => x.FindByUuidAsync(account.Uuid, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        var drivers = new Mock<IDriverRepository>();
        var service = CreateAuth(accounts, drivers);

        var result = await service.RegisterDriverAsync(account.Uuid, new RegisterDriverRequestContract
        {
            LicenseNumber = "  abc ", LicensePlate = " 51-a1 ", VehicleName = " Honda Wave "
        });

        Assert.Equal("ABC", result.Profile.LicenseNumber);
        Assert.Equal("51-A1", result.Profile.LicensePlate);
        Assert.Equal("Honda Wave", result.Profile.VehicleName);
        Assert.False(result.Profile.IsVerified);
        drivers.Verify(x => x.AddAndPromoteAsync(account, result.Profile, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterDriver_rejects_missing_existing_or_duplicate_driver()
    {
        var accountUuid = Guid.NewGuid();
        var request = new RegisterDriverRequestContract
        {
            LicenseNumber = "ABC", LicensePlate = "51-A1", VehicleName = "Wave"
        };
        var missingService = CreateAuth(new Mock<IAccountRepository>());
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            missingService.RegisterDriverAsync(accountUuid, request));

        var account = new Account { Uuid = accountUuid, Role = Role.Driver };
        var accounts = new Mock<IAccountRepository>();
        accounts.Setup(x => x.FindByUuidAsync(accountUuid, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateAuth(accounts).RegisterDriverAsync(accountUuid, request));

        account.Role = Role.Rider;
        var drivers = new Mock<IDriverRepository>();
        drivers.Setup(x => x.HasDuplicateAsync("ABC", "51-A1", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateAuth(accounts, drivers).RegisterDriverAsync(accountUuid, request));
    }

    [Fact]
    public async Task AboutMe_and_logout_require_existing_account()
    {
        var service = CreateAuth(new Mock<IAccountRepository>());

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.AboutMeAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.LogoutAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task CreateTrip_builds_requested_trip_and_trims_names()
    {
        var rider = new Account { Uuid = Guid.NewGuid(), Role = Role.Rider };
        var accounts = new Mock<IAccountRepository>();
        accounts.Setup(x => x.FindByUuidAsync(rider.Uuid, It.IsAny<CancellationToken>())).ReturnsAsync(rider);
        var trips = new Mock<ITripRepository>();
        var service = new TripService(accounts.Object, trips.Object);

        var result = await service.CreateTripAsync(rider.Uuid, CreateTripRequest());

        Assert.Equal("Pickup", result.PickupName);
        Assert.Equal("Destination", result.DestinationName);
        Assert.Equal(TripStatus.Requested, result.Status);
        Assert.NotEqual(Guid.Empty, result.Uuid);
        trips.Verify(x => x.AddAsync(result, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateTrip_rejects_active_trip_missing_rider_non_rider_and_same_location()
    {
        var riderUuid = Guid.NewGuid();
        var trips = new Mock<ITripRepository>();
        trips.Setup(x => x.FindActiveByRiderAsync(riderUuid, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Trip());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new TripService(Mock.Of<IAccountRepository>(), trips.Object).CreateTripAsync(riderUuid, CreateTripRequest()));

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            new TripService(Mock.Of<IAccountRepository>(), Mock.Of<ITripRepository>())
                .CreateTripAsync(riderUuid, CreateTripRequest()));

        var accounts = new Mock<IAccountRepository>();
        accounts.Setup(x => x.FindByUuidAsync(riderUuid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Account { Uuid = riderUuid, Role = Role.Driver });
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new TripService(accounts.Object, Mock.Of<ITripRepository>()).CreateTripAsync(riderUuid, CreateTripRequest()));

        accounts.Setup(x => x.FindByUuidAsync(riderUuid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Account { Uuid = riderUuid, Role = Role.Rider });
        var same = CreateTripRequest();
        same.Destination.Latitude = same.Pickup.Latitude;
        same.Destination.Longitude = same.Pickup.Longitude;
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new TripService(accounts.Object, Mock.Of<ITripRepository>()).CreateTripAsync(riderUuid, same));
    }

    [Fact]
    public async Task GetTrip_throws_when_not_found_and_update_refreshes_timestamp()
    {
        var trips = new Mock<ITripRepository>();
        var service = new TripService(Mock.Of<IAccountRepository>(), trips.Object);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetTripByUuidAsync(Guid.NewGuid()));

        var trip = new Trip { UpdatedAt = DateTime.UnixEpoch };
        await service.UpdateTripAsync(trip);
        Assert.True(trip.UpdatedAt > DateTime.UnixEpoch);
        trips.Verify(x => x.UpdateAsync(trip, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Rider_history_summary_counts_only_paid_fares_and_distance()
    {
        var riderUuid = Guid.NewGuid();
        var trips = new Mock<ITripRepository>();
        trips.Setup(x => x.FindHistoryByRiderAsync(riderUuid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Trip>
            {
                new() { Status = TripStatus.Paid, Price = 25_000m, DistanceKM = 3.5m },
                new() { Status = TripStatus.Paid, Price = 15_000m, DistanceKM = 2m },
                new() { Status = TripStatus.Cancelled, Price = 50_000m, DistanceKM = 6m },
                new() { Status = TripStatus.Failed },
                new() { Status = TripStatus.DroppedOff, Price = 10_000m }
            });
        var service = new TripService(Mock.Of<IAccountRepository>(), trips.Object);

        var summary = await service.GetTripHistorySummaryByRiderAsync(riderUuid);

        Assert.Equal(5, summary.TotalTrips);
        Assert.Equal(2, summary.PaidTrips);
        Assert.Equal(1, summary.CancelledTrips);
        Assert.Equal(40_000m, summary.TotalPaidAmount);
        Assert.Equal(5.5m, summary.TotalPaidDistanceKM);
        trips.Verify(x => x.FindHistoryByRiderAsync(riderUuid, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Driver_history_summary_uses_driver_history_and_handles_missing_values()
    {
        var driverUuid = Guid.NewGuid();
        var trips = new Mock<ITripRepository>();
        trips.Setup(x => x.FindHistoryByDriverAsync(driverUuid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Trip>
            {
                new() { Status = TripStatus.Paid, Price = 30_000m },
                new() { Status = TripStatus.Paid, DistanceKM = 4m },
                new() { Status = TripStatus.PaymentPending, Price = 20_000m }
            });
        var service = new TripService(Mock.Of<IAccountRepository>(), trips.Object);

        var summary = await service.GetTripHistorySummaryByDriverAsync(driverUuid);

        Assert.Equal(3, summary.TotalTrips);
        Assert.Equal(2, summary.PaidTrips);
        Assert.Equal(0, summary.CancelledTrips);
        Assert.Equal(30_000m, summary.TotalPaidAmount);
        Assert.Equal(4m, summary.TotalPaidDistanceKM);
        trips.Verify(x => x.FindHistoryByDriverAsync(driverUuid, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Empty_history_has_zero_summary()
    {
        var trips = new Mock<ITripRepository>();
        trips.Setup(x => x.FindHistoryByRiderAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Trip>());

        var summary = await new TripService(Mock.Of<IAccountRepository>(), trips.Object)
            .GetTripHistorySummaryByRiderAsync(Guid.NewGuid());

        Assert.Equal(0, summary.TotalTrips);
        Assert.Equal(0, summary.PaidTrips);
        Assert.Equal(0m, summary.TotalPaidAmount);
        Assert.Equal(0m, summary.TotalPaidDistanceKM);
    }

    [Fact]
    public void PasswordService_hashes_and_verifies()
    {
        var service = new PasswordService(new PasswordHasher<Account>());
        var account = new Account();
        var hash = service.Hash(account, "secret1");

        Assert.NotEqual("secret1", hash);
        Assert.True(service.Verify(account, hash, "secret1"));
        Assert.False(service.Verify(account, hash, "wrong"));
    }

    [Fact]
    public void JwtService_creates_signed_token_with_account_and_profile_claims()
    {
        var account = new Account
        {
            Uuid = Guid.NewGuid(), Fullname = "Test User", Phone = "0909", Role = Role.Driver
        };
        var profile = new DriverProfile
        {
            DriverUuid = Guid.NewGuid(), AccountUuid = account.Uuid, LicenseNumber = "ABC",
            VehicleName = "Wave", LicensePlate = "51-A1", IsVerified = true
        };
        var service = new JwtService(Options.Create(new JwtOptions
        {
            Issuer = "tests", Audience = "clients",
            Secret = "a-secure-test-secret-that-is-at-least-32-bytes", ExpirationMinutes = 30
        }));

        var result = service.Create(account, profile);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);

        Assert.Equal("tests", token.Issuer);
        Assert.Contains("clients", token.Audiences);
        Assert.Equal(account.Uuid.ToString(), token.Payload["uuid"]?.ToString());
        Assert.InRange(result.ExpiresAt - result.IssuedAt, 1_799, 1_801);
        using var accountJson = JsonDocument.Parse(token.Payload["account"]!.ToString()!);
        using var profileJson = JsonDocument.Parse(token.Payload["profile"]!.ToString()!);
        Assert.Equal("Test User", accountJson.RootElement.GetProperty("fullname").GetString());
        Assert.Equal(profile.DriverUuid, profileJson.RootElement.GetProperty("profileUuid").GetGuid());
    }

    private static AuthService CreateAuth(
        Mock<IAccountRepository> accounts,
        Mock<IDriverRepository>? drivers = null,
        Mock<IPasswordService>? passwordService = null) => new(
            accounts.Object,
            (drivers ?? new Mock<IDriverRepository>()).Object,
            (passwordService ?? new Mock<IPasswordService>()).Object,
            Mock.Of<IJwtService>());

    private static CreateTripRequestContract CreateTripRequest() => new()
    {
        Pickup = new GeoLocationContract { Name = " Pickup ", Latitude = 10, Longitude = 106 },
        Destination = new GeoLocationContract { Name = " Destination ", Latitude = 11, Longitude = 107 }
    };
}
