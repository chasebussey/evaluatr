using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace Evaluatr.Core.Graders;

internal static class RubricScoring
{
    private const decimal DecimalUnit = 10000000000000000000000000000m;

    public static bool TryReadScore(JsonElement element, out decimal score)
    {
        score = 0;
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetDecimal(out score) || score is < 0 or > 1)
        {
            return false;
        }

        // Check the original number's range too: decimal parsing can round tiny
        // negatives to zero or a number just above one down to one.
        var raw = element.GetRawText();
        var exponentIndex = raw.IndexOfAny(['e', 'E']);
        var mantissa = exponentIndex < 0 ? raw : raw[..exponentIndex];
        var exponent = 0;
        if (exponentIndex >= 0 && !int.TryParse(raw.AsSpan(exponentIndex + 1), NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out exponent))
        {
            return false;
        }

        var negative = mantissa[0] == '-';
        if (negative) mantissa = mantissa[1..];
        var dot = mantissa.IndexOf('.');
        var fractionDigits = dot < 0 ? 0 : mantissa.Length - dot - 1;
        var significantDigits = mantissa.Replace(".", "").TrimStart('0');
        if (significantDigits.Length == 0) return true;
        if (negative) return false;

        var magnitude = (long)significantDigits.Length + exponent - fractionDigits;
        return magnitude < 1 || magnitude == 1 && significantDigits[0] == '1'
            && significantDigits.AsSpan(1).IndexOfAnyExcept('0') < 0;
    }

    public static decimal WeightedMean(IReadOnlyList<RubricCriterionResult> criteria)
    {
        var totalWeight = BigInteger.Zero;
        var weightedTotal = BigInteger.Zero;
        foreach (var criterion in criteria)
        {
            var weight = ToUnits(criterion.Weight);
            totalWeight += weight;
            weightedTotal += weight * ToUnits(criterion.Score);
        }

        // Decimal products can underflow for tiny weights, or overflow for large
        // totals. Accumulate exact fixed-point integers and round only once, to
        // the 28 decimal places representable by a normalized decimal score.
        var units = BigInteger.DivRem(weightedTotal, totalWeight, out var remainder);
        var halfway = (remainder * 2).CompareTo(totalWeight);
        if (halfway > 0 || halfway == 0 && !units.IsEven)
        {
            units++;
        }

        return (decimal)units / DecimalUnit;
    }

    private static BigInteger ToUnits(decimal value)
    {
        var bits = decimal.GetBits(value);
        var coefficient = ((BigInteger)(uint)bits[2] << 64)
            | ((BigInteger)(uint)bits[1] << 32)
            | (uint)bits[0];
        var scale = (bits[3] >> 16) & 0xff;

        return coefficient * BigInteger.Pow(10, 28 - scale);
    }
}
