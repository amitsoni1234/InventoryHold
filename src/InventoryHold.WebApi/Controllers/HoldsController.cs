using InventoryHold.Contracts;
using InventoryHold.Domain;
using InventoryHold.Domain.Commands;
using InventoryHold.Domain.Services;
using InventoryHold.WebApi.Mapping;
using Microsoft.AspNetCore.Mvc;

namespace InventoryHold.WebApi.Controllers;

[ApiController]
[Route("api/holds")]
public sealed class HoldsController : ControllerBase
{
    private readonly HoldService _holds;

    public HoldsController(HoldService holds)
    {
        _holds = holds;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateHoldRequest? request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new ApiError
            {
                ErrorCode = ErrorCodes.Validation,
                Message = "Request body is required."
            });
        }

        var command = new CreateHoldCommand(
            request.Items?.Select(item => new CreateHoldLine(item.ProductId, item.Quantity)).ToList(),
            request.ClientRequestId);
        var result = await _holds.CreateAsync(command, cancellationToken);
        if (!result.IsSuccess)
        {
            return FromError(result.Error!);
        }

        var body = ResponseMapper.ToResponse(result.Value!);
        return CreatedAtAction(nameof(Get), new { holdId = body.HoldId }, body);
    }

    [HttpGet("{holdId}")]
    public async Task<IActionResult> Get(string holdId, CancellationToken cancellationToken)
    {
        var result = await _holds.GetAsync(holdId, cancellationToken);
        if (!result.IsSuccess)
        {
            return FromError(result.Error!);
        }

        return Ok(ResponseMapper.ToResponse(result.Value!));
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("active", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new ApiError
            {
                ErrorCode = ErrorCodes.Validation,
                Message = "Only status=active is supported."
            });
        }

        var holds = await _holds.ListActiveAsync(cancellationToken);
        return Ok(holds.Select(ResponseMapper.ToResponse).ToList());
    }

    [HttpDelete("{holdId}")]
    public async Task<IActionResult> Release(string holdId, CancellationToken cancellationToken)
    {
        var result = await _holds.ReleaseAsync(holdId, cancellationToken);
        if (!result.IsSuccess)
        {
            return FromError(result.Error!);
        }

        return NoContent();
    }

    private ObjectResult FromError(ServiceError error) =>
        StatusCode(error.StatusCode, new ApiError
        {
            ErrorCode = error.Code,
            Message = error.Message
        });
}
