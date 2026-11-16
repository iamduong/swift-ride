using MatchingPricing_Service.Application.Contracts;
using MatchingPricing_Service.Application.Services.Impl;
using MatchingPricing_Service.Application.Services.Util;

namespace MatchingPricing_Service.Application.Services;

public sealed class QuoteService(
    IOSRMService oSRMService,
    IPricingService pricingService
    ) : IQuoteService
{
    public async Task<QuoteResponseContract> RequestQuoteAsync(QuoteRequestContract contract, CancellationToken cancellationToken)
    {
        
        IReadOnlyList<LocationContract> locations = [
            new ( Longitude: contract.Pickup.Longitude, Latitude: contract.Pickup.Latitude ),
            new ( Longitude: contract.Destination.Longitude, Latitude: contract.Destination.Latitude )
        ];

        var oSRM = await oSRMService.GetRouteAsync(locations, cancellationToken);
        if (oSRM.Routes.Count < 1)
        {
            throw new InvalidDataException("OSRM không tín được khoảng cách");
        }

        var route = oSRM.Routes[0];
        // OSRM trả khoảng cách bằng mét và thời gian bằng giây; bộ tính giá dùng km và phút.
        var price = pricingService.ProcessPrice(
            route.Distance / 1000,
            route.Duration / 60,
            contract.WeatherCondition
            );

        return new QuoteResponseContract()
        {
            Price = price,
            Route = route,
            DistanceKM = route.Distance / 1000,
            DurationMin = route.Duration / 60,
            WeatherCondition = contract.WeatherCondition,
            Multiplier = pricingService.GetMultiplier(contract.WeatherCondition)
        };
    }
}
