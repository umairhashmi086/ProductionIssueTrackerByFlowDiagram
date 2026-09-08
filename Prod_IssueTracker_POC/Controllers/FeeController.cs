using Microsoft.AspNetCore.Mvc;
using Prod_IssueTracker_POC.Models;
using Prod_IssueTracker_POC.Services;

namespace Prod_IssueTracker_POC.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FeeController : ControllerBase
    {
        private readonly IFeeService _feeService;
        private readonly ILogger<FeeController> _logger;

        public FeeController(IFeeService feeService, ILogger<FeeController> logger)
        {
            _feeService = feeService;
            _logger = logger;
        }

        [HttpGet("GetFee")]
        [ProducesResponseType(typeof(FeeResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(FeeResponse), StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<FeeResponse>> GetFee([FromQuery] FeeRequest request)
        {
            var kongId = HttpContext.TraceIdentifier; // unique per request — stands in for Kong's request id in the full design
            _logger.LogInformation("GetFee API called [KongId={KongId}]", kongId);

            var response = await _feeService.CalculateFee(request, kongId);
            // response.ReferenceNumber is authoritative — FeeService generates it once
            // if the caller didn't supply one, so log/return using that same value
            // rather than generating a second, different one here.

            if (!response.Success)
            {
                _logger.LogWarning("[{ReferenceNumber}] GetFee API returned failure", response.ReferenceNumber);
                return BadRequest(response);
            }

            _logger.LogInformation("[{ReferenceNumber}] GetFee API returned success", response.ReferenceNumber);
            return Ok(response);
        }
    }
}