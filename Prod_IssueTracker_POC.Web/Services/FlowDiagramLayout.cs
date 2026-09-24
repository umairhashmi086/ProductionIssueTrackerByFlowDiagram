using Prod_IssueTracker_POC.Web.Models;

namespace Prod_IssueTracker_POC.Web.Services
{
    /// <summary>
    /// Renders the static expected flow only — a simple DAG, exactly like
    /// the original design. No parallel "actual path" column, no arrows
    /// routed around the diagram for deviations (both approaches ended up
    /// as visual clutter once more than one thing went wrong). Instead, a
    /// small red circle marker is placed just below any node that a
    /// deviation occurred after — a quick "look here" pointer. The full
    /// blow-by-blow (every occurrence, red/green, with metadata) lives in
    /// the raw tag sequence list beneath this diagram, not here.
    /// </summary>
    public static class FlowDiagramLayout
    {
        private const double NodeW = 25, NodeH = 10, RowHeight = 18, ColWidth = 35, TopPad = 8, LeftPad = 8;

        public static DiagramViewModel Build(
            Dictionary<string, string[]> flowMap,
            HashSet<string> terminalTags,
            List<TagDto> tagSequence, // annotated by FlowValidator — each entry's IsUnexpected reflects that specific occurrence
            bool deadEnd,
            string? lastValidTag)
        {
            var knownNodes = new HashSet<string>();
            foreach (var kv in flowMap)
            {
                knownNodes.Add(kv.Key);
                foreach (var v in kv.Value) knownNodes.Add(v);
            }
            foreach (var t in terminalTags) knownNodes.Add(t);

            var incoming = knownNodes.ToDictionary(n => n, _ => 0);
            foreach (var kv in flowMap)
                foreach (var v in kv.Value)
                    incoming[v] = incoming.GetValueOrDefault(v) + 1;

            var roots = knownNodes.Where(n => incoming[n] == 0).ToList();
            var level = new Dictionary<string, int>();
            foreach (var r in roots) level[r] = 0;

            // Longest-path relaxation: each node's row is one below the
            // deepest predecessor that reaches it, so the diagram reads
            // top-to-bottom in dependency order.
            var queue = new Queue<string>(roots);
            int iterations = 0;
            int maxIterations = knownNodes.Count * knownNodes.Count + 10;
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
            foreach (var n in knownNodes)
                if (!level.ContainsKey(n)) level[n] = 0;

            var byLevel = knownNodes.GroupBy(n => level[n]).OrderBy(g => g.Key).ToList();

            var positions = new Dictionary<string, (double X, double Y)>();
            foreach (var group in byLevel)
            {
                var names = group.ToList();
                for (int idx = 0; idx < names.Count; idx++)
                    positions[names[idx]] = (idx * ColWidth + LeftPad, group.Key * RowHeight + TopPad);
            }

            var reachedSet = new HashSet<string>(tagSequence.Select(t => t.TagName));
            var traversedEdges = new HashSet<string>();
            for (int i = 0; i < tagSequence.Count - 1; i++)
                traversedEdges.Add(tagSequence[i].TagName + "->" + tagSequence[i + 1].TagName);

            var retryCounts = tagSequence
                .GroupBy(t => t.TagName)
                .ToDictionary(g => g.Key, g => g.Sum(t => t.RetryCount));

            // For each deviation in the actual sequence, note which KNOWN
            // node it happened right after (so a marker can be placed near
            // something visible on this diagram). Deviations following an
            // unknown/foreign tag have nowhere to attach on this diagram —
            // that's fine, the raw list below still shows them clearly.
            var deviationsAfterNode = new Dictionary<string, int>();
            for (int i = 0; i < tagSequence.Count - 1; i++)
            {
                if (!tagSequence[i + 1].IsUnexpected) continue;
                var afterNode = tagSequence[i].TagName;
                if (!positions.ContainsKey(afterNode)) continue;
                deviationsAfterNode[afterNode] = deviationsAfterNode.GetValueOrDefault(afterNode) + 1;
            }

            var deadEndTag = deadEnd ? lastValidTag : null;

            var nodes = positions.Select(kv => new FlowNodeViewModel
            {
                Id = kv.Key,
                X = kv.Value.X,
                Y = kv.Value.Y,
                IsReached = reachedSet.Contains(kv.Key),
                IsDeadEnd = kv.Key == deadEndTag,
                IsUnexpected = false,
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

            var maxLevel = level.Values.DefaultIfEmpty(0).Max();
            var maxRowWidth = byLevel.Count > 0 ? byLevel.Max(g => g.Count()) : 1;
            var width = maxRowWidth * ColWidth + LeftPad;
            var height = (maxLevel + 1) * RowHeight + TopPad + 45;

            // A "skip" edge (one that jumps past one or more intervening
            // rows, e.g. an early node linking far down to a node that a
            // longer alternate path also reaches) is drawn as a sideways
            // bow in RenderSvg rather than a straight vertical line, so it
            // visibly routes AROUND any unrelated node sitting in the same
            // column in between instead of appearing to cut straight
          // through/merge with it. Reserve extra width for that bow here.
            var hasSkipEdge = edges.Any(e => !e.SameRow && Math.Abs(e.Y2 - e.Y1) > RowHeight * 1.5);
            if (hasSkipEdge) width += NodeW / 2 + 50;

            var svg = RenderSvg(nodes, edges, deviationsAfterNode, width, height);

            return new DiagramViewModel
            {
                Nodes = nodes,
                Edges = edges,
                Width = width,
                Height = height,
                SvgMarkup = svg
            };
        }

        private static string RenderSvg(
            List<FlowNodeViewModel> nodes, List<FlowEdgeViewModel> edges,
            Dictionary<string, int> deviationsAfterNode,
            double width, double height)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"<svg viewBox=\"0 0 {Fmt(width)} {Fmt(height)}\" xmlns=\"http://www.w3.org/2000/svg\" style=\"width:100%;height:auto;\">");
            sb.Append("<defs>");
            sb.Append("<marker id=\"arrow-ok\" markerWidth=\"4\" markerHeight=\"4\" refX=\"3\" refY=\"1.5\" orient=\"auto\"><path d=\"M0,0 L3,1.5 L0,3 Z\" fill=\"#2ecc71\" /></marker>");
            sb.Append("<marker id=\"arrow-dim\" markerWidth=\"4\" markerHeight=\"4\" refX=\"3\" refY=\"1.5\" orient=\"auto\"><path d=\"M0,0 L3,1.5 L0,3 Z\" fill=\"#3a4150\" /></marker>");
            sb.Append("</defs>");

            foreach (var e in edges)
            {
                var color = e.IsTraversed ? "#2ecc71" : "#3a4150";
                var marker = e.IsTraversed ? "url(#arrow-ok)" : "url(#arrow-dim)";
                if (e.SameRow)
                {
                    sb.Append($"<path d=\"M {Fmt(e.X1)} {Fmt(e.Y1)} C {Fmt(e.X1 + 40)} {Fmt(e.Y1)}, {Fmt(e.X2 - 40)} {Fmt(e.Y2)}, {Fmt(e.X2)} {Fmt(e.Y2)}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"1\" marker-end=\"{marker}\" />");
                }
                else
                {
                    var rowSpan = Math.Abs(e.Y2 - e.Y1);
                    var isSkip = rowSpan > RowHeight * 1.5 && Math.Abs(e.X1 - e.X2) < 0.1;
                    if (isSkip)
                    {
                        // Bows the line out to the side instead of drawing
                        // it straight down the column, so it visibly routes
                        // AROUND any unrelated node sitting in between
                        // (e.g. an alternate/failure branch off the same
                        // source) rather than appearing to run straight
                        // through — and hence "merge with" — that node.
                        var bow = NodeW / 2 + 45;
                        sb.Append($"<path d=\"M {Fmt(e.X1)} {Fmt(e.Y1)} C {Fmt(e.X1 + bow)} {Fmt(e.Y1 + 20)}, {Fmt(e.X2 + bow)} {Fmt(e.Y2 - 20)}, {Fmt(e.X2)} {Fmt(e.Y2)}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"1\" marker-end=\"{marker}\" />");
                    }
                    else
                    {
                        var midY = e.Y1 + (e.Y2 - e.Y1) / 2;
                        sb.Append($"<path d=\"M {Fmt(e.X1)} {Fmt(e.Y1)} C {Fmt(e.X1)} {Fmt(midY)}, {Fmt(e.X2)} {Fmt(midY)}, {Fmt(e.X2)} {Fmt(e.Y2)}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"1\" marker-end=\"{marker}\" />");
                    }
                }
            }

            foreach (var n in nodes)
            {
                var isBad = n.IsDeadEnd;
                var fill = isBad ? "#2b1414" : (n.IsReached ? "#12271c" : "transparent");
                var stroke = isBad ? "#ef5350" : (n.IsReached ? "#2ecc71" : "#3a4150");
                var textColor = isBad ? "#ef5350" : (n.IsReached ? "#2ecc71" : "#5c6577");
                var dashAttr = isBad ? " stroke-dasharray=\"4,2\"" : "";

                var isDimUnreached = !n.IsReached && !isBad;
                var boxW = isDimUnreached ? NodeW * 0.7 : NodeW;
                var boxH = isDimUnreached ? NodeH * 0.72 : NodeH;
                var boxX = n.X + (NodeW - boxW) / 2;
                var boxY = n.Y + (NodeH - boxH) / 2;
                var fontSize = isDimUnreached ? 2 : 2.5;

                sb.Append($"<rect x=\"{Fmt(boxX)}\" y=\"{Fmt(boxY)}\" width=\"{Fmt(boxW)}\" height=\"{Fmt(boxH)}\" rx=\"3\" fill=\"{fill}\" stroke=\"{stroke}\" stroke-width=\"{(isBad ? "0.75" : "0.5")}\"{dashAttr} />");

                var labelLines = System.Text.RegularExpressions.Regex.Replace(n.Id, "([a-z])([A-Z])", "$1\n$2").Split('\n');
                var lineHeight = fontSize + 2;
                var textCenterX = n.X + NodeW / 2;
                var textCenterY = n.Y + NodeH / 2;
                var startY = textCenterY - ((labelLines.Length - 1) * lineHeight) / 2.0;
                for (int li = 0; li < labelLines.Length; li++)
                {
                    var lineY = startY + li * lineHeight;
                    sb.Append($"<text x=\"{Fmt(textCenterX)}\" y=\"{Fmt(lineY)}\" text-anchor=\"middle\" dominant-baseline=\"middle\" font-family=\"SFMono-Regular,Consolas,monospace\" font-size=\"{fontSize}\" font-weight=\"400\" fill=\"{textColor}\">{System.Net.WebUtility.HtmlEncode(labelLines[li])}</text>");
                }

                var belowY = n.Y + NodeH + 10;

                if (n.RetryCount > 1)
                {
                    // Tiny retry indicator - just a small dot
                    var dotSize = 2.5;
                    sb.Append($"<circle cx=\"{Fmt(textCenterX + NodeW/2 - 2)}\" cy=\"{Fmt(n.Y - 2)}\" r=\"{dotSize}\" fill=\"#f0a93a\" />");
                    sb.Append($"<text x=\"{Fmt(textCenterX + NodeW/2 - 2)}\" y=\"{Fmt(n.Y - 2)}\" text-anchor=\"middle\" dominant-baseline=\"middle\" font-family=\"sans-serif\" font-size=\"1.5\" font-weight=\"600\" fill=\"#0f1115\">{n.RetryCount}</text>");
                }

                if (n.IsDeadEnd)
                {
                    sb.Append($"<text x=\"{Fmt(textCenterX)}\" y=\"{Fmt(belowY)}\" text-anchor=\"middle\" dominant-baseline=\"middle\" font-family=\"sans-serif\" font-size=\"2.5\" fill=\"#ef5350\">end</text>");
                    belowY += 10;
                }

                if (deviationsAfterNode.TryGetValue(n.Id, out var devCount))
                {
                    var cx = textCenterX;
                    var cy = belowY + 2;
                    sb.Append($"<circle cx=\"{Fmt(cx)}\" cy=\"{Fmt(cy)}\" r=\"4\" fill=\"#ef5350\" />");
                    if (devCount > 1)
                    {
                        sb.Append($"<text x=\"{Fmt(cx)}\" y=\"{Fmt(cy + 1)}\" text-anchor=\"middle\" dominant-baseline=\"middle\" font-family=\"sans-serif\" font-size=\"2\" font-weight=\"400\" fill=\"#0f1115\">{devCount}</text>");
                    }
                    else
                    {
                        sb.Append($"<text x=\"{Fmt(cx)}\" y=\"{Fmt(cy + 1)}\" text-anchor=\"middle\" dominant-baseline=\"middle\" font-family=\"sans-serif\" font-size=\"2.5\" font-weight=\"400\" fill=\"#0f1115\">!</text>");
                    }
                }
            }

            sb.Append("</svg>");
            return sb.ToString();
        }

        private static string Fmt(double value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
