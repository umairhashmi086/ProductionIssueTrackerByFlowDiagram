using Prod_IssueTracker_POC.FlowTagging;
using Prod_IssueTracker_POC.Models;

namespace Prod_IssueTracker_POC.Services
{
    public interface IFeeService
    {
        Task<bool> ValidateRequest(FeeRequest request, string referenceNumber, string kongId);
        Task<Agent?> GetAgent(string agentId, string referenceNumber, string kongId);
        Task<Branch?> GetBranch(string branchId, string referenceNumber, string kongId);
        Task<FeeDetail?> GetFee(decimal amount, Agent agent, Branch branch, string referenceNumber, string kongId);
        Task<FeeResponse> CalculateFee(FeeRequest request, string kongId);
    }

    public class FeeService : IFeeService
    {
        private readonly ILogger<FeeService> _logger;
        private readonly IFlowTagger _flowTagger;

        public FeeService(ILogger<FeeService> logger, IFlowTagger flowTagger)
        {
            _logger = logger;
            _flowTagger = flowTagger;
        }

        public async Task<bool> ValidateRequest(FeeRequest request, string referenceNumber, string kongId)
        {
            _logger.LogInformation("[{ReferenceNumber}] Validating Fee Request", referenceNumber);

            if (request == null)
            {
                _logger.LogWarning("[{ReferenceNumber}] Request is null", referenceNumber);
                _flowTagger.Tag(kongId, referenceNumber, FlowMaps.GetFeeTransaction, "ValidationFailed", new { reason = "Request is null" });
                return false;
            }

            if (string.IsNullOrWhiteSpace(request.AgentId))
            {
                _logger.LogWarning("[{ReferenceNumber}] AgentId is required", referenceNumber);
                _flowTagger.Tag(kongId, referenceNumber, FlowMaps.GetFeeTransaction, "ValidationFailed", new { reason = "AgentId is required" });
                return false;
            }

            if (string.IsNullOrWhiteSpace(request.BranchId))
            {
                _logger.LogWarning("[{ReferenceNumber}] BranchId is required", referenceNumber);
                _flowTagger.Tag(kongId, referenceNumber, FlowMaps.GetFeeTransaction, "ValidationFailed", new { reason = "BranchId is required" });
                return false;
            }

            if (request.Amount == null || request.Amount <= 0)
            {
                _logger.LogWarning("[{ReferenceNumber}] Amount must be greater than zero", referenceNumber);
                _flowTagger.Tag(kongId, referenceNumber, FlowMaps.GetFeeTransaction, "ValidationFailed", new { reason = "Amount must be greater than zero" });
                return false;
            }

            _logger.LogInformation("[{ReferenceNumber}] Request validation successful", referenceNumber);
            _flowTagger.Tag(kongId, referenceNumber, FlowMaps.GetFeeTransaction, "ValidationSuccess");
            return await Task.FromResult(true);
        }

        public async Task<Agent?> GetAgent(string agentId, string referenceNumber, string kongId)
        {
            _logger.LogInformation("[{ReferenceNumber}] Getting agent details for AgentId: {AgentId}", referenceNumber, agentId);

            await Task.Delay(100);

            var agent = new Agent
            {
                AgentId = agentId,
                AgentName = $"Agent {agentId}",
                AgentType = "Premium",
                IsActive = true
            };

            _logger.LogInformation("[{ReferenceNumber}] Agent retrieved successfully: {AgentName}", referenceNumber, agent.AgentName);

            if (agent == null || !agent.IsActive)
                _flowTagger.Tag(kongId, referenceNumber, FlowMaps.GetFeeTransaction, "AgentInvalid");
            else
                _flowTagger.Tag(kongId, referenceNumber, FlowMaps.GetFeeTransaction, "AgentRetrieved", new { agentId = agent.AgentId });

            return await Task.FromResult(agent);
        }

        public async Task<Branch?> GetBranch(string branchId, string referenceNumber, string kongId)
        {
            _logger.LogInformation("[{ReferenceNumber}] Getting branch details for BranchId: {BranchId}", referenceNumber, branchId);

            await Task.Delay(100);

            var branch = new Branch
            {
                BranchId = branchId,
                BranchName = $"Branch {branchId}",
                Location = "Main Office",
                IsActive = true
            };

            _logger.LogInformation("[{ReferenceNumber}] Branch retrieved successfully: {BranchName}", referenceNumber, branch.BranchName);

            if (branch == null || !branch.IsActive)
                _flowTagger.Tag(kongId, referenceNumber, FlowMaps.GetFeeTransaction, "BranchInvalid");
            else
                _flowTagger.Tag(kongId, referenceNumber, FlowMaps.GetFeeTransaction, "BranchRetrieved", new { branchId = branch.BranchId });

            return await Task.FromResult(branch);
        }

        public async Task<FeeDetail?> GetFee(decimal amount, Agent agent, Branch branch, string referenceNumber, string kongId)
        {
            _logger.LogInformation("[{ReferenceNumber}] Calculating fee for amount: {Amount}", referenceNumber, amount);

            await Task.Delay(100);

            var baseFee = amount * 0.02m;
            var agentCommission = agent.AgentType == "Premium" ? amount * 0.01m : amount * 0.005m;
            var branchFee = branch.IsActive ? amount * 0.005m : 0;
            var totalFee = baseFee + agentCommission + branchFee;

            var feeDetail = new FeeDetail
            {
                BaseFee = baseFee,
                AgentCommission = agentCommission,
                BranchFee = branchFee,
                TotalFee = totalFee
            };

            _logger.LogInformation("[{ReferenceNumber}] Fee calculated successfully: {TotalFee}", referenceNumber, totalFee);

            if (feeDetail == null)
                _flowTagger.Tag(kongId, referenceNumber, FlowMaps.GetFeeTransaction, "FeeCalculationFailed");
            else
                _flowTagger.Tag(kongId, referenceNumber, FlowMaps.GetFeeTransaction, "FeeCalculated", new { totalFee = feeDetail.TotalFee });

            return await Task.FromResult(feeDetail);
        }

        public async Task<FeeResponse> CalculateFee(FeeRequest request, string kongId)
        {
            var referenceNumber = request.ReferenceNumber ?? $"REF{Guid.NewGuid().ToString().Replace("-", "").Substring(0, 12).ToUpper()}";

            _logger.LogInformation("[{ReferenceNumber}] Starting Fee Calculation Process", referenceNumber);
            _flowTagger.Tag(kongId, referenceNumber, FlowMaps.GetFeeTransaction, "RequestReceived");

            var response = new FeeResponse
            {
                ReferenceNumber = referenceNumber
            };

            var isValid = await ValidateRequest(request, referenceNumber, kongId);
            if (!isValid)
            {
                response.Success = false;
                response.Message = "Request validation failed";
                _logger.LogWarning("[{ReferenceNumber}] Fee calculation failed: Request validation failed", referenceNumber);
                return response;
            }

            var agent = await GetAgent(request.AgentId!, referenceNumber, kongId);
            if (agent == null || !agent.IsActive)
            {
                response.Success = false;
                response.Message = "Invalid or inactive agent";
                _logger.LogWarning("[{ReferenceNumber}] Fee calculation failed: Invalid or inactive agent", referenceNumber);
                return response;
            }

            var branch = await GetBranch(request.BranchId!, referenceNumber, kongId);
            if (branch == null || !branch.IsActive)
            {
                response.Success = false;
                response.Message = "Invalid or inactive branch";
                _logger.LogWarning("[{ReferenceNumber}] Fee calculation failed: Invalid or inactive branch", referenceNumber);
                return response;
            }

            var fee = await GetFee(request.Amount!.Value, agent, branch, referenceNumber, kongId);
            if (fee == null)
            {
                response.Success = false;
                response.Message = "Failed to calculate fee";
                _logger.LogError("[{ReferenceNumber}] Fee calculation failed: Unable to calculate fee", referenceNumber);
                return response;
            }

            response.Success = true;
            response.Message = "Fee calculated successfully";
            response.Agent = agent;
            response.Branch = branch;
            response.Fee = fee;

            _logger.LogInformation("[{ReferenceNumber}] Fee Calculation Process completed successfully. Total Fee: {TotalFee}", referenceNumber, fee.TotalFee);
            _flowTagger.Tag(kongId, referenceNumber, FlowMaps.GetFeeTransaction, "Failed");
            _flowTagger.Tag(kongId, referenceNumber, FlowMaps.GetFeeTransaction, "Failed1");
            _flowTagger.Tag(kongId, referenceNumber, FlowMaps.GetFeeTransaction, "Failed2");
            return response;
        }
    }
}
