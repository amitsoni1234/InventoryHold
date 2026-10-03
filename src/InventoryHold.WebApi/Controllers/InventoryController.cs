using InventoryHold.Domain.Services;
using InventoryHold.WebApi.Mapping;
using Microsoft.AspNetCore.Mvc;

namespace InventoryHold.WebApi.Controllers;

[ApiController]
[Route("api/inventory")]
public sealed class InventoryController : ControllerBase
{
    private readonly InventoryQueryService _inventory;

    public InventoryController(InventoryQueryService inventory)
    {
        _inventory = inventory;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var products = await _inventory.GetInventoryAsync(cancellationToken);
        return Ok(products.Select(ResponseMapper.ToResponse).ToList());
    }
}
