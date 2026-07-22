namespace Dorc.ApiModel
{
    /// <summary>
    /// Single backend source for the tag-string capacity (docs/tag-capacity-expansion,
    /// HLPS §8 U-1). The SSDT column widths and the TypeScript mirror
    /// (dorc-web/src/helpers/tag-limits.ts) must match this value; a web test asserts
    /// the spec's maxLength against the TS constant.
    /// </summary>
    public static class TagLimits
    {
        public const int MaxTagStringLength = 4000;
    }
}
