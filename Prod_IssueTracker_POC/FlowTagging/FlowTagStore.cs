using System.Collections.Concurrent;

namespace Prod_IssueTracker_POC.FlowTagging
{
    public interface IFlowTagStore
    {
        void Add(FlowTagEntry entry);
        List<FlowTagEntry> GetByAttemptId(string attemptId);
        Dictionary<string, List<FlowTagEntry>> GetByReferenceNumber(string referenceNumber);
    }

    /// <summary>
    /// Purely in-memory — good enough to prove the concept for this POC.
    /// In the real PaymentHub design this same interface would instead be
    /// implemented against Loki (see LokiFlowTagRepository discussed
    /// separately) or Postgres — nothing else in this project would need to
    /// change if you swap the implementation later.
    ///
    /// NOTE: data resets whenever the app restarts, and this is single-
    /// instance only (won't work if you scale to multiple app instances).
    /// Fine for a local POC; not production-ready storage.
    /// </summary>
    public class InMemoryFlowTagStore : IFlowTagStore
    {
        private readonly ConcurrentDictionary<string, List<FlowTagEntry>> _byAttemptId = new();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _attemptIdsByReference = new();
        private readonly object _writeLock = new();

        public void Add(FlowTagEntry entry)
        {
            lock (_writeLock)
            {
                var list = _byAttemptId.GetOrAdd(entry.AttemptId, _ => new List<FlowTagEntry>());
                list.Add(entry);

                var attemptIds = _attemptIdsByReference.GetOrAdd(entry.ReferenceNumber, _ => new ConcurrentDictionary<string, byte>());
                attemptIds.TryAdd(entry.AttemptId, 0);
            }
        }

        public List<FlowTagEntry> GetByAttemptId(string attemptId)
            => _byAttemptId.TryGetValue(attemptId, out var list)
                ? list.OrderBy(e => e.Timestamp).ToList()
                : new List<FlowTagEntry>();

        public Dictionary<string, List<FlowTagEntry>> GetByReferenceNumber(string referenceNumber)
        {
            if (!_attemptIdsByReference.TryGetValue(referenceNumber, out var attemptIds))
                return new Dictionary<string, List<FlowTagEntry>>();

            return attemptIds.Keys.ToDictionary(id => id, GetByAttemptId);
        }
    }
}
