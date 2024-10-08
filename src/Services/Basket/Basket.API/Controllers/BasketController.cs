﻿namespace Microsoft.eShopOnContainers.Services.Basket.API.Controllers;

[Route("api/v1/[controller]")]
[Authorize]
[ApiController]
public class BasketController : ControllerBase
{
    private readonly IBasketRepository _repository;
    private readonly IIdentityService _identityService;
    private readonly IBus _bus;
    private readonly ILogger<BasketController> _logger;

    public BasketController(
        ILogger<BasketController> logger,
        IBasketRepository repository,
        IIdentityService identityService,
        IBus bus)
    {
        _logger = logger;
        _repository = repository;
        _identityService = identityService;
        _bus = bus;
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(CustomerBasket), (int)HttpStatusCode.OK)]
    public async Task<ActionResult<CustomerBasket>> GetBasketByIdAsync(string id)
    {
        var basket = await _repository.GetBasketAsync(id);

        _logger.Debug("Getting by basket id");

        return Ok(basket ?? new CustomerBasket(id));
    }

    [HttpPost]
    [ProducesResponseType(typeof(CustomerBasket), (int)HttpStatusCode.OK)]
    public async Task<ActionResult<CustomerBasket>> UpdateBasketAsync([FromBody] CustomerBasket value)
    {
        return Ok(await _repository.UpdateBasketAsync(value));
    }

    [Route("checkout")]
    [HttpPost]
    [ProducesResponseType((int)HttpStatusCode.Accepted)]
    [ProducesResponseType((int)HttpStatusCode.BadRequest)]
    public async Task<ActionResult> CheckoutAsync([FromBody] BasketCheckout basketCheckout, [FromHeader(Name = "x-requestid")] string requestId)
    {
        var userId = _identityService.GetUserIdentity();

        basketCheckout.RequestId = (Guid.TryParse(requestId, out Guid guid) && guid != Guid.Empty) ?
            guid : basketCheckout.RequestId;

        var basket = await _repository.GetBasketAsync(userId);

        if (basket == null)
        {
            return BadRequest();
        }

        var userName = this.HttpContext.User.FindFirst(x => x.Type == ClaimTypes.Name).Value;

        var eventMessage = new UserCheckoutAcceptedIntegrationEvent(userId, userName, basketCheckout.City, basketCheckout.Street,
            basketCheckout.State, basketCheckout.Country, basketCheckout.ZipCode, basketCheckout.CardNumber, basketCheckout.CardHolderName,
            basketCheckout.CardExpiration, basketCheckout.CardSecurityNumber, basketCheckout.CardTypeId, basketCheckout.Buyer, basketCheckout.RequestId, basket);

        // Once basket is checkout, sends an integration event to
        // ordering.api to convert basket to order and proceeds with
        // order creation process
        await _bus.Publish(eventMessage);

        return Accepted();
    }

    // DELETE api/values/5
    [HttpDelete("{id}")]
    [ProducesResponseType(typeof(void), (int)HttpStatusCode.OK)]
    public async Task DeleteBasketByIdAsync(string id)
    {
        await _repository.DeleteBasketAsync(id);
    }
}
