namespace Prod_IssueTracker_POC.Models
{
    /// <summary>
    /// Request model for order processing
    /// </summary>
    public class OrderRequest
    {
        /// <summary>
        /// Order ID
        /// </summary>
        public string? OrderId { get; set; }

        /// <summary>
        /// Customer name
        /// </summary>
        public string? CustomerName { get; set; }

        /// <summary>
        /// Order amount
        /// </summary>
        public decimal Amount { get; set; }

        /// <summary>
        /// Product type
        /// </summary>
        public string? ProductType { get; set; }
    }
}
