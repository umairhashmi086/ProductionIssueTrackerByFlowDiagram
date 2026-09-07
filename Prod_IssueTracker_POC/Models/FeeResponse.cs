namespace Prod_IssueTracker_POC.Models
{
    public class FeeResponse
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? ReferenceNumber { get; set; }
        public Agent? Agent { get; set; }
        public Branch? Branch { get; set; }
        public FeeDetail? Fee { get; set; }
    }

    public class Agent
    {
        public string? AgentId { get; set; }
        public string? AgentName { get; set; }
        public string? AgentType { get; set; }
        public bool IsActive { get; set; }
    }

    public class Branch
    {
        public string? BranchId { get; set; }
        public string? BranchName { get; set; }
        public string? Location { get; set; }
        public bool IsActive { get; set; }
    }

    public class FeeDetail
    {
        public decimal BaseFee { get; set; }
        public decimal AgentCommission { get; set; }
        public decimal BranchFee { get; set; }
        public decimal TotalFee { get; set; }
    }
}