using System.Collections.Concurrent;

namespace Prod_IssueTracker_POC.FlowTagging
{
    public interface IFlowTagStore
    {
        void Add(FlowTagEntry entry);
        List<FlowTagEntry> GetByKongId(string kongId);
        Dictionary<string, List<FlowTagEntry>> GetByReferenceNumber(string referenceNumber);
    }

    /// <summary>
    /// Purely in-memory — good enough to prove the concept for this POC.
    /// In the real PaymentHub design this same interface would instead be
    /// implemented against Loki (see LokiFlowTagStore) or Postgres — nothing
    /// else in this project would need to change if you swap the
    /// implementation later.
    ///
    /// NOTE: data resets whenever the app restarts, and this is single-
    /// instance only (won't work if you scale to multiple app instances).
    /// Fine for a local POC; not production-ready storage.
    /// </summary>
    public class InMemoryFlowTagStore : IFlowTagStore
    {
        private readonly ConcurrentDictionary<string, List<FlowTagEntry>> _byKongId = new();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _kongIdsByReference = new();
        private readonly object _writeLock = new();

        public void Add(FlowTagEntry entry)
        {
            lock (_writeLock)
            {
                var list = _byKongId.GetOrAdd(entry.KongId, _ => new List<FlowTagEntry>());
                list.Add(entry);

                var kongIds = _kongIdsByReference.GetOrAdd(entry.ReferenceNumber, _ => new ConcurrentDictionary<string, byte>());
                kongIds.TryAdd(entry.KongId, 0);
            }
        }

        public List<FlowTagEntry> GetByKongId(string kongId)
            => _byKongId.TryGetValue(kongId, out var list)
                ? list.OrderBy(e => e.Timestamp).ToList()
                : new List<FlowTagEntry>();

        public Dictionary<string, List<FlowTagEntry>> GetByReferenceNumber(string referenceNumber)
        {
            if (!_kongIdsByReference.TryGetValue(referenceNumber, out var kongIds))
                return new Dictionary<string, List<FlowTagEntry>>();

            return kongIds.Keys.ToDictionary(id => id, GetByKongId);
        }
    }
}
