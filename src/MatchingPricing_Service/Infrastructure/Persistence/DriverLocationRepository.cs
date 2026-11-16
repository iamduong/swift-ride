using MatchingPricing_Service.Application.Contracts;
using MatchingPricing_Service.Domain;
using MatchingPricing_Service.Infrastructure.Persistence.Impl;
using MongoDB.Driver;
using MongoDB.Driver.GeoJsonObjectModel;

namespace MatchingPricing_Service.Infrastructure.Persistence;

public sealed class DriverLocationRepository : IDriverLocationRepository
{
    private readonly IMongoCollection<DriverLocation> _collection;

    public DriverLocationRepository(MongoDBContext context)
    {
        _collection = context.DriverLocationCollection;
    }

    public async Task<DriverLocation?> FindByDriverUuidAsync(Guid driverUuid, CancellationToken cancellationToken) =>
        await _collection.Find(driver => driver.DriverUuid == driverUuid).FirstOrDefaultAsync(cancellationToken);

    public async Task<DriverLocation> UpdateLocationAsync(
        Guid driverUuid,
        double latitude,
        double longitude,
        CancellationToken cancellationToken)
    {
        // Upsert tạo hồ sơ vị trí khi tài xế gửi tọa độ lần đầu.
        var point = new GeoJsonPoint<GeoJson2DGeographicCoordinates>(
            new GeoJson2DGeographicCoordinates(longitude, latitude));
        var now = DateTime.UtcNow;
        var update = Builders<DriverLocation>.Update
            .Set(driver => driver.Location, point)
            .Set(driver => driver.UpdatedAt, now)
            .SetOnInsert(driver => driver.DriverUuid, driverUuid)
            .SetOnInsert(driver => driver.IsOnline, true)
            .SetOnInsert(driver => driver.IsAvailable, true);

        return await _collection.FindOneAndUpdateAsync(
            driver => driver.DriverUuid == driverUuid,
            update,
            new FindOneAndUpdateOptions<DriverLocation>
            {
                IsUpsert = true,
                ReturnDocument = ReturnDocument.After
            },
            cancellationToken);
    }

    public async Task SetAvailabilityAsync(
        Guid driverUuid,
        bool isAvailable,
        CancellationToken cancellationToken)
    {
        var result = await _collection.UpdateOneAsync(
            driver => driver.DriverUuid == driverUuid,
            Builders<DriverLocation>.Update
                .Set(driver => driver.IsAvailable, isAvailable)
                .Set(driver => driver.UpdatedAt, DateTime.UtcNow),
            cancellationToken: cancellationToken);
        if (result.MatchedCount == 0) throw new KeyNotFoundException("Không tìm thấy vị trí tài xế.");
    }

    public async Task<DriverLocation?> FindNearestAvailableAsync(LocationContract location, IReadOnlyCollection<Guid> excludeDriverUuids, double maxDistanceMeters, CancellationToken cancellationToken)
    {
        var pickupPoint = new GeoJsonPoint<GeoJson2DGeographicCoordinates>(
            new GeoJson2DGeographicCoordinates(
                location.Longitude, location.Latitude
            )
        );

        // NearSphere dùng chỉ mục 2dsphere để lấy tài xế gần điểm đón nhất.
        var filters = new List<FilterDefinition<DriverLocation>>
        {
            Builders<DriverLocation>.Filter.Eq(driver => driver.IsOnline, true),
            Builders<DriverLocation>.Filter.Eq(driver => driver.IsAvailable, true),
            Builders<DriverLocation>.Filter.NearSphere(driver => driver.Location, pickupPoint, maxDistance: maxDistanceMeters)
        };

        if (excludeDriverUuids.Count > 0)
        {
            filters.Add(
                Builders<DriverLocation>.Filter.Nin(driver => driver.DriverUuid, excludeDriverUuids)
            );
        }

        var filter = Builders<DriverLocation>.Filter.And(filters);

        return await this._collection.Find(filter).Limit(1).FirstOrDefaultAsync(cancellationToken);
    }
    
    public async Task SetOnlineStatusAsync(Guid driverUuid, bool isOnline, CancellationToken cancellationToken)
    {
        var result = await _collection.UpdateOneAsync(
            driver => driver.DriverUuid == driverUuid,
            Builders<DriverLocation>.Update
                .Set(driver => driver.IsOnline, isOnline)
                .Set(driver => driver.UpdatedAt, DateTime.UtcNow),
                cancellationToken: cancellationToken
        );

        if (result.MatchedCount == 0) throw new KeyNotFoundException("Không tìm thấy thông tin tài xế");
    }
}
