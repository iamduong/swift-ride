using MatchingPricing_Service.Application.Contracts;
using MatchingPricing_Service.Application.Services.Impl;
using Microsoft.AspNetCore.Mvc;

namespace MatchingPricing_Service.Api;

[ApiController]
[Route("quote")]
public sealed class QuoteController(IQuoteService quoteService) : ControllerBase
{

    [HttpPost]
    public async Task<IActionResult> RequestQuoteAsync(
        QuoteRequestContract contract,

        CancellationToken cancellationToken)
    {
        var result = await quoteService.RequestQuoteAsync(contract, cancellationToken);
        
        return Ok(result);
    }
}
