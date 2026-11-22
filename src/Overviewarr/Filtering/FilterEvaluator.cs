using System.Globalization;
using Overviewarr.Releases;
using Radzen;

namespace Overviewarr.Filtering;

/// <summary>
/// Evaluates the filter builder's conditions (DESIGN.md section 8). Text comparisons ignore case. A list property
/// contains a value when one of its values equals it, and doesn't contain it when none does. A condition without a
/// value, or a group without conditions, doesn't narrow the result.
/// </summary>
public static class FilterEvaluator
{
    public static IReadOnlyList<ReleaseRecord> Apply(IEnumerable<ReleaseRecord> releases,
        IEnumerable<CompositeFilterDescriptor>? filters, LogicalFilterOperator logicalOperator)
    {
        var conditions = filters?.ToList() ?? [];
        return conditions.Count == 0
            ? releases.ToList()
            : releases.Where(r => Matches(r, conditions, logicalOperator)).ToList();
    }

    public static bool Matches(ReleaseRecord release, IEnumerable<CompositeFilterDescriptor> filters, LogicalFilterOperator logicalOperator) =>
        Combine(filters.Select(f => Evaluate(release, f)), logicalOperator) ?? true;

    private static bool? Evaluate(ReleaseRecord release, CompositeFilterDescriptor filter)
    {
        if (string.IsNullOrEmpty(filter.Property))
        {
            return filter.Filters is null ? null : Combine(filter.Filters.Select(f => Evaluate(release, f)), filter.LogicalFilterOperator);
        }

        var field = FilterFields.Find(filter.Property);
        if (field is null || filter.FilterOperator is not { } op || IsEmpty(filter.FilterValue))
        {
            return null;
        }

        var value = field.Value(release);
        return field.Kind switch
        {
            FilterKind.Text => CompareText(value as string ?? string.Empty, Convert.ToString(filter.FilterValue, CultureInfo.InvariantCulture)!, op),
            FilterKind.Number => ToDouble(value) is double number && ToDouble(filter.FilterValue) is double target && CompareNumber(number, target, op),
            FilterKind.YesNo => ToBool(filter.FilterValue) is bool target && op switch
            {
                FilterOperator.Equals => (bool)value! == target,
                FilterOperator.NotEquals => (bool)value! != target,
                _ => false,
            },
            FilterKind.TextList => ContainsText((IEnumerable<string>)value!, Convert.ToString(filter.FilterValue, CultureInfo.InvariantCulture)!.Trim(), op),
            FilterKind.NumberList => ToDouble(filter.FilterValue) is double target && ContainsNumber((IEnumerable<int>)value!, target, op),
            _ => false,
        };
    }

    private static bool? Combine(IEnumerable<bool?> results, LogicalFilterOperator logicalOperator)
    {
        var applied = results.Where(r => r is not null).Select(r => r!.Value).ToList();
        if (applied.Count == 0)
        {
            return null;
        }

        return logicalOperator == LogicalFilterOperator.Or ? applied.Any(r => r) : applied.All(r => r);
    }

    private static bool CompareText(string value, string target, FilterOperator op) => op switch
    {
        FilterOperator.Equals => value.Equals(target, StringComparison.OrdinalIgnoreCase),
        FilterOperator.NotEquals => !value.Equals(target, StringComparison.OrdinalIgnoreCase),
        FilterOperator.Contains => value.Contains(target, StringComparison.OrdinalIgnoreCase),
        FilterOperator.DoesNotContain => !value.Contains(target, StringComparison.OrdinalIgnoreCase),
        FilterOperator.StartsWith => value.StartsWith(target, StringComparison.OrdinalIgnoreCase),
        FilterOperator.EndsWith => value.EndsWith(target, StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    private static bool CompareNumber(double value, double target, FilterOperator op) => op switch
    {
        FilterOperator.Equals => value == target,
        FilterOperator.NotEquals => value != target,
        FilterOperator.LessThan => value < target,
        FilterOperator.LessThanOrEquals => value <= target,
        FilterOperator.GreaterThan => value > target,
        FilterOperator.GreaterThanOrEquals => value >= target,
        _ => false,
    };

    private static bool ContainsText(IEnumerable<string> values, string target, FilterOperator op)
    {
        var contains = values.Any(v => v.Equals(target, StringComparison.OrdinalIgnoreCase));
        return op switch
        {
            FilterOperator.Contains => contains,
            FilterOperator.DoesNotContain => !contains,
            _ => false,
        };
    }

    private static bool ContainsNumber(IEnumerable<int> values, double target, FilterOperator op)
    {
        var contains = values.Any(v => v == target);
        return op switch
        {
            FilterOperator.Contains => contains,
            FilterOperator.DoesNotContain => !contains,
            _ => false,
        };
    }

    private static bool IsEmpty(object? value) => value is null || (value is string text && string.IsNullOrWhiteSpace(text));

    private static double? ToDouble(object? value) => value switch
    {
        null => null,
        string text => double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed
            : double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out parsed) ? parsed : null,
        IConvertible convertible => convertible.ToDouble(CultureInfo.InvariantCulture),
        _ => null,
    };

    private static bool? ToBool(object? value) => value switch
    {
        bool b => b,
        string text when bool.TryParse(text, out var parsed) => parsed,
        _ => null,
    };
}
