namespace Prod_IssueTracker_POC.Models
{
    /// <summary>
    /// Response model for order processing
    /// </summary>
    public class OrderResponse
    {
        /// <summary>
        /// Whether order processing was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Status message
        /// </summary>
        public string? Message { get; set; }

        /// <summary>
        /// Order ID
        /// </summary>
        public string? OrderId { get; set; }

        /// <summary>
        /// Reference number for tracking
        /// </summary>
        public string? ReferenceNumber { get; set; }

        /// <summary>
        /// Processing status
        /// </summary>
        public string? Status { get; set; }

        /// <summary>
        /// Estimated delivery date
        /// </summary>
        public DateTime? EstimatedDelivery { get; set; }
    }
}
