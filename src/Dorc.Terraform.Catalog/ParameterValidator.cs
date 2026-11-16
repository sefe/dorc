using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace Dorc.Terraform.Catalog
{
    public sealed class ParameterValidator : IParameterValidator
    {
        // Matches the convention used by Dorc.ApiModel.RequestPropertyRedaction
        // and Dorc.TerraformRunner.Logging.SensitivePropertyRedactor (this
        // assembly references neither, so the marker is duplicated by value).
        private const string RedactedValue = "[REDACTED]";

        // Upper bound on a single manifest-pattern match against a
        // user-supplied value; guards the request thread against ReDoS.
        private static readonly TimeSpan RegexMatchTimeout = TimeSpan.FromSeconds(1);
        private static readonly Regex NumberSyntax = new(
            @"\A-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?\z",
            RegexOptions.CultureInvariant, RegexMatchTimeout);

        // Decimal.TryParse can succeed after rounding/underflow. Reject those
        // values so normalizing a property cannot change the Terraform input.
        public static bool TryParseNumberExact(string raw, out decimal value)
        {
            value = default;
            try
            {
                return NumberSyntax.IsMatch(raw)
                    && decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                    && NumberIdentity(raw) == NumberIdentity(value.ToString(CultureInfo.InvariantCulture));
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }

        private static (bool Negative, string Digits, BigInteger Exponent) NumberIdentity(string raw)
        {
            var parts = raw.TrimStart('-').Split(new[] { 'e', 'E' }, 2);
            var exponent = parts.Length == 2
                ? BigInteger.Parse(parts[1], CultureInfo.InvariantCulture) : BigInteger.Zero;
            var dot = parts[0].IndexOf('.');
            if (dot >= 0) exponent -= parts[0].Length - dot - 1;
            var digits = parts[0].Replace(".", "").TrimStart('0');
            if (digits.Length == 0) return (false, "0", BigInteger.Zero);
            var significant = digits.TrimEnd('0');
            exponent += digits.Length - significant.Length;
            return (raw.StartsWith('-'), significant, exponent);
        }

        public ParameterValidationResult Validate(
            TerraformTemplateManifest manifest,
            IReadOnlyDictionary<string, string?> suppliedValues)
        {
            if (manifest is null) throw new ArgumentNullException(nameof(manifest));
            if (suppliedValues is null) throw new ArgumentNullException(nameof(suppliedValues));

            var errors = new List<ParameterValidationError>();

            ValidateRequired(manifest, suppliedValues, errors);
            ValidateValues(manifest, suppliedValues, errors);
            ValidateUnknown(manifest, suppliedValues, errors);
            ValidateConflicts(manifest, suppliedValues, errors);

            return new ParameterValidationResult(errors.Count == 0, errors);
        }

        private static void ValidateConflicts(
            TerraformTemplateManifest manifest,
            IReadOnlyDictionary<string, string?> supplied,
            List<ParameterValidationError> errors)
        {
            string? EffectiveValue(string name) =>
                supplied.TryGetValue(name, out var value)
                    ? value : manifest.Parameters.First(p => p.Name == name).Default;
            foreach (var conflict in manifest.ParameterConflicts ?? Array.Empty<TerraformParameterConflict>())
            {
                if (EffectiveValue(conflict.Parameter) == conflict.Value
                    && EffectiveValue(conflict.WhenParameter) == conflict.WhenValue)
                    errors.Add(new ParameterValidationError(
                        conflict.Parameter, ParameterValidationErrorKind.NotAllowed, conflict.Message));
            }
        }

        private static void ValidateRequired(
            TerraformTemplateManifest manifest,
            IReadOnlyDictionary<string, string?> supplied,
            List<ParameterValidationError> errors)
        {
            // Iterate only over required parameters that the caller failed to
            // supply (or supplied as empty). The two conditions live in the
            // sequence expression so the loop body is purely the side effect
            // of recording the error.
            var missing = manifest.Parameters
                .Where(p => p.Required)
                .Where(p => !supplied.TryGetValue(p.Name, out var v) || string.IsNullOrEmpty(v));
            foreach (var p in missing)
            {
                errors.Add(new ParameterValidationError(
                    p.Name,
                    ParameterValidationErrorKind.Missing,
                    $"required parameter '{p.Name}' was not supplied"));
            }
        }

        private static void ValidateValues(
            TerraformTemplateManifest manifest,
            IReadOnlyDictionary<string, string?> supplied,
            List<ParameterValidationError> errors)
        {
            foreach (var p in manifest.Parameters)
            {
                if (!supplied.TryGetValue(p.Name, out var suppliedValue)) continue;
                // Required blanks are already reported as Missing. Optional
                // strings retain intentional empty values, but a present blank
                // scalar cannot fall back to a default in the runner.
                if (string.IsNullOrEmpty(suppliedValue)
                    && (p.Required || p.Type is not (TerraformParameterType.Number or TerraformParameterType.Bool)))
                    continue;
                var raw = suppliedValue ?? string.Empty;

                // Validation errors travel back in HTTP 400 bodies and are
                // rendered in the deploy wizard, so a Sensitive parameter's
                // value must never be interpolated into a message.
                var displayValue = p.Sensitive ? RedactedValue : $"'{raw}'";

                if (!TryCoerceType(raw, p.Type))
                {
                    errors.Add(new ParameterValidationError(
                        p.Name,
                        ParameterValidationErrorKind.TypeMismatch,
                        $"parameter '{p.Name}' value {displayValue} is not a valid {p.Type}"));
                    continue;
                }
                if (p.Integer && decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var integer)
                    && integer != decimal.Truncate(integer))
                {
                    errors.Add(new ParameterValidationError(p.Name, ParameterValidationErrorKind.TypeMismatch,
                        $"parameter '{p.Name}' must be an integer"));
                    continue;
                }

                if (p.AllowedValues is { Count: > 0 } && !p.AllowedValues.Contains(raw))
                {
                    errors.Add(new ParameterValidationError(
                        p.Name,
                        ParameterValidationErrorKind.NotAllowed,
                        $"parameter '{p.Name}' value {displayValue} is not in the allowed-values list"));
                    continue;
                }

                var patternError = ValidatePattern(p, raw, displayValue);
                if (patternError is not null)
                {
                    errors.Add(patternError);
                    continue;
                }

                if ((p.Min is not null || p.Max is not null)
                    && p.Type == TerraformParameterType.Number
                    && decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
                {
                    if (p.Min is not null && n < p.Min)
                    {
                        errors.Add(new ParameterValidationError(
                            p.Name,
                            ParameterValidationErrorKind.OutOfRange,
                            $"parameter '{p.Name}' value {(p.Sensitive ? RedactedValue : $"'{n}'")} is below minimum {p.Min}"));
                    }
                    if (p.Max is not null && n > p.Max)
                    {
                        errors.Add(new ParameterValidationError(
                            p.Name,
                            ParameterValidationErrorKind.OutOfRange,
                            $"parameter '{p.Name}' value {(p.Sensitive ? RedactedValue : $"'{n}'")} is above maximum {p.Max}"));
                    }
                }
            }
        }

        private static ParameterValidationError? ValidatePattern(
            TerraformTemplateParameter parameter, string raw, string displayValue)
        {
            if (string.IsNullOrEmpty(parameter.Pattern)) return null;
            string message;
            try
            {
                var regex = new Regex(parameter.Pattern, RegexOptions.CultureInvariant, RegexMatchTimeout);
                if (regex.IsMatch(raw)) return null;
                message = $"parameter '{parameter.Name}' value {displayValue} does not match required pattern";
            }
            catch (ArgumentException)
            {
                message = $"parameter '{parameter.Name}' has an invalid regex pattern in the manifest";
            }
            catch (RegexMatchTimeoutException)
            {
                message = $"parameter '{parameter.Name}' could not be validated against its pattern within the time limit";
            }
            return new ParameterValidationError(
                parameter.Name, ParameterValidationErrorKind.PatternMismatch, message);
        }

        private static void ValidateUnknown(
            TerraformTemplateManifest manifest,
            IReadOnlyDictionary<string, string?> supplied,
            List<ParameterValidationError> errors)
        {
            var declared = new HashSet<string>(manifest.Parameters.Select(p => p.Name), StringComparer.Ordinal);
            foreach (var name in supplied.Keys.Where(n => !declared.Contains(n)))
            {
                errors.Add(new ParameterValidationError(
                    name,
                    ParameterValidationErrorKind.UnknownParameter,
                    $"parameter '{name}' is not declared by manifest '{manifest.Name}@{manifest.Version}'"));
            }
        }

        private static bool TryCoerceType(string raw, TerraformParameterType type)
        {
            switch (type)
            {
                case TerraformParameterType.String:
                    return true;
                case TerraformParameterType.Number:
                    return TryParseNumberExact(raw, out _);
                case TerraformParameterType.Bool:
                    return raw is "true" or "false";
                case TerraformParameterType.List:
                case TerraformParameterType.Map:
                case TerraformParameterType.Object:
                    // For complex types we accept any non-empty string at validation
                    // time and defer structural correctness to terraform itself.
                    return !string.IsNullOrEmpty(raw);
                default:
                    return false;
            }
        }
    }
}
