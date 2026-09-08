using Prod_IssueTracker_POC.Web.Models;

namespace Prod_IssueTracker_POC.Web.FlowTagging
{
    public record AttemptValidationResult(
        DateTimeOffset StartedAt,
        bool IsHealthy,
        bool HasDeviations,
        bool DeadEnd,
        string? LastTag,
        List<TagDto> AnnotatedTags);

    public class FlowValidator
    {
        /// <summary>
        /// Walks the ENTIRE raw sequence and evaluates every transition
        /// individually — unlike a "stop at the first problem" validator,
        /// this keeps going after a deviation, so if the flow gets back on
        /// track afterward, later tags are correctly marked healthy again
        /// instead of the whole attempt just being reported as "broken at X"
        /// with everything after that point left unexamined.
        ///
        /// A transition is tolerated (not flagged) when:
        ///   - it's a normal edge defined in the flow map, OR
        ///   - the target tag repeats the current one (a retry-in-place), OR
        ///   - the target tag was already seen earlier in this attempt (a
        ///     retry loop-back, e.g. an exception step routing back to the
        ///     step it's retrying).
        /// Anything else — a tag that's genuinely new and not an allowed
        /// next step from here — is flagged as a real deviation.
        /// </summary>
        public AttemptValidationResult Validate(List<TagDto> rawTags, Dictionary<string, string[]> flowMap, HashSet<string> terminalTags)
        {
            if (rawTags.Count == 0)
                return new AttemptValidationResult(DateTimeOffset.MinValue, false, false, true, null, new List<TagDto>());

            var annotated = new List<TagDto> { CopyWithFlag(rawTags[0], isUnexpected: false) };
            var seenTags = new HashSet<string> { rawTags[0].TagName };
            var hasDeviations = false;

            for (int i = 0; i < rawTags.Count - 1; i++)
            {
                var current = rawTags[i].TagName;
                var next = rawTags[i + 1].TagName;

                bool isValidTransition =
                    next == current ||                                              // retry-in-place
                    seenTags.Contains(next) ||                                       // retry loop-back
                    (flowMap.TryGetValue(current, out var allowed) && allowed.Contains(next)); // normal edge

                if (!isValidTransition) hasDeviations = true;

                annotated.Add(CopyWithFlag(rawTags[i + 1], isUnexpected: !isValidTransition));
                seenTags.Add(next);
            }

            var lastTag = rawTags[^1].TagName;
            var reachedTerminal = terminalTags.Contains(lastTag);

            return new AttemptValidationResult(
                rawTags[0].Timestamp,
                IsHealthy: !hasDeviations && reachedTerminal,
                HasDeviations: hasDeviations,
                DeadEnd: !reachedTerminal,
                LastTag: lastTag,
                AnnotatedTags: annotated);
        }

        private static TagDto CopyWithFlag(TagDto t, bool isUnexpected) => new()
        {
            TagName = t.TagName,
            Timestamp = t.Timestamp,
            MetadataJson = t.MetadataJson,
            RetryCount = t.RetryCount,
            IsUnexpected = isUnexpected
        };

        /// <summary>
        /// Generic cross-attempt rule: if the previous attempt (same reference
        /// number, earlier in time) never reached a terminal tag, flag this one —
        /// same pattern as the original MoneyGram incident (a retry colliding
        /// with an unfinished prior attempt).
        /// </summary>
        public string? TryLinkToPriorAttempt(AttemptValidationResult? previous, string? previousAttemptId)
        {
            if (previous == null || !previous.DeadEnd) return null;

            return $"Possible retry collision: prior attempt {previousAttemptId} never reached a terminal " +
                   $"state (stuck at '{previous.LastTag}'). This attempt started afterward under the " +
                   "same reference number while that prior attempt was still unresolved.";
        }
    }
}
