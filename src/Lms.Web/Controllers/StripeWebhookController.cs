using Lms.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lms.Web.Controllers;

[ApiController]
[Route("api/webhooks/stripe")]
public class StripeWebhookController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly ILogger<StripeWebhookController> _logger;

    public StripeWebhookController(IPaymentService paymentService, ILogger<StripeWebhookController> logger)
    {
        _paymentService = paymentService;
        _logger = logger;
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> HandleAsync()
    {
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync();
        var signature = Request.Headers["Stripe-Signature"].ToString();

        var result = await _paymentService.ProcessStripeWebhookAsync(payload, signature);
        if (!result.Success)
        {
            _logger.LogWarning("Stripe webhook rejected: {Message}", result.Message);
            return BadRequest(new
            {
                result.Success,
                result.Message,
                result.EventType,
                result.StripePaymentIntentId
            });
        }

        return Ok(new
        {
            result.Success,
            result.Message,
            result.EventType,
            result.StripePaymentIntentId
        });
    }
}
