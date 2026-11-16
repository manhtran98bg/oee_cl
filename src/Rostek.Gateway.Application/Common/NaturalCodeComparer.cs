namespace Rostek.Gateway.Application.Common;

public sealed class NaturalCodeComparer : IComparer<string>
{
    public static NaturalCodeComparer Instance { get; } = new();

    private NaturalCodeComparer()
    {
    }

    public int Compare(string? left, string? right)
    {
        if (ReferenceEquals(left, right)) return 0;
        if (left is null) return -1;
        if (right is null) return 1;

        var leftIndex = 0;
        var rightIndex = 0;
        while (leftIndex < left.Length && rightIndex < right.Length)
        {
            if (char.IsDigit(left[leftIndex]) && char.IsDigit(right[rightIndex]))
            {
                var numberComparison = CompareNumber(
                    left,
                    ref leftIndex,
                    right,
                    ref rightIndex);
                if (numberComparison != 0)
                {
                    return numberComparison;
                }

                continue;
            }

            var characterComparison = char.ToUpperInvariant(left[leftIndex])
                .CompareTo(char.ToUpperInvariant(right[rightIndex]));
            if (characterComparison != 0)
            {
                return characterComparison;
            }

            leftIndex++;
            rightIndex++;
        }

        var remainingComparison = (left.Length - leftIndex).CompareTo(right.Length - rightIndex);
        return remainingComparison != 0
            ? remainingComparison
            : StringComparer.OrdinalIgnoreCase.Compare(left, right);
    }

    private static int CompareNumber(
        string left,
        ref int leftIndex,
        string right,
        ref int rightIndex)
    {
        var leftEnd = FindNumberEnd(left, leftIndex);
        var rightEnd = FindNumberEnd(right, rightIndex);
        var leftSignificant = SkipLeadingZeros(left, leftIndex, leftEnd);
        var rightSignificant = SkipLeadingZeros(right, rightIndex, rightEnd);
        var leftLength = leftEnd - leftSignificant;
        var rightLength = rightEnd - rightSignificant;

        var lengthComparison = leftLength.CompareTo(rightLength);
        if (lengthComparison != 0)
        {
            return lengthComparison;
        }

        for (var offset = 0; offset < leftLength; offset++)
        {
            var digitComparison = left[leftSignificant + offset].CompareTo(right[rightSignificant + offset]);
            if (digitComparison != 0)
            {
                return digitComparison;
            }
        }

        leftIndex = leftEnd;
        rightIndex = rightEnd;
        return 0;
    }

    private static int FindNumberEnd(string value, int start)
    {
        var index = start;
        while (index < value.Length && char.IsDigit(value[index])) index++;
        return index;
    }

    private static int SkipLeadingZeros(string value, int start, int end)
    {
        var index = start;
        while (index < end - 1 && value[index] == '0') index++;
        return index;
    }
}
