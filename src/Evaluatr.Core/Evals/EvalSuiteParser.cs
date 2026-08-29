using Evaluatr.Core.Graders;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Evaluatr.Core.Evals;

public static class EvalSuiteParser
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .WithTypeDiscriminatingNodeDeserializer(options =>
        {
            options.AddKeyValueTypeDiscriminator<IGrader>(
                "type",
                new Dictionary<string, Type>
                {
                    ["assertion"] = typeof(AssertionGrader),
                    ["rubric"] = typeof(RubricGrader),
                });
        })
        .Build();

    public static EvalSuite Parse(string yaml)
    {
        return Deserializer.Deserialize<EvalSuite>(yaml);
    }

    public static EvalSuite ParseFile(string path)
    {
        using var reader = new StreamReader(path);
        return Deserializer.Deserialize<EvalSuite>(reader);
    }
}
