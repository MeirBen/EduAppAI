using System.Globalization;
using System.Numerics;

namespace FamilyLearning.Evaluation;

/// <summary>Exact evaluation of bare calculation text, so recalculating a key needs neither floating point nor a reviewer.</summary>
/// <remarks>Accepts the whole-item form generated prompts and numeric keys use: signed decimals, + − × · : ÷ /, parentheses
/// and an optional trailing "=" with a blank or question mark. Any other text, including a missing-number blank, is not a
/// calculation.</remarks>
internal static class ExactArithmetic
{
    /// <summary>A value in lowest terms with a positive denominator, so equal values are structurally equal.</summary>
    internal readonly record struct Rational(BigInteger Numerator, BigInteger Denominator)
    {
        internal static Rational Create(BigInteger numerator, BigInteger denominator)
        {
            if (denominator.Sign < 0) (numerator, denominator) = (-numerator, -denominator);
            var divisor = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
            return divisor.IsOne ? new(numerator, denominator) : new(numerator / divisor, denominator / divisor);
        }

        public static Rational operator +(Rational a, Rational b) => Create(a.Numerator * b.Denominator + b.Numerator * a.Denominator, a.Denominator * b.Denominator);
        public static Rational operator -(Rational a, Rational b) => Create(a.Numerator * b.Denominator - b.Numerator * a.Denominator, a.Denominator * b.Denominator);
        public static Rational operator *(Rational a, Rational b) => Create(a.Numerator * b.Numerator, a.Denominator * b.Denominator);
    }

    internal static bool TryEvaluate(string? text, out Rational value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parser = new Parser(text);
        if (!parser.TryExpression(out value)) return false;
        parser.SkipTrailingEquals();
        return parser.AtEnd;
    }

    private sealed class Parser(string text)
    {
        private int position;

        internal bool AtEnd { get { SkipSpace(); return position == text.Length; } }

        internal bool TryExpression(out Rational value)
        {
            if (!TryTerm(out value)) return false;
            while (true)
            {
                SkipSpace();
                if (!TryOperator("+-−", out var symbol)) return true;
                if (!TryTerm(out var right)) return false;
                value = symbol == '+' ? value + right : value - right;
            }
        }

        private bool TryTerm(out Rational value)
        {
            if (!TryFactor(out value)) return false;
            while (true)
            {
                SkipSpace();
                if (!TryOperator("×*·⋅:÷/", out var symbol)) return true;
                if (!TryFactor(out var right)) return false;
                if (symbol is '×' or '*' or '·' or '⋅') value *= right;
                else if (right.Numerator.IsZero) return false;
                else value *= Rational.Create(right.Denominator, right.Numerator);
            }
        }

        private bool TryFactor(out Rational value)
        {
            SkipSpace();
            value = default;
            if (position == text.Length) return false;
            if (text[position] is '+' or '-' or '−')
            {
                var negative = text[position++] != '+';
                if (!TryFactor(out value)) return false;
                if (negative) value = Rational.Create(-value.Numerator, value.Denominator);
                return true;
            }
            if (text[position] == '(')
            {
                position++;
                if (!TryExpression(out value)) return false;
                SkipSpace();
                if (position == text.Length || text[position] != ')') return false;
                position++;
                return true;
            }
            return TryNumber(out value);
        }

        private bool TryNumber(out Rational value)
        {
            value = default;
            var start = position;
            while (position < text.Length && char.IsAsciiDigit(text[position])) position++;
            var integerDigits = position - start;
            var fractionDigits = 0;
            if (position < text.Length && text[position] == '.')
            {
                var point = position++;
                while (position < text.Length && char.IsAsciiDigit(text[position])) position++;
                fractionDigits = position - point - 1;
                if (fractionDigits == 0) return false;
            }
            if (integerDigits == 0) return false;
            var digits = text.AsSpan(start, position - start).ToString().Replace(".", "");
            value = Rational.Create(BigInteger.Parse(digits, CultureInfo.InvariantCulture), BigInteger.Pow(10, fractionDigits));
            return true;
        }

        private bool TryOperator(string symbols, out char symbol)
        {
            symbol = position < text.Length ? text[position] : '\0';
            if (!symbols.Contains(symbol)) return false;
            position++;
            return true;
        }

        internal void SkipTrailingEquals()
        {
            SkipSpace();
            if (position == text.Length || text[position] != '=') return;
            position++;
            SkipSpace();
            while (position < text.Length && text[position] is '?' or '_') position++;
        }

        private void SkipSpace()
        {
            while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
        }
    }
}
