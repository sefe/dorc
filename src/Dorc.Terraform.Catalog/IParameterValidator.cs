namespace Dorc.Terraform.Catalog
{
    public interface IParameterValidator
    {
        ParameterValidationResult Validate(
            TerraformTemplateManifest manifest,
            IReadOnlyDictionary<string, string?> suppliedValues);

        ParameterValidationResult Validate(
            TerraformTemplateManifest manifest,
            IReadOnlyDictionary<string, string?> suppliedValues,
            bool isEphemeral,
            bool isProd = false);

        IReadOnlyDictionary<string, string?> EnforceEphemeralInvariants(
            TerraformTemplateManifest manifest,
            IReadOnlyDictionary<string, string?> suppliedValues,
            bool isEphemeral);
    }

    public sealed record ParameterValidationResult(
        bool IsValid,
        IReadOnlyList<ParameterValidationError> Errors);

    public sealed record ParameterValidationError(
        string ParameterName,
        ParameterValidationErrorKind Kind,
        string Message);

    public enum ParameterValidationErrorKind
    {
        Missing,
        TypeMismatch,
        NotAllowed,
        PatternMismatch,
        OutOfRange,
        UnknownParameter,
    }
}
