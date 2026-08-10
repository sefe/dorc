using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace Dorc.ApiModel
{
    /// <summary>
    /// Validates a tag set against what the database can actually store.
    ///
    /// Replaces the [StringLength] that guarded the delimited column: the limit is
    /// now per tag (deploy.DatabaseTag.Tag / deploy.ServerTag.Tag), not on a joined
    /// total, so a caller can send as many tags as it likes provided each one fits.
    /// A tag containing the delimiter is rejected too — it would round-trip as two
    /// tags through the deployment-variable payload, which is still delimited.
    /// </summary>
    public class TagSetAttribute : ValidationAttribute
    {
        protected override ValidationResult IsValid(object value, ValidationContext context)
        {
            var tags = value as IEnumerable<string>;
            if (tags == null)
                return ValidationResult.Success;

            var members = new[] { context.MemberName };

            var tooLong = tags.Where(t => t != null && t.Trim().Length > TagLimits.MaxTagLength).ToArray();
            if (tooLong.Length > 0)
            {
                var offender = tooLong[0].Trim();
                var excerpt = offender.Length > 20 ? offender.Substring(0, 20) + "..." : offender;
                return new ValidationResult(
                    $"Each tag must be at most {TagLimits.MaxTagLength} characters. Too long: '{excerpt}'.",
                    members);
            }

            if (tags.Any(t => t != null && t.Contains(TagString.Delimiter)))
                return new ValidationResult(
                    $"A tag must not contain '{TagString.Delimiter}'.", members);

            return ValidationResult.Success;
        }
    }
}
