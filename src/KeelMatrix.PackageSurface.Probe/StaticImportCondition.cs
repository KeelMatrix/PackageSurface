using System.Text.RegularExpressions;
using static KeelMatrix.PackageSurface.Probe.AnalysisLimits;

namespace KeelMatrix.PackageSurface.Probe;

internal static class StaticImportCondition
{
    internal static readonly Regex PropertyComparison = new(
        "^\\s*['\\\"]?\\$\\(\\s*(?<property>[A-Za-z_][A-Za-z0-9_.-]*)\\s*\\)['\\\"]?\\s*(?<operator>==|!=)\\s*['\\\"](?<value>[^'\\\"]*)['\\\"]\\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static readonly Regex ExistsCondition = new(
        "^\\s*Exists\\s*\\(\\s*['\\\"](?<path>[^'\\\"]*)['\\\"]\\s*\\)\\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static ConditionNode? CombineConditionNodes(ConditionNode? left, ConditionNode? right) =>
        left is null ? right : right is null ? left : new AndCondition(left, right);

    internal static ImportApplicability DetermineApplicability(
        IReadOnlyList<ConditionClause> conditions,
        Func<string, ParsedCondition> evaluateExists,
        out ConditionFailure? failure)
    {
        failure = null;
        ConditionNode? combined = null;
        foreach (var condition in conditions)
        {
            var parsed = ParseCondition(condition.Expression!, evaluateExists);
            if (!parsed.IsKnown)
            {
                failure = new ConditionFailure(condition.Owner, parsed.Reason!);
                return new ImportApplicability(null, parsed.Reason);
            }

            combined = combined is null ? parsed.Node : new AndCondition(combined, parsed.Node!);
        }

        return ImportApplicability.Known(combined);
    }

    internal static ParsedCondition ParseCondition(
        string expression,
        Func<string, ParsedCondition> evaluateExists)
    {
        if (!IsConditionWithinLimits(expression))
        {
            return ParsedCondition.Unknown("the condition exceeds the supported resource limits");
        }

        var normalized = StripOuterParentheses(expression.Trim());
        if (normalized.Length == 0)
        {
            return ParsedCondition.Unknown("the condition is empty");
        }

        if (TrySplitTopLevel(normalized, "OR", out var disjunction))
        {
            return CombineConditions(disjunction, evaluateExists, static (left, right) => new OrCondition(left, right));
        }

        if (TrySplitTopLevel(normalized, "AND", out var conjunction))
        {
            return CombineConditions(conjunction, evaluateExists, static (left, right) => new AndCondition(left, right));
        }

        var comparison = PropertyComparison.Match(normalized);
        if (comparison.Success)
        {
            var property = comparison.Groups["property"].Value;
            var @operator = comparison.Groups["operator"].Value;
            var value = comparison.Groups["value"].Value;
            if (property.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase))
            {
                if (value.Contains("$(", StringComparison.Ordinal) &&
                    !IsTargetFrameworkPropertyReference(value))
                {
                    return ParsedCondition.Unknown("the condition contains an unsupported property expansion");
                }

                return ParsedCondition.Known(new PropertyComparisonCondition(property, @operator, value));
            }

            if (property.Equals("ExcludeRestorePackageImports", StringComparison.OrdinalIgnoreCase) &&
                @operator == "!=" && value.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                return ParsedCondition.Known(new ConstantCondition(true));
            }

            return ParsedCondition.Unknown("the condition references an unsupported property");
        }

        var exists = ExistsCondition.Match(normalized);
        if (exists.Success)
        {
            return evaluateExists(exists.Groups["path"].Value);
        }

        return ParsedCondition.Unknown("the expression is outside the supported condition grammar");
    }

    internal static ParsedCondition CombineConditions(
        IReadOnlyList<string> expressions,
        Func<string, ParsedCondition> evaluateExists,
        Func<ConditionNode, ConditionNode, ConditionNode> combine)
    {
        ConditionNode? combined = null;
        foreach (var expression in expressions)
        {
            var parsed = ParseCondition(expression, evaluateExists);
            if (!parsed.IsKnown)
            {
                return ParsedCondition.Unknown($"a subexpression is unproven: {parsed.Reason}");
            }

            combined = combined is null ? parsed.Node : combine(combined, parsed.Node!);
        }

        return ParsedCondition.Known(combined!);
    }

    internal static bool IsConditionWithinLimits(string expression)
    {
        if (expression.Length > MaxConditionLength) return false;
        var depth = 0;
        var tokens = 0;
        var quote = '\0';
        foreach (var character in expression)
        {
            if (quote != '\0')
            {
                if (character == quote) quote = '\0';
                continue;
            }

            if (character is '\'' or '"')
            {
                quote = character;
                continue;
            }

            if (character == '(')
            {
                if (++depth > MaxConditionDepth) return false;
                tokens++;
            }
            else if (character == ')')
            {
                if (--depth < 0) return false;
                tokens++;
            }
            else if (char.IsLetterOrDigit(character) || character is '_' or '=' or '!')
            {
                tokens++;
                if (tokens > MaxConditionTokens) return false;
            }
        }

        return quote == '\0' && depth == 0 && tokens <= MaxConditionTokens;
    }

    internal static string StripOuterParentheses(string expression)
    {
        while (expression.Length >= 2 && expression[0] == '(' && expression[^1] == ')' && HasSingleOuterPair(expression))
        {
            expression = expression[1..^1].Trim();
        }

        return expression;
    }

    internal static bool HasSingleOuterPair(string expression)
    {
        var depth = 0;
        var quote = '\0';
        for (var index = 0; index < expression.Length; index++)
        {
            var character = expression[index];
            if (quote != '\0')
            {
                if (character == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (character is '\'' or '\"')
            {
                quote = character;
            }
            else if (character == '(')
            {
                depth++;
            }
            else if (character == ')' && --depth == 0 && index != expression.Length - 1)
            {
                return false;
            }
        }

        return depth == 0 && quote == '\0';
    }

    internal static bool TrySplitTopLevel(string expression, string operatorText, out string[] parts)
    {
        var matches = new List<string>();
        var start = 0;
        var depth = 0;
        var quote = '\0';
        for (var index = 0; index < expression.Length; index++)
        {
            var character = expression[index];
            if (quote != '\0')
            {
                if (character == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (character is '\'' or '\"')
            {
                quote = character;
                continue;
            }

            if (character == '(')
            {
                depth++;
                continue;
            }

            if (character == ')')
            {
                depth--;
                continue;
            }

            if (depth != 0 || index + operatorText.Length > expression.Length ||
                !expression.AsSpan(index, operatorText.Length).Equals(operatorText.AsSpan(), StringComparison.OrdinalIgnoreCase) ||
                !IsConditionBoundary(expression, index - 1) ||
                !IsConditionBoundary(expression, index + operatorText.Length))
            {
                continue;
            }

            matches.Add(expression[start..index].Trim());
            start = index + operatorText.Length;
            index += operatorText.Length - 1;
        }

        if (matches.Count == 0)
        {
            parts = Array.Empty<string>();
            return false;
        }

        matches.Add(expression[start..].Trim());
        parts = matches.ToArray();
        return true;
    }

    internal static bool IsConditionBoundary(string expression, int index) =>
        index < 0 || index >= expression.Length || !char.IsLetterOrDigit(expression[index]) && expression[index] != '_';

    internal static bool IsTargetFrameworkPropertyReference(string value)
    {
        var normalized = value.Trim();
        return normalized.StartsWith("$(", StringComparison.Ordinal) &&
            normalized.EndsWith(')') &&
            normalized[2..^1].Trim().Equals("TargetFramework", StringComparison.OrdinalIgnoreCase);
    }

    internal sealed record ConditionClause(string Owner, string? Expression);

    internal sealed record ConditionFailure(string Owner, string Message);

    internal sealed record ParsedCondition(ConditionNode? Node, string? Reason)
    {
        public bool IsKnown => Reason is null;

        public static ParsedCondition Known(ConditionNode node) => new(node, null);

        public static ParsedCondition Unknown(string reason) => new(null, reason);
    }

    internal abstract record ConditionNode
    {
        public abstract bool Evaluate(SurfaceContextKind context, string targetFramework);
    }

    internal sealed record ConstantCondition(bool Value) : ConditionNode
    {
        public override bool Evaluate(SurfaceContextKind context, string targetFramework) => Value;
    }

    internal sealed record PropertyComparisonCondition(string Property, string Operator, string Value) : ConditionNode
    {
        public override bool Evaluate(SurfaceContextKind context, string targetFramework)
        {
            var actual = context == SurfaceContextKind.Project ? string.Empty : targetFramework;
            var expected = Property.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase) &&
                IsTargetFrameworkPropertyReference(Value)
                ? actual
                : Value;
            var equal = string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
            return Operator == "==" ? equal : !equal;
        }
    }

    internal sealed record AndCondition(ConditionNode Left, ConditionNode Right) : ConditionNode
    {
        public override bool Evaluate(SurfaceContextKind context, string targetFramework) =>
            Left.Evaluate(context, targetFramework) && Right.Evaluate(context, targetFramework);
    }

    internal sealed record OrCondition(ConditionNode Left, ConditionNode Right) : ConditionNode
    {
        public override bool Evaluate(SurfaceContextKind context, string targetFramework) =>
            Left.Evaluate(context, targetFramework) || Right.Evaluate(context, targetFramework);
    }

    internal sealed record ImportApplicability(ConditionNode? Condition, string? Failure)
    {
        public bool IsKnown => Failure is null;

        public static ImportApplicability Known(ConditionNode? condition) => new(condition, null);

        public bool AppliesTo(SurfaceContextKind context, string targetFramework) =>
            IsKnown && (Condition?.Evaluate(context, targetFramework) ?? true);
    }
}
