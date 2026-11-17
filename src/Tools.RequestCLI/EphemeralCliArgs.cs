using System;
using System.Collections.Generic;

namespace Tools.RequestCLI
{
    public enum EphemeralCliVerb
    {
        Up,
        Down,
        ExtendTtl,
        BlueprintList,
        BlueprintExport,
        BlueprintApply
    }

    public sealed class EphemeralCliArgs
    {
        public EphemeralCliVerb Verb { get; }
        public string Project { get; private set; } = string.Empty;
        public string Template { get; private set; } = string.Empty;
        public string TargetEnv { get; private set; } = string.Empty;
        public string Tier { get; private set; } = string.Empty;
        public int? TtlMinutes { get; private set; }
        public string BaseEnv { get; private set; } = string.Empty;
        public string FilePath { get; private set; } = string.Empty;
        public Dictionary<string, string> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string Reason { get; private set; } = string.Empty;
        public bool Block { get; private set; } = true;
        public bool ForceDeleteMetadataOnNoComponents { get; private set; }
        public bool RunValidationComponents { get; private set; }
        public int TimeoutMinutes { get; set; } = 30;
        public string? BuildNum { get; set; }
        public string? BuildUrl { get; set; }
        public string? BuildText { get; set; }

        public static bool IsEphemeralCommand(string[]? args)
        {
            return args is { Length: > 0 }
                && string.Equals(args[0], "ephemeral", StringComparison.OrdinalIgnoreCase);
        }

        public EphemeralCliArgs(string[] args)
        {
            if (args is null || args.Length < 2 || !string.Equals(args[0], "ephemeral", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Expected 'ephemeral up', 'ephemeral down', 'ephemeral extend-ttl', or 'ephemeral blueprint <list|export|apply>' command.", nameof(args));
            }

            var optionStartIndex = 2;
            if (string.Equals(args[1], "up", StringComparison.OrdinalIgnoreCase))
            {
                Verb = EphemeralCliVerb.Up;
            }
            else if (string.Equals(args[1], "down", StringComparison.OrdinalIgnoreCase))
            {
                Verb = EphemeralCliVerb.Down;
            }
            else if (string.Equals(args[1], "extend-ttl", StringComparison.OrdinalIgnoreCase))
            {
                Verb = EphemeralCliVerb.ExtendTtl;
            }
            else if (string.Equals(args[1], "blueprint-list", StringComparison.OrdinalIgnoreCase))
            {
                Verb = EphemeralCliVerb.BlueprintList;
            }
            else if (string.Equals(args[1], "blueprint-export", StringComparison.OrdinalIgnoreCase))
            {
                Verb = EphemeralCliVerb.BlueprintExport;
            }
            else if (string.Equals(args[1], "blueprint-apply", StringComparison.OrdinalIgnoreCase))
            {
                Verb = EphemeralCliVerb.BlueprintApply;
            }
            else if (string.Equals(args[1], "blueprint", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length < 3)
                {
                    throw new ArgumentException("Expected sub-command for 'ephemeral blueprint': 'list', 'export', or 'apply'.", nameof(args));
                }

                optionStartIndex = 3;
                if (string.Equals(args[2], "list", StringComparison.OrdinalIgnoreCase))
                {
                    Verb = EphemeralCliVerb.BlueprintList;
                }
                else if (string.Equals(args[2], "export", StringComparison.OrdinalIgnoreCase))
                {
                    Verb = EphemeralCliVerb.BlueprintExport;
                }
                else if (string.Equals(args[2], "apply", StringComparison.OrdinalIgnoreCase))
                {
                    Verb = EphemeralCliVerb.BlueprintApply;
                }
                else
                {
                    throw new ArgumentException($"Unsupported blueprint sub-command '{args[2]}'. Expected 'list', 'export', or 'apply'.", nameof(args));
                }
            }
            else
            {
                throw new ArgumentException($"Unsupported ephemeral verb '{args[1]}'. Expected 'up', 'down', 'extend-ttl', or 'blueprint'.", nameof(args));
            }

            ParseOptions(args, optionStartIndex);
            Validate();
        }

        private void ParseOptions(string[] args, int startIndex)
        {
            for (int i = startIndex; i < args.Length; i++)
            {
                var token = args[i];
                if (string.Equals(token, "--no-wait", StringComparison.OrdinalIgnoreCase))
                {
                    Block = false;
                    continue;
                }

                if (string.Equals(token, "--wait", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(token, "--block", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length && bool.TryParse(args[i + 1], out var explicitBool))
                    {
                        Block = explicitBool;
                        i++;
                    }
                    else
                    {
                        Block = true;
                    }
                    continue;
                }

                if (string.Equals(token, "--force-delete", StringComparison.OrdinalIgnoreCase))
                {
                    ForceDeleteMetadataOnNoComponents = true;
                    continue;
                }

                if (string.Equals(token, "--run-validation", StringComparison.OrdinalIgnoreCase))
                {
                    RunValidationComponents = true;
                    continue;
                }

                var (key, value) = ExtractKeyAndValue(args, ref i);
                ApplyOption(key, value);
            }
        }

        private static (string Key, string Value) ExtractKeyAndValue(string[] args, ref int index)
        {
            var token = args[index];
            if (token.StartsWith("/", StringComparison.Ordinal))
            {
                var colonIdx = token.IndexOf(':');
                if (colonIdx <= 1)
                {
                    throw new ArgumentException($"Invalid option '{token}'. Expected '/key:value'.");
                }

                return (token.Substring(1, colonIdx - 1).ToLowerInvariant(), token.Substring(colonIdx + 1).Trim());
            }

            if (token.StartsWith("--", StringComparison.Ordinal))
            {
                var stripped = token.Substring(2);
                var eqIdx = stripped.IndexOf('=');
                if (eqIdx > 0)
                {
                    return (stripped.Substring(0, eqIdx).ToLowerInvariant(), stripped.Substring(eqIdx + 1).Trim());
                }

                if (index + 1 >= args.Length)
                {
                    throw new ArgumentException($"Option '{token}' requires a value.");
                }

                index++;
                return (stripped.ToLowerInvariant(), args[index].Trim());
            }

            throw new ArgumentException($"Unrecognized argument '{token}'.");
        }

        private void ApplyOption(string key, string value)
        {
            if (key == "project")
            {
                Project = value;
            }
            else if (key == "template")
            {
                Template = value;
            }
            else if (key is "target-env" or "targetenv" or "env")
            {
                TargetEnv = value;
            }
            else if (key == "tier")
            {
                Tier = value;
            }
            else if (key is "ttl" or "ttl-minutes" or "ttlminutes" or "additional-minutes" or "additionalminutes")
            {
                if (!int.TryParse(value, out var ttl) || ttl <= 0)
                {
                    throw new ArgumentException($"Invalid TTL value '{value}'. TTL must be a positive integer.");
                }
                TtlMinutes = ttl;
            }
            else if (key is "base-env" or "baseenv")
            {
                BaseEnv = value;
            }
            else if (key is "file" or "output")
            {
                FilePath = value;
            }
            else if (key is "property" or "properties" or "prop")
            {
                ParseProperties(value);
            }
            else if (key == "reason")
            {
                Reason = value;
            }
            else if (key is "block" or "wait")
            {
                if (!bool.TryParse(value, out var parsedBool))
                {
                    throw new ArgumentException($"Invalid boolean value '{value}' for option '{key}'.");
                }
                Block = parsedBool;
            }
            else if (key is "timeout" or "timeout-minutes" or "timeoutminutes")
            {
                if (!int.TryParse(value, out var timeout) || timeout <= 0)
                {
                    throw new ArgumentException($"Invalid timeout value '{value}'. Timeout must be a positive integer.");
                }
                TimeoutMinutes = timeout;
            }
            else if (key is "build-num" or "buildnum")
            {
                BuildNum = value;
            }
            else if (key is "build-url" or "buildurl")
            {
                BuildUrl = value;
            }
            else if (key is "build-text" or "buildtext")
            {
                BuildText = value;
            }
            else
            {
                throw new ArgumentException($"Unknown option '{key}'.");
            }
        }

        private void ParseProperties(string rawProperties)
        {
            var pairs = rawProperties.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var pair in pairs)
            {
                var eqIdx = pair.IndexOf('=');
                if (eqIdx <= 0 || eqIdx == pair.Length - 1)
                {
                    throw new ArgumentException($"Invalid property assignment '{pair}'. Expected 'Key=Value'.");
                }

                var propKey = pair.Substring(0, eqIdx).Trim();
                var propValue = pair.Substring(eqIdx + 1).Trim();
                Properties[propKey] = propValue;
            }
        }

        private void Validate()
        {
            if (Verb is EphemeralCliVerb.BlueprintList or EphemeralCliVerb.BlueprintExport or EphemeralCliVerb.BlueprintApply)
            {
                if (Verb == EphemeralCliVerb.BlueprintList && string.IsNullOrWhiteSpace(Project))
                {
                    throw new ArgumentException("Project (--project or /project:) is required for 'ephemeral blueprint list'.");
                }

                if (Verb == EphemeralCliVerb.BlueprintExport)
                {
                    if (string.IsNullOrWhiteSpace(Project))
                    {
                        throw new ArgumentException("Project (--project or /project:) is required for 'ephemeral blueprint export'.");
                    }

                    if (string.IsNullOrWhiteSpace(Template))
                    {
                        throw new ArgumentException("Template (--template or /template:) is required for 'ephemeral blueprint export'.");
                    }
                }

                if (Verb == EphemeralCliVerb.BlueprintApply && string.IsNullOrWhiteSpace(FilePath))
                {
                    throw new ArgumentException("File path (--file or /file:) is required for 'ephemeral blueprint apply'.");
                }

                return;
            }

            if (string.IsNullOrWhiteSpace(TargetEnv))
            {
                throw new ArgumentException("Target environment (--target-env or /targetenv:) is required.");
            }

            if (Verb == EphemeralCliVerb.Up && string.IsNullOrWhiteSpace(Project))
            {
                throw new ArgumentException("Project (--project or /project:) is required for 'ephemeral up'.");
            }

            if (Verb == EphemeralCliVerb.ExtendTtl && !TtlMinutes.HasValue)
            {
                throw new ArgumentException("TTL minutes (--ttl or --additional-minutes) is required for 'ephemeral extend-ttl'.");
            }
        }
    }
}
