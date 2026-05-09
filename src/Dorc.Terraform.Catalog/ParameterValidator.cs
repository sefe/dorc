using System.Globalization;
using System.Text.RegularExpressions;

namespace Dorc.Terraform.Catalog
{
    public sealed class ParameterValidator : IParameterValidator
    {
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
            foreach (var p in manifest.Parameters.Where(p => p.Required))
            {
                if (!supplied.TryGetValue(p.Name, out var v) || string.IsNullOrEmpty(v))
                {
                    errors.Add(new ParameterValidationError(
                        p.Name,
                        ParameterValidationErrorKind.Missing,
                        $"required parameter '{p.Name}' was not supplied"));
                }
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
                .Select(p => (Param: p, Raw: supplied.TryGetValue(p.Name, out var v) ? v : null))
                .Where(t => !string.IsNullOrEmpty(t.Raw));

            foreach (var (p, rawNullable) in pairs)
            {
                // The Where above guarantees Raw is non-null and non-empty;
                // bind to a non-nullable local so callees with non-null
                // string parameters do not fire CS8604.
                var raw = rawNullable!;

                if (!TryCoerceType(raw, p.Type))
                {
                    errors.Add(new ParameterValidationError(
                        p.Name,
                        ParameterValidationErrorKind.TypeMismatch,
                        $"parameter '{p.Name}' value '{raw}' is not a valid {p.Type}"));
                    continue;
                }

                if (p.AllowedValues is { Count: > 0 } && !p.AllowedValues.Contains(raw))
                {
                    errors.Add(new ParameterValidationError(
                        p.Name,
                        ParameterValidationErrorKind.NotAllowed,
                        $"parameter '{p.Name}' value '{raw}' is not in the allowed-values list"));
                    continue;
                }

                if (!string.IsNullOrEmpty(p.Pattern))
                {
                    Regex regex;
                    try { regex = new Regex(p.Pattern, RegexOptions.CultureInvariant); }
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
                    if (!regex.IsMatch(raw))
                    {
                        errors.Add(new ParameterValidationError(
                            p.Name,
                            ParameterValidationErrorKind.PatternMismatch,
                            $"parameter '{p.Name}' value '{raw}' does not match required pattern"));
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
                            $"parameter '{p.Name}' value '{n}' is below minimum {p.Min}"));
                    }
                    if (p.Max is not null && n > p.Max)
                    {
                        errors.Add(new ParameterValidationError(
                            p.Name,
                            ParameterValidationErrorKind.OutOfRange,
                            $"parameter '{p.Name}' value '{n}' is above maximum {p.Max}"));
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
