using Evaluatr.Core.Graders;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Evaluatr.Core.Evals;

/// <summary>Maps the YAML wire schema to validated, typed suite models.</summary>
public static class EvalSuiteParser
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithDuplicateKeyChecking()
        .Build();

    /// <summary>Parses YAML and validates grader configuration.</summary>
    public static EvalSuite Parse(string yaml)
    {
        var definition = Deserializer.Deserialize<SuiteDefinition>(yaml)
            ?? throw new EvalSuiteValidationException("Suite must not be empty.");
        var suite = new EvalSuite
        {
            Name = Required(definition.Name, "name"),
            Description = definition.Description,
            Harness = definition.Harness!, // Harness validation belongs to suite preflight.
            Evals = (definition.Evals ?? []).Select(eval => new Eval
            {
                Name = Required(eval.Name, "eval name"),
                Description = Required(eval.Description, "eval description"),
                Repo = eval.Repo,
                BaseCommit = eval.BaseCommit,
                Agent = eval.Agent,
                Prompt = Required(eval.Prompt, "prompt"),
                Graders = (eval.Graders ?? []).Select(MapGrader).ToList(),
            }).ToList(),
        };

        return suite;
    }

    /// <summary>Reads and parses a suite file.</summary>
    public static EvalSuite ParseFile(string path) => Parse(File.ReadAllText(path));

    private static IGrader MapGrader(GraderDefinition definition)
    {
        var name = Required(definition.Name, "grader name");
        if (definition.HasLegacyAssertion)
        {
            throw new EvalSuiteValidationException(
                $"Grader '{name}': legacy 'assertion:' is no longer supported. Replace it with 'check:' and check-specific fields (path, text, or tool).");
        }

        if (definition.HasLegacyRubric)
        {
            throw new EvalSuiteValidationException(
                $"Grader '{name}': legacy 'rubric:' is no longer supported. Replace it with 'criteria:' entries (id, description, weight) and a required 'passThreshold:'.");
        }

        if (definition.Type == "rubric")
        {
            RejectFields(definition, "check", "path", "text", "tool", "required");
            var rubric = new RubricGrader
            {
                Name = name,
                Description = definition.Description,
                Provider = definition.Provider,
                Model = definition.Model,
                PassThreshold = definition.PassThreshold
                    ?? throw new EvalSuiteValidationException($"Rubric '{name}' must specify 'passThreshold'."),
                Criteria = (definition.Criteria ?? []).Select(criterion => new RubricCriterion
                {
                    Id = Required(criterion.Id, "criterion id"),
                    Description = Required(criterion.Description, "criterion description"),
                    Weight = criterion.Weight ?? 1,
                }).ToArray(),
            };
            rubric.Validate();
            return rubric;
        }

        if (definition.Type != "assertion")
        {
            throw new EvalSuiteValidationException($"Grader '{name}' has unknown type '{definition.Type}'.");
        }

        RejectFields(definition, "criteria", "passThreshold", "provider", "model");
        AssertionCheck check;
        switch (definition.Check)
        {
            case "file-exists":
                RejectFields(definition, "text", "tool");
                check = new FileExistsCheck(Required(definition.Path, "path"));
                break;
            case "file-contains":
                RejectFields(definition, "tool");
                check = new FileContainsCheck(Required(definition.Path, "path"), Required(definition.Text, "text"));
                break;
            case "transcript-contains":
                RejectFields(definition, "path", "tool");
                check = new TranscriptContainsCheck(Required(definition.Text, "text"));
                break;
            case "tool-called":
                RejectFields(definition, "path", "text");
                check = new ToolCalledCheck(Required(definition.Tool, "tool"));
                break;
            default:
                throw new EvalSuiteValidationException($"Grader '{name}' has unknown or missing check '{definition.Check}'.");
        }

        check.Validate();
        return new AssertionGrader
        {
            Name = name,
            Description = definition.Description,
            Required = definition.Required ?? false,
            Check = check,
        };
    }

    private static string Required(string? value, string field)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new EvalSuiteValidationException($"Field '{field}' must not be empty.");
        }

        return value;
    }

    private static void RejectFields(GraderDefinition definition, params string[] fields)
    {
        foreach (var field in fields)
        {
            var present = field switch
            {
                "check" => definition.Check is not null,
                "path" => definition.Path is not null,
                "text" => definition.Text is not null,
                "tool" => definition.Tool is not null,
                "criteria" => definition.Criteria is not null,
                "passThreshold" => definition.PassThreshold is not null,
                "provider" => definition.Provider is not null,
                "model" => definition.Model is not null,
                "required" => definition.Required is not null,
                _ => throw new InvalidOperationException($"Unknown schema field: {field}"),
            };
            if (present)
            {
                throw new EvalSuiteValidationException($"Grader '{definition.Name}': field '{field}' does not apply to this check/type.");
            }
        }
    }

    // Wire-only types keep YAML's flat discriminator schema out of the domain model.
    private sealed class SuiteDefinition
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Harness { get; set; }
        public List<EvalDefinition>? Evals { get; set; }
    }

    private sealed class EvalDefinition
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Repo { get; set; }
        public string? BaseCommit { get; set; }
        public string? Agent { get; set; }
        public string? Prompt { get; set; }
        public List<GraderDefinition>? Graders { get; set; }
    }

    private sealed class GraderDefinition
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Type { get; set; }
        [YamlIgnore]
        public bool HasLegacyAssertion { get; private set; }

        // Track presence, even for 'assertion: null'; the legacy value is never used.
        public string? Assertion
        {
            get => null;
            set => HasLegacyAssertion = true;
        }

        public string? Check { get; set; }
        public bool? Required { get; set; }
        public string? Path { get; set; }
        public string? Text { get; set; }
        public string? Tool { get; set; }
        [YamlIgnore]
        public bool HasLegacyRubric { get; private set; }

        public string? Rubric
        {
            get => null;
            set => HasLegacyRubric = true;
        }

        public string? Provider { get; set; }
        public string? Model { get; set; }
        public decimal? PassThreshold { get; set; }
        public List<CriterionDefinition>? Criteria { get; set; }
    }

    private sealed class CriterionDefinition
    {
        public string? Id { get; set; }
        public string? Description { get; set; }
        public decimal? Weight { get; set; }
    }
}
