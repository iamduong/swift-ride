using MatchingPricing_Service.Application.Services.Impl;
using MatchingPricing_Service.Application.Services.Pricing;
using MatchingPricing_Service.Domain;

namespace MatchingPricing_Service.Application.Services.Util;

public sealed class PricingService : IPricingService
{
    private readonly IReadOnlyDictionary<WeatherCondition, IPricingStrategy> _strategies;

    public PricingService(IEnumerable<IPricingStrategy> strategies)
    {
        // Chọn công thức tính giá theo điều kiện thời tiết của chuyến đi.
        _strategies = strategies.ToDictionary(strategy => strategy.WeatherCondition);
    }

    public decimal ProcessPrice(
        double distanceKilometers,
        double durationMinutes,
        WeatherCondition weatherCondition,
        decimal promotionMultiplier = 1)
    {
        if (!double.IsFinite(distanceKilometers) || distanceKilometers < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(distanceKilometers));
        }

        if (!double.IsFinite(durationMinutes) || durationMinutes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationMinutes));
        }

        if (promotionMultiplier is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(promotionMultiplier),
                "Hệ số khuyến mãi phải nằm trong khoảng từ 0 đến 1.");
        }

        var strategy = GetStrategy(weatherCondition);
        var price = strategy.Calculate(new PricingContext(
            (decimal)distanceKilometers,
            (decimal)durationMinutes,
            promotionMultiplier));

        if (price < 0)
        {
            throw new InvalidOperationException("Giá chuyến đi không được âm.");
        }

        return price;
    }

    public decimal GetMultiplier(WeatherCondition weatherCondition) =>
        GetStrategy(weatherCondition).Multiplier;

    private IPricingStrategy GetStrategy(WeatherCondition weatherCondition) =>
        _strategies.TryGetValue(weatherCondition, out var strategy)
            ? strategy
            : throw new ArgumentOutOfRangeException(
                nameof(weatherCondition),
                weatherCondition,
                "Không hỗ trợ điều kiện thời tiết này.");
    }
