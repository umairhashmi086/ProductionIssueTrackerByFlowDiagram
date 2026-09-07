using Prod_IssueTracker_POC.Web.Models;

namespace Prod_IssueTracker_POC.Web.Services
{
    /// <summary>
    /// Layered-layout diagram builder — computes node/edge positions and
    /// renders the full SVG server-side, so the Controller can hand a
    /// ready-to-render model to the View (no client-side layout JS).
    /// </summary>
    public static class FlowDiagramLayout
    {
        private const double NodeW = 150, NodeH = 46, RowHeight = 100, ColWidth = 175, TopPad = 20, LeftPad = 20;

        public static DiagramViewModel Build(
            Dictionary<string, string[]> flowMap,
            HashSet<string> terminalTags,
            List<TagDto> tagSequence,
            bool deadEnd,
            string? lastValidTag,
            string? unexpectedTag)
        {
            var allNodes = new HashSet<string>();
            foreach (var kv in flowMap)
            {
                allNodes.Add(kv.Key);
                foreach (var v in kv.Value) allNodes.Add(v);
            }
            foreach (var t in terminalTags) allNodes.Add(t);

            var incoming = allNodes.ToDictionary(n => n, _ => 0);
            foreach (var kv in flowMap)
                foreach (var v in kv.Value)
                    incoming[v] = incoming.GetValueOrDefault(v) + 1;

            var roots = allNodes.Where(n => incoming[n] == 0).ToList();
            var level = new Dictionary<string, int>();
            foreach (var r in roots) level[r] = 0;

            var queue = new Queue<string>(roots);
            int iterations = 0;
            int maxIterations = allNodes.Count * allNodes.Count + 10;
            while (queue.Count > 0 && iterations++ < maxIterations)
            {
                var n = queue.Dequeue();
                if (!flowMap.TryGetValue(n, out var nexts)) continue;
                foreach (var next in nexts)
                {
                    var newLevel = (level.GetValueOrDefault(n)) + 1;
                    if (!level.TryGetValue(next, out var existing) || newLevel > existing)
                    {
                        level[next] = newLevel;
                        queue.Enqueue(next);
                    }
                }
            }
            foreach (var n in allNodes)
                if (!level.ContainsKey(n)) level[n] = 0;

            var byLevel = allNodes.GroupBy(n => level[n]).OrderBy(g => g.Key).ToList();

            var positions = new Dictionary<string, (double X, double Y)>();
            foreach (var group in byLevel)
            {
                var names = group.ToList();
                for (int idx = 0; idx < names.Count; idx++)
                    positions[names[idx]] = (idx * ColWidth + LeftPad, group.Key * RowHeight + TopPad);
            }

            var reachedTags = tagSequence.Select(t => t.TagName).ToList();
            var reachedSet = new HashSet<string>(reachedTags);
            var traversedEdges = new HashSet<string>();
            for (int i = 0; i < reachedTags.Count - 1; i++)
                traversedEdges.Add(reachedTags[i] + "->" + reachedTags[i + 1]);

            // Total occurrences of each tag name across the WHOLE sequence —
            // catches both consecutive retries (already merged into one entry
            // with RetryCount>1 by CollapseConsecutiveDuplicates) and
            // non-consecutive loop-backs (A->B->A, counted as 2 separate
            // entries for A, each with RetryCount=1, summing to 2 total).
            var retryCounts = tagSequence
                .GroupBy(t => t.TagName)
                .ToDictionary(g => g.Key, g => g.Sum(t => t.RetryCount));

            var deadEndTag = deadEnd ? lastValidTag : null;

            var nodes = positions.Select(kv => new FlowNodeViewModel
            {
                Id = kv.Key,
                X = kv.Value.X,
                Y = kv.Value.Y,
                IsReached = reachedSet.Contains(kv.Key),
                IsDeadEnd = kv.Key == deadEndTag,
                IsUnexpected = kv.Key == unexpectedTag,
                RetryCount = retryCounts.GetValueOrDefault(kv.Key, 1)
            }).ToList();

            var edges = new List<FlowEdgeViewModel>();
            foreach (var kv in flowMap)
            {
                if (!positions.TryGetValue(kv.Key, out var from)) continue;
                foreach (var toId in kv.Value)
                {
                    if (!positions.TryGetValue(toId, out var to)) continue;
                    var isTraversed = traversedEdges.Contains(kv.Key + "->" + toId);
                    var sameRow = Math.Abs(from.Y - to.Y) < 0.1;
                    edges.Add(new FlowEdgeViewModel
                    {
                        FromId = kv.Key,
                        ToId = toId,
                        IsTraversed = isTraversed,
                        X1 = from.X + NodeW / 2,
                        Y1 = sameRow ? from.Y + NodeH / 2 : from.Y + NodeH,
                        X2 = to.X + (sameRow ? 0 : NodeW / 2),
                        Y2 = sameRow ? to.Y + NodeH / 2 : to.Y,
                        SameRow = sameRow
                    });
                }
            }

            // Retry loop-back edges: transitions that actually happened in
            // the sequence but aren't part of the static flow map above — a
            // step that failed and routed back to an earlier step it's
            // retrying (e.g. CommitFailed -> CommitRequested). FlowValidator
            // treats these as legitimate (see the "seen before" rule), but
            // without this, they'd be completely invisible in the diagram —
            // same-tag consecutive repeats (A->A) are excluded here since
            // those are already merged into one node with a "retried:N"
            // badge and need no separate arrow.
            var retryEdgeCounts = new Dictionary<(string From, string To), int>();
            var retryEdgeOrder = new List<(string From, string To)>();
            for (int i = 0; i < tagSequence.Count - 1; i++)
            {
                var cur = tagSequence[i].TagName;
                var nxt = tagSequence[i + 1].TagName;
                if (cur == nxt) continue;
                var isNormalEdge = flowMap.TryGetValue(cur, out var allowedHere) && allowedHere.Contains(nxt);
                if (isNormalEdge) continue;
                if (!positions.ContainsKey(cur) || !positions.ContainsKey(nxt)) continue;

                var key = (cur, nxt);
                if (!retryEdgeCounts.ContainsKey(key)) retryEdgeOrder.Add(key);
                retryEdgeCounts[key] = retryEdgeCounts.GetValueOrDefault(key) + 1;
            }

            var maxNodeRight = positions.Count > 0 ? positions.Values.Max(p => p.X) + NodeW : LeftPad + NodeW;
            for (int i = 0; i < retryEdgeOrder.Count; i++)
            {
                var (fromId, toId) = retryEdgeOrder[i];
                var from = positions[fromId];
                var to = positions[toId];
                var laneX = maxNodeRight + 30 + i * 30;

                edges.Add(new FlowEdgeViewModel
                {
                    FromId = fromId,
                    ToId = toId,
                    IsTraversed = true,
                    IsRetryLoopBack = true,
                    OccurrenceCount = retryEdgeCounts[(fromId, toId)],
                    LaneX = laneX,
                    X1 = from.X + NodeW,
                    Y1 = from.Y + NodeH / 2,
                    X2 = to.X + NodeW,
                    Y2 = to.Y + NodeH / 2
                });
            }

            var retryLaneCount = retryEdgeOrder.Count;

            var maxLevel = level.Values.DefaultIfEmpty(0).Max();
            var maxWidth = byLevel.Count > 0 ? byLevel.Max(g => g.Count()) : 1;
            var baseWidth = maxWidth * ColWidth + LeftPad;
            var width = retryLaneCount > 0
                ? Math.Max(baseWidth, maxNodeRight + retryLaneCount * 30 + 40)
                : baseWidth;
            var height = (maxLevel + 1) * RowHeight + TopPad + 45;

            var svg = RenderSvg(nodes, edges, width, height);

            return new DiagramViewModel
            {
                Nodes = nodes,
                Edges = edges,
                Width = width,
                Height = height,
                SvgMarkup = svg
            };
        }

        private static string RenderSvg(List<FlowNodeViewModel> nodes, List<FlowEdgeViewModel> edges, double width, double height)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"<svg viewBox=\"0 0 {Fmt(width)} {Fmt(height)}\" xmlns=\"http://www.w3.org/2000/svg\" style=\"width:100%;height:auto;\">");
            sb.Append("<defs>");
            sb.Append("<marker id=\"arrow-ok\" markerWidth=\"8\" markerHeight=\"8\" refX=\"6\" refY=\"3\" orient=\"auto\"><path d=\"M0,0 L6,3 L0,6 Z\" fill=\"#2ecc71\" /></marker>");
            sb.Append("<marker id=\"arrow-dim\" markerWidth=\"8\" markerHeight=\"8\" refX=\"6\" refY=\"3\" orient=\"auto\"><path d=\"M0,0 L6,3 L0,6 Z\" fill=\"#3a4150\" /></marker>");
            sb.Append("<marker id=\"arrow-retry\" markerWidth=\"8\" markerHeight=\"8\" refX=\"6\" refY=\"3\" orient=\"auto\"><path d=\"M0,0 L6,3 L0,6 Z\" fill=\"#f0a93a\" /></marker>");
            sb.Append("</defs>");

            foreach (var e in edges.Where(e => !e.IsRetryLoopBack))
            {
                var color = e.IsTraversed ? "#2ecc71" : "#3a4150";
                var marker = e.IsTraversed ? "url(#arrow-ok)" : "url(#arrow-dim)";
                if (e.SameRow)
                {
                    sb.Append($"<path d=\"M {Fmt(e.X1)} {Fmt(e.Y1)} C {Fmt(e.X1 + 40)} {Fmt(e.Y1)}, {Fmt(e.X2 - 40)} {Fmt(e.Y2)}, {Fmt(e.X2)} {Fmt(e.Y2)}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"2\" marker-end=\"{marker}\" />");
                }
                else
                {
                    var midY = e.Y1 + (e.Y2 - e.Y1) / 2;
                    sb.Append($"<path d=\"M {Fmt(e.X1)} {Fmt(e.Y1)} C {Fmt(e.X1)} {Fmt(midY)}, {Fmt(e.X2)} {Fmt(midY)}, {Fmt(e.X2)} {Fmt(e.Y2)}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"2\" marker-end=\"{marker}\" />");
                }
            }

            // Retry loop-back edges: routed out to the right of the diagram
            // in their own "lane" so multiple back-edges don't overlap each
            // other or cross through unrelated nodes. Amber + dashed to read
            // clearly as "this is a retry path", not a normal flow step.
            foreach (var e in edges.Where(e => e.IsRetryLoopBack))
            {
                sb.Append($"<path d=\"M {Fmt(e.X1)} {Fmt(e.Y1)} C {Fmt(e.LaneX)} {Fmt(e.Y1)}, {Fmt(e.LaneX)} {Fmt(e.Y2)}, {Fmt(e.X2)} {Fmt(e.Y2)}\" fill=\"none\" stroke=\"#f0a93a\" stroke-width=\"2\" stroke-dasharray=\"5,3\" marker-end=\"url(#arrow-retry)\" />");

                if (e.OccurrenceCount > 1)
                {
                    var midY = e.Y1 + (e.Y2 - e.Y1) / 2;
                    var label = $"×{e.OccurrenceCount}";
                    sb.Append($"<rect x=\"{Fmt(e.LaneX - 14)}\" y=\"{Fmt(midY - 8)}\" width=\"28\" height=\"14\" rx=\"7\" fill=\"#2b2211\" stroke=\"#f0a93a\" stroke-width=\"1\" />");
                    sb.Append($"<text x=\"{Fmt(e.LaneX)}\" y=\"{Fmt(midY + 3)}\" text-anchor=\"middle\" font-family=\"sans-serif\" font-size=\"10\" font-weight=\"600\" fill=\"#f0a93a\">{System.Net.WebUtility.HtmlEncode(label)}</text>");
                }
            }

            foreach (var n in nodes)
            {
                var isBad = n.IsDeadEnd || n.IsUnexpected;
                var fill = isBad ? "#2b1414" : (n.IsReached ? "#12271c" : "transparent");
                var stroke = isBad ? "#ef5350" : (n.IsReached ? "#2ecc71" : "#3a4150");
                var textColor = isBad ? "#ef5350" : (n.IsReached ? "#2ecc71" : "#5c6577");
                var dashAttr = isBad ? " stroke-dasharray=\"4,2\"" : "";

                sb.Append($"<rect x=\"{Fmt(n.X)}\" y=\"{Fmt(n.Y)}\" width=\"150\" height=\"46\" rx=\"8\" fill=\"{fill}\" stroke=\"{stroke}\" stroke-width=\"{(isBad ? "2.5" : "1.5")}\"{dashAttr} />");

                var labelLines = System.Text.RegularExpressions.Regex.Replace(n.Id, "([a-z])([A-Z])", "$1\n$2").Split('\n');
                const double lineHeight = 13;
                var startY = n.Y + 23 - ((labelLines.Length - 1) * lineHeight) / 2.0 + 4;
                for (int li = 0; li < labelLines.Length; li++)
                {
                    var lineY = startY + li * lineHeight;
                    sb.Append($"<text x=\"{Fmt(n.X + 75)}\" y=\"{Fmt(lineY)}\" text-anchor=\"middle\" font-family=\"SFMono-Regular,Consolas,monospace\" font-size=\"10\" font-weight=\"600\" fill=\"{textColor}\">{System.Net.WebUtility.HtmlEncode(labelLines[li])}</text>");
                }

                // Extra annotation lines stack below the node: retry badge
                // first (if any), then the dead-end label (if any) — a node
                // can in principle be both (retried a few times, then the
                // final attempt still didn't reach a terminal tag).
                var belowY = n.Y + NodeH + 14;

                if (n.RetryCount > 1)
                {
                    var badgeText = $"retried:{n.RetryCount}";
                    var pillWidth = 16 + badgeText.Length * 5.2;
                    sb.Append($"<rect x=\"{Fmt(n.X + 75 - pillWidth / 2)}\" y=\"{Fmt(belowY - 10)}\" width=\"{Fmt(pillWidth)}\" height=\"14\" rx=\"7\" fill=\"#2b2211\" stroke=\"#f0a93a\" stroke-width=\"1\" />");
                    sb.Append($"<text x=\"{Fmt(n.X + 75)}\" y=\"{Fmt(belowY)}\" text-anchor=\"middle\" font-family=\"sans-serif\" font-size=\"9\" font-weight=\"600\" fill=\"#f0a93a\">{System.Net.WebUtility.HtmlEncode(badgeText)}</text>");
                    belowY += 16;
                }

                if (n.IsDeadEnd)
                {
                    sb.Append($"<text x=\"{Fmt(n.X + 75)}\" y=\"{Fmt(belowY)}\" text-anchor=\"middle\" font-family=\"sans-serif\" font-size=\"10\" fill=\"#ef5350\">dead end</text>");
                }
            }

            sb.Append("</svg>");
            return sb.ToString();
        }

        private static string Fmt(double value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
