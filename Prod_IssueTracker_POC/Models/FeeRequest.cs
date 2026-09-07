namespace Prod_IssueTracker_POC.Models
{
    public class FeeRequest
    {
        public string? AgentId { get; set; }
        public string? BranchId { get; set; }
        public decimal? Amount { get; set; }
        public string? ReferenceNumber { get; set; }
    }
}