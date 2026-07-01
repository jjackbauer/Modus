using System.Text;

namespace Wip.Bones.Web.Viewer;

internal static class BonesViewerLearningLoopStatusMarkup
{
    private const string Placeholder = "\u2014";

    internal static void Append(StringBuilder builder, string indent)
    {
        builder.AppendLine($"{indent}<section id=\"learning-loop-status\" class=\"learning-loop-status hidden\" aria-label=\"Learning loop status\"");
        builder.AppendLine($"{indent}  data-is-running=\"\"");
        builder.AppendLine($"{indent}  data-cap-reached=\"\"");
        builder.AppendLine($"{indent}  data-iteration-count=\"\"");
        builder.AppendLine($"{indent}  data-current-stage=\"\"");
        builder.AppendLine($"{indent}  data-failure-stage=\"\"");
        builder.AppendLine($"{indent}  data-games-simulated=\"\"");
        builder.AppendLine($"{indent}  data-max-games-per-run=\"\"");
        builder.AppendLine($"{indent}  data-learning-player-status=\"\"");
        builder.AppendLine($"{indent}  data-turn-index=\"\"");
        builder.AppendLine($"{indent}  data-frame-count=\"\"");
        builder.AppendLine($"{indent}  data-is-complete=\"\"");
        builder.AppendLine($"{indent}  data-winner-seat=\"\"");
        builder.AppendLine($"{indent}  data-last-promotion-outcome=\"\">");
        builder.AppendLine($"{indent}  <p id=\"loop-running-status\" class=\"loop-running-status\" data-is-running=\"\" data-cap-reached=\"\">");
        builder.AppendLine($"{indent}    <span class=\"status-label\">Loop</span>");
        builder.AppendLine($"{indent}    <span class=\"status-value\" id=\"loop-running-value\">{Placeholder}</span>");
        builder.AppendLine($"{indent}  </p>");
        builder.AppendLine($"{indent}  <p id=\"loop-iteration\" class=\"loop-iteration\" data-iteration-count=\"\">");
        builder.AppendLine($"{indent}    <span class=\"status-label\">Iteration</span>");
        builder.AppendLine($"{indent}    <span class=\"status-value\" id=\"loop-iteration-value\">{Placeholder}</span>");
        builder.AppendLine($"{indent}  </p>");
        builder.AppendLine($"{indent}  <p id=\"loop-stage\" class=\"loop-stage\" data-current-stage=\"\" data-failure-stage=\"\">");
        builder.AppendLine($"{indent}    <span class=\"status-label\">Stage</span>");
        builder.AppendLine($"{indent}    <span class=\"status-value\" id=\"loop-stage-value\">{Placeholder}</span>");
        builder.AppendLine($"{indent}  </p>");
        builder.AppendLine($"{indent}  <p id=\"loop-game-budget\" class=\"loop-game-budget\" data-games-simulated=\"\" data-max-games-per-run=\"\">");
        builder.AppendLine($"{indent}    <span class=\"status-label\">Game budget</span>");
        builder.AppendLine($"{indent}    <span class=\"status-value\" id=\"loop-game-budget-value\">{Placeholder}</span>");
        builder.AppendLine($"{indent}  </p>");
        builder.AppendLine($"{indent}  <p id=\"loop-match-turns\" class=\"loop-match-turns\" data-turn-index=\"\" data-frame-count=\"\">");
        builder.AppendLine($"{indent}    <span class=\"status-label\">Match turns</span>");
        builder.AppendLine($"{indent}    <span class=\"status-value\" id=\"loop-match-turns-value\">{Placeholder}</span>");
        builder.AppendLine($"{indent}  </p>");
        builder.AppendLine($"{indent}  <p id=\"loop-match-complete\" class=\"loop-match-complete hidden\" data-is-complete=\"\" data-winner-seat=\"\">");
        builder.AppendLine($"{indent}    <span class=\"status-label\">Match complete</span>");
        builder.AppendLine($"{indent}    <span class=\"status-value\" id=\"loop-match-complete-value\">{Placeholder}</span>");
        builder.AppendLine($"{indent}  </p>");
        builder.AppendLine($"{indent}  <div id=\"loop-learning-player\" class=\"loop-learning-player hidden\"");
        builder.AppendLine($"{indent}    data-seat=\"\"");
        builder.AppendLine($"{indent}    data-active-strategy-id=\"\"");
        builder.AppendLine($"{indent}    data-candidate-strategy-id=\"\"");
        builder.AppendLine($"{indent}    data-wins=\"\"");
        builder.AppendLine($"{indent}    data-losses=\"\"");
        builder.AppendLine($"{indent}    data-score-differential=\"\">");
        builder.AppendLine($"{indent}    <span class=\"status-label\">Learning player</span>");
        builder.AppendLine($"{indent}    <span class=\"status-value\" id=\"loop-learning-player-value\">{Placeholder}</span>");
        builder.AppendLine($"{indent}  </div>");
        builder.AppendLine($"{indent}  <p id=\"loop-ponder-pending\" class=\"loop-ponder-pending hidden\" data-learning-player-status=\"\">");
        builder.AppendLine($"{indent}    <span class=\"status-label\">Ponder</span>");
        builder.AppendLine($"{indent}    <span class=\"status-value\" id=\"loop-ponder-pending-value\">Strategy pending</span>");
        builder.AppendLine($"{indent}  </p>");
        builder.AppendLine($"{indent}  <p id=\"loop-learning-player-error\" class=\"loop-learning-player-error status-error hidden\" data-learning-player-error=\"\">");
        builder.AppendLine($"{indent}    <span class=\"status-label\">Learning player error</span>");
        builder.AppendLine($"{indent}    <span class=\"status-value\" id=\"loop-learning-player-error-value\">{Placeholder}</span>");
        builder.AppendLine($"{indent}  </p>");
        builder.AppendLine($"{indent}  <p id=\"loop-last-iteration-error\" class=\"loop-last-iteration-error status-error hidden\" data-failure-stage=\"\">");
        builder.AppendLine($"{indent}    <span class=\"status-label\">Last iteration error</span>");
        builder.AppendLine($"{indent}    <span class=\"status-value\" id=\"loop-last-iteration-error-value\">{Placeholder}</span>");
        builder.AppendLine($"{indent}  </p>");
        builder.AppendLine($"{indent}  <p id=\"loop-last-promotion\" class=\"loop-last-promotion hidden\" data-last-promotion-outcome=\"\">");
        builder.AppendLine($"{indent}    <span class=\"status-label\">Last promotion</span>");
        builder.AppendLine($"{indent}    <span class=\"status-value\" id=\"loop-last-promotion-value\">{Placeholder}</span>");
        builder.AppendLine($"{indent}  </p>");
        builder.AppendLine($"{indent}  <p id=\"loop-library-best\" class=\"loop-library-best hidden\" data-library-strategy-id=\"\" data-library-effectiveness=\"\">");
        builder.AppendLine($"{indent}    <span class=\"status-label\">Library best</span>");
        builder.AppendLine($"{indent}    <span class=\"status-value\" id=\"loop-library-best-value\">{Placeholder}</span>");
        builder.AppendLine($"{indent}  </p>");
        builder.AppendLine($"{indent}  <p id=\"loop-model-provider\" class=\"loop-model-provider\" data-provider=\"\" data-model=\"\">");
        builder.AppendLine($"{indent}    <span class=\"status-label\">Model provider</span>");
        builder.AppendLine($"{indent}    <span class=\"status-value\" id=\"loop-model-provider-value\">{Placeholder}</span>");
        builder.AppendLine($"{indent}  </p>");
        builder.AppendLine($"{indent}</section>");
    }
}