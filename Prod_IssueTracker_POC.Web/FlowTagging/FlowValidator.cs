using Prod_IssueTracker_POC.Web.Models;

namespace Prod_IssueTracker_POC.Web.FlowTagging
{
    public record AttemptValidationResult(
        DateTimeOffset StartedAt,
        bool IsHealthy,
        string? LastValidTag,
        bool DeadEnd,
        string? UnexpectedTag);

    public class FlowValidator
    {
        /// <summary>
        /// Collapses consecutive occurrences of the same tag into one entry
        /// with RetryCount set — this is what makes a saga step being retried
        /// (e.g. a MassTransit retry policy re-running CommitRequested 3 times)
        /// show up as one step with "retried:3" instead of tripping the
        /// adjacency check, which has no self-loop defined for any tag.
        /// Call this BEFORE Validate — Validate assumes duplicates are
        /// already collapsed, so the same collapsed list should be used for
        /// both validation and display (raw tag list + diagram).
        /// </summary>
        public static List<TagDto> CollapseConsecutiveDuplicates(List<TagDto> tags)
        {
            var result = new List<TagDto>();
            foreach (var tag in tags)
            {
                var last = result.Count > 0 ? result[^1] : null;
                if (last != null && last.TagName == tag.TagName)
                {
                    // Same step retried — bump the count, keep the earliest
                    // timestamp (when the retrying run of this step started)
                    // but take the latest metadata (the most recent/final
                    // outcome of that step, e.g. the last error message).
                    last.RetryCount++;
                    last.MetadataJson = tag.MetadataJson;
                }
                else
                {
                    result.Add(new TagDto
                    {
                        TagName = tag.TagName,
                        Timestamp = tag.Timestamp,
                        MetadataJson = tag.MetadataJson,
                        RetryCount = 1
                    });
                }
            }
            return result;
        }

        public AttemptValidationResult Validate(List<TagDto> tags, Dictionary<string, string[]> flowMap, HashSet<string> terminalTags)
        {
            if (tags.Count == 0)
                return new AttemptValidationResult(DateTimeOffset.MinValue, false, null, true, null);

            // Tracks every tag name seen so far as we walk forward, so a jump
            // back to an earlier tag (e.g. an exception step routing back to
            // the step it's retrying — A->B->A->C, or A->B->B->A->B->D) can be
            // recognized as a retry loop-back rather than a real deviation.
            // Consecutive duplicates are assumed already collapsed by
            // CollapseConsecutiveDuplicates before this runs.
            var seenTags = new HashSet<string> { tags[0].TagName };

            for (int i = 0; i < tags.Count - 1; i++)
            {
                var current = tags[i].TagName;
                var next = tags[i + 1].TagName;

                if (!flowMap.TryGetValue(current, out var allowed))
                    return new AttemptValidationResult(tags[0].Timestamp, false, current, true, null);

                if (!allowed.Contains(next))
                {
                    // Not a normally-allowed transition — but if we've already
                    // been at `next` earlier in this attempt, this is a retry
                    // looping back to an earlier step, not a genuine deviation.
                    if (!seenTags.Contains(next))
                        return new AttemptValidationResult(tags[0].Timestamp, false, current, false, next);
                }

                seenTags.Add(next);
            }

            var lastTag = tags[^1].TagName;
            var reachedTerminal = terminalTags.Contains(lastTag);

            return new AttemptValidationResult(
                tags[0].Timestamp,
                IsHealthy: reachedTerminal,
                LastValidTag: lastTag,
                DeadEnd: !reachedTerminal,
                UnexpectedTag: null);
        }

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
                   $"state (stuck at '{previous.LastValidTag}'). This attempt started afterward under the " +
                   "same reference number while that prior attempt was still unresolved.";
        }
    }
}
