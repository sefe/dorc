using System.Globalization;
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

            return new ParameterValidationResult(errors.Count == 0, errors);
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
            // Project (parameter, raw-value) pairs for parameters that the
            // caller actually supplied with a non-empty value. The filter
            // hoists the previous in-loop guard into the sequence expression.
            var pairs = manifest.Parameters
                .Select(p =>
                {
                    string? raw = null;
                    if (supplied.TryGetValue(p.Name, out var v)) raw = v;
                    return (Param: p, Raw: raw);
                })
                .Where(t => !string.IsNullOrEmpty(t.Raw));

            foreach (var (p, rawNullable) in pairs)
            {
                // The Where above guarantees Raw is non-null and non-empty;
                // bind to a non-nullable local so callees with non-null
                // string parameters do not fire CS8604.
                var raw = rawNullable!;

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

                if (p.AllowedValues is { Count: > 0 } && !p.AllowedValues.Contains(raw))
                {
                    errors.Add(new ParameterValidationError(
                        p.Name,
                        ParameterValidationErrorKind.NotAllowed,
                        $"parameter '{p.Name}' value {displayValue} is not in the allowed-values list"));
                    continue;
                }

                if (!string.IsNullOrEmpty(p.Pattern))
                {
                    Regex regex;
                    // Bound every match: a manifest pattern with catastrophic
                    // backtracking (e.g. `^(a+)+$`) run against an unbounded
                    // user-supplied value on the request thread is a ReDoS
                    // vector. A timeout turns a hang into a validation error.
                    try { regex = new Regex(p.Pattern, RegexOptions.CultureInvariant, RegexMatchTimeout); }
                    catch (ArgumentException)
                    {
                        // Bad regex in the manifest itself; surface as a TypeMismatch
                        // against the parameter so the manifest author sees it.
                        errors.Add(new ParameterValidationError(
                            p.Name,
                            ParameterValidationErrorKind.PatternMismatch,
                            $"parameter '{p.Name}' has an invalid regex pattern in the manifest"));
                        continue;
                    }
                    bool patternMatched;
                    try { patternMatched = regex.IsMatch(raw); }
                    catch (RegexMatchTimeoutException)
                    {
                        errors.Add(new ParameterValidationError(
                            p.Name,
                            ParameterValidationErrorKind.PatternMismatch,
                            $"parameter '{p.Name}' could not be validated against its pattern within the time limit"));
                        continue;
                    }
                    if (!patternMatched)
                    {
                        errors.Add(new ParameterValidationError(
                            p.Name,
                            ParameterValidationErrorKind.PatternMismatch,
                            $"parameter '{p.Name}' value {displayValue} does not match required pattern"));
                        continue;
                    }
                }

                if ((p.Min is not null || p.Max is not null)
                    && p.Type == TerraformParameterType.Number
                    && decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var n))
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
                    return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out _);
                case TerraformParameterType.Bool:
                    return bool.TryParse(raw, out _);
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
