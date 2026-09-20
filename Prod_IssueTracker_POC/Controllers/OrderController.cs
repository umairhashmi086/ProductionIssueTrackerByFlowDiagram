using Microsoft.AspNetCore.Mvc;
using Prod_IssueTracker_POC.FlowTagging;
using Prod_IssueTracker_POC.Models;

namespace Prod_IssueTracker_POC.Controllers
{
    /// <summary>
    /// Simple order processing API for testing flow tracking
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class OrderController : ControllerBase
    {
        private readonly IFlowTagger _flowTagger;
        private readonly ILogger<OrderController> _logger;

        public OrderController(IFlowTagger flowTagger, ILogger<OrderController> logger)
        {
            _flowTagger = flowTagger;
            _logger = logger;
        }

        /// <summary>
        /// Process a simple order with flow tracking
        /// </summary>
        /// <param name="request">Order details</param>
        /// <returns>Order processing response</returns>
        /// <remarks>
        /// This is a simple test API that demonstrates flow tagging with minimal steps:
        /// 1. OrderValidated - Input validation complete
        /// 2. OrderAccepted - Order accepted into system
        /// 3. OrderProcessing - Currently being processed
        /// 4. OrderCompleted - Order processing complete
        ///
        /// Sample request:
        ///
        ///     POST /api/order/process
        ///     {
        ///       "orderId": "ORD-001",
        ///       "customerName": "John Doe",
        ///       "amount": 99.99,
        ///       "productType": "Electronics"
        ///     }
        ///
        /// </remarks>
        [HttpPost("process")]
        [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<OrderResponse>> ProcessOrder([FromBody] OrderRequest request)
        {
            var kongId = HttpContext.TraceIdentifier;
            var referenceNumber = $"ORD-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N").Substring(0, 6)}";

            _logger.LogInformation("Order processing started [KongId={KongId}] [Reference={Reference}]", kongId, referenceNumber);

            // Step 1: Validate order
            if (string.IsNullOrWhiteSpace(request.OrderId) || request.Amount <= 0)
            {
                _logger.LogWarning("[{Reference}] Order validation failed", referenceNumber);
                return BadRequest(new OrderResponse
                {
                    Success = false,
                    Message = "Invalid order data",
                    ReferenceNumber = referenceNumber
                });
            }

            _flowTagger.Tag(kongId, referenceNumber, FlowMaps.SimpleOrderFlow, "OrderValidated",
                new { orderId = request.OrderId, amount = request.Amount });

            // Step 2: Accept order
            await Task.Delay(100); // Simulate some processing
            _flowTagger.Tag(kongId, referenceNumber, FlowMaps.SimpleOrderFlow, "OrderAccepted",
                new { customerName = request.CustomerName });

            // Step 3: Processing
            await Task.Delay(150); // Simulate more processing
            _flowTagger.Tag(kongId, referenceNumber, FlowMaps.SimpleOrderFlow, "OrderProcessing",
                new { productType = request.ProductType });

            // Step 4: Complete
            await Task.Delay(100); // Simulate final processing
            _flowTagger.Tag(kongId, referenceNumber, FlowMaps.SimpleOrderFlow, "OrderCompleted",
                new { status = "success", timestamp = DateTime.UtcNow });

            var response = new OrderResponse
            {
                Success = true,
                Message = "Order processed successfully",
                OrderId = request.OrderId,
                ReferenceNumber = referenceNumber,
                Status = "Completed",
                EstimatedDelivery = DateTime.UtcNow.AddDays(5)
            };

            _logger.LogInformation("[{Reference}] Order processing completed successfully", referenceNumber);
            return Ok(response);
        }
    }
}
